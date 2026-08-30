package com.kurastorage.feature.media.pdf

import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.TemporaryPdfStore
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class PdfViewerViewModelTest {
    @get:Rule val temporary = TemporaryFolder()
    private val dispatcher = UnconfinedTestDispatcher()

    @Before
    fun setUp() = Dispatchers.setMain(dispatcher)

    @After
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `exact size limit confirms but larger PDF fails before content`() =
        runTest(dispatcher) {
            val file = pdf()
            val exactRepository = MetadataRepository(TemporaryPdfStore.MAX_FILE_BYTES)
            val exactStore = TemporaryPdfStore(temporary.newFolder("exact"), "scope", exactRepository)
            val exact = PdfViewerViewModel(file.id, FakeFiles(file), exactRepository, exactStore)

            assertEquals(PdfLoadState.CONFIRMING, exact.state.value.loadState)
            assertEquals(0, exactRepository.contentRequests)

            val largeRepository = MetadataRepository(TemporaryPdfStore.MAX_FILE_BYTES + 1)
            val largeStore = TemporaryPdfStore(temporary.newFolder("large"), "scope", largeRepository)
            val large = PdfViewerViewModel(file.id, FakeFiles(file), largeRepository, largeStore)

            assertEquals(PdfLoadState.FAILED, large.state.value.loadState)
            assertEquals(0, largeRepository.contentRequests)
        }

    private class MetadataRepository(
        private val size: Long,
    ) : MediaRepository {
        var contentRequests = 0

        @Suppress("MaxLineLength")
        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(size), "application/pdf", true)

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            contentRequests++
            error("not used")
        }
    }

    private class FakeFiles(
        private val file: FileEntry,
    ) : FileRepository {
        override suspend fun detail(fileId: String) = file

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ) = FilePage(parentId, listOf(file), page, pageSize, 1L)

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

    private fun pdf() =
        FileEntry(
            "pdf",
            null,
            "report.pdf",
            FileEntryType.FILE,
            "application/pdf",
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
}
