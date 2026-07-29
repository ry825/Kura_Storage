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
    }
}
