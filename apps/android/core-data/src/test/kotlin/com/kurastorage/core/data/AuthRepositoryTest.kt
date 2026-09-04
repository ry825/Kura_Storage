package com.kurastorage.core.data

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DeviceRegistrationMetadata
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.SessionMetadata
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
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.concurrent.atomic.AtomicInteger

class AuthRepositoryTest {
    @Test
    fun `registration is local only and persists registration with session`() =
        runTest {
            val fixture = Fixture()

            val result = fixture.repository.register(ConnectionRoute.LOCAL_DIRECT, "family", "secret", "Android")

            assertEquals(DeviceId(DEVICE_ID), result.deviceId)
            assertEquals(registration(), fixture.metadataStore.registration)
            assertEquals(sessionMetadata(), fixture.metadataStore.session)
            assertEquals("refresh-1", fixture.tokenStore.token)
            assertEquals(1, fixture.api.registerCount)

            runCatching {
                fixture.repository.register(ConnectionRoute.REMOTE_SECURE, "family", "secret", "Android")
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
            assertEquals(UserRole.ADMIN, fixture.metadataStore.session?.role)
        }

    @Test
    fun `logout clears session secrets but retains registration`() =
        runTest {
            val fixture = authenticatedFixture()

            fixture.repository.logout()

            assertLoggedOutWithRegistration(fixture)
            assertEquals(DEVICE_ID, fixture.api.lastLogoutRequest?.deviceId)
            assertEquals("refresh-1", fixture.api.lastLogoutRequest?.refreshToken)
        }

    @Test
    fun `logout network failure still clears session secrets and retains registration`() =
        runTest {
            val fixture = authenticatedFixture()
            fixture.api.logoutError = KuraStorageException.Network(IOException("offline"))

            val result = runCatching { fixture.repository.logout() }

            assertTrue(result.isFailure)
            assertLoggedOutWithRegistration(fixture)
        }

    @Test
    fun `login after logout reuses device without registering again`() =
        runTest {
            val fixture = authenticatedFixture()
            fixture.repository.logout()

            fixture.repository.login("family", "new-secret")

            assertEquals(DEVICE_ID, fixture.api.lastLoginRequest?.deviceId)
            assertEquals(0, fixture.api.registerCount)
            assertEquals(DeviceId(DEVICE_ID), fixture.repository.deviceId())
        }

    @Test
    fun `expired session clears only session credential`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential(expiry = Instant.parse("2026-07-25T00:00:00Z"))

            assertNull(fixture.repository.storedCredential())

            assertLoggedOutWithRegistration(fixture)
        }

    @Test
    fun `missing encrypted token clears only session credential`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential(token = null)

            assertNull(fixture.repository.storedCredential())

            assertLoggedOutWithRegistration(fixture)
        }

    @Test
    fun `keystore loss clears only session credential`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.tokenStore.readError = KuraStorageException.CredentialUnavailable()

            assertNull(fixture.repository.storedCredential())

            assertLoggedOutWithRegistration(fixture)
        }

    @Test
    fun `authentication required during refresh retains registration`() =
        assertSessionRefreshFailureRetainsRegistration(ErrorCode.AUTHENTICATION_REQUIRED)

    @Test
    fun `refresh token reuse retains registration`() {
        assertSessionRefreshFailureRetainsRegistration(ErrorCode.REFRESH_TOKEN_REUSED)
    }

    @Test
    fun `device revoked during login clears registration and session`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.api.loginError = apiError(ErrorCode.DEVICE_REVOKED)

            runCatching { fixture.repository.login("family", "secret") }

            assertAllCredentialsCleared(fixture)
        }

    @Test
    fun `device revoked during refresh clears registration and session`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.api.refreshError = apiError(ErrorCode.DEVICE_REVOKED)

            runCatching { fixture.repository.refresh() }

            assertAllCredentialsCleared(fixture)
        }

    @Test
    fun `invalid stored device ID clears registration and session`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.metadataStore.registration =
                DeviceRegistrationMetadata(
                    deviceId = DeviceId("not-a-uuid"),
                    username = "family",
                )

            assertNull(fixture.repository.storedRegistration())

            assertAllCredentialsCleared(fixture)
        }

    @Test
    fun `login response for another device is rejected without losing registration`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.api.loginResponse = token("1", deviceId = OTHER_DEVICE_ID)

            val result = runCatching { fixture.repository.login("family", "secret") }

            assertTrue(result.exceptionOrNull() is KuraStorageException.InvalidServerResponse)
            assertLoggedOutWithRegistration(fixture)
        }

    @Test
    fun `refresh response for another user is rejected without losing registration`() =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.api.refreshResponse = token("2", userId = OTHER_USER_ID)

            val result = runCatching { fixture.repository.refresh() }

            assertTrue(result.exceptionOrNull() is KuraStorageException.InvalidServerResponse)
            assertLoggedOutWithRegistration(fixture)
        }

    @Test
    fun `registration persistence failure clears partial credentials`() =
        runTest {
            val fixture = Fixture()
            fixture.metadataStore.writeSessionError = IllegalStateException("storage unavailable")

            runCatching {
                fixture.repository.register(ConnectionRoute.LOCAL_DIRECT, "family", "secret", "Android")
            }

            assertAllCredentialsCleared(fixture)
        }

    private fun assertSessionRefreshFailureRetainsRegistration(code: ErrorCode) =
        runTest {
            val fixture = Fixture()
            fixture.seedCredential()
            fixture.api.refreshError = apiError(code)

            runCatching { fixture.repository.refresh() }

            assertLoggedOutWithRegistration(fixture)
        }

    private suspend fun authenticatedFixture(): Fixture =
        Fixture().also {
            it.seedCredential()
            it.repository.login("family", "secret")
        }

    private fun assertLoggedOutWithRegistration(fixture: Fixture) {
        assertNull(fixture.repository.accessToken())
        assertNull(fixture.repository.role())
        assertNull(fixture.repository.userId())
        assertNull(fixture.repository.deviceId())
        assertNull(fixture.tokenStore.token)
        assertNull(fixture.metadataStore.session)
        assertEquals(registration(), fixture.metadataStore.registration)
    }

    private fun assertAllCredentialsCleared(fixture: Fixture) {
        assertNull(fixture.repository.accessToken())
        assertNull(fixture.tokenStore.token)
        assertNull(fixture.metadataStore.session)
        assertNull(fixture.metadataStore.registration)
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
                Clock.fixed(NOW, ZoneOffset.UTC),
            )

        fun seedCredential(
            expiry: Instant = EXPIRY,
            token: String? = "refresh-0",
        ) {
            metadataStore.registration = registration()
            metadataStore.session = sessionMetadata(expiry)
            tokenStore.token = token
        }
    }

    private class FakeAuthenticationApi : AuthenticationApi {
        val refreshCount = AtomicInteger()
        var registerCount = 0
        var loginError: KuraStorageException? = null
        var refreshError: KuraStorageException? = null
        var logoutError: KuraStorageException? = null
        var loginResponse = token("1")
        var refreshResponse = token("2")
        var lastLoginRequest: LoginRequestDto? = null
        var lastLogoutRequest: LogoutRequestDto? = null

        override suspend fun registerDevice(request: RegisterDeviceRequestDto): TokenResponseDto {
            registerCount++
            return token("1")
        }

        override suspend fun login(request: LoginRequestDto): TokenResponseDto {
            lastLoginRequest = request
            loginError?.let { throw it }
            return loginResponse
        }

        override suspend fun refresh(request: RefreshRequestDto): TokenResponseDto {
            refreshCount.incrementAndGet()
            delay(20)
            refreshError?.let { throw it }
            return refreshResponse
        }

        override suspend fun logout(
            accessToken: String,
            request: LogoutRequestDto,
        ) {
            lastLogoutRequest = request
            logoutError?.let { throw it }
        }
    }

    private class FakeMetadataStore : CredentialMetadataStore {
        var registration: DeviceRegistrationMetadata? = null
        var session: SessionMetadata? = null
        var writeSessionError: Throwable? = null

        override suspend fun readRegistration() = registration

        override suspend fun writeRegistration(metadata: DeviceRegistrationMetadata) {
            registration = metadata
        }

        override suspend fun readSession() = session

        override suspend fun writeSession(metadata: SessionMetadata) {
            writeSessionError?.let { throw it }
            session = metadata
        }

        override suspend fun clearSession() {
            session = null
        }

        override suspend fun clearRegistration() {
            registration = null
            session = null
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
        const val DEVICE_ID = "11111111-1111-4111-8111-111111111111"
        const val OTHER_DEVICE_ID = "11111111-1111-4111-8111-111111111112"
        const val USER_ID = "22222222-2222-4222-8222-222222222222"
        const val OTHER_USER_ID = "22222222-2222-4222-8222-222222222223"
        val NOW: Instant = Instant.parse("2026-07-26T00:00:00Z")
        val EXPIRY: Instant = Instant.parse("2026-07-27T00:00:00Z")

        fun registration() = DeviceRegistrationMetadata(DeviceId(DEVICE_ID), "family")

        fun sessionMetadata(expiry: Instant = EXPIRY) = SessionMetadata(USER_ID, expiry, UserRole.ADMIN)

        fun apiError(code: ErrorCode) = KuraStorageException.Api(ApiError(code, "request-7", 401))

        fun token(
            suffix: String,
            deviceId: String = DEVICE_ID,
            userId: String = USER_ID,
        ) = TokenResponseDto(
            userId = userId,
            deviceId = deviceId,
            accessToken = "access-$suffix",
            refreshToken = "refresh-$suffix",
            accessTokenExpiresAt = "2026-07-26T01:00:00Z",
            refreshTokenExpiresAt = EXPIRY.toString(),
            role = "ADMIN",
        )
    }
}
