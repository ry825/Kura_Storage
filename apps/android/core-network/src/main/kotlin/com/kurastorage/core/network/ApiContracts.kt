package com.kurastorage.core.network

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject

@Serializable
data class SystemHealthDto(
    val api: String,
    val protocolVersion: Int,
    val storage: String,
)

@Serializable
data class RegisterDeviceRequestDto(
    val username: String,
    val password: String,
    val deviceName: String,
)

@Serializable
data class LoginRequestDto(
    val username: String,
    val password: String,
    val deviceId: String,
)

@Serializable
data class RefreshRequestDto(
    val deviceId: String,
    val refreshToken: String,
)

@Serializable
data class LogoutRequestDto(
    val deviceId: String,
    val refreshToken: String,
)

@Serializable
data class TokenResponseDto(
    val deviceId: String,
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiresAt: String,
    val refreshTokenExpiresAt: String,
)

@Serializable
data class ErrorResponseDto(
    val code: String,
    val message: String,
    val requestId: String,
    val details: JsonObject = buildJsonObject {},
)
