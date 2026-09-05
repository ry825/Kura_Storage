package com.kurastorage.feature.media.photo

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.SupportedMediaMimeTypes
import com.kurastorage.feature.media.MediaRequestTicket
import com.kurastorage.feature.media.MediaViewerController
import com.kurastorage.feature.media.MediaViewerState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class PhotoViewerUiState(
    val file: FileEntry? = null,
    val media: MediaViewerState? = null,
    val zoom: Float = 1f,
    val canGoPrevious: Boolean = false,
    val canGoNext: Boolean = false,
    val error: MediaUiError? = null,
    val originalSizeLabel: String? = null,
    val previousPrefetch: FileEntry? = null,
    val nextPrefetch: FileEntry? = null,
    val currentPosition: Int = 0,
    val totalCount: Int = 0,
)

@Suppress("TooManyFunctions")
class PhotoViewerViewModel(
    private val initialFileId: String,
    orderedFileIds: List<String>,
    private val files: FileRepository,
    private val controller: MediaViewerController,
) : ViewModel() {
    private val candidates = orderedFileIds.distinct().ifEmpty { listOf(initialFileId) }
    private val mutableState = MutableStateFlow(PhotoViewerUiState())
    private var currentIndex = candidates.indexOf(initialFileId).coerceAtLeast(0)

    val state: StateFlow<PhotoViewerUiState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            controller.state.collect { media ->
                mutableState.update {
                    it.copy(
                        media = media,
                        originalSizeLabel =
                            media?.originalSizeLabel
                                ?: media?.confirmation?.formattedSize
                                ?: it.originalSizeLabel,
                    )
                }
            }
        }
        load(initialFileId)
    }

    fun selectQuality(quality: MediaQuality) {
        viewModelScope.launch { controller.selectQuality(quality) }
    }

    fun requestTicket(): MediaRequestTicket? = controller.requestTicket()

    fun contentReady(ticket: MediaRequestTicket) = controller.contentReady(ticket)

    fun contentGenerating(
        ticket: MediaRequestTicket,
        error: com.kurastorage.core.data.media.MediaGeneratingException,
    ) = controller.contentGenerating(ticket, error.job)

    fun contentFailed(ticket: MediaRequestTicket) = controller.contentFailed(ticket, MediaUiError.UNSUPPORTED)

    fun retryGeneration() {
        viewModelScope.launch { controller.retryGeneration() }
    }

    fun setZoom(value: Float) {
        mutableState.update { it.copy(zoom = value.coerceIn(MIN_ZOOM, MAX_ZOOM)) }
    }

    fun previous() = move(-1)

    fun next() = move(1)

    private fun move(delta: Int) {
        viewModelScope.launch {
            var target = currentIndex + delta
            while (target in candidates.indices) {
                val file = runCatching { files.detail(candidates[target]) }.getOrNull()
                if (file?.isViewablePhoto() == true) {
                    currentIndex = target
                    show(file)
                    return@launch
                }
                target += delta
            }
            mutableState.update {
                it.copy(
                    canGoPrevious = if (delta < 0) false else it.canGoPrevious,
                    canGoNext = if (delta > 0) false else it.canGoNext,
                )
            }
        }
    }

    private fun load(fileId: String) {
        viewModelScope.launch {
            runCatching { files.detail(fileId) }
                .onSuccess { file ->
                    if (!file.isViewablePhoto()) {
                        mutableState.update { it.copy(error = MediaUiError.UNSUPPORTED) }
                        return@onSuccess
                    }
                    show(file)
                }.onFailure { mutableState.update { it.copy(error = MediaUiError.UNKNOWN) } }
        }
    }

    private suspend fun show(file: FileEntry) {
        mutableState.value =
            PhotoViewerUiState(
                file = file,
                canGoPrevious = currentIndex > 0,
                canGoNext = currentIndex < candidates.lastIndex,
                currentPosition = currentIndex + 1,
                totalCount = candidates.size,
            )
        controller.start(file.id, file.fileVersion, MediaKind.IMAGE)
        val previous = adjacent(-1)
        val next = adjacent(1)
        mutableState.update {
            it.copy(
                canGoPrevious = previous != null,
                canGoNext = next != null,
                previousPrefetch = previous,
                nextPrefetch = next,
            )
        }
    }

    private suspend fun adjacent(delta: Int): FileEntry? {
        var target = currentIndex + delta
        while (target in candidates.indices) {
            val file = runCatching { files.detail(candidates[target]) }.getOrNull()
            if (file?.isViewablePhoto() == true) return file
            target += delta
        }
        return null
    }

    override fun onCleared() {
        controller.close()
    }

    companion object {
        const val MIN_ZOOM = 1f
        const val MAX_ZOOM = 4f

        fun FileEntry.isViewablePhoto(): Boolean =
            entryType == FileEntryType.FILE &&
                status == FileEntryStatus.ACTIVE &&
                SupportedMediaMimeTypes.isPhoto(mimeType)
    }
}
