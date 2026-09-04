package com.kurastorage.feature.auth

import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DeviceRegistrationMetadata
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class AuthViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before
    fun setUp() = Dispatchers.setMain(dispatcher)

    @After
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `stored refresh credential restores an authenticated session without a password`() =
        runTest(dispatcher) {
            val repository = FakeAuthenticationRepository(stored = credential())

            val viewModel = AuthViewModel(ConnectionRoute.REMOTE_SECURE, "device", repository)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(AuthUiState.Authenticated, viewModel.state.value)
            assertEquals(1, repository.refreshCalls)
            assertEquals(0, repository.loginCalls)
        }

    @Test
    fun `missing registration requires local registration and rejects remote registration`() =
        runTest(dispatcher) {
            val local = AuthViewModel(ConnectionRoute.LOCAL_DIRECT, "device", FakeAuthenticationRepository())
            val remote = AuthViewModel(ConnectionRoute.REMOTE_SECURE, "device", FakeAuthenticationRepository())
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(AuthUiState.Form(registration = true, deviceName = "device"), local.state.value)
            assertEquals(AuthUiState.RequiresLocalDirect, remote.state.value)
        }

    @Test
    fun `registered device without session shows sign in with stored username`() =
        runTest(dispatcher) {
            val repository = FakeAuthenticationRepository(registration = registration())

            val viewModel = AuthViewModel(ConnectionRoute.REMOTE_SECURE, "device", repository)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(AuthUiState.Form(registration = false, username = "member"), viewModel.state.value)
            assertEquals(0, repository.refreshCalls)
            assertEquals(0, repository.registerCalls)
        }

    @Test
    fun `expired server session shows actionable sign in`() =
        runTest(dispatcher) {
            val error = ApiError(ErrorCode.AUTHENTICATION_REQUIRED, "request-1", 401)
            val repository = FakeAuthenticationRepository(stored = credential(), refreshError = error)

            val viewModel = AuthViewModel(ConnectionRoute.REMOTE_SECURE, "device", repository)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                AuthUiState.Form(registration = false, username = "member", error = error),
                viewModel.state.value,
            )
        }

    @Test
    fun `refresh token reuse shows security reason on sign in`() =
        runTest(dispatcher) {
            val error = ApiError(ErrorCode.REFRESH_TOKEN_REUSED, "request-2", 401)
            val repository = FakeAuthenticationRepository(stored = credential(), refreshError = error)

            val viewModel = AuthViewModel(ConnectionRoute.LOCAL_DIRECT, "device", repository)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                AuthUiState.Form(registration = false, username = "member", error = error),
                viewModel.state.value,
            )
        }

    @Test
    fun `revoked device returns to route appropriate registration flow`() =
        runTest(dispatcher) {
            val error = ApiError(ErrorCode.DEVICE_REVOKED, "request-3", 403)
            val localRepository = FakeAuthenticationRepository(stored = credential(), refreshError = error)
            val remoteRepository = FakeAuthenticationRepository(stored = credential(), refreshError = error)

            val local = AuthViewModel(ConnectionRoute.LOCAL_DIRECT, "Pixel", localRepository)
            val remote = AuthViewModel(ConnectionRoute.REMOTE_SECURE, "Pixel", remoteRepository)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(AuthUiState.Form(registration = true, deviceName = "Pixel"), local.state.value)
            assertEquals(AuthUiState.RequiresLocalDirect, remote.state.value)
        }

    @Test
    fun `registration blocks duplicate submit and keeps username on an inline failure`() =
        runTest(dispatcher) {
            val error = ApiError(ErrorCode.VALIDATION_FAILED, "request-1", 400)
            val repository = FakeAuthenticationRepository(registerError = error)
            val viewModel = AuthViewModel(ConnectionRoute.LOCAL_DIRECT, "Pixel", repository)
            dispatcher.scheduler.advanceUntilIdle()

            viewModel.submit("member", "secret")
            viewModel.submit("member", "secret")
            assertTrue((viewModel.state.value as AuthUiState.Form).submitting)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(1, repository.registerCalls)
            assertEquals(
                AuthUiState.Form(
                    registration = true,
                    username = "member",
                    deviceName = "Pixel",
                    error = error,
                ),
                viewModel.state.value,
            )
        }

    @Test
    fun `remote unregistered state never invokes registration`() =
        runTest(dispatcher) {
            val repository = FakeAuthenticationRepository()
            val viewModel = AuthViewModel(ConnectionRoute.REMOTE_SECURE, "device", repository)
            dispatcher.scheduler.advanceUntilIdle()

            viewModel.submit("member", "secret")
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(AuthUiState.RequiresLocalDirect, viewModel.state.value)
            assertEquals(0, repository.registerCalls)
        }

    @Test
    fun `device revoked during sign in requires registration`() =
        runTest(dispatcher) {
            val error = ApiError(ErrorCode.DEVICE_REVOKED, "request-4", 403)
            val repository = FakeAuthenticationRepository(registration = registration(), loginError = error)
            val viewModel = AuthViewModel(ConnectionRoute.LOCAL_DIRECT, "Pixel", repository)
            dispatcher.scheduler.advanceUntilIdle()

            viewModel.submit("member", "secret")
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(AuthUiState.Form(registration = true, deviceName = "Pixel"), viewModel.state.value)
        }

    private class FakeAuthenticationRepository(
        private val stored: StoredCredential? = null,
        private val registration: DeviceRegistrationMetadata? = null,
        private val registerError: ApiError? = null,
        private val loginError: ApiError? = null,
        private val refreshError: ApiError? = null,
    ) : AuthenticationRepository {
        var refreshCalls = 0
        var loginCalls = 0
        var registerCalls = 0

        override suspend fun storedCredential() = stored

        override suspend fun storedRegistration(): DeviceRegistrationMetadata? =
            registration
                ?: stored?.let {
                    DeviceRegistrationMetadata(it.deviceId, it.username)
                }

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ): AuthSession {
            registerCalls++
            registerError?.let { throw KuraStorageException.Api(it) }
            return session()
        }

        override suspend fun login(
            username: String,
            password: String,
        ): AuthSession {
            loginCalls++
            loginError?.let { throw KuraStorageException.Api(it) }
            return session()
        }

        override suspend fun refresh(): AuthSession {
            refreshCalls++
            refreshError?.let { throw KuraStorageException.Api(it) }
            return session()
        }

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String) = session()

        override suspend fun logout() = Unit

        override fun accessToken() = null

        override fun role() = null
    }

    companion object {
        private val EXPIRY = Instant.parse("2099-01-01T00:00:00Z")

        private fun registration() =
            DeviceRegistrationMetadata(
                deviceId = DeviceId("00000000-0000-4000-8000-000000000001"),
                username = "member",
            )

        private fun credential() =
            StoredCredential(
                userId = "00000000-0000-4000-8000-000000000002",
                deviceId = registration().deviceId,
                refreshToken = "refresh-token",
                refreshTokenExpiresAt = EXPIRY,
                username = registration().username,
                role = UserRole.MEMBER,
            )

        private fun session() =
            AuthSession(
                userId = "00000000-0000-4000-8000-000000000002",
                deviceId = registration().deviceId,
                accessToken = "access-token",
                refreshToken = "refresh-token",
                accessTokenExpiresAt = EXPIRY,
                refreshTokenExpiresAt = EXPIRY,
                role = UserRole.MEMBER,
            )
    }
}
