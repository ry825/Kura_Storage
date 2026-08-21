package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.AdminStorageApi
import com.kurastorage.core.network.AdminStorageStatusDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.TrashPurgeRunSummaryDto
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.Instant

class AdminStorageRepositoryTest {
    @Test
    fun `admin maps exact bytes and latest run while member makes no request`() =
        runTest {
            val api = FakeAdminApi()
            val adminAuth = FakeAuth(UserRole.ADMIN)
            val admin = DefaultAdminStorageRepository(api, AuthenticatedRequestExecutor(adminAuth), adminAuth)

            val status = checkNotNull(admin.get())

            assertEquals(10_737_418_240L, status.capacityWarningThresholdBytes)
            assertEquals(4096L, status.lastPurgeRun?.releasedBytes)
            assertEquals(1, api.calls)

            val memberAuth = FakeAuth(UserRole.MEMBER)
            val member = DefaultAdminStorageRepository(api, AuthenticatedRequestExecutor(memberAuth), memberAuth)
            assertNull(member.get())
            assertEquals(1, api.calls)
        }

    private class FakeAdminApi : AdminStorageApi {
        var calls = 0

        override suspend fun getAdminStorage(accessToken: String): NetworkCallResult<AdminStorageStatusDto> {
            calls++
            return NetworkCallResult.Success(
                AdminStorageStatusDto(
                    storage = "AVAILABLE",
                    totalBytes = 1000,
                    availableBytes = 100,
                    capacityWarningThresholdBytes = 10_737_418_240,
                    capacityWarning = true,
                    trashBytes = 4096,
                    expiredTrashRootCount = 1,
                    retentionDays = 30,
                    recoveryRequiredPurgeCount = 0,
                    lastPurgeRun =
                        TrashPurgeRunSummaryDto(
                            startedAt = "2026-08-20T00:00:00Z",
                            completedAt = "2026-08-20T00:00:01Z",
                            status = "COMPLETED",
                            examinedRootCount = 1,
                            deletedRootCount = 1,
                            releasedBytes = 4096,
                            errorCount = 0,
                        ),
                ),
            )
        }
    }

    private class FakeAuth(
        private val userRole: UserRole,
    ) : AuthenticationRepository {
        private val session = AuthSession(DeviceId("device"), "token", "refresh", Instant.MAX, Instant.MAX, userRole)

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

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String) = session

        override suspend fun logout() = Unit

        override fun accessToken() = "token"

        override fun role() = userRole
    }
}
