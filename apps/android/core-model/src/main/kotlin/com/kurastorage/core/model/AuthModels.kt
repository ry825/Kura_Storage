package com.kurastorage.core.model

import java.time.Instant

@JvmInline
value class DeviceId(
    val value: String,
)

enum class UserRole { ADMIN, MEMBER }

data class AuthSession(
    val deviceId: DeviceId,
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiresAt: Instant,
    val refreshTokenExpiresAt: Instant,
    val role: UserRole = UserRole.MEMBER,
    val userId: String = "00000000-0000-0000-0000-000000000000",
)

data class StoredCredential(
    val deviceId: DeviceId,
    val refreshToken: String,
    val refreshTokenExpiresAt: Instant,
    val username: String?,
    val role: UserRole = UserRole.MEMBER,
    val userId: String = "00000000-0000-0000-0000-000000000000",
)

enum class AuthState {
    AUTHENTICATED,
    AUTHENTICATION_REQUIRED,
    DEVICE_REVOKED,
}
