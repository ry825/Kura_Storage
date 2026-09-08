package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.AdminMediaDerivativeApi
import com.kurastorage.core.network.MediaDerivativeStatusDto
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.time.Instant

class AdminMediaDerivativeRepositoryTest {
    @Test
    fun `maps bounded aggregate without exposing entry identity`() =
        runTest {
            val api =
                FakeApi(
                    MediaDerivativeStatusDto(
                        1,
                        10,
                        7,
                        1,
                        1,
                        0,
                        0,
                        1,
                        12_345,
                        0,
                        2,
                        "2026-09-07T00:00:00Z",
                    ),
                )
            val status = DefaultAdminMediaDerivativeRepository(api, AuthenticatedRequestExecutor(FakeAuth())).get()

            assertEquals(10, status.photoCount)
            assertEquals(7, status.readyCount)
            assertEquals(0.7f, status.coverage)
            assertEquals(12_345, status.readyBytes)
            assertEquals(Instant.parse("2026-09-07T00:00:00Z"), status.observedAt)
        }

    @Test
    fun `rejects inconsistent counts`() {
        assertThrows(KuraStorageException.InvalidServerResponse::class.java) {
            MediaDerivativeStatusDto(
                1,
                1,
                1,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "invalid",
            ).toModel()
        }
    }

    private class FakeApi(
        private val value: MediaDerivativeStatusDto,
    ) : AdminMediaDerivativeApi {
        override suspend fun getMediaDerivatives(accessToken: String) = NetworkCallResult.Success(value)
    }

    private class FakeAuth : AuthenticationRepository {
        private val session =
            AuthSession(
                DeviceId("device"),
                "token",
                "refresh",
                Instant.MAX,
                Instant.MAX,
                UserRole.ADMIN,
            )

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

        override fun role() = UserRole.ADMIN

        override fun userId() = "00000000-0000-0000-0000-000000000001"

        override fun deviceId() = DeviceId("00000000-0000-0000-0000-000000000002")
    }
}
