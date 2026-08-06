package com.kurastorage.feature.files

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FilePager
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.TransferRepository
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class FileBrowserState(
    val loading: Boolean = true,
    val entries: List<FileEntry> = emptyList(),
    val parentId: String? = null,
    val canLoadMore: Boolean = false,
    val selected: FileEntry? = null,
    val transfer: TransferEvent? = null,
    val error: BrowserError? = null,
)

data class BrowserError(
    val message: String,
    val category: ErrorCategory,
    val requestId: String? = null,
)

@Suppress("TooManyFunctions")
class FileBrowserViewModel(
    private val files: FileRepository,
    private val transfers: TransferRepository,
    private val trashMode: Boolean = false,
) : ViewModel() {
    private val mutableState = MutableStateFlow(FileBrowserState())
    val state: StateFlow<FileBrowserState> = mutableState.asStateFlow()
    private var pager = pager(null)
    private var transferJob: Job? = null
    private var lastUpload: UploadOperation? = null
    private var lastDownload: DownloadOperation? = null
    private var lastWasUpload = false
    private val folderStack = ArrayDeque<String?>().apply { addLast(null) }

    init {
        refresh()
    }

    fun refresh() = load { pager.refresh() }

    fun loadMore() = load { pager.loadNext() }

    fun open(entry: FileEntry) {
        if (entry.entryType.name == "FOLDER" && !trashMode) {
            folderStack.addLast(entry.id)
            pager = pager(entry.id)
            refresh()
        } else {
            mutableState.update { it.copy(selected = entry) }
        }
    }

    fun select(entry: FileEntry) = mutableState.update { it.copy(selected = entry) }

    fun back(): Boolean {
        if (trashMode || folderStack.size <= 1) return false
        folderStack.removeLast()
        pager = pager(folderStack.last())
        refresh()
        return true
    }

    fun dismissDetail() = mutableState.update { it.copy(selected = null) }

    fun createFolder(name: String) = mutate { files.createFolder(folderStack.last(), name) }

    fun trash(entry: FileEntry) = mutate { files.trash(entry.id) }

    fun restore(entry: FileEntry) = mutate { files.restore(entry.id) }

    fun startUpload(
        sourceUri: String,
        fileName: String,
        size: Long,
        contentType: String?,
    ) {
        val destination = mutableState.value.parentId ?: error("Root folder has not loaded")
        lastUpload = transfers.newUpload(sourceUri, destination, fileName, size, contentType)
        lastWasUpload = true
        runUpload(checkNotNull(lastUpload))
    }

    fun retryTransfer() {
        if (lastWasUpload) {
            lastUpload?.let(::runUpload)
        } else {
            lastDownload?.let {
                runTransfer(transfers.download(it), refreshAfter = false)
            }
        }
    }

    fun startDownload(
        file: FileEntry,
        destinationUri: String,
    ) {
        lastDownload = DownloadOperation(file, destinationUri)
        lastWasUpload = false
        runTransfer(transfers.download(checkNotNull(lastDownload)), refreshAfter = false)
    }

    @Suppress("MaxLineLength")
    fun downloadedFileIntent(destinationUri: String) = transfers.openDownloadedFile(destinationUri, lastDownload?.file?.mimeType)

    fun cancelTransfer() {
        transferJob?.cancel()
        transferJob = null
        mutableState.update { it.copy(transfer = null) }
    }

    private fun runUpload(operation: UploadOperation) = runTransfer(transfers.upload(operation), refreshAfter = true)

    private fun runTransfer(
        flow: kotlinx.coroutines.flow.Flow<TransferEvent>,
        refreshAfter: Boolean,
    ) {
        transferJob?.cancel()
        transferJob =
            viewModelScope.launch {
                flow.collect { event ->
                    mutableState.update { it.copy(transfer = event, error = event.toBrowserError()) }
                    if (refreshAfter && event is TransferEvent.UploadCompleted) refresh()
                }
            }
    }

    private fun mutate(action: suspend () -> FileEntry) {
        viewModelScope.launch {
            runCatching { action() }
                .onSuccess { refresh() }
                .onFailure { showError(it) }
        }
    }

    private fun load(action: suspend () -> com.kurastorage.core.model.FilePage) {
        viewModelScope.launch {
            mutableState.update { it.copy(loading = true, error = null) }
            runCatching { action() }
                .onSuccess { page ->
                    mutableState.update {
                        it.copy(
                            loading = false,
                            entries = page.items,
                            parentId = page.parentId,
                            canLoadMore = page.hasNextPage,
                        )
                    }
                }.onFailure(::showError)
        }
    }

    private fun showError(error: Throwable) {
        mutableState.update { it.copy(loading = false, error = error.toBrowserError()) }
    }

    private fun pager(parentId: String?) =
        FilePager { page ->
            if (trashMode) files.listTrash(page) else files.list(parentId, page)
        }

    private fun TransferEvent.toBrowserError() = (this as? TransferEvent.Failed)?.error?.toBrowserError()

    private fun Throwable.toBrowserError(): BrowserError {
        val api = (this as? KuraStorageException.Api)?.error
        return BrowserError(
            message =
                when (api?.category) {
                    ErrorCategory.STORAGE -> "Storage is unavailable."
                    ErrorCategory.CONFLICT -> "An item with the same name already exists."
                    ErrorCategory.AUTHENTICATION ->
                        if (api.code == com.kurastorage.core.model.ErrorCode.DEVICE_REVOKED) {
                            "This device was revoked. Register it again on the local network."
                        } else {
                            "Sign in is required."
                        }
                    ErrorCategory.AUTHORIZATION -> "Access was denied or the item is no longer available."
                    ErrorCategory.VALIDATION -> "The selected file or value is invalid."
                    else -> "The operation failed."
                },
            category =
                if (this is SecurityException) {
                    ErrorCategory.AUTHORIZATION
                } else {
                    api?.category ?: ErrorCategory.CONNECTION
                },
            requestId = api?.requestId,
        )
    }
}
