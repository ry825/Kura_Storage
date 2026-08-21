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
    val role: String = "MEMBER",
)

@Serializable
data class ErrorResponseDto(
    val code: String,
    val message: String,
    val requestId: String,
    val details: JsonObject = buildJsonObject {},
)

@Serializable
data class CreateFolderRequestDto(
    val parentId: String? = null,
    val name: String,
)

@Serializable
data class UpdateFileRequestDto(
    val name: String? = null,
    val parentId: String? = null,
)

@Serializable
data class FileEntryDto(
    val id: String,
    val parentId: String? = null,
    val name: String,
    val entryType: String,
    val mimeType: String? = null,
    val size: Long,
    val status: String,
    val fileVersion: Long,
    val trashedAt: String? = null,
    val createdAt: String,
    val updatedAt: String,
    val purgeEligibleAt: String? = null,
)

@Serializable
data class FileEntryPageDto(
    val parentId: String? = null,
    val items: List<FileEntryDto>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Long,
)

@Serializable
data class TrashPurgeRunSummaryDto(
    val startedAt: String,
    val completedAt: String? = null,
    val status: String,
    val examinedRootCount: Int,
    val deletedRootCount: Int,
    val releasedBytes: Long,
    val errorCount: Int,
)

@Serializable
data class AdminStorageStatusDto(
    val storage: String,
    val totalBytes: Long? = null,
    val availableBytes: Long? = null,
    val capacityWarningThresholdBytes: Long,
    val capacityWarning: Boolean? = null,
    val trashBytes: Long,
    val expiredTrashRootCount: Int,
    val retentionDays: Int,
    val recoveryRequiredPurgeCount: Int,
    val lastPurgeRun: TrashPurgeRunSummaryDto? = null,
)
