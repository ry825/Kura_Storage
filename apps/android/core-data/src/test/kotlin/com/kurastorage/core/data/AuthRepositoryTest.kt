package com.kurastorage.core.data

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.AuthenticationApi
import com.kurastorage.core.network.LoginRequestDto
import com.kurastorage.core.network.LogoutRequestDto
import com.kurastorage.core.network.RefreshRequestDto
import com.kurastorage.core.network.RegisterDeviceRequestDto
import com.kurastorage.core.network.TokenResponseDto
import com.kurastorage.core.security.EncryptedTokenStore
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.concurrent.atomic.AtomicInteger

class AuthRepositoryTest {
    @Test
    fun `registration is allowed only on local direct`() =
        runTest {
            val fixture = Fixture()
            val result =
                fixture.repository.register(
                    ConnectionRoute.LOCAL_DIRECT,
                    "family",
                    "secret",
                    "Android",
                )
            assertEquals(DeviceId(DEVICE_ID), result.deviceId)
            assertEquals(USER_ID, result.userId)
            assertEquals(UserRole.ADMIN, result.role)
            assertEquals(UserRole.ADMIN, fixture.repository.role())

            runCatching {
                fixture.repository.register(
                    ConnectionRoute.REMOTE_SECURE,
                    "family",
                    "secret",
                    "Android",
                )
            }.onSuccess { error("Remote registration unexpectedly succeeded") }
        }

    @Test
    fun `concurrent 401 responses perform one refresh and retry each request once`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.repository.login("family", "secret")
            val executor = AuthenticatedRequestExecutor(fixture.repository)
            val invocations = AtomicInteger()

            val results =
                List(12) {
                    async {
                        executor.execute { token ->
                            invocations.incrementAndGet()
                            if (token == "access-1") {
                                AuthenticatedCallResult.Unauthorized
                            } else {
                                AuthenticatedCallResult.Success(token)
                            }
                        }
                    }
                }.awaitAll()

            assertEquals(List(12) { "access-2" }, results)
            assertEquals(1, fixture.api.refreshCount.get())
            assertEquals(24, invocations.get())
            assertEquals(UserRole.ADMIN, fixture.repository.role())
            assertEquals(UserRole.ADMIN, fixture.metadataStore.metadata?.role)
        }

    @Test
    fun `device revoked clears encrypted token and metadata`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.api.loginError =
                KuraStorageException.Api(ApiError(ErrorCode.DEVICE_REVOKED, "request-7", 403))

            runCatching { fixture.repository.login("family", "secret") }

            assertNull(fixture.tokenStore.token)
            assertNull(fixture.metadataStore.metadata)
        }

    @Test
    fun `logout clears role with encrypted token and metadata`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.repository.login("family", "secret")

            fixture.repository.logout()

            assertNull(fixture.repository.role())
            assertNull(fixture.tokenStore.token)
            assertNull(fixture.metadataStore.metadata)
        }

    @Test
    fun `keystore loss clears unusable credential metadata`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.tokenStore.readError = KuraStorageException.CredentialUnavailable()

            assertNull(fixture.repository.storedCredential())
            assertNull(fixture.metadataStore.metadata)
        }

    @Test
    fun `credential persistence failure clears partially written token`() =
        runTest {
            val fixture = Fixture()
            fixture.metadataStore.writeError = IllegalStateException("storage unavailable")

            runCatching {
                fixture.repository.register(
                    ConnectionRoute.LOCAL_DIRECT,
                    "family",
                    "secret",
                    "Android",
                )
            }

            assertNull(fixture.tokenStore.token)
            assertNull(fixture.metadataStore.metadata)
        }

    private class Fixture {
        val api = FakeAuthenticationApi()
        val metadataStore = FakeMetadataStore()
        val tokenStore = FakeTokenStore()
        val repository =
            DefaultAuthenticationRepository(
                api,
                metadataStore,
                tokenStore,
                Clock.fixed(Instant.parse("2026-07-26T00:00:00Z"), ZoneOffset.UTC),
            )

        fun seedCredential() {
            metadataStore.metadata =
                CredentialMetadata(
                    DeviceId(DEVICE_ID),
                    Instant.parse("2026-07-27T00:00:00Z"),
                    "family",
                    UserRole.ADMIN,
                )
            tokenStore.token = "refresh-0"
        }
    }

    private class FakeAuthenticationApi : AuthenticationApi {
        val refreshCount = AtomicInteger()
        var loginError: KuraStorageException.Api? = null

        override suspend fun registerDevice(request: RegisterDeviceRequestDto) = token("1")

        override suspend fun login(request: LoginRequestDto): TokenResponseDto {
            loginError?.let { throw it }
            return token("1")
        }

        override suspend fun refresh(request: RefreshRequestDto): TokenResponseDto {
            refreshCount.incrementAndGet()
            delay(20)
            return token("2")
        }

        override suspend fun logout(
            accessToken: String,
            request: LogoutRequestDto,
        ) = Unit
    }

    private class FakeMetadataStore : CredentialMetadataStore {
        var metadata: CredentialMetadata? = null
        var writeError: Throwable? = null

        override suspend fun read() = metadata

        override suspend fun write(metadata: CredentialMetadata) {
            writeError?.let { throw it }
            this.metadata = metadata
        }

        override suspend fun clear() {
            metadata = null
        }
    }

    private class FakeTokenStore : EncryptedTokenStore {
        var token: String? = null
        var readError: Throwable? = null

        override fun read(): String? {
            readError?.let { throw it }
            return token
        }

        override fun write(refreshToken: String) {
            token = refreshToken
        }

        override fun clear() {
            token = null
        }
    }

    private companion object {
        const val DEVICE_ID = "11111111-1111-1111-1111-111111111111"
        const val USER_ID = "22222222-2222-2222-2222-222222222222"

        fun token(suffix: String) =
            TokenResponseDto(
                userId = USER_ID,
                deviceId = DEVICE_ID,
                accessToken = "access-$suffix",
                refreshToken = "refresh-$suffix",
                accessTokenExpiresAt = "2026-07-26T01:00:00Z",
                refreshTokenExpiresAt = "2026-07-27T00:00:00Z",
                role = "ADMIN",
            )
    }
}
