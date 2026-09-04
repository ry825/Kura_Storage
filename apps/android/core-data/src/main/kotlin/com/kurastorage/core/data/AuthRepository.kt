package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DeviceRegistrationMetadata
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.SessionMetadata
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.AuthenticationApi
import com.kurastorage.core.network.LoginRequestDto
import com.kurastorage.core.network.LogoutRequestDto
import com.kurastorage.core.network.RefreshRequestDto
import com.kurastorage.core.network.RegisterDeviceRequestDto
import com.kurastorage.core.network.TokenResponseDto
import com.kurastorage.core.security.EncryptedTokenStore
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.Clock
import java.time.Instant
import java.time.format.DateTimeParseException
import java.util.UUID
import java.util.concurrent.atomic.AtomicReference

private const val HTTP_UNAUTHORIZED = 401

@Suppress("TooManyFunctions")
interface AuthenticationRepository {
    suspend fun storedCredential(): StoredCredential?

    suspend fun storedRegistration(): DeviceRegistrationMetadata? =
        storedCredential()?.let { DeviceRegistrationMetadata(it.deviceId, it.username) }

    suspend fun register(
        route: ConnectionRoute,
        username: String,
        password: String,
        deviceName: String,
    ): AuthSession

    suspend fun login(
        username: String,
        password: String,
    ): AuthSession

    suspend fun refresh(): AuthSession

    suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession

    suspend fun logout()

    fun accessToken(): String?

    fun role(): UserRole? = null

    fun userId(): String? = null

    fun deviceId(): DeviceId? = null
}

@Suppress("TooManyFunctions")
class DefaultAuthenticationRepository(
    private val api: AuthenticationApi,
    private val metadataStore: CredentialMetadataStore,
    private val tokenStore: EncryptedTokenStore,
    private val clock: Clock = Clock.systemUTC(),
) : AuthenticationRepository {
    private val session = AtomicReference<AuthSession?>()
    private val refreshMutex = Mutex()

    @Suppress("ReturnCount")
    override suspend fun storedCredential(): StoredCredential? {
        val registration = storedRegistration() ?: return null
        val metadata = metadataStore.readSession()
        if (metadata == null || !metadata.refreshTokenExpiresAt.isAfter(clock.instant())) {
            clearSessionCredentials()
            return null
        }
        val refreshToken =
            try {
                tokenStore.read()
            } catch (_: KuraStorageException.CredentialUnavailable) {
                clearSessionCredentials()
                null
            }
        if (refreshToken == null) {
            clearSessionCredentials()
            return null
        }
        return StoredCredential(
            userId = metadata.userId,
            deviceId = registration.deviceId,
            refreshToken = refreshToken,
            refreshTokenExpiresAt = metadata.refreshTokenExpiresAt,
            username = registration.username,
            role = metadata.role,
        )
    }

    @Suppress("ReturnCount")
    override suspend fun storedRegistration(): DeviceRegistrationMetadata? {
        val registration = metadataStore.readRegistration() ?: return null
        if (runCatching { UUID.fromString(registration.deviceId.value) }.isFailure) {
            clearRegistrationCredentials()
            return null
        }
        return registration
    }

    override suspend fun register(
        route: ConnectionRoute,
        username: String,
        password: String,
        deviceName: String,
    ): AuthSession {
        require(route == ConnectionRoute.LOCAL_DIRECT) {
            "Device registration requires LOCAL_DIRECT"
        }
        return persistNewRegistration(
            api.registerDevice(RegisterDeviceRequestDto(username, password, deviceName)),
            username,
        )
    }

    override suspend fun login(
        username: String,
        password: String,
    ): AuthSession {
        val registration = storedRegistration() ?: authenticationRequired()
        return try {
            persistExistingRegistration(
                response = api.login(LoginRequestDto(username, password, registration.deviceId.value)),
                registration = registration.copy(username = username),
                expectedUserId = null,
                updateRegistration = true,
            )
        } catch (error: KuraStorageException.Api) {
            if (error.error.code == ErrorCode.DEVICE_REVOKED) clearRegistrationCredentials()
            throw error
        }
    }

    override suspend fun refresh(): AuthSession =
        refreshMutex.withLock {
            val current = session.get()
            if (current != null && current.accessTokenExpiresAt.isAfter(clock.instant())) return@withLock current
            rotateRefreshToken()
        }

    override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession =
        refreshMutex.withLock {
            val current = session.get()
            if (current != null && current.accessToken != rejectedAccessToken) return@withLock current
            rotateRefreshToken()
        }

    override suspend fun logout() {
        val current = session.get()
        try {
            val credential = storedCredential()
            if (current != null && credential != null) {
                api.logout(
                    current.accessToken,
                    LogoutRequestDto(credential.deviceId.value, credential.refreshToken),
                )
            }
        } finally {
            clearSessionCredentials()
        }
    }

    override fun accessToken(): String? =
        session
            .get()
            ?.takeIf { it.accessTokenExpiresAt.isAfter(clock.instant()) }
            ?.accessToken

    override fun role(): UserRole? = session.get()?.role

    override fun userId(): String? = session.get()?.userId

    override fun deviceId(): DeviceId? = session.get()?.deviceId

    @Suppress("TooGenericExceptionCaught")
    private suspend fun persistNewRegistration(
        response: TokenResponseDto,
        username: String,
    ): AuthSession {
        val authSession = response.toAuthSession()
        val registration = DeviceRegistrationMetadata(authSession.deviceId, username)
        try {
            metadataStore.writeRegistration(registration)
            tokenStore.write(authSession.refreshToken)
            metadataStore.writeSession(
                SessionMetadata(
                    userId = authSession.userId,
                    refreshTokenExpiresAt = authSession.refreshTokenExpiresAt,
                    role = authSession.role,
                ),
            )
            session.set(authSession)
        } catch (error: Exception) {
            clearRegistrationCredentials()
            throw error
        }
        return authSession
    }

    @Suppress("TooGenericExceptionCaught")
    private suspend fun persistExistingRegistration(
        response: TokenResponseDto,
        registration: DeviceRegistrationMetadata,
        expectedUserId: String?,
        updateRegistration: Boolean,
    ): AuthSession {
        val authSession = response.toAuthSession()
        if (authSession.deviceId != registration.deviceId || expectedUserId?.let { it != authSession.userId } == true) {
            clearSessionCredentials()
            throw KuraStorageException.InvalidServerResponse()
        }
        try {
            if (updateRegistration) metadataStore.writeRegistration(registration)
            tokenStore.write(authSession.refreshToken)
            metadataStore.writeSession(
                SessionMetadata(
                    userId = authSession.userId,
                    refreshTokenExpiresAt = authSession.refreshTokenExpiresAt,
                    role = authSession.role,
                ),
            )
            session.set(authSession)
        } catch (error: Exception) {
            clearSessionCredentials()
            throw error
        }
        return authSession
    }

    private fun TokenResponseDto.toAuthSession(): AuthSession {
        try {
            UUID.fromString(userId)
            UUID.fromString(deviceId)
            return AuthSession(
                userId = userId,
                deviceId = DeviceId(deviceId),
                accessToken = accessToken,
                refreshToken = refreshToken,
                accessTokenExpiresAt = Instant.parse(accessTokenExpiresAt),
                refreshTokenExpiresAt = Instant.parse(refreshTokenExpiresAt),
                role = UserRole.valueOf(role),
            )
        } catch (_: IllegalArgumentException) {
            throw KuraStorageException.InvalidServerResponse()
        } catch (_: DateTimeParseException) {
            throw KuraStorageException.InvalidServerResponse()
        }
    }

    private suspend fun clearSessionCredentials() {
        session.set(null)
        try {
            tokenStore.clear()
        } finally {
            metadataStore.clearSession()
        }
    }

    private suspend fun clearRegistrationCredentials() {
        session.set(null)
        try {
            tokenStore.clear()
        } finally {
            metadataStore.clearRegistration()
        }
    }

    private suspend fun rotateRefreshToken(): AuthSession {
        val credential = storedCredential() ?: authenticationRequired()
        return try {
            persistExistingRegistration(
                response = api.refresh(RefreshRequestDto(credential.deviceId.value, credential.refreshToken)),
                registration = DeviceRegistrationMetadata(credential.deviceId, credential.username),
                expectedUserId = credential.userId,
                updateRegistration = false,
            )
        } catch (error: KuraStorageException.Api) {
            when (error.error.code) {
                ErrorCode.DEVICE_REVOKED -> clearRegistrationCredentials()
                ErrorCode.AUTHENTICATION_REQUIRED,
                ErrorCode.REFRESH_TOKEN_REUSED,
                -> clearSessionCredentials()
                else -> Unit
            }
            throw error
        }
    }

    private fun authenticationRequired(): Nothing =
        throw KuraStorageException.Api(
            com.kurastorage.core.model
                .ApiError(ErrorCode.AUTHENTICATION_REQUIRED, null, HTTP_UNAUTHORIZED),
        )
}

sealed interface AuthenticatedCallResult<out T> {
    data class Success<T>(
        val value: T,
    ) : AuthenticatedCallResult<T>

    data object Unauthorized : AuthenticatedCallResult<Nothing>
}

class AuthenticatedRequestExecutor(
    private val repository: AuthenticationRepository,
) {
    suspend fun <T> execute(call: suspend (accessToken: String) -> AuthenticatedCallResult<T>): T {
        val initialToken = repository.accessToken() ?: repository.refresh().accessToken
        return when (val first = call(initialToken)) {
            is AuthenticatedCallResult.Success -> first.value
            AuthenticatedCallResult.Unauthorized -> {
                val refreshedToken = repository.refreshAfterUnauthorized(initialToken).accessToken
                when (val retry = call(refreshedToken)) {
                    is AuthenticatedCallResult.Success -> retry.value
                    AuthenticatedCallResult.Unauthorized -> throw KuraStorageException.Api(
                        com.kurastorage.core.model.ApiError(
                            ErrorCode.AUTHENTICATION_REQUIRED,
                            null,
                            HTTP_UNAUTHORIZED,
                        ),
                    )
                }
            }
        }
    }
}
