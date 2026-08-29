package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.OwnerSummaryDto
import com.kurastorage.core.network.RecentFileItemDto
import com.kurastorage.core.network.RecentFilePageDto
import com.kurastorage.core.network.SearchApi
import com.kurastorage.core.network.SearchPageDto
import com.kurastorage.core.network.SearchRequestDto
import com.kurastorage.core.network.SearchResultItemDto
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.time.Instant

class SearchRepositoryTest {
    @Test
    fun `search maps every filter and retries the same request after 401`() =
        runTest {
            val api = FakeSearchApi().apply { unauthorizedSearchOnce = true }
            val auth = FakeAuth()
            val repository = DefaultSearchRepository(api, AuthenticatedRequestExecutor(auth))
            val input =
                SearchInput(
                    query = " Report ",
                    entryType = FileEntryType.FILE,
                    fileCategory = SearchFileCategory.DOCUMENT,
                    status = FileEntryStatus.MISSING_CANDIDATE,
                    updatedFrom = Instant.parse("2026-08-01T00:00:00Z"),
                    updatedTo = Instant.parse("2026-08-25T00:00:00Z"),
                    minSize = 1,
                    maxSize = 999,
                    ownerUserId = OWNER,
                    shareTargetId = TARGET,
                    tagIds = listOf(ID, ID_2),
                    page = 2,
                    pageSize = 50,
                )

            val page = repository.search(input)

            assertEquals(listOf("token", "refreshed"), api.searchTokens)
            assertEquals(2, api.searchRequests.size)
            assertEquals(api.searchRequests[0], api.searchRequests[1])
            assertEquals("report", api.searchRequests.first().query)
            assertEquals("DOCUMENT", api.searchRequests.first().fileCategory)
            assertEquals(listOf(ID, ID_2), api.searchRequests.first().tagIds)
            assertEquals(FileEntryStatus.MISSING_CANDIDATE, page.items.single().status)
            assertFalse(
                page.items
                    .single()
                    .capabilities.canDownload,
            )
        }

    @Test
    fun `search pager fixes conditions and rejects duplicate server IDs`() =
        runTest {
            val api = FakeSearchApi()
            val repository = DefaultSearchRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            val pager = SearchPager(repository, SearchInput(query = "report", pageSize = 1))

            assertEquals(listOf(ID), pager.refresh().items.map { it.id })
            assertEquals(listOf(ID, ID_2), pager.loadNext().items.map { it.id })
            assertEquals(listOf(1, 2), api.searchRequests.map { it.page })

            api.duplicateSecondPage = true
            val duplicatePager = SearchPager(repository, SearchInput(query = "report", pageSize = 1))
            duplicatePager.refresh()
            assertTrue(
                runCatching { duplicatePager.loadNext() }.exceptionOrNull() is
                    KuraStorageException.InvalidServerResponse,
            )
        }

    @Test
    fun `invalid UUID time page and unknown metadata fail closed`() =
        runTest {
            val api = FakeSearchApi()
            val repository = DefaultSearchRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            api.item = item().copy(id = "not-a-uuid")
            assertInvalid { repository.search(SearchInput(query = "x")) }
            api.item = item().copy(updatedAt = "not-time")
            assertInvalid { repository.search(SearchInput(query = "x")) }
            api.item = item().copy(permission = "FUTURE")
            assertInvalid { repository.search(SearchInput(query = "x")) }
            api.item = item().copy(permission = "MANAGER", permissionSource = "OWNER", shareTargetId = null)
            assertInvalid { repository.search(SearchInput(query = "x")) }
            api.item = item().copy(permission = "OWNER", permissionSource = "DIRECT")
            assertInvalid { repository.search(SearchInput(query = "x")) }
            api.item = item()
            api.invalidPage = true
            assertInvalid { repository.search(SearchInput(query = "x")) }
        }

    @Test
    fun `all category status permission and source metadata map through the existing models`() =
        runTest {
            val api = FakeSearchApi()
            val repository = DefaultSearchRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            listOf("IMAGE", "VIDEO", "AUDIO", "DOCUMENT", "ARCHIVE", "OTHER").forEach { category ->
                api.item = item().copy(fileCategory = category)
                assertEquals(
                    category,
                    repository
                        .search(SearchInput(query = "x"))
                        .items
                        .single()
                        .fileCategory
                        ?.name,
                )
            }
            listOf("ACTIVE", "MISSING_CANDIDATE", "MISSING").forEach { status ->
                api.item = item().copy(status = status)
                assertEquals(
                    status,
                    repository
                        .search(SearchInput(query = "x"))
                        .items
                        .single()
                        .status.name,
                )
            }
            listOf("VIEWER", "CONTRIBUTOR", "EDITOR", "MANAGER").forEach { permission ->
                api.item = item().copy(permission = permission)
                assertEquals(
                    permission,
                    repository
                        .search(SearchInput(query = "x"))
                        .items
                        .single()
                        .permission.name,
                )
            }
            listOf("DIRECT", "INHERITED").forEach { source ->
                api.item = item().copy(permissionSource = source, shareTargetId = TARGET)
                assertEquals(
                    source,
                    repository
                        .search(SearchInput(query = "x"))
                        .items
                        .single()
                        .permissionSource.name,
                )
            }
        }

    @Test
    fun `owner permission is normalized to the existing manager capability model`() =
        runTest {
            val api = FakeSearchApi()
            val repository = DefaultSearchRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            api.item = item().copy(permission = "OWNER", permissionSource = "OWNER", shareTargetId = null)
            assertEquals(
                "MANAGER",
                repository
                    .search(SearchInput(query = "x"))
                    .items
                    .single()
                    .permission.name,
            )
            assertEquals(
                "OWNER",
                repository
                    .search(SearchInput(query = "x"))
                    .items
                    .single()
                    .permissionSource.name,
            )
        }

    @Test
    fun `invalid local search input never reaches the network`() =
        runTest {
            val api = FakeSearchApi()
            val repository = DefaultSearchRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            assertTrue(runCatching { repository.search(SearchInput()) }.isFailure)
            assertTrue(runCatching { repository.search(SearchInput(query = "x".repeat(201))) }.isFailure)
            assertTrue(api.searchRequests.isEmpty())
        }

    @Test
    fun `recent PUT retries idempotently and reconciles an unknown result with GET`() =
        runTest {
            val api = FakeSearchApi().apply { unauthorizedRecordOnce = true }
            val auth = FakeAuth()
            val repository = DefaultRecentFileRepository(api, AuthenticatedRequestExecutor(auth))

            assertEquals(RecentRecordOutcome.Confirmed, repository.record(ID))
            assertEquals(listOf(ID, ID), api.recordIds)
            assertEquals(listOf("token", "refreshed"), api.recordTokens)

            api.recordFailure = KuraStorageException.Network(IOException("result unknown"))
            val reconciled = repository.record(ID) as RecentRecordOutcome.Reconciled
            assertEquals(
                ID,
                reconciled.page.items
                    .single()
                    .id,
            )
            assertEquals(1, api.recentCalls)
        }

    private suspend fun assertInvalid(block: suspend () -> Unit) {
        assertTrue(runCatching { block() }.exceptionOrNull() is KuraStorageException.InvalidServerResponse)
    }

    private class FakeSearchApi : SearchApi {
        var item = item()
        val searchTokens = mutableListOf<String>()
        val searchRequests = mutableListOf<SearchRequestDto>()
        var unauthorizedSearchOnce = false
        var duplicateSecondPage = false
        var invalidPage = false
        val recordIds = mutableListOf<String>()
        val recordTokens = mutableListOf<String>()
        var unauthorizedRecordOnce = false
        var recordFailure: Throwable? = null
        var recentCalls = 0

        override suspend fun search(
            accessToken: String,
            request: SearchRequestDto,
        ): NetworkCallResult<SearchPageDto> {
            searchTokens += accessToken
            searchRequests += request
            if (unauthorizedSearchOnce && searchTokens.size == 1) return NetworkCallResult.Unauthorized
            val responseItem =
                when {
                    request.page == 2 && duplicateSecondPage -> item
                    request.page == 2 -> item.copy(id = ID_2)
                    else -> item
                }
            val totalCount = if (request.pageSize == 50) 51 else 2
            return NetworkCallResult.Success(
                SearchPageDto(
                    listOf(responseItem),
                    if (invalidPage) request.page + 1 else request.page,
                    request.pageSize,
                    totalCount,
                ),
            )
        }

        override suspend fun listRecentFiles(
            accessToken: String,
            page: Int,
            pageSize: Int,
        ): NetworkCallResult<RecentFilePageDto> {
            recentCalls++
            return NetworkCallResult.Success(
                RecentFilePageDto(listOf(recentItem()), page, pageSize, 1),
            )
        }

        override suspend fun recordRecentFile(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<Unit> {
            recordTokens += accessToken
            recordIds += fileId
            recordFailure?.let { throw it }
            if (unauthorizedRecordOnce && recordTokens.size == 1) return NetworkCallResult.Unauthorized
            return NetworkCallResult.Success(Unit)
        }
    }

    private class FakeAuth : AuthenticationRepository {
        var refreshCalls = 0

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ): AuthSession = error("unused")

        override suspend fun login(
            username: String,
            password: String,
        ): AuthSession = error("unused")

        override suspend fun refresh(): AuthSession = session("token")

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession =
            session("refreshed").also { refreshCalls++ }

        override suspend fun logout() = Unit

        override fun accessToken(): String = "token"

        override fun role(): UserRole = UserRole.MEMBER
    }

    private companion object {
        const val ID = "00000000-0000-4000-8000-000000000001"
        const val ID_2 = "00000000-0000-4000-8000-000000000002"
        const val OWNER = "00000000-0000-4000-8000-000000000003"
        const val TARGET = "00000000-0000-4000-8000-000000000004"
        const val TIME = "2026-08-25T00:00:00Z"

        fun item() =
            SearchResultItemDto(
                ID,
                "FILE",
                "report.pdf",
                "application/pdf",
                "DOCUMENT",
                20,
                "MISSING_CANDIDATE",
                TIME,
                OwnerSummaryDto(OWNER, "Owner"),
                "VIEWER",
                "DIRECT",
                TARGET,
            )

        fun recentItem() =
            RecentFileItemDto(
                ID,
                "FILE",
                "report.pdf",
                "application/pdf",
                "DOCUMENT",
                20,
                "ACTIVE",
                TIME,
                OwnerSummaryDto(OWNER, "Owner"),
                "VIEWER",
                "DIRECT",
                TARGET,
                TIME,
            )

        fun session(token: String) =
            AuthSession(
                com.kurastorage.core.model
                    .DeviceId(ID),
                token,
                "refresh-token-with-more-than-thirty-two-characters",
                Instant.MAX,
                Instant.MAX,
                UserRole.MEMBER,
            )
    }
}
