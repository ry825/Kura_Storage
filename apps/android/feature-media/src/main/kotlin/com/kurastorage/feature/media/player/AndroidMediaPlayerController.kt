@file:Suppress("CyclomaticComplexMethod", "MagicNumber", "MaxLineLength")

package com.kurastorage.feature.media.player

import android.content.Context
import android.net.Uri
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSourceException
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.ProgressiveMediaSource
import com.kurastorage.core.data.media.KuraMediaDataSource
import com.kurastorage.core.data.media.MediaDataSourceIOException
import com.kurastorage.core.data.media.MediaGeneratingIOException
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.ReadyMediaSource
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.util.concurrent.atomic.AtomicBoolean

@UnstableApi
class AndroidMediaPlayerController(
    context: Context,
    private val repository: MediaRepository,
    mobileNetwork: Boolean,
    scope: CoroutineScope,
) : ObservablePlayerEngine {
    val player: ExoPlayer =
        ExoPlayer
            .Builder(context.applicationContext)
            .setLoadControl(
                DefaultLoadControl
                    .Builder()
                    .setBufferDurationsMs(
                        if (mobileNetwork) MOBILE_MIN_BUFFER_MS else WIFI_MIN_BUFFER_MS,
                        if (mobileNetwork) MOBILE_MAX_BUFFER_MS else WIFI_MAX_BUFFER_MS,
                        PLAYBACK_BUFFER_MS,
                        REBUFFER_MS,
                    ).build(),
            ).build()

    private val mutableStates = MutableStateFlow(PlayerSnapshot())
    private val closed = AtomicBoolean(false)
    private val ticker: Job
    private var preparedSource: ReadyMediaSource? = null

    override val states: StateFlow<PlayerSnapshot> = mutableStates.asStateFlow()
    override val snapshot: PlayerSnapshot get() = mutableStates.value

    init {
        player.setAudioAttributes(
            AudioAttributes
                .Builder()
                .setContentType(C.AUDIO_CONTENT_TYPE_MOVIE)
                .setUsage(C.USAGE_MEDIA)
                .build(),
            true,
        )
        player.setHandleAudioBecomingNoisy(true)
        player.repeatMode = Player.REPEAT_MODE_OFF
        player.addListener(
            object : Player.Listener {
                override fun onEvents(
                    player: Player,
                    events: Player.Events,
                ) = publish()

                override fun onPlayerError(error: PlaybackException) {
                    val generating = error.findCause<MediaGeneratingIOException>()
                    mutableStates.value =
                        snapshot.copy(
                            phase = PlayerPhase.FAILED,
                            error = if (generating == null) error.toFailure() else null,
                            generatingJob = generating?.job,
                            playWhenReady = false,
                        )
                }
            },
        )
        ticker =
            scope.launch {
                while (isActive && !closed.get()) {
                    publish()
                    delay(PROGRESS_TICK_MS)
                }
            }
    }

    override fun prepare(
        source: ReadyMediaSource,
        positionMs: Long,
        rate: PlaybackRate,
        playWhenReady: Boolean,
    ) {
        check(!closed.get()) { "Player is closed" }
        if (source == preparedSource && player.playerError == null && player.playbackState != Player.STATE_IDLE) return
        preparedSource = source
        val mediaSource =
            ProgressiveMediaSource
                .Factory(KuraMediaDataSource.Factory(repository, source))
                .createMediaSource(MediaItem.fromUri(PLAYER_URI))
        player.setMediaSource(mediaSource, positionMs.coerceAtLeast(0))
        player.setPlaybackSpeed(rate.value)
        player.playWhenReady = playWhenReady
        player.prepare()
        publish()
    }

    override fun play() = player.play()

    override fun pause() = player.pause()

    override fun seekTo(positionMs: Long) = player.seekTo(positionMs)

    override fun setRate(rate: PlaybackRate) = player.setPlaybackSpeed(rate.value)

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        ticker.cancel()
        player.release()
    }

    private fun publish() {
        if (closed.get()) return
        val duration = player.duration.takeIf { it != C.TIME_UNSET && it >= 0 } ?: 0
        if (player.playbackState == Player.STATE_READY && duration > 0 && player.currentPosition > duration) {
            player.seekTo(duration)
        }
        mutableStates.value =
            snapshot.copy(
                positionMs = player.currentPosition.coerceAtLeast(0),
                durationMs = duration,
                bufferedPositionMs = player.bufferedPosition.coerceAtLeast(0),
                seekable = player.isCurrentMediaItemSeekable,
                playWhenReady = player.playWhenReady,
                rate = PlaybackRate(player.playbackParameters.speed.coerceIn(PlaybackRate.MIN_VALUE, PlaybackRate.MAX_VALUE)),
                phase =
                    when (player.playbackState) {
                        Player.STATE_BUFFERING -> PlayerPhase.BUFFERING
                        Player.STATE_READY -> PlayerPhase.READY
                        Player.STATE_ENDED -> PlayerPhase.ENDED
                        else -> PlayerPhase.IDLE
                    },
                error = null,
                generatingJob = null,
                videoAspectRatio = player.videoSize.toDisplayAspectRatio(),
            )
    }

    private fun androidx.media3.common.VideoSize.toDisplayAspectRatio(): Float {
        if (width <= 0 || height <= 0 || pixelWidthHeightRatio <= 0f) return snapshot.videoAspectRatio
        val displayed = width * pixelWidthHeightRatio / height
        return displayed.takeIf { it.isFinite() && it > 0f } ?: snapshot.videoAspectRatio
    }

    private fun PlaybackException.toFailure(): PlayerFailure {
        val dataError = findCause<MediaDataSourceIOException>()
        return when {
            dataError is MediaDataSourceIOException.Http && dataError.statusCode == 401 -> PlayerFailure.AUTHENTICATION
            dataError is MediaDataSourceIOException.Http && dataError.statusCode in setOf(403, 404) -> PlayerFailure.PERMISSION
            dataError is MediaDataSourceIOException.Http && dataError.statusCode == 409 -> PlayerFailure.FILE_CHANGED
            dataError is MediaDataSourceIOException.Http && dataError.statusCode == 416 -> PlayerFailure.RANGE
            dataError is MediaDataSourceIOException.InvalidRange -> PlayerFailure.RANGE
            dataError is MediaDataSourceIOException.Network -> PlayerFailure.NETWORK
            dataError is MediaDataSourceIOException.Incomplete -> PlayerFailure.INCOMPLETE
            dataError is MediaDataSourceIOException.Http -> PlayerFailure.SERVER
            errorCode == PlaybackException.ERROR_CODE_DECODING_FORMAT_UNSUPPORTED -> PlayerFailure.UNSUPPORTED_CODEC
            errorCode in DECODER_ERROR_CODES -> PlayerFailure.DECODER
            findCause<DataSourceException>() != null -> PlayerFailure.NETWORK
            else -> PlayerFailure.UNKNOWN
        }
    }

    private inline fun <reified T : Throwable> Throwable.findCause(): T? {
        var current: Throwable? = this
        while (current != null) {
            if (current is T) return current
            current = current.cause
        }
        return null
    }

    private companion object {
        val PLAYER_URI: Uri = Uri.parse("kurastorage-media://selected")
        const val WIFI_MIN_BUFFER_MS = 15_000
        const val WIFI_MAX_BUFFER_MS = 50_000
        const val MOBILE_MIN_BUFFER_MS = 5_000
        const val MOBILE_MAX_BUFFER_MS = 15_000
        const val PLAYBACK_BUFFER_MS = 1_500
        const val REBUFFER_MS = 3_000
        const val PROGRESS_TICK_MS = 500L
        val DECODER_ERROR_CODES =
            setOf(
                PlaybackException.ERROR_CODE_DECODER_INIT_FAILED,
                PlaybackException.ERROR_CODE_DECODER_QUERY_FAILED,
                PlaybackException.ERROR_CODE_DECODING_FAILED,
            )
    }
}
