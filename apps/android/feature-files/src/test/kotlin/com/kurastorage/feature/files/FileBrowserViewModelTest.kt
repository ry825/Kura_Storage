package com.kurastorage.feature.files

import android.content.Intent
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.RecentFileRepository
import com.kurastorage.core.data.RecentRecordOutcome
import com.kurastorage.core.data.TransferRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.RecentFilePage
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.model.UploadState
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.io.IOException
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset

@OptIn(ExperimentalCoroutinesApi::class)
@Suppress("LargeClass")
class FileBrowserViewModelTest {
    private val dispatcher = UnconfinedTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `refresh and load more append pages`() =
        runTest(dispatcher) {
            val viewModel = FileBrowserViewModel(FakeFiles(), FakeTransfers())

            assertEquals(
                listOf("file-1"),
                viewModel.state.value.entries
                    .map { it.id },
            )
            viewModel.loadMore()
            assertEquals(
                listOf("file-1", "file-2"),
                viewModel.state.value.entries
                    .map { it.id },
            )
        }

    @Test
    fun `empty page is represented without an error`() =
        runTest(dispatcher) {
            val viewModel = FileBrowserViewModel(FakeFiles(empty = true), FakeTransfers())

            assertFalse(viewModel.state.value.loading)
            assertEquals(emptyList<FileEntry>(), viewModel.state.value.entries)
            assertNull(viewModel.state.value.error)
        }

    @Test
    fun `folder navigation maintains a stable breadcrumb trail`() =
        runTest(dispatcher) {
            val viewModel = FileBrowserViewModel(FakeFiles(), FakeTransfers())
            val album = folder("album", "root", "Family photos")

            viewModel.open(album)
            assertEquals(
                listOf("My files", "Family photos"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )

            assertTrue(viewModel.back())
            assertEquals(
                listOf("My files"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )
        }

    @Test
    fun `duplicate folder taps commit once and stale different target cannot win`() =
        runTest(dispatcher) {
            val files = NavigationFiles()
            val viewModel = FileBrowserViewModel(files, FakeTransfers())
            val first = folder("first", null, "First")
            val second = folder("second", null, "Second")

            viewModel.open(first)
            viewModel.open(first)
            assertEquals(listOf("first"), files.detailCalls)

            viewModel.open(second)
            assertEquals(listOf("first", "second"), files.detailCalls)
            files.complete("second", second)
            assertEquals(
                listOf("My files", "Second"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )

            files.complete("first", first)
            assertEquals(
                listOf("My files", "Second"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )
            assertEquals("second", viewModel.state.value.parentId)
        }

    @Test
    fun `back during folder open cancels the pending location without creating a ghost path`() =
        runTest(dispatcher) {
            val files = NavigationFiles()
            val viewModel = FileBrowserViewModel(files, FakeTransfers())
            val pending = folder("pending", null, "Pending")

            viewModel.open(pending)
            assertTrue(viewModel.back())
            files.complete("pending", pending)

            assertEquals(
                listOf("My files"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )
            assertFalse(viewModel.state.value.loading)
        }

    @Test
    fun `folder list failure retains the previously committed location`() =
        runTest(dispatcher) {
            val files = NavigationFiles()
            val viewModel = FileBrowserViewModel(files, FakeTransfers())
            val failing = folder("failing", null, "Failing")

            viewModel.open(failing)
            files.fail(failing.id, failing)

            assertEquals(
                listOf("My files"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )
            assertEquals(null, viewModel.state.value.parentId)
            assertEquals(
                ErrorCode.STORAGE_UNAVAILABLE,
                viewModel.state.value.error
                    ?.code,
            )
        }

    @Test
    fun `shared root remains its own boundary and cannot back into personal root`() =
        runTest(dispatcher) {
            val files = NavigationFiles()
            val shared = folder("shared", null, "Shared album")
            val viewModel =
                FileBrowserViewModel(
                    files,
                    FakeTransfers(),
                    initialParentId = shared.id,
                )
            files.complete(shared.id, shared)

            assertEquals(
                listOf("Shared album"),
                viewModel.state.value.breadcrumbs
                    .map { it.label },
            )
            assertFalse(viewModel.state.value.personalRoot)
            assertFalse(viewModel.back())
        }

    @Test
    fun `missing recheck blocks duplicate and refreshes page one after rediscovery`() =
        runTest(dispatcher) {
            val gate = CompletableDeferred<Unit>()
            val target = file("missing").copy(status = FileEntryStatus.MISSING)
            val files = MissingFiles(target, recheckGate = gate)
            val viewModel = FileBrowserViewModel(files, FakeTransfers())

            viewModel.select(target)
            viewModel.recheckMissing(target)
            viewModel.recheckMissing(target)
            assertEquals(1, files.recheckCalls)
            assertEquals(setOf(target.id), viewModel.state.value.missingActionIds)

            gate.complete(Unit)
            assertEquals(1, files.recheckCalls)
            assertEquals(FileEntryStatus.ACTIVE, files.target.status)
            assertEquals(emptySet<String>(), viewModel.state.value.missingActionIds)
            assertEquals(null, viewModel.state.value.selected)
            assertEquals(2, files.listCalls)
        }

    @Test
    fun `unknown missing index deletion uses refreshed presence and allows retry`() =
        runTest(dispatcher) {
            val target = file("missing").copy(status = FileEntryStatus.MISSING)
            val files =
                MissingFiles(target).apply {
                    deleteFailure = KuraStorageException.Network(IOException("response unknown"))
                }
            val viewModel = FileBrowserViewModel(files, FakeTransfers())

            viewModel.beginMissingIndexDelete(target)
            viewModel.confirmMissingIndexDelete()

            assertEquals(1, files.deleteCalls)
            assertEquals(true, files.present)
            assertEquals(
                false,
                viewModel.state.value.missingIndexDelete
                    ?.resultUnknown,
            )
            assertEquals(2, files.listCalls)
        }

    @Test
    fun `unknown missing index deletion closes only after authoritative refresh no longer contains target`() =
        runTest(dispatcher) {
            val target = file("missing").copy(status = FileEntryStatus.MISSING)
            val files =
                MissingFiles(target).apply {
                    deleteFailure = KuraStorageException.Network(IOException("response unknown"))
                }
            val viewModel = FileBrowserViewModel(files, FakeTransfers())
            files.present = false

            viewModel.beginMissingIndexDelete(target)
            viewModel.confirmMissingIndexDelete()

            assertEquals(1, files.deleteCalls)
            assertNull(viewModel.state.value.missingIndexDelete)
            assertEquals("最新の一覧を取得しました。対象は一覧にありません。", viewModel.state.value.placementResult)
            assertEquals(2, files.listCalls)
        }

    @Test
    fun `load error is mapped and refresh retries successfully`() =
        runTest(dispatcher) {
            val files = FakeFiles(failNext = true)
            val viewModel = FileBrowserViewModel(files, FakeTransfers())

            assertEquals(
                ErrorCategory.STORAGE,
                viewModel.state.value.error
                    ?.category,
            )
            assertEquals(
                "request-id",
                viewModel.state.value.error
                    ?.requestId,
            )

            viewModel.refresh()

            assertNull(viewModel.state.value.error)
            assertEquals(
                listOf("file-1"),
                viewModel.state.value.entries
                    .map { it.id },
            )
        }

    @Test
    fun `upload retry reuses operation and cancel clears active progress`() =
        runTest(dispatcher) {
            val retryTransfers =
                FakeTransfers { operation, attempt ->
                    if (attempt == 1) {
                        flowOf(TransferEvent.Failed(IOException("offline")))
                    } else {
                        flowOf(TransferEvent.UploadCompleted(file(operation.fileName)))
                    }
                }
            val viewModel = FileBrowserViewModel(FakeFiles(), retryTransfers)

            viewModel.startUpload("content://source", "retry.txt", 5, "text/plain")
            viewModel.retryTransfer()

            assertEquals(2, retryTransfers.uploads.size)
            assertEquals(
                retryTransfers.uploads.first().idempotencyKey,
                retryTransfers.uploads.last().idempotencyKey,
            )

            val cancellableTransfers =
                FakeTransfers { _, _ ->
                    flow {
                        emit(TransferEvent.Progress(1, 5))
                        awaitCancellation()
                    }
                }
            val cancellableViewModel = FileBrowserViewModel(FakeFiles(), cancellableTransfers)
            cancellableViewModel.startUpload("content://source", "cancel.txt", 5, "text/plain")
            assertEquals(TransferEvent.Progress(1, 5), cancellableViewModel.state.value.transfer)

            cancellableViewModel.cancelTransfer()

            assertEquals(
                UploadState.CANCELLED,
                (cancellableViewModel.state.value.transfer as TransferEvent.UploadStatus).operation.state,
            )
        }

    @Test
    fun `paused upload retry keeps authoritative session offset and double start is ignored`() =
        runTest(dispatcher) {
            val gate = CompletableDeferred<Unit>()
            val transfers =
                FakeTransfers { operation, attempt ->
                    if (attempt == 1) {
                        flow {
                            emit(
                                TransferEvent.UploadStatus(
                                    operation.copy(
                                        sessionId = "session",
                                        confirmedOffset = 4,
                                        state = UploadState.PAUSED,
                                    ),
                                    "Connection interrupted",
                                    canRetry = true,
                                ),
                            )
                            gate.await()
                        }
                    } else {
                        flowOf(TransferEvent.UploadCompleted(file("uploaded")))
                    }
                }
            val viewModel = FileBrowserViewModel(FakeFiles(), transfers)

            viewModel.startUpload("content://source", "video.mp4", 10, "video/mp4")
            viewModel.startUpload("content://other", "other.mp4", 10, "video/mp4")
            assertEquals(1, transfers.uploads.size)
            gate.complete(Unit)
            viewModel.retryTransfer()

            assertEquals("session", transfers.uploads.last().sessionId)
            assertEquals(4, transfers.uploads.last().confirmedOffset)
            assertEquals(transfers.uploads.first().idempotencyKey, transfers.uploads.last().idempotencyKey)
        }

    @Test
    fun `completed upload refreshes listing and a different selection starts a new identity`() =
        runTest(dispatcher) {
            val files = FakeFiles()
            val transfers = FakeTransfers()
            val viewModel = FileBrowserViewModel(files, transfers)
            val initialListCalls = files.listCalls

            viewModel.startUpload("content://first", "first.txt", 5, "text/plain")
            viewModel.startUpload("content://second", "second.txt", 6, "text/plain")

            assertEquals(initialListCalls + 2, files.listCalls)
            assertEquals(2, transfers.uploads.size)
            assertEquals(listOf("content://first", "content://second"), transfers.uploads.map { it.sourceUri })
            assertFalse(transfers.uploads.first().idempotencyKey == transfers.uploads.last().idempotencyKey)
            assertNull(transfers.uploads.last().sessionId)
            assertEquals(0, transfers.uploads.last().confirmedOffset)
        }

    @Test
    fun `rename validates input and refreshes list and selected detail after success`() =
        runTest(dispatcher) {
            val files = PlacementFiles(file("entry"))
            val viewModel = FileBrowserViewModel(files, FakeTransfers())
            val target =
                viewModel.state.value.entries
                    .single()
            viewModel.select(target)

            viewModel.beginRename(target)
            assertEquals(
                "entry.txt",
                viewModel.state.value.rename
                    ?.input,
            )
            viewModel.updateRenameInput("bad/name")
            viewModel.submitRename()
            assertEquals(
                ErrorCode.VALIDATION_FAILED,
                viewModel.state.value.rename
                    ?.error
                    ?.code,
            )
            assertEquals(0, files.renameCalls)

            viewModel.updateRenameInput("renamed.txt")
            viewModel.submitRename()

            assertEquals(1, files.renameCalls)
            assertNull(viewModel.state.value.rename)
            assertEquals(
                "renamed.txt",
                viewModel.state.value.entries
                    .single()
                    .name,
            )
            assertEquals(
                "renamed.txt",
                viewModel.state.value.selected
                    ?.name,
            )
            assertEquals("Renamed to renamed.txt.", viewModel.state.value.placementResult)
            assertEquals(2, files.detailCalls)
        }

    @Test
    fun `only an authoritatively displayed active file records recent history without duplicate recomposition calls`() =
        runTest(dispatcher) {
            val recent = FakeRecentFiles()
            val viewModel = FileBrowserViewModel(FakeFiles(), FakeTransfers(), recentFiles = recent)
            val active = file("opened")

            viewModel.select(active)
            viewModel.select(active)
            assertEquals(emptyList<String>(), recent.recorded)
            viewModel.detailDisplayed(checkNotNull(viewModel.state.value.selected))
            viewModel.detailDisplayed(checkNotNull(viewModel.state.value.selected))
            assertEquals(listOf(active.id), recent.recorded)
            assertEquals(
                active.id,
                viewModel.state.value.selected
                    ?.id,
            )

            viewModel.dismissDetail()
            viewModel.select(active)
            viewModel.detailDisplayed(checkNotNull(viewModel.state.value.selected))
            assertEquals(listOf(active.id, active.id), recent.recorded)

            viewModel.select(active.copy(entryType = FileEntryType.FOLDER))
            viewModel.detailDisplayed(checkNotNull(viewModel.state.value.selected))
            viewModel.select(active.copy(status = FileEntryStatus.MISSING))
            viewModel.detailDisplayed(checkNotNull(viewModel.state.value.selected))
            assertEquals(2, recent.recorded.size)
        }

    @Test
    fun `recent synchronization failure never hides an opened file`() =
        runTest(dispatcher) {
            val recent = FakeRecentFiles().apply { failure = KuraStorageException.Network(IOException("unknown")) }
            val viewModel = FileBrowserViewModel(FakeFiles(), FakeTransfers(), recentFiles = recent)

            viewModel.select(file("opened"))
            viewModel.detailDisplayed(checkNotNull(viewModel.state.value.selected))

            assertEquals(
                "opened",
                viewModel.state.value.selected
                    ?.id,
            )
            assertEquals(
                "File opened, but recent history could not be synchronized.",
                viewModel.state.value.historySyncError,
            )
        }

    @Test
    fun `rename conflict and unknown result stay explicit and offer refresh`() =
        runTest(dispatcher) {
            val files = PlacementFiles(file("entry"))
            val viewModel = FileBrowserViewModel(files, FakeTransfers())

            files.renameFailure = apiFailure(ErrorCode.FILE_NAME_CONFLICT, 409)
            viewModel.beginRename(
                viewModel.state.value.entries
                    .single(),
            )
            viewModel.updateRenameInput("conflict.txt")
            viewModel.submitRename()
            assertEquals(
                ErrorCode.FILE_NAME_CONFLICT,
                viewModel.state.value.rename
                    ?.error
                    ?.code,
            )
            assertFalse(
                checkNotNull(
                    viewModel.state.value.rename
                        ?.error,
                ).resultUnknown,
            )

            files.renameFailure = KuraStorageException.Network(IOException("connection lost"))
            viewModel.updateRenameInput("unknown.txt")
            viewModel.submitRename()
            assertEquals(
                true,
                viewModel.state.value.rename
                    ?.error
                    ?.resultUnknown,
            )
            val listCalls = files.listCalls

            viewModel.refreshAfterPlacementFailure()

            assertNull(viewModel.state.value.rename)
            assertEquals(listCalls + 1, files.listCalls)
        }

    @Test
    fun `move picker starts at root filters candidates pages and blocks target subtree`() =
        runTest(dispatcher) {
            val target = folder("target", "root", "Target")
            val destination = folder("destination", "root", "Destination")
            val files =
                PlacementFiles(
                    target,
                    pagedPicker = true,
                    rootItems = listOf(target, destination, file("plain")),
                )
            val viewModel = FileBrowserViewModel(files, FakeTransfers())

            viewModel.beginMove(target)
            var picker = checkNotNull(viewModel.state.value.movePicker)
            assertEquals("root", picker.currentFolderId)
            assertEquals("My files", picker.currentFolderName)
            assertEquals(listOf("target", "destination"), picker.folders.map { it.id })
            assertFalse(picker.canOpen(target))
            assertFalse(picker.canConfirm)

            val callsBeforeBlockedOpen = files.listCalls
            viewModel.openMoveFolder(target)
            assertEquals(
                "root",
                viewModel.state.value.movePicker
                    ?.currentFolderId,
            )
            assertEquals(callsBeforeBlockedOpen, files.listCalls)

            viewModel.loadMoreMoveFolders()
            picker = checkNotNull(viewModel.state.value.movePicker)
            assertEquals(listOf("target", "destination", "archive"), picker.folders.map { it.id })

            viewModel.openMoveFolder(destination)
            picker = checkNotNull(viewModel.state.value.movePicker)
            assertEquals("destination", picker.currentFolderId)
            assertEquals("Destination", picker.currentFolderName)
            assertEquals(true, picker.canGoBack)
            assertEquals(true, picker.canConfirm)

            viewModel.backMoveFolder()
            assertEquals(
                "root",
                viewModel.state.value.movePicker
                    ?.currentFolderId,
            )
        }

    @Test
    fun `move success refreshes origin and detail while server errors remain authoritative`() =
        runTest(dispatcher) {
            val target = file("entry")
            val destination = folder("destination", "root", "Destination")
            val files = PlacementFiles(target, rootItems = listOf(target, destination))
            val viewModel = FileBrowserViewModel(files, FakeTransfers())
            viewModel.select(target)
            viewModel.beginMove(target)
            viewModel.openMoveFolder(destination)
            viewModel.confirmMove()

            assertEquals(listOf("destination"), files.moveDestinations)
            assertNull(viewModel.state.value.movePicker)
            assertEquals(
                "destination",
                viewModel.state.value.selected
                    ?.parentId,
            )
            assertEquals("Moved entry.txt.", viewModel.state.value.placementResult)

            listOf(
                ErrorCode.FILE_MOVE_CYCLE to 409,
                ErrorCode.FILE_NOT_FOUND to 404,
                ErrorCode.STORAGE_UNAVAILABLE to 503,
                ErrorCode.RECOVERY_REQUIRED to 409,
            ).forEach { (code, status) ->
                files.moveFailure = apiFailure(code, status)
                viewModel.beginMove(files.current.copy(parentId = "root"))
                viewModel.openMoveFolder(destination)
                viewModel.confirmMove()
                assertEquals(
                    code,
                    viewModel.state.value.movePicker
                        ?.error
                        ?.code,
                )
                assertEquals(
                    "destination",
                    viewModel.state.value.movePicker
                        ?.currentFolderId,
                )
                viewModel.dismissMove()
            }
        }

    @Test
    fun `permanent delete creates one key blocks duplicate and restore then refreshes authoritative list`() =
        runTest(dispatcher) {
            val target = file("trash").copy(status = FileEntryStatus.TRASHED)
            val gate = CompletableDeferred<Unit>()
            val files = PurgeFiles(target, gate)
            val viewModel =
                FileBrowserViewModel(
                    files,
                    FakeTransfers(),
                    trashMode = true,
                    idempotencyKeyFactory = { "key-1" },
                )

            viewModel.beginPermanentDelete(target)
            viewModel.confirmPermanentDelete()
            viewModel.confirmPermanentDelete()
            viewModel.restore(target)

            assertEquals(listOf("key-1"), files.purgeKeys)
            assertEquals(0, files.restoreCalls)
            assertEquals(
                true,
                viewModel.state.value.permanentDelete
                    ?.submitting,
            )
            gate.complete(Unit)

            assertNull(viewModel.state.value.permanentDelete)
            assertEquals(emptyList<FileEntry>(), viewModel.state.value.entries)
            assertEquals("Deleted permanently.", viewModel.state.value.placementResult)
        }

    @Test
    fun `cancelling unsent permanent delete discards its key`() =
        runTest(dispatcher) {
            val target = file("trash").copy(status = FileEntryStatus.TRASHED)
            val files = PurgeFiles(target)
            val keys = listOf("discarded-key", "replacement-key").iterator()
            val viewModel =
                FileBrowserViewModel(
                    files,
                    FakeTransfers(),
                    trashMode = true,
                    idempotencyKeyFactory = { keys.next() },
                )

            viewModel.beginPermanentDelete(target)
            assertEquals(
                "discarded-key",
                viewModel.state.value.permanentDelete
                    ?.idempotencyKey,
            )
            viewModel.cancelPermanentDelete()
            assertNull(viewModel.state.value.permanentDelete)
            assertEquals(emptyList<String>(), files.purgeKeys)

            viewModel.beginPermanentDelete(target)
            assertEquals(
                "replacement-key",
                viewModel.state.value.permanentDelete
                    ?.idempotencyKey,
            )
        }

    @Test
    fun `unknown permanent delete keeps item and same key until refresh or retry confirms result`() =
        runTest(dispatcher) {
            val target = file("trash").copy(status = FileEntryStatus.TRASHED)
            val files = PurgeFiles(target).apply { purgeFailure = KuraStorageException.Network(IOException("lost")) }
            val viewModel =
                FileBrowserViewModel(
                    files,
                    FakeTransfers(),
                    trashMode = true,
                    idempotencyKeyFactory = { "stable-key" },
                )

            viewModel.beginPermanentDelete(target)
            viewModel.confirmPermanentDelete()

            assertEquals(
                true,
                viewModel.state.value.permanentDelete
                    ?.resultUnknown,
            )
            assertEquals(listOf(target), viewModel.state.value.entries)
            viewModel.cancelPermanentDelete()
            assertEquals(
                "stable-key",
                viewModel.state.value.permanentDelete
                    ?.idempotencyKey,
            )
            files.purgeFailure = null
            viewModel.confirmPermanentDelete()

            assertEquals(listOf("stable-key", "stable-key"), files.purgeKeys)
            assertNull(viewModel.state.value.permanentDelete)
        }

    @Test
    fun `permanent delete maps authoritative errors and retention uses server UTC deadline`() =
        runTest(dispatcher) {
            val deadline = Instant.parse("2026-08-21T00:00:00Z")
            val target = file("trash").copy(status = FileEntryStatus.TRASHED, purgeEligibleAt = deadline)
            val files = PurgeFiles(target)
            val viewModel =
                FileBrowserViewModel(
                    files,
                    FakeTransfers(),
                    trashMode = true,
                    clock = Clock.fixed(deadline, ZoneOffset.UTC),
                    zoneId = ZoneOffset.ofHours(10),
                    idempotencyKeyFactory = { "key" },
                )
            viewModel.select(target)
            assertEquals(
                RetentionStage.DEADLINE_REACHED,
                viewModel.state.value.retention
                    ?.stage,
            )
            assertEquals(
                true,
                viewModel.state.value.retention
                    ?.text
                    ?.contains("AEST") == true ||
                    viewModel.state.value.retention
                        ?.text
                        ?.contains("+10:00") == true,
            )

            listOf(
                ErrorCode.FILE_NOT_FOUND to 404,
                ErrorCode.IDEMPOTENCY_CONFLICT to 409,
                ErrorCode.RECOVERY_REQUIRED to 409,
                ErrorCode.STORAGE_UNAVAILABLE to 503,
            ).forEach { (code, status) ->
                files.purgeFailure = apiFailure(code, status)
                viewModel.beginPermanentDelete(target)
                viewModel.confirmPermanentDelete()
                assertEquals(
                    code,
                    viewModel.state.value.permanentDelete
                        ?.error
                        ?.code,
                )
                if (code == ErrorCode.FILE_NOT_FOUND) {
                    files.present = false
                    viewModel.refresh()
                    assertNull(viewModel.state.value.permanentDelete)
                    files.present = true
                    viewModel.refresh()
                } else {
                    viewModel.cancelPermanentDelete()
                }
            }
        }

    private open class FakeFiles(
        private val empty: Boolean = false,
        private var failNext: Boolean = false,
    ) : FileRepository {
        var listCalls = 0

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage {
            listCalls++
            if (failNext) {
                failNext = false
                throw KuraStorageException.Api(
                    ApiError(ErrorCode.STORAGE_UNAVAILABLE, "request-id", 503),
                )
            }
            val items = if (empty) emptyList() else listOf(file("file-$page"))
            return FilePage("root", items, page, 1, if (empty) 0 else 2)
        }

        override suspend fun detail(fileId: String) =
            if (fileId == "album") {
                folder("album", "root", "Family photos")
            } else {
                file(fileId)
            }

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ) = file("folder")

        override suspend fun rename(
            fileId: String,
            name: String,
        ) = file(fileId).copy(name = name)

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ) = file(fileId).copy(parentId = targetParentId)

        override suspend fun trash(fileId: String) = file(fileId)

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ) = FilePage(null, emptyList(), 1, 100, 0)

        override suspend fun restore(fileId: String) = file(fileId)
    }

    private class NavigationFiles : FakeFiles(empty = true) {
        val detailCalls = mutableListOf<String>()
        private val details = mutableMapOf<String, CompletableDeferred<FileEntry>>()
        private val pages = mutableMapOf<String, CompletableDeferred<FilePage>>()

        override suspend fun detail(fileId: String): FileEntry {
            detailCalls += fileId
            return withContext(NonCancellable) {
                details.getOrPut(fileId) { CompletableDeferred() }.await()
            }
        }

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage =
            if (parentId == null) {
                FilePage(null, emptyList(), 1, pageSize, 0)
            } else {
                pages.getOrPut(parentId) { CompletableDeferred() }.await()
            }

        fun complete(
            id: String,
            folder: FileEntry,
        ) {
            details.getOrPut(id) { CompletableDeferred() }.complete(folder)
            pages.getOrPut(id) { CompletableDeferred() }.complete(FilePage(id, emptyList(), 1, 50, 0))
        }

        fun fail(
            id: String,
            folder: FileEntry,
        ) {
            details.getOrPut(id) { CompletableDeferred() }.complete(folder)
            pages.getOrPut(id) { CompletableDeferred() }.completeExceptionally(
                KuraStorageException.Api(ApiError(ErrorCode.STORAGE_UNAVAILABLE, "navigation", 503)),
            )
        }
    }

    private class FakeTransfers(
        private val uploadResult: (UploadOperation, Int) -> Flow<TransferEvent> = { _, _ ->
            flowOf(TransferEvent.UploadCompleted(file("uploaded")))
        },
    ) : TransferRepository {
        val uploads = mutableListOf<UploadOperation>()
        val cancellations = mutableListOf<UploadOperation>()
        private var newUploadCount = 0

        override fun newUpload(
            sourceUri: String,
            destinationFolderId: String,
            fileName: String,
            size: Long,
            contentType: String?,
        ): UploadOperation {
            newUploadCount++
            return UploadOperation(
                sourceUri,
                destinationFolderId,
                fileName,
                size,
                contentType,
                idempotencyKey = "key-$newUploadCount",
            )
        }

        override fun upload(operation: UploadOperation): Flow<TransferEvent> {
            uploads += operation
            return uploadResult(operation, uploads.size)
        }

        override fun download(operation: DownloadOperation): Flow<TransferEvent> =
            flowOf(TransferEvent.DownloadCompleted(operation.destinationUri))

        override suspend fun cancelUpload(operation: UploadOperation) {
            cancellations += operation
        }

        override fun openDownloadedFile(
            destinationUri: String,
            mimeType: String?,
        ): Intent = error("unused")
    }

    private class FakeRecentFiles : RecentFileRepository {
        val recorded = mutableListOf<String>()
        var failure: Throwable? = null

        override suspend fun list(
            page: Int,
            pageSize: Int,
        ) = RecentFilePage(emptyList(), page, pageSize, 0)

        override suspend fun record(fileId: String): RecentRecordOutcome {
            recorded += fileId
            failure?.let { throw it }
            return RecentRecordOutcome.Confirmed
        }
    }

    private class MissingFiles(
        initial: FileEntry,
        private val recheckGate: CompletableDeferred<Unit>? = null,
    ) : FileRepository {
        var target = initial
        var present = true
        var listCalls = 0
        var recheckCalls = 0
        var deleteCalls = 0
        var deleteFailure: Throwable? = null

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage {
            listCalls++
            return FilePage("root", if (present) listOf(target) else emptyList(), 1, 100, if (present) 1 else 0)
        }

        override suspend fun detail(fileId: String) = target

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ) = target

        override suspend fun rename(
            fileId: String,
            name: String,
        ) = target

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ) = target

        override suspend fun trash(fileId: String) = target

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ) = FilePage(null, emptyList(), 1, 100, 0)

        override suspend fun restore(fileId: String) = target

        override suspend fun recheckMissing(fileId: String): FileEntry {
            recheckCalls++
            recheckGate?.await()
            return target
                .copy(status = FileEntryStatus.ACTIVE, missingDetectedAt = null, missingLastCheckedAt = null)
                .also { target = it }
        }

        override suspend fun deleteMissingIndexEntry(fileId: String) {
            deleteCalls++
            deleteFailure?.let { throw it }
            present = false
        }
    }

    private class PurgeFiles(
        private val target: FileEntry,
        private val gate: CompletableDeferred<Unit>? = null,
    ) : FileRepository {
        var present = true
        var restoreCalls = 0
        var purgeFailure: Throwable? = null
        val purgeKeys = mutableListOf<String>()

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ) = page()

        override suspend fun detail(fileId: String) = target

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ) = target

        override suspend fun rename(
            fileId: String,
            name: String,
        ) = target

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ) = target

        override suspend fun trash(fileId: String) = target

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ) = page()

        override suspend fun restore(fileId: String): FileEntry {
            restoreCalls++
            return target
        }

        override suspend fun purge(
            fileId: String,
            idempotencyKey: String,
        ) {
            purgeKeys += idempotencyKey
            gate?.await()
            purgeFailure?.let { throw it }
            present = false
        }

        private fun page() = FilePage(null, if (present) listOf(target) else emptyList(), 1, 100, if (present) 1 else 0)
    }

    private class PlacementFiles(
        initial: FileEntry,
        private val pagedPicker: Boolean = false,
        private val rootItems: List<FileEntry> = listOf(initial),
    ) : FileRepository {
        var current = initial
        var renameFailure: Throwable? = null
        var moveFailure: Throwable? = null
        var renameCalls = 0
        var detailCalls = 0
        var listCalls = 0
        val moveDestinations = mutableListOf<String>()

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage {
            listCalls += 1
            val updatedItems = rootItems.map { if (it.id == current.id) current else it }
            return when {
                parentId == "destination" ->
                    FilePage("destination", listOf(folder("nested", "destination", "Nested")), 1, 100, 1)
                !pagedPicker -> FilePage("root", updatedItems, 1, 100, updatedItems.size.toLong())
                page == 1 -> FilePage("root", updatedItems.take(2), 1, 2, 3)
                else -> FilePage("root", listOf(folder("archive", "root", "Archive")), 2, 2, 3)
            }
        }

        override suspend fun detail(fileId: String): FileEntry {
            detailCalls += 1
            return current
        }

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ) = folder("created", parentId, name)

        override suspend fun rename(
            fileId: String,
            name: String,
        ): FileEntry {
            renameCalls += 1
            renameFailure?.let { throw it }
            return current.copy(name = name).also { current = it }
        }

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ): FileEntry {
            moveDestinations += targetParentId
            moveFailure?.let { throw it }
            return current.copy(parentId = targetParentId).also { current = it }
        }

        override suspend fun trash(fileId: String) = current

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ) = FilePage(null, emptyList(), 1, 100, 0)

        override suspend fun restore(fileId: String) = current
    }

    private companion object {
        fun file(id: String) =
            FileEntry(
                id,
                "root",
                "$id.txt",
                FileEntryType.FILE,
                "text/plain",
                1,
                FileEntryStatus.ACTIVE,
                1,
                null,
                Instant.EPOCH,
                Instant.EPOCH,
                owner = OwnerSummary("owner", "Owner"),
                permission = SharePermission.MANAGER,
                permissionSource = PermissionSource.OWNER,
            )

        fun folder(
            id: String,
            parentId: String?,
            name: String,
        ) = FileEntry(
            id,
            parentId,
            name,
            FileEntryType.FOLDER,
            null,
            0,
            FileEntryStatus.ACTIVE,
            1,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
            owner = OwnerSummary("owner", "Owner"),
            permission = SharePermission.MANAGER,
            permissionSource = PermissionSource.OWNER,
        )

        fun apiFailure(
            code: ErrorCode,
            status: Int,
        ) = KuraStorageException.Api(ApiError(code, "request-id", status))
    }
}
