package com.kurastorage.feature.search

import com.kurastorage.core.data.RecentFileRepository
import com.kurastorage.core.data.RecentRecordOutcome
import com.kurastorage.core.data.SearchRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.RecentFilePage
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.SearchPage
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class SearchViewModelsTest {
    private val dispatcher = StandardTestDispatcher()

    @Before
    fun setUp() = Dispatchers.setMain(dispatcher)

    @After
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `search is explicit validates input and ignores an obsolete response`() =
        runTest(dispatcher) {
            val repository = DeferredSearchRepository()
            val viewModel = SearchViewModel(repository, { fileEntry(it) })

            viewModel.updateInput(SearchInput(query = "first"))
            assertEquals(0, repository.requests.size)
            viewModel.search()
            runCurrent()
            assertTrue(viewModel.state.value.loading)

            viewModel.updateInput(SearchInput(query = "second"))
            viewModel.search()
            runCurrent()
            repository.complete("second", page(item(ID_2, "second")))
            repository.complete("first", page(item(ID, "first")))
            advanceUntilIdle()

            assertEquals(
                listOf(ID_2),
                viewModel.state.value.items
                    .map { it.id },
            )
            viewModel.updateInput(SearchInput())
            viewModel.search()
            assertTrue(viewModel.state.value.validationError != null)
            assertEquals(2, repository.requests.size)
        }

    @Test
    fun `search paging prevents duplicate loads and revalidates result selection`() =
        runTest(dispatcher) {
            val repository = PagingSearchRepository()
            var opened: String? = null
            val viewModel = SearchViewModel(repository, { fileEntry(it) })
            viewModel.updateInput(SearchInput(query = "report", pageSize = 1))
            viewModel.search()
            advanceUntilIdle()

            viewModel.loadMore()
            viewModel.loadMore()
            advanceUntilIdle()
            assertEquals(listOf(1, 2), repository.pages)

            viewModel.open(
                viewModel.state.value.items
                    .first(),
            ) { id, _ -> opened = id }
            advanceUntilIdle()
            assertEquals(ID, opened)
        }

    @Test
    fun `recent handles missing safely and refreshes after revoked detail`() =
        runTest(dispatcher) {
            val repository = FakeRecentRepository()
            var detailFailure = true
            val viewModel =
                RecentFilesViewModel(repository) { id ->
                    if (detailFailure) {
                        throw KuraStorageException.Api(ApiError(ErrorCode.FILE_NOT_FOUND, null, 404))
                    }
                    fileEntry(id)
                }
            advanceUntilIdle()
            val missing = recent(item(ID_2, "missing").copy(status = FileEntryStatus.MISSING))
            viewModel.open(missing) { _, _ -> error("MISSING must not open") }
            advanceUntilIdle()
            assertEquals(1, repository.listCalls)

            viewModel.open(
                viewModel.state.value.items
                    .first(),
            ) { _, _ -> error("revoked must not open") }
            advanceUntilIdle()
            assertEquals(2, repository.listCalls)

            detailFailure = false
            var opened: String? = null
            viewModel.open(
                viewModel.state.value.items
                    .first(),
            ) { id, _ -> opened = id }
            advanceUntilIdle()
            assertEquals(ID, opened)
        }

    private class DeferredSearchRepository : SearchRepository {
        val requests = mutableListOf<SearchInput>()
        private val responses = mutableMapOf<String, CompletableDeferred<SearchPage>>()

        override suspend fun search(input: SearchInput): SearchPage {
            requests += input
            val key = checkNotNull(input.query)
            val deferred = responses.getOrPut(key) { CompletableDeferred() }
            return try {
                deferred.await()
            } catch (_: kotlinx.coroutines.CancellationException) {
                withContext(NonCancellable) { deferred.await() }
            }
        }

        fun complete(
            key: String,
            page: SearchPage,
        ) {
            responses.getOrPut(key) { CompletableDeferred() }.complete(page)
        }
    }

    private class PagingSearchRepository : SearchRepository {
        val pages = mutableListOf<Int>()

        override suspend fun search(input: SearchInput): SearchPage {
            pages += input.page
            return SearchPage(listOf(item(if (input.page == 1) ID else ID_2, "item")), input.page, 1, 2)
        }
    }

    private class FakeRecentRepository : RecentFileRepository {
        var listCalls = 0

        override suspend fun list(
            page: Int,
            pageSize: Int,
        ): RecentFilePage {
            listCalls++
            return RecentFilePage(listOf(recent(item(ID, "recent"))), page, pageSize, 1)
        }

        override suspend fun record(fileId: String): RecentRecordOutcome = RecentRecordOutcome.Confirmed
    }

    private companion object {
        const val ID = "00000000-0000-4000-8000-000000000001"
        const val ID_2 = "00000000-0000-4000-8000-000000000002"
        const val OWNER = "00000000-0000-4000-8000-000000000003"
        val NOW: Instant = Instant.parse("2026-08-25T00:00:00Z")

        fun item(
            id: String,
            name: String,
        ) = SearchResultItem(
            id,
            FileEntryType.FILE,
            name,
            "application/pdf",
            SearchFileCategory.DOCUMENT,
            20,
            FileEntryStatus.ACTIVE,
            NOW,
            OwnerSummary(OWNER, "Owner"),
            SharePermission.VIEWER,
            PermissionSource.DIRECT,
            OWNER,
        )

        fun page(item: SearchResultItem) = SearchPage(listOf(item), 1, 50, 1)

        fun recent(item: SearchResultItem) = RecentFileItem(item, NOW)

        fun fileEntry(id: String) =
            FileEntry(
                id,
                null,
                "detail",
                FileEntryType.FILE,
                "application/pdf",
                20,
                FileEntryStatus.ACTIVE,
                1,
                null,
                NOW,
                NOW,
                owner = OwnerSummary(OWNER, "Owner"),
                permission = SharePermission.VIEWER,
                permissionSource = PermissionSource.DIRECT,
                shareTargetId = OWNER,
            )
    }
}
