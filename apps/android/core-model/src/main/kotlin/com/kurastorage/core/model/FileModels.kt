package com.kurastorage.core.model

import java.time.Instant

enum class FileEntryType {
    FILE,
    FOLDER,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String?): FileEntryType = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class FileEntryStatus {
    ACTIVE,
    MISSING_CANDIDATE,
    MISSING,
    TRASHED,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String?): FileEntryStatus = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

data class FileEntry(
    val id: String,
    val parentId: String?,
    val name: String,
    val entryType: FileEntryType,
    val mimeType: String?,
    val size: Long,
    val status: FileEntryStatus,
    val fileVersion: Long,
    val trashedAt: Instant?,
    val createdAt: Instant,
    val updatedAt: Instant,
    val purgeEligibleAt: Instant? = null,
    val missingDetectedAt: Instant? = null,
    val missingLastCheckedAt: Instant? = null,
    val owner: OwnerSummary = OwnerSummary.UNKNOWN,
    val permission: SharePermission = SharePermission.UNKNOWN,
    val permissionSource: PermissionSource = PermissionSource.UNKNOWN,
    val shareTargetId: String? = null,
)

data class TrashPurgeRunSummary(
    val startedAt: Instant,
    val completedAt: Instant?,
    val status: String,
    val examinedRootCount: Int,
    val deletedRootCount: Int,
    val releasedBytes: Long,
    val errorCount: Int,
)

data class AdminStorageStatus(
    val storage: String,
    val totalBytes: Long?,
    val availableBytes: Long?,
    val capacityWarningThresholdBytes: Long,
    val capacityWarning: Boolean?,
    val trashBytes: Long,
    val expiredTrashRootCount: Int,
    val retentionDays: Int,
    val recoveryRequiredPurgeCount: Int,
    val lastPurgeRun: TrashPurgeRunSummary?,
)

data class FilePage(
    val parentId: String?,
    val items: List<FileEntry>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Long,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}

data class UploadOperation(
    val sourceUri: String,
    val destinationFolderId: String,
    val fileName: String,
    val size: Long,
    val contentType: String?,
    val sha256: String? = null,
    val idempotencyKey: String,
    val sessionId: String? = null,
    val confirmedOffset: Long = 0,
    val expiresAt: Instant? = null,
    val state: UploadState = UploadState.PREPARING,
)

enum class UploadState {
    PREPARING,
    CREATING_SESSION,
    UPLOADING,
    PAUSED,
    VERIFYING,
    COMPLETED,
    CANCELLED,
    FAILED,
}

data class DownloadOperation(
    val file: FileEntry,
    val destinationUri: String,
)

sealed interface TransferEvent {
    data class Progress(
        val transferredBytes: Long,
        val totalBytes: Long?,
    ) : TransferEvent

    data class UploadCompleted(
        val file: FileEntry,
    ) : TransferEvent

    data class UploadStatus(
        val operation: UploadOperation,
        val message: String? = null,
        val canRetry: Boolean = false,
    ) : TransferEvent

    data class DownloadCompleted(
        val destinationUri: String,
    ) : TransferEvent

    data class Failed(
        val error: Throwable,
        val partialFileRemoved: Boolean? = null,
    ) : TransferEvent
}
