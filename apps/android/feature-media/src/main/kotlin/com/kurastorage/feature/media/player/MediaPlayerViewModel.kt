@file:Suppress("MaxLineLength", "ReturnCount", "TooManyFunctions")

package com.kurastorage.feature.media.player

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.SupportedMediaMimeTypes
import com.kurastorage.feature.media.MediaRequestTicket
import com.kurastorage.feature.media.MediaViewerController
import com.kurastorage.feature.media.MediaViewerState
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class MediaPlayerUiState(
    val file: FileEntry? = null,
    val kind: MediaKind,
    val media: MediaViewerState? = null,
    val player: PlayerSnapshot = PlayerSnapshot(),
    val error: MediaUiError? = null,
    val reconnecting: Boolean = false,
)

class MediaPlayerViewModel(
    private val fileId: String,
    private val kind: MediaKind,
    private val files: FileRepository,
    private val mediaController: MediaViewerController,
    private val readinessProbe: MediaReadinessProbe,
) : ViewModel() {
    private val mutableState = MutableStateFlow(MediaPlayerUiState(kind = kind))
    private var engine: ObservablePlayerEngine? = null
    private var engineCollection: Job? = null
    private var prepareJob: Job? = null
    private var activeTicket: MediaRequestTicket? = null
    private var pendingRestore = PlayerSnapshot()
    private var lastPlayableQuality: MediaQuality? = null
    private var previousQuality: MediaQuality? = null

    val state: StateFlow<MediaPlayerUiState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            mediaController.state.collect { media ->
                mutableState.update {
                    it.copy(
                        media = media,
                        reconnecting = if (media?.loadState is MediaLoadState.Failed) false else it.reconnecting,
                    )
                }
                if (media?.loadState is MediaLoadState.Loading) maybePrepare()
            }
        }
        viewModelScope.launch { load() }
    }

    fun attachEngine(value: ObservablePlayerEngine) {
        if (engine === value) return
        detachEngine()
        engine = value
        val media = mutableState.value.media
        if (media?.loadState is MediaLoadState.Ready) {
            value.prepare(
                media.loadState.source,
                pendingRestore.positionMs,
                pendingRestore.rate,
                pendingRestore.playWhenReady,
            )
        } else {
            maybePrepare()
        }
        engineCollection =
            viewModelScope.launch {
                value.states.collect(::onPlayerSnapshot)
            }
    }

    fun detachEngine() {
        val detachedSnapshot = engine?.snapshot
        engineCollection?.cancel()
        engineCollection = null
        engine?.pause()
        detachedSnapshot?.let {
            pendingRestore = it
            mutableState.update { state -> state.copy(player = it) }
        }
        engine = null
    }

    fun selectQuality(quality: MediaQuality) {
        if (kind != MediaKind.VIDEO) return
        val currentQuality = mutableState.value.media?.quality
        if (quality == currentQuality) return
        previousQuality = currentQuality
        pendingRestore = (engine?.snapshot ?: mutableState.value.player)
        mutableState.update { it.copy(reconnecting = false) }
        activeTicket = null
        prepareJob?.cancel()
        viewModelScope.launch { mediaController.selectQuality(quality) }
    }

    fun confirmOriginal() {
        mediaController.confirmOriginal()
        maybePrepare()
    }

    fun cancelOriginal() {
        val fallback = previousQuality ?: lastPlayableQuality ?: MediaQuality.MEDIUM
        if (kind == MediaKind.VIDEO && fallback != MediaQuality.ORIGINAL) {
            viewModelScope.launch { mediaController.selectQuality(fallback) }
        } else {
            mediaController.cancelOriginalConfirmation()
        }
    }

    fun retryGeneration() {
        viewModelScope.launch { mediaController.retryGeneration() }
    }

    fun retryPlayback() {
        val quality = mutableState.value.media?.quality ?: return
        activeTicket = null
        mutableState.update { it.copy(reconnecting = true) }
        viewModelScope.launch { mediaController.selectQuality(quality) }
    }

    fun play() = engine?.play()

    fun pause() = engine?.pause()

    fun seekTo(positionMs: Long) = engine?.let { PlayerCommandController(it).seekTo(positionMs) }

    fun skipBack(amountMs: Long) = engine?.let { PlayerCommandController(it).skipBack(amountMs) }

    fun skipForward(amountMs: Long) = engine?.let { PlayerCommandController(it).skipForward(amountMs) }

    fun setRate(rate: PlaybackRate) = engine?.let { PlayerCommandController(it).setRate(rate) }

    fun onAppBackgrounded() = pause()

    private suspend fun load() {
        runCatching { files.detail(fileId) }
            .onSuccess { file ->
                if (!file.isPlayableAs(kind)) {
                    mutableState.update { it.copy(error = MediaUiError.UNSUPPORTED) }
                    return@onSuccess
                }
                mutableState.update { it.copy(file = file) }
                mediaController.start(file.id, file.fileVersion, kind)
            }.onFailure { mutableState.update { it.copy(error = MediaUiError.UNKNOWN) } }
    }

    private fun maybePrepare() {
        val player = engine ?: return
        val ticket = mediaController.requestTicket() ?: return
        if (ticket == activeTicket && prepareJob?.isActive == true) return
        activeTicket = ticket
        prepareJob?.cancel()
        prepareJob =
            viewModelScope.launch {
                runCatching { readinessProbe.check(ticket) }
                    .onSuccess { readiness ->
                        when (readiness) {
                            MediaReadiness.Ready ->
                                player.prepare(
                                    ticket.source,
                                    pendingRestore.positionMs,
                                    pendingRestore.rate,
                                    pendingRestore.playWhenReady,
                                )
                            is MediaReadiness.Generating -> mediaController.contentGenerating(ticket, readiness.job)
                        }
                    }.onFailure {
                        mediaController.contentFailed(ticket, MediaUiError.DISCONNECTED)
                    }
            }
    }

    private fun onPlayerSnapshot(snapshot: PlayerSnapshot) {
        pendingRestore = snapshot
        mutableState.update {
            it.copy(
                player = snapshot,
                reconnecting = if (snapshot.phase == PlayerPhase.READY || snapshot.phase == PlayerPhase.FAILED) false else it.reconnecting,
            )
        }
        val ticket = activeTicket ?: return
        snapshot.generatingJob?.let {
            mediaController.contentGenerating(ticket, it)
            return
        }
        when (snapshot.phase) {
            PlayerPhase.READY -> {
                lastPlayableQuality = mutableState.value.media?.quality
                mediaController.contentReady(ticket)
            }
            PlayerPhase.FAILED -> mediaController.contentFailed(ticket, snapshot.error.toUiError())
            else -> Unit
        }
    }

    override fun onCleared() {
        detachEngine()
        mediaController.close()
    }

    private fun FileEntry.isPlayableAs(expected: MediaKind): Boolean =
        entryType == FileEntryType.FILE &&
            status == FileEntryStatus.ACTIVE &&
            when (expected) {
                MediaKind.VIDEO -> SupportedMediaMimeTypes.isVideo(mimeType)
                MediaKind.AUDIO -> SupportedMediaMimeTypes.isAudio(mimeType)
                else -> false
            }

    private fun PlayerFailure?.toUiError(): MediaUiError =
        when (this) {
            PlayerFailure.AUTHENTICATION -> MediaUiError.AUTHENTICATION_REQUIRED
            PlayerFailure.PERMISSION -> MediaUiError.PERMISSION_DENIED
            PlayerFailure.FILE_CHANGED -> MediaUiError.FILE_CHANGED
            PlayerFailure.RANGE -> MediaUiError.RANGE_INVALID
            PlayerFailure.NETWORK -> MediaUiError.DISCONNECTED
            PlayerFailure.UNSUPPORTED_CODEC,
            PlayerFailure.DECODER,
            -> MediaUiError.UNSUPPORTED
            PlayerFailure.UNKNOWN,
            null,
            -> MediaUiError.UNKNOWN
        }
}
