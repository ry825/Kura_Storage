@file:Suppress("MaxLineLength")

package com.kurastorage.feature.files

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FilePager
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.RecentFileRepository
import com.kurastorage.core.data.TransferRepository
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.model.UploadState
import com.kurastorage.core.model.filePermissionCapabilities
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.Clock
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.UUID

data class FileBrowserState(
    val loading: Boolean = true,
    val entries: List<FileEntry> = emptyList(),
    val parentId: String? = null,
    val canLoadMore: Boolean = false,
    val selected: FileEntry? = null,
    val transfer: TransferEvent? = null,
    val rename: RenameState? = null,
    val movePicker: MovePickerState? = null,
    val permanentDelete: PermanentDeleteState? = null,
    val missingIndexDelete: MissingIndexDeleteState? = null,
    val missingActionIds: Set<String> = emptySet(),
    val retention: RetentionDisplayState? = null,
    val placementResult: String? = null,
    val error: BrowserError? = null,
    val currentFolder: FileEntry? = null,
    val personalRoot: Boolean = true,
    val historySyncError: String? = null,
)

data class MissingIndexDeleteState(
    val target: FileEntry,
    val submitting: Boolean = false,
    val resultUnknown: Boolean = false,
    val error: BrowserError? = null,
)

data class PermanentDeleteState(
    val target: FileEntry,
    val idempotencyKey: String,
    val submitting: Boolean = false,
    val resultUnknown: Boolean = false,
    val error: BrowserError? = null,
)

enum class RetentionStage { BEFORE_DEADLINE, DEADLINE_REACHED, UNKNOWN }

data class RetentionDisplayState(
    val stage: RetentionStage,
    val text: String,
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
    val destinationWritable: Boolean = false,
) {
    fun canOpen(folder: FileEntry): Boolean =
        folder.status == FileEntryStatus.ACTIVE &&
            folder.entryType == FileEntryType.FOLDER &&
            filePermissionCapabilities(folder.permission, folder.permissionSource).canMove &&
            !(target.entryType == FileEntryType.FOLDER && folder.id == target.id)

    val canConfirm: Boolean
        get() = currentFolderId != null && currentFolderId != target.parentId && destinationWritable && !loading && !submitting
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
    val writable: Boolean,
)

@Suppress("TooManyFunctions", "LargeClass", "LongParameterList")
class FileBrowserViewModel(
    private val files: FileRepository,
    private val transfers: TransferRepository,
    private val trashMode: Boolean = false,
    private val clock: Clock = Clock.systemUTC(),
    private val zoneId: ZoneId = ZoneId.systemDefault(),
    private val idempotencyKeyFactory: () -> String = { UUID.randomUUID().toString() },
    private val initialParentId: String? = null,
    private val initialSelectionId: String? = null,
    private val recentFiles: RecentFileRepository? = null,
) : ViewModel() {
    private val mutableState = MutableStateFlow(FileBrowserState(personalRoot = initialParentId == null))
    val state: StateFlow<FileBrowserState> = mutableState.asStateFlow()
    private var pager = pager(initialParentId)
    private var movePager: FilePager? = null
    private var transferJob: Job? = null
    private var lastUpload: UploadOperation? = null
    private var lastDownload: DownloadOperation? = null
    private var lastWasUpload = false
    private var placementDetailId: String? = null
    private val folderStack = ArrayDeque<String?>().apply { addLast(initialParentId) }
    private val moveFolderStack = ArrayDeque<PickerLocation>()
    private var displayedFileId: String? = null

    init {
        refresh()
        initialParentId?.let(::loadCurrentFolder)
        initialSelectionId?.let { id ->
            displayFile(id)
        }
    }

    fun refresh() =
        load({ pager.refresh() }) { page ->
            val pending = mutableState.value.permanentDelete
            val requiresAuthoritativeConfirmation =
                pending?.resultUnknown == true || pending?.error?.code == ErrorCode.FILE_NOT_FOUND
            if (requiresAuthoritativeConfirmation && page.items.none { it.id == pending.target.id }) {
                mutableState.update {
                    it.copy(
                        permanentDelete = null,
                        selected = null,
                        retention = null,
                        placementResult = "Permanent deletion confirmed.",
                    )
                }
            }
            reconcileUnknownMissingIndexDelete(page)
        }

    fun loadMore() = load(action = { pager.loadNext() })

    fun open(entry: FileEntry) {
        if (entry.entryType == FileEntryType.FOLDER && entry.status == FileEntryStatus.ACTIVE && !trashMode) {
            folderStack.addLast(entry.id)
            pager = pager(entry.id)
            mutableState.update { it.copy(currentFolder = entry) }
            refresh()
        } else {
            select(entry)
        }
    }

    fun select(entry: FileEntry) {
        if (entry.entryType == FileEntryType.FILE && entry.status == FileEntryStatus.ACTIVE) {
            displayFile(entry.id)
        } else {
            showDetail(entry)
        }
    }

    fun back(): Boolean {
        if (trashMode || folderStack.size <= 1) return false
        folderStack.removeLast()
        pager = pager(folderStack.last())
        folderStack.last()?.let(::loadCurrentFolder)
        refresh()
        return true
    }

    fun dismissDetail() {
        displayedFileId = null
        mutableState.update { it.copy(selected = null, retention = null, historySyncError = null) }
    }

    @Suppress("ComplexCondition")
    fun detailDisplayed(entry: FileEntry) {
        if (
            entry.entryType != FileEntryType.FILE ||
            entry.status != FileEntryStatus.ACTIVE ||
            mutableState.value.selected?.id != entry.id ||
            displayedFileId == entry.id
        ) {
            return
        }
        displayedFileId = entry.id
        val recent = recentFiles ?: return
        viewModelScope.launch {
            runCatching { recent.record(entry.id) }
                .onFailure {
                    mutableState.update { state ->
                        state.copy(historySyncError = "File opened, but recent history could not be synchronized.")
                    }
                }
        }
    }

    fun createFolder(name: String) {
        if (currentCapabilities().canCreate) mutate { files.createFolder(folderStack.last(), name) }
    }

    fun trash(entry: FileEntry) {
        if (entry.status == FileEntryStatus.ACTIVE && capabilities(entry).canTrash) mutate { files.trash(entry.id) }
    }

    fun recheckMissing(entry: FileEntry) {
        if (entry.status !in setOf(FileEntryStatus.MISSING, FileEntryStatus.MISSING_CANDIDATE) ||
            !capabilities(entry).canManageTrash ||
            entry.id in mutableState.value.missingActionIds
        ) {
            return
        }
        mutableState.update { it.copy(missingActionIds = it.missingActionIds + entry.id, error = null) }
        viewModelScope.launch {
            val result = runCatching { files.recheckMissing(entry.id) }
            val refreshed = runCatching { pager.refresh() }
            refreshed.onSuccess(::showPage)
            result
                .onSuccess { updated ->
                    mutableState.update {
                        it.copy(
                            selected = updated.takeUnless { item -> item.status == FileEntryStatus.ACTIVE },
                            missingActionIds = it.missingActionIds - entry.id,
                            placementResult =
                                if (updated.status == FileEntryStatus.ACTIVE) "ファイルを再発見しました。" else "再確認しました。",
                            error = refreshed.exceptionOrNull()?.toBrowserError(),
                        )
                    }
                }.onFailure { failure ->
                    mutableState.update {
                        it.copy(
                            missingActionIds = it.missingActionIds - entry.id,
                            error = failure.toBrowserError(),
                        )
                    }
                }
        }
    }

    fun beginMissingIndexDelete(entry: FileEntry) {
        if (entry.status != FileEntryStatus.MISSING ||
            !capabilities(entry).canManageTrash ||
            entry.id in mutableState.value.missingActionIds
        ) {
            return
        }
        mutableState.update {
            it.copy(
                missingIndexDelete = MissingIndexDeleteState(entry),
                selected = null,
                error = null,
            )
        }
    }

    fun cancelMissingIndexDelete() {
        val deletion = mutableState.value.missingIndexDelete ?: return
        if (!deletion.submitting && !deletion.resultUnknown) {
            mutableState.update { it.copy(missingIndexDelete = null) }
        }
    }

    fun confirmMissingIndexDelete() {
        val deletion = mutableState.value.missingIndexDelete ?: return
        if (deletion.submitting || deletion.target.id in mutableState.value.missingActionIds) return
        mutableState.update {
            it.copy(
                missingIndexDelete = deletion.copy(submitting = true, error = null),
                missingActionIds = it.missingActionIds + deletion.target.id,
            )
        }
        viewModelScope.launch {
            val result = runCatching { files.deleteMissingIndexEntry(deletion.target.id) }
            val refreshed = runCatching { pager.refresh() }
            refreshed.onSuccess(::showPage)
            result
                .onSuccess {
                    mutableState.update {
                        it.copy(
                            selected = null,
                            missingIndexDelete = null,
                            missingActionIds = it.missingActionIds - deletion.target.id,
                            placementResult = "一覧から削除しました。HDD上のファイルは削除していません。",
                            error = refreshed.exceptionOrNull()?.toBrowserError(),
                        )
                    }
                }.onFailure { failure ->
                    val error = failure.toBrowserError()
                    val page = refreshed.getOrNull()
                    if (error.resultUnknown && page != null) {
                        reconcileUnknownMissingIndexDelete(page, deletion.copy(resultUnknown = true))
                    } else {
                        mutableState.update {
                            it.copy(
                                missingIndexDelete =
                                    deletion.copy(
                                        submitting = false,
                                        resultUnknown = error.resultUnknown,
                                        error = error,
                                    ),
                                missingActionIds = it.missingActionIds - deletion.target.id,
                                error = null,
                            )
                        }
                    }
                }
        }
    }

    fun restore(entry: FileEntry) {
        val deletion = mutableState.value.permanentDelete
        if (deletion?.target?.id == entry.id && deletion.submitting) return
        if (capabilities(entry).canManageTrash) mutate { files.restore(entry.id) }
    }

    fun beginPermanentDelete(entry: FileEntry) {
        if (!trashMode || !capabilities(entry).canManageTrash || mutableState.value.permanentDelete?.submitting == true) return
        mutableState.update {
            it.copy(
                selected = null,
                retention = null,
                permanentDelete = PermanentDeleteState(entry, idempotencyKeyFactory()),
                error = null,
                placementResult = null,
            )
        }
    }

    fun cancelPermanentDelete() {
        val deletion = mutableState.value.permanentDelete
        if (deletion?.submitting == true || deletion?.resultUnknown == true) return
        mutableState.update { it.copy(permanentDelete = null) }
    }

    fun confirmPermanentDelete() {
        val deletion = mutableState.value.permanentDelete ?: return
        if (deletion.submitting) return
        mutableState.update {
            it.copy(permanentDelete = deletion.copy(submitting = true, error = null, resultUnknown = false))
        }
        viewModelScope.launch {
            runCatching { files.purge(deletion.target.id, deletion.idempotencyKey) }
                .onSuccess {
                    runCatching { pager.refresh() }
                        .onSuccess { page ->
                            showPage(page)
                            mutableState.update {
                                it.copy(
                                    selected = null,
                                    retention = null,
                                    permanentDelete = null,
                                    placementResult = "Deleted permanently.",
                                    error = null,
                                )
                            }
                        }.onFailure { failure ->
                            mutableState.update {
                                it.copy(
                                    permanentDelete =
                                        deletion.copy(
                                            submitting = false,
                                            resultUnknown = true,
                                            error = failure.toBrowserError(resultUnknownOverride = true),
                                        ),
                                )
                            }
                        }
                }.onFailure { failure ->
                    val error = failure.toBrowserError()
                    mutableState.update {
                        it.copy(
                            permanentDelete =
                                deletion.copy(
                                    submitting = false,
                                    resultUnknown = error.resultUnknown,
                                    error = error,
                                ),
                        )
                    }
                }
        }
    }

    fun beginRename(entry: FileEntry) {
        if (trashMode || entry.status != FileEntryStatus.ACTIVE || !capabilities(entry).canRename) return
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
                        authoritativeRefresh()
                        mutableState.update {
                            it.copy(rename = rename.copy(submitting = false, error = failure.toBrowserError()))
                        }
                    }
            }
        }
    }

    fun beginMove(entry: FileEntry) {
        if (trashMode || entry.status != FileEntryStatus.ACTIVE || !capabilities(entry).canMove) return
        placementDetailId =
            mutableState.value.selected
                ?.id
                ?.takeIf { it == entry.id }
        moveFolderStack.clear()
        val rootId = initialParentId
        moveFolderStack.addLast(
            PickerLocation(
                rootId,
                mutableState.value.currentFolder?.name ?: if (rootId == null) "My files" else "Shared folder",
                rootId == null || currentCapabilities().canMove,
            ),
        )
        mutableState.update {
            it.copy(
                selected = null,
                movePicker = MovePickerState(target = entry),
                placementResult = null,
                error = null,
            )
        }
        resetMovePager(rootId)
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
        moveFolderStack.addLast(PickerLocation(folder.id, folder.name, capabilities(folder).canMove))
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
                        authoritativeRefresh()
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
        if (transferJob?.isActive == true || !currentCapabilities().canCreate) return
        val destination = mutableState.value.parentId ?: error("Root folder has not loaded")
        lastUpload = transfers.newUpload(sourceUri, destination, fileName, size, contentType)
        lastWasUpload = true
        runUpload(checkNotNull(lastUpload))
    }

    fun retryTransfer() {
        if (transferJob?.isActive == true) return
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
        if (file.status != FileEntryStatus.ACTIVE || !capabilities(file).canDownload) return
        lastDownload = DownloadOperation(file, destinationUri)
        lastWasUpload = false
        runTransfer(transfers.download(checkNotNull(lastDownload)), refreshAfter = false)
    }

    @Suppress("MaxLineLength")
    fun downloadedFileIntent(destinationUri: String) = transfers.openDownloadedFile(destinationUri, lastDownload?.file?.mimeType)

    fun cancelTransfer() {
        transferJob?.cancel()
        transferJob = null
        val operation = lastUpload
        if (lastWasUpload && operation != null) {
            mutableState.update {
                it.copy(transfer = TransferEvent.UploadStatus(operation, "Cancelling upload"))
            }
            viewModelScope.launch {
                runCatching { transfers.cancelUpload(operation) }
                    .onSuccess {
                        val cancelled = operation.copy(state = UploadState.CANCELLED)
                        lastUpload = cancelled
                        mutableState.update {
                            it.copy(transfer = TransferEvent.UploadStatus(cancelled, "Upload cancelled"), error = null)
                        }
                    }.onFailure { failure ->
                        mutableState.update {
                            it.copy(
                                transfer = TransferEvent.Failed(failure),
                                error = failure.toBrowserError(),
                            )
                        }
                    }
            }
        } else {
            mutableState.update { it.copy(transfer = null) }
        }
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
                    if (event is TransferEvent.UploadStatus) lastUpload = event.operation
                    mutableState.update { it.copy(transfer = event, error = event.toBrowserError()) }
                    if (refreshAfter && event is TransferEvent.UploadCompleted) refresh()
                    if (refreshAfter && event is TransferEvent.Failed) authoritativeRefresh()
                }
            }
    }

    private fun mutate(action: suspend () -> FileEntry) {
        viewModelScope.launch {
            runCatching { action() }
                .onSuccess { refresh() }
                .onFailure { failure ->
                    authoritativeRefresh()
                    showError(failure)
                }
        }
    }

    private suspend fun authoritativeRefresh() {
        runCatching { pager.refresh() }.onSuccess(::showPage)
    }

    private fun load(
        action: suspend () -> FilePage,
        after: (FilePage) -> Unit = {},
    ) {
        viewModelScope.launch {
            mutableState.update { it.copy(loading = true, error = null, placementResult = null) }
            runCatching { action() }
                .onSuccess { page ->
                    showPage(page)
                    after(page)
                }.onFailure(::showError)
        }
    }

    private fun showDetail(entry: FileEntry) {
        mutableState.update { it.copy(selected = entry, retention = retention(entry)) }
    }

    private fun displayFile(fileId: String) {
        if (displayedFileId == fileId && mutableState.value.selected?.id == fileId) return
        viewModelScope.launch {
            runCatching { files.detail(fileId) }
                .onSuccess { latest ->
                    if (latest.entryType != FileEntryType.FILE || latest.status != FileEntryStatus.ACTIVE) {
                        showDetail(latest)
                        return@onSuccess
                    }
                    mutableState.update {
                        it.copy(selected = latest, retention = retention(latest), historySyncError = null)
                    }
                }.onFailure(::showError)
        }
    }

    private fun retention(entry: FileEntry): RetentionDisplayState {
        val deadline =
            entry.purgeEligibleAt
                ?: return RetentionDisplayState(RetentionStage.UNKNOWN, "Automatic deletion time is unavailable.")
        val local = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm z").withZone(zoneId).format(deadline)
        return if (deadline.isAfter(clock.instant())) {
            RetentionDisplayState(RetentionStage.BEFORE_DEADLINE, "Scheduled for automatic deletion: $local")
        } else {
            RetentionDisplayState(RetentionStage.DEADLINE_REACHED, "Automatic deletion is due since $local")
        }
    }

    private fun showPage(page: FilePage) {
        mutableState.update {
            it.copy(
                loading = false,
                entries = page.items,
                parentId = page.parentId,
                canLoadMore = page.hasNextPage,
                currentFolder = if (page.parentId == null) null else it.currentFolder?.takeIf { folder -> folder.id == page.parentId },
                personalRoot = initialParentId == null && folderStack.size == 1,
            )
        }
    }

    private fun loadCurrentFolder(parentId: String) {
        viewModelScope.launch {
            runCatching { files.detail(parentId) }.onSuccess { folder ->
                mutableState.update { state -> state.copy(currentFolder = folder) }
            }
        }
    }

    private fun capabilities(entry: FileEntry) = filePermissionCapabilities(entry.permission, entry.permissionSource)

    private fun currentCapabilities() =
        mutableState.value.currentFolder?.let(::capabilities)
            ?: if (initialParentId == null && folderStack.size == 1) {
                filePermissionCapabilities(SharePermission.MANAGER, PermissionSource.OWNER)
            } else {
                filePermissionCapabilities(SharePermission.UNKNOWN, PermissionSource.UNKNOWN)
            }

    private fun reconcileUnknownMissingIndexDelete(
        page: FilePage,
        deletion: MissingIndexDeleteState? = mutableState.value.missingIndexDelete,
    ) {
        if (deletion?.resultUnknown != true) return
        val stillPresent = page.items.any { it.id == deletion.target.id }
        mutableState.update {
            it.copy(
                missingIndexDelete =
                    if (stillPresent) {
                        deletion.copy(
                            submitting = false,
                            resultUnknown = false,
                            error =
                                BrowserError(
                                    message = "最新の一覧では索引項目が残っています。必要なら再試行してください。",
                                    category = ErrorCategory.CONNECTION,
                                ),
                        )
                    } else {
                        null
                    },
                selected = if (stillPresent) it.selected else null,
                missingActionIds = it.missingActionIds - deletion.target.id,
                placementResult =
                    if (stillPresent) {
                        it.placementResult
                    } else {
                        "最新の一覧を取得しました。対象は一覧にありません。"
                    },
                error = null,
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
                                    destinationWritable = location.writable,
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
            ErrorCode.FILE_MISSING_CANDIDATE -> "ファイルを確認中です。しばらくしてから再確認してください。"
            ErrorCode.FILE_MISSING -> "ファイルが見つかりません。"
            ErrorCode.FILE_STATE_CONFLICT, ErrorCode.INDEX_CONFLICT -> "状態が変更されました。一覧を更新してください。"
            ErrorCode.IDEMPOTENCY_CONFLICT -> "This deletion key conflicts with another request. Refresh the list."
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
