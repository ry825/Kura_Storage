package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.model.media.MediaCleanupFailureCode
import com.kurastorage.core.model.media.MediaCleanupRunStatus
import com.kurastorage.core.network.AdminMediaCacheApi
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.media.AdminMediaCacheStatusDto
import com.kurastorage.core.network.media.MediaCleanupRunDto
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.time.Instant

class AdminMediaCacheRepositoryTest {
    @Test
    fun `strict mapping preserves unknown status but rejects inconsistent totals`() =
        runTest {
            val api =
                FakeApi().apply {
                    status = status().copy(lastCleanupRun = run().copy(status = "FUTURE", failureCode = "FUTURE"))
                }
            val repository = repository(api)
            assertEquals(MediaCleanupRunStatus.UNKNOWN, repository.get().lastCleanupRun?.status)
            assertEquals(MediaCleanupFailureCode.UNKNOWN, repository.get().lastCleanupRun?.failureCode)

            api.status = status().copy(cacheBytes = 11)
            assertTrue(runCatching { repository.get() }.exceptionOrNull() is KuraStorageException.InvalidServerResponse)
        }

    @Test
    fun `unknown network outcome retries the identical idempotency key`() =
        runTest {
            val api = FakeApi().apply { failFirstCleanup = true }
            val repository = repository(api)

            assertTrue(runCatching { repository.requestCleanup() }.exceptionOrNull() is KuraStorageException.Network)
            assertTrue(repository.hasUnknownCleanupOutcome())
            repository.requestCleanup()

            assertEquals(listOf(KEY, KEY), api.keys)
            assertFalse(repository.hasUnknownCleanupOutcome())
        }

    @Test
    fun `401 refreshes once and member request still reaches server authorization`() =
        runTest {
            val auth = FakeAuth(UserRole.MEMBER)
            val api = FakeApi().apply { unauthorizedOnce = true }
            val repository = DefaultAdminMediaCacheRepository(api, AuthenticatedRequestExecutor(auth)) { KEY }

            repository.get()

            assertEquals(2, api.statusCalls)
            assertEquals(1, auth.refreshCalls)
        }

    private fun repository(api: FakeApi) =
        DefaultAdminMediaCacheRepository(api, AuthenticatedRequestExecutor(FakeAuth(UserRole.ADMIN))) { KEY }

    private class FakeApi : AdminMediaCacheApi {
        var status = status()
        var statusCalls = 0
        var unauthorizedOnce = false
        var failFirstCleanup = false
        val keys = mutableListOf<String>()

        override suspend fun getMediaCache(accessToken: String): NetworkCallResult<AdminMediaCacheStatusDto> {
            statusCalls++
            if (unauthorizedOnce) {
                unauthorizedOnce = false
                return NetworkCallResult.Unauthorized
            }
            return NetworkCallResult.Success(status)
        }

        override suspend fun requestMediaCacheCleanup(
            accessToken: String,
            idempotencyKey: String,
        ): NetworkCallResult<MediaCleanupRunDto> {
            keys += idempotencyKey
            if (failFirstCleanup) {
                failFirstCleanup = false
                throw KuraStorageException.Network(IOException("unknown"))
            }
            return NetworkCallResult.Success(run())
        }
    }

    private class FakeAuth(
        private val role: UserRole,
    ) : AuthenticationRepository {
        var refreshCalls = 0
        private val session = AuthSession(DeviceId("device"), "token", "refresh", Instant.MAX, Instant.MAX, role)

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
            return session
        }

        override suspend fun logout() = Unit

        override fun accessToken() = "token"

        override fun role() = role
    }

    private companion object {
        const val KEY = "11111111-1111-1111-1111-111111111111"

        fun run() =
            MediaCleanupRunDto(
                "22222222-2222-2222-2222-222222222222",
                "MANUAL",
                "PENDING",
                "2026-09-04T00:00:00Z",
                examinedCount = 0,
                deletedCount = 0,
                releasedBytes = 0,
                failureCount = 0,
            )

        fun status() =
            AdminMediaCacheStatusDto(
                10,
                1,
                2,
                3,
                4,
                100,
                60,
                1,
                2,
                3,
                1,
                0,
                run(),
            )
    }
}
