package com.kurastorage.feature.media.player

import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.ReadyMediaSource
import kotlinx.coroutines.flow.StateFlow

data class PlayerSnapshot(
    val positionMs: Long = 0,
    val durationMs: Long = 0,
    val bufferedPositionMs: Long = 0,
    val seekable: Boolean = false,
    val playWhenReady: Boolean = false,
    val rate: PlaybackRate = PlaybackRate(1f),
    val phase: PlayerPhase = PlayerPhase.IDLE,
    val error: PlayerFailure? = null,
    val generatingJob: MediaJobSnapshot? = null,
)

enum class PlayerPhase {
    IDLE,
    BUFFERING,
    READY,
    ENDED,
    FAILED,
}

enum class PlayerFailure {
    AUTHENTICATION,
    PERMISSION,
    FILE_CHANGED,
    RANGE,
    NETWORK,
    INCOMPLETE,
    SERVER,
    UNSUPPORTED_CODEC,
    DECODER,
    UNKNOWN,
}

interface PlayerEngine {
    val snapshot: PlayerSnapshot

    fun play()

    fun pause()

    fun seekTo(positionMs: Long)

    fun setRate(rate: PlaybackRate)
}

interface ObservablePlayerEngine :
    PlayerEngine,
    AutoCloseable {
    val states: StateFlow<PlayerSnapshot>

    fun prepare(
        source: ReadyMediaSource,
        positionMs: Long,
        rate: PlaybackRate,
        playWhenReady: Boolean,
    )
}

class PlayerCommandController(
    private val engine: PlayerEngine,
) {
    fun play() = engine.play()

    fun pause() = engine.pause()

    fun seekTo(positionMs: Long) {
        val current = engine.snapshot
        if (!current.seekable) return
        engine.seekTo(positionMs.coerceIn(0, current.durationMs.coerceAtLeast(0)))
    }

    fun skipBack(amountMs: Long) {
        require(amountMs > 0)
        seekTo(engine.snapshot.positionMs - amountMs)
    }

    fun skipForward(amountMs: Long) {
        require(amountMs > 0)
        seekTo(engine.snapshot.positionMs + amountMs)
    }

    fun setRate(rate: PlaybackRate) {
        require(rate.value in APPROVED_RATES) { "Playback rate is not an approved UI value" }
        engine.setRate(rate)
    }

    companion object {
        private val RATE_SEQUENCE = listOf(0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f)
        val APPROVED_RATES = RATE_SEQUENCE.toSet()

        fun nextRate(current: PlaybackRate): PlaybackRate {
            val index = RATE_SEQUENCE.indexOf(current.value)
            return PlaybackRate(RATE_SEQUENCE[(index + 1) % RATE_SEQUENCE.size])
        }
    }
}
