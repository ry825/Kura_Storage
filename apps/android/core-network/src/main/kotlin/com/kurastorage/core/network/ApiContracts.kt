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
    val missingDetectedAt: String? = null,
    val missingLastCheckedAt: String? = null,
    val owner: OwnerSummaryDto? = null,
    val permission: String? = null,
    val permissionSource: String? = null,
    val shareTargetId: String? = null,
)

@Serializable
data class OwnerSummaryDto(
    val id: String,
    val displayName: String,
)

data class SearchRequestDto(
    val query: String? = null,
    val entryType: String? = null,
    val fileCategory: String? = null,
    val status: String? = null,
    val updatedFrom: String? = null,
    val updatedTo: String? = null,
    val minSize: Long? = null,
    val maxSize: Long? = null,
    val ownerUserId: String? = null,
    val shareTargetId: String? = null,
    val page: Int = 1,
    val pageSize: Int = 50,
)

@Serializable
data class SearchResultItemDto(
    val id: String,
    val entryType: String,
    val name: String,
    val mimeType: String? = null,
    val fileCategory: String? = null,
    val size: Long,
    val status: String,
    val updatedAt: String,
    val owner: OwnerSummaryDto,
    val permission: String,
    val permissionSource: String,
    val shareTargetId: String? = null,
)

@Serializable
data class SearchPageDto(
    val items: List<SearchResultItemDto>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
)

@Serializable
data class RecentFileItemDto(
    val id: String,
    val entryType: String,
    val name: String,
    val mimeType: String? = null,
    val fileCategory: String? = null,
    val size: Long,
    val status: String,
    val updatedAt: String,
    val owner: OwnerSummaryDto,
    val permission: String,
    val permissionSource: String,
    val shareTargetId: String? = null,
    val openedAt: String,
)

@Serializable
data class RecentFilePageDto(
    val items: List<RecentFileItemDto>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
)

@Serializable
data class ShareCandidateDto(
    val userId: String,
    val displayName: String,
)

@Serializable
data class ShareMemberDto(
    val userId: String,
    val displayName: String,
    val permission: String,
)

@Serializable
data class CreateShareMemberDto(
    val userId: String,
    val permission: String,
)

@Serializable
data class CreateShareRequestDto(
    val targetEntryId: String,
    val members: List<CreateShareMemberDto>,
)

@Serializable
data class SetShareMemberRequestDto(
    val permission: String,
)

@Serializable
data class ShareItemDto(
    val id: String,
    val targetEntryId: String,
    val entryType: String,
    val name: String,
    val owner: OwnerSummaryDto,
    val permission: String? = null,
    val members: List<ShareMemberDto>,
    val createdAt: String,
    val updatedAt: String,
)

@Serializable
data class SharePageDto(
    val items: List<ShareItemDto>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
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

@Serializable
data class CreateUploadSessionRequestDto(
    val destinationFolderId: String,
    val fileName: String,
    val contentType: String? = null,
    val size: Long,
    val sha256: String? = null,
)

@Serializable
data class UploadSessionDto(
    val id: String,
    val status: String,
    val size: Long,
    val receivedBytes: Long,
    val nextOffset: Long,
    val preferredChunkBytes: Int,
    val maximumChunkBytes: Int,
    val expiresAt: String,
    val absoluteExpiresAt: String,
    val resumable: Boolean,
    val file: FileEntryDto? = null,
)

@Serializable
data class UploadChunkDto(
    val offset: Long,
    val length: Long,
    val sha256: String,
    val receivedBytes: Long,
    val nextOffset: Long,
    val expiresAt: String,
    val replayed: Boolean,
)
