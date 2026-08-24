package com.kurastorage.core.data

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ShareScope
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.network.CreateShareRequestDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.OwnerSummaryDto
import com.kurastorage.core.network.SetShareMemberRequestDto
import com.kurastorage.core.network.ShareCandidateDto
import com.kurastorage.core.network.ShareItemDto
import com.kurastorage.core.network.ShareMemberDto
import com.kurastorage.core.network.SharePageDto
import com.kurastorage.core.network.SharingApi
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.time.Instant

class SharingRepositoryTest {
    @Test
    fun `maps all sharing models and pager deduplicates stable pages`() =
        runTest {
            val api = FakeSharingApi()
            val repository = DefaultSharingRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            val pager = SharePager { page -> repository.list(ShareScope.RECEIVED, FileEntryType.FOLDER, page, 1) }

            assertEquals(USER, repository.candidates().single().userId)
            assertEquals(SharePermission.MANAGER, repository.detail(SHARE).permission)
            assertEquals(listOf(SHARE), pager.refresh().items.map { it.id })
            assertEquals(listOf(SHARE, SHARE_2), pager.loadNext().items.map { it.id })
            assertEquals(listOf("received", "received"), api.scopes)
            assertEquals(listOf("FOLDER", "FOLDER"), api.targetTypes)
        }

    @Test
    fun `create and update preserve intent across one 401 refresh`() =
        runTest {
            val api = FakeSharingApi().apply { unauthorizedSetOnce = true }
            val auth = FakeAuth()
            val repository = DefaultSharingRepository(api, AuthenticatedRequestExecutor(auth))

            repository.create(TARGET, mapOf(USER to SharePermission.VIEWER))
            repository.setMember(SHARE, USER, SharePermission.EDITOR)

            assertEquals(listOf("EDITOR", "EDITOR"), api.setRequests.map { it.permission })
            assertEquals(listOf("token", "refreshed"), api.setTokens)
            assertEquals(1, auth.refreshCalls)
            assertEquals(
                "VIEWER",
                api.createRequest
                    ?.members
                    ?.single()
                    ?.permission,
            )
        }

    @Test
    fun `invalid UUID time and enum are never successful and unknown mutation refreshes detail`() =
        runTest {
            val api = FakeSharingApi()
            val repository = DefaultSharingRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            api.item = item().copy(id = "not-a-uuid")
            assertTrue(runCatching { repository.detail(SHARE) }.exceptionOrNull() is IllegalArgumentException)
            api.item = item().copy(updatedAt = "not-time")
            assertTrue(runCatching { repository.detail(SHARE) }.exceptionOrNull() is Exception)
            api.item = item().copy(permission = "FUTURE")
            assertEquals(SharePermission.UNKNOWN, repository.detail(SHARE).permission)

            api.item = item()
            api.setFailure = KuraStorageException.Network(IOException("unknown"))
            assertTrue(runCatching { repository.setMember(SHARE, USER, SharePermission.EDITOR) }.isFailure)
            assertEquals(4, api.detailCalls)

            api.setFailure = KuraStorageException.Api(ApiError(ErrorCode.SHARE_CONFLICT, "conflict", 409))
            assertTrue(runCatching { repository.setMember(SHARE, USER, SharePermission.EDITOR) }.isFailure)
            assertEquals(5, api.detailCalls)
        }

    private class FakeSharingApi : SharingApi {
        private val candidates = listOf(ShareCandidateDto(USER, "Alex"))
        var item = item()
        val scopes = mutableListOf<String>()
        val targetTypes = mutableListOf<String?>()
        var createRequest: CreateShareRequestDto? = null
        val setRequests = mutableListOf<SetShareMemberRequestDto>()
        val setTokens = mutableListOf<String>()
        var unauthorizedSetOnce = false
        var setFailure: Throwable? = null
        var detailCalls = 0

        override suspend fun listCandidates(accessToken: String) = NetworkCallResult.Success(candidates)

        override suspend fun createShare(
            accessToken: String,
            request: CreateShareRequestDto,
        ): NetworkCallResult<ShareItemDto> {
            createRequest = request
            return NetworkCallResult.Success(item)
        }

        override suspend fun listShares(
            accessToken: String,
            scope: String,
            targetType: String?,
            page: Int,
            pageSize: Int,
        ): NetworkCallResult<SharePageDto> {
            scopes += scope
            targetTypes += targetType
            val items = if (page == 1) listOf(item) else listOf(item, item().copy(id = SHARE_2))
            return NetworkCallResult.Success(SharePageDto(items, page, 1, 2))
        }

        override suspend fun getShare(
            accessToken: String,
            shareId: String,
        ): NetworkCallResult<ShareItemDto> {
            detailCalls++
            return NetworkCallResult.Success(item)
        }

        override suspend fun setMember(
            accessToken: String,
            shareId: String,
            userId: String,
            request: SetShareMemberRequestDto,
        ): NetworkCallResult<ShareItemDto> {
            setTokens += accessToken
            setRequests += request
            setFailure?.let { throw it }
            if (unauthorizedSetOnce && setTokens.size == 1) return NetworkCallResult.Unauthorized
            return NetworkCallResult.Success(item.copy(permission = request.permission))
        }

        override suspend fun removeMember(
            accessToken: String,
            shareId: String,
            userId: String,
        ) = NetworkCallResult.Success(Unit)

        override suspend fun deleteShare(
            accessToken: String,
            shareId: String,
        ) = NetworkCallResult.Success(Unit)
    }

    private class FakeAuth : AuthenticationRepository {
        var refreshCalls = 0
        private val session = AuthSession(DeviceId("device"), "token", "refresh", Instant.MAX, Instant.MAX)

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = session

        override suspend fun login(
            username: String,
            password: String,
        ) = session

        override suspend fun refresh() = session

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession {
            refreshCalls++
            return session.copy(accessToken = "refreshed")
        }

        override suspend fun logout() = Unit

        override fun accessToken() = "token"
    }

    private companion object {
        const val SHARE = "11111111-1111-1111-1111-111111111111"
        const val SHARE_2 = "11111111-1111-1111-1111-111111111112"
        const val TARGET = "22222222-2222-2222-2222-222222222222"
        const val USER = "33333333-3333-3333-3333-333333333333"
        const val OWNER = "44444444-4444-4444-4444-444444444444"

        fun item() =
            ShareItemDto(
                SHARE,
                TARGET,
                "FOLDER",
                "Photos",
                OwnerSummaryDto(OWNER, "Owner"),
                "MANAGER",
                listOf(ShareMemberDto(USER, "Alex", "VIEWER")),
                "2026-08-23T00:00:00Z",
                "2026-08-23T00:00:00Z",
            )
    }
}
