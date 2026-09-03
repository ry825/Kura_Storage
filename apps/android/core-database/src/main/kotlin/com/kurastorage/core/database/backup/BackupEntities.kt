package com.kurastorage.core.database.backup

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(
    tableName = "backup_rules",
    indices = [Index(value = ["account_scope_id", "enabled"])],
)
data class BackupRuleEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "account_scope_id") val accountScopeId: String,
    @ColumnInfo(name = "source_type") val sourceType: String,
    @ColumnInfo(name = "source_locator") val sourceLocator: String,
    @ColumnInfo(name = "display_name") val displayName: String,
    @ColumnInfo(name = "remote_folder_id") val remoteFolderId: String,
    val enabled: Boolean,
    @ColumnInfo(name = "network_mode") val networkMode: String,
    @ColumnInfo(name = "requires_charging_for_initial_run") val requiresChargingForInitialRun: Boolean,
    @ColumnInfo(name = "minimum_battery_percent") val minimumBatteryPercent: Int,
    @ColumnInfo(name = "initial_run_completed_at") val initialRunCompletedAt: Long?,
    @ColumnInfo(name = "paused_at") val pausedAt: Long?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
)

@Entity(
    tableName = "local_sync_items",
    foreignKeys = [
        ForeignKey(
            entity = BackupRuleEntity::class,
            parentColumns = ["id"],
            childColumns = ["rule_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["rule_id"]),
        Index(value = ["account_scope_id", "rule_id", "local_document_key"], unique = true),
        Index(value = ["account_scope_id", "lifecycle_state", "next_attempt_at", "first_seen_at", "id"]),
        Index(value = ["account_scope_id", "remote_file_id"], unique = true),
    ],
)
data class LocalSyncItemEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "account_scope_id") val accountScopeId: String,
    @ColumnInfo(name = "rule_id") val ruleId: String,
    @ColumnInfo(name = "local_document_key") val localDocumentKey: String,
    @ColumnInfo(name = "source_locator") val sourceLocator: String,
    @ColumnInfo(name = "relative_path") val relativePath: String,
    @ColumnInfo(name = "display_name") val displayName: String,
    val size: Long,
    @ColumnInfo(name = "modified_at") val modifiedAt: Long,
    val checksum: String?,
    @ColumnInfo(name = "source_fingerprint") val sourceFingerprint: String,
    @ColumnInfo(name = "remote_file_id") val remoteFileId: String?,
    @ColumnInfo(name = "remote_file_version") val remoteFileVersion: Long?,
    @ColumnInfo(name = "lifecycle_state") val lifecycleState: String,
    @ColumnInfo(name = "wait_reason") val waitReason: String,
    @ColumnInfo(name = "failure_reason") val failureReason: String,
    @ColumnInfo(name = "retry_count") val retryCount: Int,
    @ColumnInfo(name = "next_attempt_at") val nextAttemptAt: Long?,
    @ColumnInfo(name = "lease_owner") val leaseOwner: String?,
    @ColumnInfo(name = "lease_expires_at") val leaseExpiresAt: Long?,
    @ColumnInfo(name = "upload_session_id") val uploadSessionId: String?,
    @ColumnInfo(name = "idempotency_key") val idempotencyKey: String?,
    @ColumnInfo(name = "confirmed_offset") val confirmedOffset: Long,
    @ColumnInfo(name = "first_seen_at") val firstSeenAt: Long,
    @ColumnInfo(name = "last_seen_at") val lastSeenAt: Long,
    @ColumnInfo(name = "last_attempt_at") val lastAttemptAt: Long?,
    @ColumnInfo(name = "completed_at") val completedAt: Long?,
)

@Entity(
    tableName = "external_wifi_policies",
    indices = [
        Index(
            value = ["account_scope_id", "normalized_ssid", "normalized_bssid_key"],
            unique = true,
        ),
        Index(value = ["account_scope_id", "enabled"]),
    ],
)
data class ExternalWifiPolicyEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "account_scope_id") val accountScopeId: String,
    @ColumnInfo(name = "display_name") val displayName: String,
    @ColumnInfo(name = "normalized_ssid") val normalizedSsid: String,
    @ColumnInfo(name = "normalized_bssid") val normalizedBssid: String?,
    @ColumnInfo(name = "normalized_bssid_key") val normalizedBssidKey: String,
    @ColumnInfo(name = "treat_as_metered") val treatAsMetered: Boolean,
    val enabled: Boolean,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
)

@Entity(
    tableName = "scan_checkpoints",
    foreignKeys = [
        ForeignKey(
            entity = BackupRuleEntity::class,
            parentColumns = ["id"],
            childColumns = ["rule_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
)
data class ScanCheckpointEntity(
    @PrimaryKey @ColumnInfo(name = "rule_id") val ruleId: String,
    @ColumnInfo(name = "media_store_version") val mediaStoreVersion: String?,
    val generation: Long?,
    @ColumnInfo(name = "full_scan_token") val fullScanToken: String?,
    @ColumnInfo(name = "last_completed_at") val lastCompletedAt: Long?,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
)

@Entity(
    tableName = "source_identity_mappings",
    primaryKeys = ["rule_id", "provider_key"],
    foreignKeys = [
        ForeignKey(
            entity = BackupRuleEntity::class,
            parentColumns = ["id"],
            childColumns = ["rule_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["rule_id"]),
        Index(value = ["rule_id", "local_document_key"], unique = true),
    ],
)
data class SourceIdentityMappingEntity(
    @ColumnInfo(name = "rule_id") val ruleId: String,
    @ColumnInfo(name = "provider_key") val providerKey: String,
    @ColumnInfo(name = "identity_discriminator") val identityDiscriminator: String,
    @ColumnInfo(name = "local_document_key") val localDocumentKey: String,
    @ColumnInfo(name = "first_seen_at") val firstSeenAt: Long,
    @ColumnInfo(name = "last_seen_at") val lastSeenAt: Long,
)

data class BackupStateCountEntity(
    @ColumnInfo(name = "lifecycle_state") val lifecycleState: String,
    val count: Int,
)

data class BackupRuleStateCountEntity(
    @ColumnInfo(name = "rule_id") val ruleId: String,
    @ColumnInfo(name = "lifecycle_state") val lifecycleState: String,
    val count: Int,
)

data class BackupWaitReasonCountEntity(
    @ColumnInfo(name = "wait_reason") val waitReason: String,
    val count: Int,
)

data class BackupPendingEstimateEntity(
    val itemCount: Int,
    val byteCount: Long,
    val maximumItemBytes: Long,
)
