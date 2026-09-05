@file:Suppress("MaxLineLength")

package com.kurastorage.feature.media.pdf

import android.graphics.Bitmap
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.media.InsufficientPdfStorageException
import com.kurastorage.core.data.media.InvalidPdfException
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.PdfTooLargeException
import com.kurastorage.core.data.media.TemporaryPdfStore
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class PdfLoadState { LOADING_METADATA, CONFIRMING, DOWNLOADING, RENDERING, READY, FAILED }

enum class PdfFailure {
    AUTHENTICATION,
    PERMISSION,
    NOT_FOUND,
    TOO_LARGE,
    STORAGE,
    INCOMPLETE,
    CORRUPT,
    PASSWORD_PROTECTED,
    RENDER,
    NETWORK,
    UNKNOWN,
}

data class PdfViewerUiState(
    val file: FileEntry? = null,
    val metadata: OriginalMetadata? = null,
    val loadState: PdfLoadState = PdfLoadState.LOADING_METADATA,
    val bitmap: Bitmap? = null,
    val pageIndex: Int = 0,
    val pageCount: Int = 0,
    val zoom: Float = 1f,
    val failure: PdfFailure? = null,
)

@Suppress("TooManyFunctions")
class PdfViewerViewModel(
    private val fileId: String,
    private val files: FileRepository,
    private val media: MediaRepository,
    private val store: TemporaryPdfStore,
) : ViewModel() {
    private val mutableState = MutableStateFlow(PdfViewerUiState())
    private var document: PdfDocumentController? = null
    private var renderJob: Job? = null
    private var viewportWidth = DEFAULT_VIEWPORT_WIDTH
    private var viewportHeight = DEFAULT_VIEWPORT_HEIGHT

    val state: StateFlow<PdfViewerUiState> = mutableState.asStateFlow()

    init {
        inspect()
    }

    @Suppress("ReturnCount")
    fun confirm() {
        val file = mutableState.value.file ?: return
        val metadata = mutableState.value.metadata ?: return
        if (mutableState.value.loadState != PdfLoadState.CONFIRMING) return
        open(file, metadata)
    }

    fun retryOpen() {
        val current = mutableState.value
        if (current.loadState != PdfLoadState.FAILED) return
        val file = current.file
        val metadata = current.metadata
        if (file == null || metadata == null) {
            inspect()
        } else {
            open(file, metadata)
        }
    }

    private fun open(
        file: FileEntry,
        metadata: OriginalMetadata,
    ) {
        viewModelScope.launch {
            mutableState.update { it.copy(loadState = PdfLoadState.DOWNLOADING, failure = null) }
            runCatching {
                val cached = store.download(file.id, file.fileVersion, metadata)
                PdfDocumentController.open(store.acquire(cached))
            }.onSuccess { opened ->
                document?.close()
                document = opened
                mutableState.update { it.copy(pageCount = opened.pageCount, pageIndex = 0) }
                render()
            }.onFailure { fail(it, PdfFailure.CORRUPT) }
        }
    }

    fun setViewport(
        width: Int,
        height: Int,
    ) {
        if (width <= 0 || height <= 0) return
        if (width == viewportWidth && height == viewportHeight) return
        viewportWidth = width
        viewportHeight = height
        if (document != null) render()
    }

    fun setZoom(zoom: Float) {
        mutableState.update { it.copy(zoom = zoom.coerceIn(PdfDocumentController.MIN_ZOOM, PdfDocumentController.MAX_ZOOM)) }
        render()
    }

    fun previous() = selectPage(mutableState.value.pageIndex - 1)

    fun next() = selectPage(mutableState.value.pageIndex + 1)

    fun selectPage(pageIndex: Int) {
        if (pageIndex !in 0 until mutableState.value.pageCount) return
        mutableState.update { it.copy(pageIndex = pageIndex, zoom = 1f) }
        render()
    }

    private fun inspect() {
        viewModelScope.launch {
            runCatching {
                val file = files.detail(fileId)
                require(file.isViewablePdf())
                file to media.inspectOriginal(fileId)
            }.onSuccess { (file, metadata) ->
                when {
                    metadata.mimeType
                        .substringBefore(';')
                        .trim()
                        .lowercase() != "application/pdf" -> fail(InvalidPdfException(), PdfFailure.CORRUPT)
                    !metadata.acceptsRanges -> fail(InvalidPdfException(), PdfFailure.INCOMPLETE)
                    metadata.size.value > TemporaryPdfStore.MAX_FILE_BYTES -> fail(PdfTooLargeException(), PdfFailure.TOO_LARGE)
                    else -> mutableState.value = PdfViewerUiState(file, metadata, PdfLoadState.CONFIRMING)
                }
            }.onFailure { fail(it, PdfFailure.UNKNOWN) }
        }
    }

    private fun render() {
        val active = document ?: return
        val snapshot = mutableState.value
        renderJob?.cancel()
        renderJob =
            viewModelScope.launch {
                mutableState.update {
                    // Keep the published bitmap alive while Compose may still hold its display list.
                    // Bitmap pixels are heap-managed; recycling here races the UI renderer.
                    it.copy(loadState = PdfLoadState.RENDERING)
                }
                runCatching {
                    active.render(snapshot.pageIndex, viewportWidth, viewportHeight, snapshot.zoom)
                }.onSuccess { bitmap ->
                    mutableState.update { current ->
                        if (current.pageIndex == snapshot.pageIndex && current.zoom == snapshot.zoom) {
                            current.copy(loadState = PdfLoadState.READY, bitmap = bitmap)
                        } else {
                            // This bitmap was never published to Compose and remains safe to recycle.
                            bitmap.recycle()
                            current
                        }
                    }
                }.onFailure { fail(it, PdfFailure.RENDER) }
            }
    }

    private fun fail(
        error: Throwable,
        fallback: PdfFailure,
    ) {
        val failure = error.toPdfFailure(fallback)
        mutableState.update {
            it.copy(loadState = PdfLoadState.FAILED, bitmap = null, failure = failure)
        }
    }

    override fun onCleared() {
        closeDocument()
    }

    fun closeDocument() {
        renderJob?.cancel()
        document?.close()
        document = null
        if (mutableState.value.metadata != null && mutableState.value.loadState != PdfLoadState.FAILED) {
            mutableState.update { it.copy(loadState = PdfLoadState.CONFIRMING, bitmap = null, pageCount = 0, pageIndex = 0, zoom = 1f) }
        }
    }

    private fun FileEntry.isViewablePdf(): Boolean =
        entryType == FileEntryType.FILE &&
            status == FileEntryStatus.ACTIVE &&
            mimeType?.substringBefore(';')?.trim()?.lowercase() == "application/pdf"

    private companion object {
        const val DEFAULT_VIEWPORT_WIDTH = 1_080
        const val DEFAULT_VIEWPORT_HEIGHT = 1_920
    }
}

@Suppress("CyclomaticComplexMethod")
private fun Throwable.toPdfFailure(fallback: PdfFailure): PdfFailure =
    when (this) {
        is PdfTooLargeException -> PdfFailure.TOO_LARGE
        is InsufficientPdfStorageException -> PdfFailure.STORAGE
        is InvalidPdfException -> PdfFailure.CORRUPT
        is KuraStorageException.CredentialUnavailable -> PdfFailure.AUTHENTICATION
        is KuraStorageException.Network -> PdfFailure.NETWORK
        is KuraStorageException.InvalidServerResponse -> PdfFailure.INCOMPLETE
        is KuraStorageException.Api ->
            when (error.statusCode) {
                HTTP_UNAUTHORIZED -> PdfFailure.AUTHENTICATION
                HTTP_FORBIDDEN -> PdfFailure.PERMISSION
                HTTP_NOT_FOUND -> PdfFailure.NOT_FOUND
                else -> PdfFailure.NETWORK
            }
        is SecurityException ->
            if (message?.contains("password", ignoreCase = true) == true) {
                PdfFailure.PASSWORD_PROTECTED
            } else {
                PdfFailure.PERMISSION
            }
        else -> fallback
    }

private const val HTTP_UNAUTHORIZED = 401
private const val HTTP_FORBIDDEN = 403
private const val HTTP_NOT_FOUND = 404
