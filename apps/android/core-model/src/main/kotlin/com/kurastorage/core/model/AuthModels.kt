package com.kurastorage.core.model

import java.time.Instant

@JvmInline
value class DeviceId(
    val value: String,
)

data class AuthSession(
    val deviceId: DeviceId,
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiresAt: Instant,
    val refreshTokenExpiresAt: Instant,
)

data class StoredCredential(
    val deviceId: DeviceId,
    val refreshToken: String,
    val refreshTokenExpiresAt: Instant,
    val username: String?,
)

enum class AuthState {
    AUTHENTICATED,
    AUTHENTICATION_REQUIRED,
    DEVICE_REVOKED,
}
