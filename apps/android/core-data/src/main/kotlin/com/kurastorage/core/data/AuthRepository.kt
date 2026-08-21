package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
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
import java.util.concurrent.atomic.AtomicReference

private const val HTTP_UNAUTHORIZED = 401

interface AuthenticationRepository {
    suspend fun storedCredential(): StoredCredential?

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
        val metadata = metadataStore.read() ?: return null
        if (!metadata.refreshTokenExpiresAt.isAfter(clock.instant())) {
            clearCredentials()
            return null
        }
        val refreshToken =
            try {
                tokenStore.read()
            } catch (_: KuraStorageException.CredentialUnavailable) {
                clearCredentials()
                null
            } ?: return null
        return StoredCredential(
            deviceId = metadata.deviceId,
            refreshToken = refreshToken,
            refreshTokenExpiresAt = metadata.refreshTokenExpiresAt,
            username = metadata.username,
            role = metadata.role,
        )
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
        return persist(
            api.registerDevice(RegisterDeviceRequestDto(username, password, deviceName)),
            username,
        )
    }

    override suspend fun login(
        username: String,
        password: String,
    ): AuthSession {
        val credential =
            storedCredential() ?: throw KuraStorageException.Api(
                com.kurastorage.core.model
                    .ApiError(ErrorCode.AUTHENTICATION_REQUIRED, null, HTTP_UNAUTHORIZED),
            )
        return try {
            persist(
                api.login(LoginRequestDto(username, password, credential.deviceId.value)),
                username,
            )
        } catch (error: KuraStorageException.Api) {
            if (error.error.code == ErrorCode.DEVICE_REVOKED) clearCredentials()
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
        val credential = storedCredential()
        try {
            if (current != null && credential != null) {
                api.logout(
                    current.accessToken,
                    LogoutRequestDto(credential.deviceId.value, credential.refreshToken),
                )
            }
        } finally {
            clearCredentials()
        }
    }

    override fun accessToken(): String? =
        session
            .get()
            ?.takeIf { it.accessTokenExpiresAt.isAfter(clock.instant()) }
            ?.accessToken

    override fun role(): UserRole? = session.get()?.role

    @Suppress("TooGenericExceptionCaught")
    private suspend fun persist(
        response: TokenResponseDto,
        username: String?,
    ): AuthSession {
        val authSession =
            AuthSession(
                deviceId = DeviceId(response.deviceId),
                accessToken = response.accessToken,
                refreshToken = response.refreshToken,
                accessTokenExpiresAt = Instant.parse(response.accessTokenExpiresAt),
                refreshTokenExpiresAt = Instant.parse(response.refreshTokenExpiresAt),
                role = UserRole.valueOf(response.role),
            )
        try {
            tokenStore.write(authSession.refreshToken)
            metadataStore.write(
                CredentialMetadata(
                    deviceId = authSession.deviceId,
                    refreshTokenExpiresAt = authSession.refreshTokenExpiresAt,
                    username = username,
                    role = authSession.role,
                ),
            )
            session.set(authSession)
        } catch (error: Exception) {
            clearCredentials()
            throw error
        }
        return authSession
    }

    private suspend fun clearCredentials() {
        session.set(null)
        tokenStore.clear()
        metadataStore.clear()
    }

    private suspend fun rotateRefreshToken(): AuthSession {
        val credential = storedCredential() ?: authenticationRequired()
        return try {
            persist(
                api.refresh(RefreshRequestDto(credential.deviceId.value, credential.refreshToken)),
                credential.username,
            )
        } catch (error: KuraStorageException.Api) {
            if (
                error.error.code in
                setOf(
                    ErrorCode.AUTHENTICATION_REQUIRED,
                    ErrorCode.DEVICE_REVOKED,
                    ErrorCode.REFRESH_TOKEN_REUSED,
                )
            ) {
                clearCredentials()
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
