package com.kurastorage.feature.media.photo

import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaGeneratingException
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.NetworkTransport
import com.kurastorage.core.data.media.NetworkTransportSource
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.RegisteredWifiSource
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.QualityPreferences
import com.kurastorage.feature.media.MediaViewerController
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class PhotoViewerViewModelTest {
    private val dispatcher = UnconfinedTestDispatcher()

    @Before
    fun setUp() = Dispatchers.setMain(dispatcher)

    @After
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `adjacent navigation revalidates and skips unavailable candidates`() =
        runTest(dispatcher) {
            val first = file("first")
            val missing = file("missing").copy(status = FileEntryStatus.MISSING)
            val last = file("last")
            val files = FakeFiles(listOf(first, missing, last))
            val repository = FakeMediaRepository()
            val controller =
                MediaViewerController(
                    repository,
                    FakeQualityStore(),
                    NetworkQualityContextResolver(
                        NetworkTransportSource { NetworkTransport.CELLULAR },
                        RegisteredWifiSource { false },
                    ),
                    TransferConfirmationPolicy(repository),
                    ConnectionRoute.REMOTE_SECURE,
                    backgroundScope,
                )
            val viewModel =
                PhotoViewerViewModel(first.id, listOf(first.id, missing.id, last.id), files, controller)

            assertEquals(
                first.id,
                viewModel.state.value.file
                    ?.id,
            )
            assertEquals(MediaVariant.IMAGE_LOW, viewModel.requestTicket()?.source?.variant)
            assertEquals(1, viewModel.state.value.currentPosition)
            assertEquals(3, viewModel.state.value.totalCount)
            viewModel.next()
            assertEquals(
                last.id,
                viewModel.state.value.file
                    ?.id,
            )
            assertEquals(3, viewModel.state.value.currentPosition)
            viewModel.setZoom(10f)
            assertEquals(4f, viewModel.state.value.zoom)
        }

    @Test
    fun `unsupported and missing initial entries fail closed`() =
        runTest(dispatcher) {
            val unsupported = file("text").copy(mimeType = "text/plain")
            val unsupportedViewModel =
                PhotoViewerViewModel(
                    unsupported.id,
                    listOf(unsupported.id),
                    FakeFiles(listOf(unsupported)),
                    controller(backgroundScope),
                )
            assertEquals(
                com.kurastorage.core.model.media.MediaUiError.UNSUPPORTED,
                unsupportedViewModel.state.value.error,
            )

            val missingViewModel =
                PhotoViewerViewModel("absent", listOf("absent"), FakeFiles(emptyList()), controller(backgroundScope))
            assertEquals(com.kurastorage.core.model.media.MediaUiError.UNKNOWN, missingViewModel.state.value.error)
        }

    @Test
    fun `viewer delegates quality generation ready and failure states and navigates backward`() =
        runTest(dispatcher) {
            val first = file("first")
            val second = file("second")
            val viewModel =
                PhotoViewerViewModel(
                    second.id,
                    listOf(first.id, second.id),
                    FakeFiles(listOf(first, second)),
                    controller(backgroundScope),
                )
            assertTrue(viewModel.state.value.canGoPrevious)
            viewModel.previous()
            assertEquals(
                first.id,
                viewModel.state.value.file
                    ?.id,
            )
            viewModel.previous()
            assertFalse(viewModel.state.value.canGoPrevious)

            viewModel.selectQuality(MediaQuality.MEDIUM)
            val ticket = checkNotNull(viewModel.requestTicket())
            val generating = MediaJobSnapshot("job", MediaJobStatus.GENERATING, null, null, null, null, 1, false)
            viewModel.contentGenerating(ticket, MediaGeneratingException(generating))
            assertTrue(
                viewModel.state.value.media
                    ?.loadState is MediaLoadState.Generating,
            )
            viewModel.contentFailed(ticket)
            assertTrue(
                viewModel.state.value.media
                    ?.loadState is MediaLoadState.Failed,
            )

            viewModel.selectQuality(MediaQuality.ORIGINAL)
            val original = checkNotNull(viewModel.requestTicket())
            viewModel.contentReady(original)
            assertTrue(
                viewModel.state.value.media
                    ?.loadState is MediaLoadState.Ready,
            )
        }

    private class FakeFiles(
        entries: List<FileEntry>,
    ) : FileRepository {
        private val entries = entries.associateBy(FileEntry::id)

        override suspend fun detail(fileId: String) = checkNotNull(entries[fileId])

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ) = FilePage(parentId, entries.values.toList(), page, pageSize, entries.size.toLong())

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ): FileEntry = error("not used")

        override suspend fun rename(
            fileId: String,
            name: String,
        ): FileEntry = error("not used")

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ): FileEntry = error("not used")

        override suspend fun trash(fileId: String): FileEntry = error("not used")

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ): FilePage = error("not used")

        override suspend fun restore(fileId: String): FileEntry = error("not used")
    }

    private class FakeMediaRepository : MediaRepository {
        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(100), "image/jpeg", true)

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult = error("viewer fetches through Coil")
    }

    private class FakeQualityStore : QualityPreferenceStore {
        override suspend fun read() = QualityPreferences()

        override suspend fun update(
            context: com.kurastorage.core.model.media.NetworkQualityContext,
            quality: MediaQuality,
        ) = Unit
    }

    private fun controller(scope: kotlinx.coroutines.CoroutineScope): MediaViewerController {
        val repository = FakeMediaRepository()
        return MediaViewerController(
            repository,
            FakeQualityStore(),
            NetworkQualityContextResolver(
                NetworkTransportSource { NetworkTransport.CELLULAR },
                RegisteredWifiSource { false },
            ),
            TransferConfirmationPolicy(repository),
            ConnectionRoute.REMOTE_SECURE,
            scope,
        )
    }

    private fun file(id: String) =
        FileEntry(
            id,
            null,
            "$id.jpg",
            FileEntryType.FILE,
            "image/jpeg",
            100,
            FileEntryStatus.ACTIVE,
            1,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
            owner = OwnerSummary("owner", "Owner"),
            permission = SharePermission.MANAGER,
            permissionSource = PermissionSource.OWNER,
        )
}
