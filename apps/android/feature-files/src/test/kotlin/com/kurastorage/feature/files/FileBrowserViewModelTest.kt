package com.kurastorage.feature.files

import android.content.Intent
import com.kurastorage.core.data.FileRepository
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
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test
import java.io.IOException
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
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

            assertNull(cancellableViewModel.state.value.transfer)
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
            assertEquals(1, files.detailCalls)
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

    private class FakeFiles(
        private val empty: Boolean = false,
        private var failNext: Boolean = false,
    ) : FileRepository {
        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage {
            if (failNext) {
                failNext = false
                throw KuraStorageException.Api(
                    ApiError(ErrorCode.STORAGE_UNAVAILABLE, "request-id", 503),
                )
            }
            val items = if (empty) emptyList() else listOf(file("file-$page"))
            return FilePage("root", items, page, 1, if (empty) 0 else 2)
        }

        override suspend fun detail(fileId: String) = file(fileId)

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

    private class FakeTransfers(
        private val uploadResult: (UploadOperation, Int) -> Flow<TransferEvent> = { _, _ ->
            flowOf(TransferEvent.UploadCompleted(file("uploaded")))
        },
    ) : TransferRepository {
        val uploads = mutableListOf<UploadOperation>()

        override fun newUpload(
            sourceUri: String,
            destinationFolderId: String,
            fileName: String,
            size: Long,
            contentType: String?,
        ) = UploadOperation(sourceUri, destinationFolderId, fileName, size, contentType, idempotencyKey = "key")

        override fun upload(operation: UploadOperation): Flow<TransferEvent> {
            uploads += operation
            return uploadResult(operation, uploads.size)
        }

        override fun download(operation: DownloadOperation): Flow<TransferEvent> =
            flowOf(TransferEvent.DownloadCompleted(operation.destinationUri))

        override fun openDownloadedFile(
            destinationUri: String,
            mimeType: String?,
        ): Intent = error("unused")
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
        )

        fun apiFailure(
            code: ErrorCode,
            status: Int,
        ) = KuraStorageException.Api(ApiError(code, "request-id", status))
    }
}
