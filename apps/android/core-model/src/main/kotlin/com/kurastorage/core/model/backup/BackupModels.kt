package com.kurastorage.core.model.backup

import java.net.URI
import java.time.Instant
import java.util.UUID

private val sha256Pattern = Regex("[0-9a-f]{64}")
private const val MAX_RULE_DISPLAY_NAME_LENGTH = 120
private const val MIN_BATTERY_PERCENT = 0
private const val MAX_BATTERY_PERCENT = 100
private const val MAX_SOURCE_LOCATOR_LENGTH = 128
private const val MAX_LOCAL_DOCUMENT_KEY_LENGTH = 128
private const val MAX_WIFI_DISPLAY_NAME_LENGTH = 80
private const val MAX_SSID_LENGTH = 32

@JvmInline
value class AccountScopeId(
    val value: String,
) {
    init {
        require(sha256Pattern.matches(value)) { "Account scope must be a lowercase SHA-256 value" }
    }
}

@JvmInline
value class BackupRuleId(
    val value: String,
) {
    init {
        UUID.fromString(value)
    }
}

@JvmInline
value class LocalSyncItemId(
    val value: String,
) {
    init {
        UUID.fromString(value)
    }
}

@JvmInline
value class ExternalWifiPolicyId(
    val value: String,
) {
    init {
        UUID.fromString(value)
    }
}

enum class BackupSourceType {
    MEDIA_IMAGES,
    MEDIA_VIDEOS,
    MEDIA_AUDIO,
    SAF_TREE,
}

enum class BackupNetworkMode {
    LOCAL_DIRECT_ONLY,
    LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER,
}

enum class SyncLifecycleState {
    DISCOVERED,
    PENDING,
    COMPARING,
    READY_TO_UPLOAD,
    UPLOADING,
    COMPLETED,
    FAILED,
    LOCAL_MISSING,
}

enum class BackupWaitReason {
    NONE,
    NETWORK,
    ALLOWED_WIFI,
    BATTERY,
    CHARGING,
    AUTHENTICATION,
    STORAGE,
    SOURCE_PERMISSION,
    SERVER_RECONCILIATION,
}

enum class BackupFailureReason {
    NONE,
    SOURCE_CHANGED,
    SOURCE_UNAVAILABLE,
    PERMISSION_REVOKED,
    REMOTE_CONFLICT,
    RETRY_EXHAUSTED,
    PROTOCOL_ERROR,
}

data class LocalBackupRule(
    val id: BackupRuleId,
    val accountScopeId: AccountScopeId,
    val sourceType: BackupSourceType,
    val sourceLocator: String,
    val displayName: String,
    val remoteFolderId: String,
    val enabled: Boolean,
    val networkMode: BackupNetworkMode,
    val requiresChargingForInitialRun: Boolean,
    val minimumBatteryPercent: Int,
    val initialRunCompletedAt: Instant?,
    val pausedAt: Instant?,
    val createdAt: Instant,
    val updatedAt: Instant,
) {
    init {
        require(displayName.isNotBlank() && displayName.length <= MAX_RULE_DISPLAY_NAME_LENGTH)
        require(minimumBatteryPercent in MIN_BATTERY_PERCENT..MAX_BATTERY_PERCENT)
        UUID.fromString(remoteFolderId)
        if (sourceType == BackupSourceType.SAF_TREE) {
            require(URI(sourceLocator).scheme == "content")
        } else {
            require(sourceLocator.isNotBlank() && sourceLocator.length <= MAX_SOURCE_LOCATOR_LENGTH)
        }
    }
}

data class LocalSyncItem(
    val id: LocalSyncItemId,
    val accountScopeId: AccountScopeId,
    val ruleId: BackupRuleId,
    val localDocumentKey: String,
    val sourceLocator: String,
    val relativePath: String,
    val displayName: String,
    val size: Long,
    val modifiedAt: Instant,
    val checksum: String?,
    val sourceFingerprint: String,
    val remoteFileId: String?,
    val remoteFileVersion: Long?,
    val lifecycleState: SyncLifecycleState,
    val waitReason: BackupWaitReason,
    val failureReason: BackupFailureReason,
    val retryCount: Int,
    val nextAttemptAt: Instant?,
    val leaseOwner: String?,
    val leaseExpiresAt: Instant?,
    val uploadSessionId: String?,
    val idempotencyKey: String?,
    val confirmedOffset: Long,
    val firstSeenAt: Instant,
    val lastSeenAt: Instant,
    val lastAttemptAt: Instant?,
    val completedAt: Instant?,
) {
    init {
        require(localDocumentKey.length in 1..MAX_LOCAL_DOCUMENT_KEY_LENGTH)
        require(sourceLocator.isNotBlank())
        require(relativePath.isNotBlank() && !relativePath.startsWith('/') && ".." !in relativePath.split('/'))
        require(displayName.isNotBlank())
        require(size >= 0 && confirmedOffset in 0..size)
        require(checksum == null || sha256Pattern.matches(checksum))
        require(retryCount >= 0)
    }
}

data class ExternalWifiPolicy(
    val id: ExternalWifiPolicyId,
    val accountScopeId: AccountScopeId,
    val displayName: String,
    val normalizedSsid: String,
    val normalizedBssid: String?,
    val treatAsMetered: Boolean,
    val enabled: Boolean,
    val createdAt: Instant,
    val updatedAt: Instant,
) {
    init {
        require(displayName.isNotBlank() && displayName.length <= MAX_WIFI_DISPLAY_NAME_LENGTH)
        require(normalizedSsid.isNotBlank() && normalizedSsid.length <= MAX_SSID_LENGTH)
    }
}

data class ScanCheckpoint(
    val ruleId: BackupRuleId,
    val mediaStoreVersion: String?,
    val generation: Long?,
    val fullScanToken: String?,
    val lastCompletedAt: Instant?,
    val updatedAt: Instant,
)

data class BackupStateCount(
    val lifecycleState: SyncLifecycleState,
    val count: Int,
)
