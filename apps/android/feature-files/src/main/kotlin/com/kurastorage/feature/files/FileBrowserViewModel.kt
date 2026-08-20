package com.kurastorage.feature.files

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FilePager
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.TransferRepository
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
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
    val rename: RenameState? = null,
    val movePicker: MovePickerState? = null,
    val placementResult: String? = null,
    val error: BrowserError? = null,
)

data class RenameState(
    val target: FileEntry,
    val input: String,
    val submitting: Boolean = false,
    val error: BrowserError? = null,
)

data class MovePickerState(
    val target: FileEntry,
    val currentFolderId: String? = null,
    val currentFolderName: String = "My files",
    val folders: List<FileEntry> = emptyList(),
    val canLoadMore: Boolean = false,
    val canGoBack: Boolean = false,
    val loading: Boolean = true,
    val submitting: Boolean = false,
    val error: BrowserError? = null,
) {
    fun canOpen(folder: FileEntry): Boolean =
        folder.status == FileEntryStatus.ACTIVE &&
            folder.entryType == FileEntryType.FOLDER &&
            !(target.entryType == FileEntryType.FOLDER && folder.id == target.id)

    val canConfirm: Boolean
        get() = currentFolderId != null && currentFolderId != target.parentId && !loading && !submitting
}

data class BrowserError(
    val message: String,
    val category: ErrorCategory,
    val code: ErrorCode? = null,
    val requestId: String? = null,
    val resultUnknown: Boolean = false,
)

private data class PickerLocation(
    val id: String?,
    val name: String,
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
    private var movePager: FilePager? = null
    private var transferJob: Job? = null
    private var lastUpload: UploadOperation? = null
    private var lastDownload: DownloadOperation? = null
    private var lastWasUpload = false
    private var placementDetailId: String? = null
    private val folderStack = ArrayDeque<String?>().apply { addLast(null) }
    private val moveFolderStack = ArrayDeque<PickerLocation>()

    init {
        refresh()
    }

    fun refresh() = load { pager.refresh() }

    fun loadMore() = load { pager.loadNext() }

    fun open(entry: FileEntry) {
        if (entry.entryType == FileEntryType.FOLDER && !trashMode) {
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

    fun beginRename(entry: FileEntry) {
        if (trashMode) return
        placementDetailId =
            mutableState.value.selected
                ?.id
                ?.takeIf { it == entry.id }
        mutableState.update {
            it.copy(selected = null, rename = RenameState(entry, entry.name), placementResult = null, error = null)
        }
    }

    fun updateRenameInput(input: String) {
        mutableState.update { state ->
            state.copy(rename = state.rename?.copy(input = input, error = null))
        }
    }

    fun dismissRename() {
        placementDetailId = null
        mutableState.update { it.copy(rename = null) }
    }

    fun submitRename() {
        val rename = mutableState.value.rename ?: return
        if (rename.submitting) return
        val validation = validateName(rename.input)
        if (validation != null) {
            mutableState.update { it.copy(rename = rename.copy(error = validation)) }
        } else {
            mutableState.update { it.copy(rename = rename.copy(submitting = true, error = null)) }
            viewModelScope.launch {
                runCatching { files.rename(rename.target.id, rename.input) }
                    .onSuccess { updated -> completePlacement("Renamed to ${updated.name}.") }
                    .onFailure { failure ->
                        mutableState.update {
                            it.copy(rename = rename.copy(submitting = false, error = failure.toBrowserError()))
                        }
                    }
            }
        }
    }

    fun beginMove(entry: FileEntry) {
        if (trashMode) return
        placementDetailId =
            mutableState.value.selected
                ?.id
                ?.takeIf { it == entry.id }
        moveFolderStack.clear()
        moveFolderStack.addLast(PickerLocation(null, "My files"))
        mutableState.update {
            it.copy(
                selected = null,
                movePicker = MovePickerState(target = entry),
                placementResult = null,
                error = null,
            )
        }
        resetMovePager(null)
        loadMovePage(refresh = true)
    }

    fun dismissMove() {
        placementDetailId = null
        movePager = null
        moveFolderStack.clear()
        mutableState.update { it.copy(movePicker = null) }
    }

    fun openMoveFolder(folder: FileEntry) {
        val picker = mutableState.value.movePicker ?: return
        if (!picker.canOpen(folder) || picker.loading || picker.submitting) return
        moveFolderStack.addLast(PickerLocation(folder.id, folder.name))
        resetMovePager(folder.id)
        loadMovePage(refresh = true)
    }

    fun backMoveFolder() {
        val picker = mutableState.value.movePicker ?: return
        if (moveFolderStack.size <= 1 || picker.loading || picker.submitting) return
        moveFolderStack.removeLast()
        resetMovePager(moveFolderStack.last().id)
        loadMovePage(refresh = true)
    }

    fun loadMoreMoveFolders() {
        val picker = mutableState.value.movePicker ?: return
        if (!picker.canLoadMore || picker.loading || picker.submitting) return
        loadMovePage(refresh = false)
    }

    fun confirmMove() {
        val picker = mutableState.value.movePicker ?: return
        val destinationId = picker.currentFolderId ?: return
        if (picker.canConfirm) {
            mutableState.update { it.copy(movePicker = picker.copy(submitting = true, error = null)) }
            viewModelScope.launch {
                runCatching { files.move(picker.target.id, destinationId) }
                    .onSuccess { updated -> completePlacement("Moved ${updated.name}.") }
                    .onFailure { failure ->
                        mutableState.update {
                            it.copy(movePicker = picker.copy(submitting = false, error = failure.toBrowserError()))
                        }
                    }
            }
        }
    }

    fun refreshAfterPlacementFailure() {
        placementDetailId = null
        mutableState.update { it.copy(rename = null, movePicker = null, error = null) }
        refresh()
    }

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

    private fun load(action: suspend () -> FilePage) {
        viewModelScope.launch {
            mutableState.update { it.copy(loading = true, error = null, placementResult = null) }
            runCatching { action() }
                .onSuccess(::showPage)
                .onFailure(::showError)
        }
    }

    private fun showPage(page: FilePage) {
        mutableState.update {
            it.copy(
                loading = false,
                entries = page.items,
                parentId = page.parentId,
                canLoadMore = page.hasNextPage,
            )
        }
    }

    private suspend fun completePlacement(result: String) {
        runCatching {
            val page = pager.refresh()
            val selected = placementDetailId?.let { files.detail(it) }
            page to selected
        }.onSuccess { (page, selected) ->
            movePager = null
            moveFolderStack.clear()
            placementDetailId = null
            mutableState.update {
                it.copy(
                    loading = false,
                    entries = page.items,
                    parentId = page.parentId,
                    canLoadMore = page.hasNextPage,
                    selected = selected,
                    rename = null,
                    movePicker = null,
                    placementResult = result,
                    error = null,
                )
            }
        }.onFailure { failure ->
            val error = failure.toBrowserError(resultUnknownOverride = false)
            mutableState.update {
                it.copy(rename = null, movePicker = null, placementResult = result, error = error)
            }
        }
    }

    private fun resetMovePager(parentId: String?) {
        movePager = FilePager { page -> files.list(parentId, page) }
    }

    private fun loadMovePage(refresh: Boolean) {
        val picker = mutableState.value.movePicker ?: return
        val activePager = movePager ?: return
        mutableState.update { it.copy(movePicker = picker.copy(loading = true, error = null)) }
        viewModelScope.launch {
            runCatching { if (refresh) activePager.refresh() else activePager.loadNext() }
                .onSuccess { page ->
                    val location = moveFolderStack.last()
                    mutableState.update { state ->
                        state.copy(
                            movePicker =
                                state.movePicker?.copy(
                                    currentFolderId = page.parentId,
                                    currentFolderName = location.name,
                                    folders =
                                        page.items.filter {
                                            it.status == FileEntryStatus.ACTIVE &&
                                                it.entryType == FileEntryType.FOLDER
                                        },
                                    canLoadMore = page.hasNextPage,
                                    canGoBack = moveFolderStack.size > 1,
                                    loading = false,
                                ),
                        )
                    }
                }.onFailure { failure ->
                    mutableState.update { state ->
                        state.copy(
                            movePicker =
                                state.movePicker?.copy(
                                    loading = false,
                                    error = failure.toBrowserError(),
                                ),
                        )
                    }
                }
        }
    }

    private fun showError(error: Throwable) {
        mutableState.update { it.copy(loading = false, error = error.toBrowserError()) }
    }

    private fun pager(parentId: String?) =
        FilePager { page ->
            if (trashMode) files.listTrash(page) else files.list(parentId, page)
        }

    private fun validateName(name: String): BrowserError? {
        val invalid =
            name.isBlank() ||
                name.length > MAX_FILE_NAME_LENGTH ||
                name.any { it == '/' || it == '\\' || it == '\u0000' || it.isISOControl() }
        return if (invalid) {
            BrowserError(
                message = "Enter a name without separators or control characters (maximum 255 characters).",
                category = ErrorCategory.VALIDATION,
                code = ErrorCode.VALIDATION_FAILED,
            )
        } else {
            null
        }
    }

    private fun TransferEvent.toBrowserError() = (this as? TransferEvent.Failed)?.error?.toBrowserError()

    private fun Throwable.toBrowserError(resultUnknownOverride: Boolean? = null): BrowserError {
        val api = (this as? KuraStorageException.Api)?.error
        val resultUnknown = resultUnknownOverride ?: (this is KuraStorageException.Network)
        return BrowserError(
            message = browserErrorMessage(api?.code, api?.category, resultUnknown),
            category =
                if (this is SecurityException) {
                    ErrorCategory.AUTHORIZATION
                } else {
                    api?.category ?: ErrorCategory.CONNECTION
                },
            code = api?.code,
            requestId = api?.requestId,
            resultUnknown = resultUnknown,
        )
    }

    private fun browserErrorMessage(
        code: ErrorCode?,
        category: ErrorCategory?,
        resultUnknown: Boolean,
    ): String =
        when (code) {
            ErrorCode.FILE_NAME_CONFLICT ->
                "An item with the same name already exists. Choose another name or folder."
            ErrorCode.FILE_MOVE_CYCLE -> "This folder cannot be moved there. Choose another folder."
            ErrorCode.FILE_NOT_FOUND -> "The item or destination is no longer available. Refresh the list."
            ErrorCode.RECOVERY_REQUIRED -> "Recovery is required before this item can be changed."
            ErrorCode.STORAGE_UNAVAILABLE, ErrorCode.STORAGE_CAPACITY_INSUFFICIENT -> "Storage is unavailable."
            else -> categoryErrorMessage(code, category, resultUnknown)
        }

    private fun categoryErrorMessage(
        code: ErrorCode?,
        category: ErrorCategory?,
        resultUnknown: Boolean,
    ): String =
        when (category) {
            ErrorCategory.STORAGE -> "Storage is unavailable."
            ErrorCategory.CONFLICT -> "The operation conflicts with the current file state."
            ErrorCategory.AUTHENTICATION ->
                if (code == ErrorCode.DEVICE_REVOKED) {
                    "This device was revoked. Register it again on the local network."
                } else {
                    "Sign in is required."
                }
            ErrorCategory.AUTHORIZATION -> "Access was denied or the item is no longer available."
            ErrorCategory.VALIDATION -> "The selected file or value is invalid."
            else ->
                if (resultUnknown) {
                    "The result is unknown because the connection was interrupted. Refresh to confirm."
                } else {
                    "The operation failed."
                }
        }

    private companion object {
        const val MAX_FILE_NAME_LENGTH = 255
    }
}
