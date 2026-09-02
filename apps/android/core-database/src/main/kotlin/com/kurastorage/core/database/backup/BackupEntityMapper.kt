package com.kurastorage.core.database.backup

import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupStateCount
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.ScanCheckpoint
import com.kurastorage.core.model.backup.SyncLifecycleState
import java.time.Instant

object BackupEntityMapper {
    fun toEntity(rule: LocalBackupRule) =
        BackupRuleEntity(
            id = rule.id.value,
            accountScopeId = rule.accountScopeId.value,
            sourceType = rule.sourceType.name,
            sourceLocator = rule.sourceLocator,
            displayName = rule.displayName,
            remoteFolderId = rule.remoteFolderId,
            enabled = rule.enabled,
            networkMode = rule.networkMode.name,
            requiresChargingForInitialRun = rule.requiresChargingForInitialRun,
            minimumBatteryPercent = rule.minimumBatteryPercent,
            initialRunCompletedAt = rule.initialRunCompletedAt?.toEpochMilli(),
            pausedAt = rule.pausedAt?.toEpochMilli(),
            createdAt = rule.createdAt.toEpochMilli(),
            updatedAt = rule.updatedAt.toEpochMilli(),
        )

    fun toModel(entity: BackupRuleEntity) =
        LocalBackupRule(
            id = BackupRuleId(entity.id),
            accountScopeId = AccountScopeId(entity.accountScopeId),
            sourceType = enumValueOf<BackupSourceType>(entity.sourceType),
            sourceLocator = entity.sourceLocator,
            displayName = entity.displayName,
            remoteFolderId = entity.remoteFolderId,
            enabled = entity.enabled,
            networkMode = enumValueOf<BackupNetworkMode>(entity.networkMode),
            requiresChargingForInitialRun = entity.requiresChargingForInitialRun,
            minimumBatteryPercent = entity.minimumBatteryPercent,
            initialRunCompletedAt = entity.initialRunCompletedAt?.let(Instant::ofEpochMilli),
            pausedAt = entity.pausedAt?.let(Instant::ofEpochMilli),
            createdAt = Instant.ofEpochMilli(entity.createdAt),
            updatedAt = Instant.ofEpochMilli(entity.updatedAt),
        )

    fun toEntity(item: LocalSyncItem) =
        LocalSyncItemEntity(
            id = item.id.value,
            accountScopeId = item.accountScopeId.value,
            ruleId = item.ruleId.value,
            localDocumentKey = item.localDocumentKey,
            sourceLocator = item.sourceLocator,
            relativePath = item.relativePath,
            displayName = item.displayName,
            size = item.size,
            modifiedAt = item.modifiedAt.toEpochMilli(),
            checksum = item.checksum,
            sourceFingerprint = item.sourceFingerprint,
            remoteFileId = item.remoteFileId,
            remoteFileVersion = item.remoteFileVersion,
            lifecycleState = item.lifecycleState.name,
            waitReason = item.waitReason.name,
            failureReason = item.failureReason.name,
            retryCount = item.retryCount,
            nextAttemptAt = item.nextAttemptAt?.toEpochMilli(),
            leaseOwner = item.leaseOwner,
            leaseExpiresAt = item.leaseExpiresAt?.toEpochMilli(),
            uploadSessionId = item.uploadSessionId,
            idempotencyKey = item.idempotencyKey,
            confirmedOffset = item.confirmedOffset,
            firstSeenAt = item.firstSeenAt.toEpochMilli(),
            lastSeenAt = item.lastSeenAt.toEpochMilli(),
            lastAttemptAt = item.lastAttemptAt?.toEpochMilli(),
            completedAt = item.completedAt?.toEpochMilli(),
        )

    fun toModel(entity: LocalSyncItemEntity) =
        LocalSyncItem(
            id = LocalSyncItemId(entity.id),
            accountScopeId = AccountScopeId(entity.accountScopeId),
            ruleId = BackupRuleId(entity.ruleId),
            localDocumentKey = entity.localDocumentKey,
            sourceLocator = entity.sourceLocator,
            relativePath = entity.relativePath,
            displayName = entity.displayName,
            size = entity.size,
            modifiedAt = Instant.ofEpochMilli(entity.modifiedAt),
            checksum = entity.checksum,
            sourceFingerprint = entity.sourceFingerprint,
            remoteFileId = entity.remoteFileId,
            remoteFileVersion = entity.remoteFileVersion,
            lifecycleState = enumValueOf<SyncLifecycleState>(entity.lifecycleState),
            waitReason = enumValueOf<BackupWaitReason>(entity.waitReason),
            failureReason = enumValueOf<BackupFailureReason>(entity.failureReason),
            retryCount = entity.retryCount,
            nextAttemptAt = entity.nextAttemptAt?.let(Instant::ofEpochMilli),
            leaseOwner = entity.leaseOwner,
            leaseExpiresAt = entity.leaseExpiresAt?.let(Instant::ofEpochMilli),
            uploadSessionId = entity.uploadSessionId,
            idempotencyKey = entity.idempotencyKey,
            confirmedOffset = entity.confirmedOffset,
            firstSeenAt = Instant.ofEpochMilli(entity.firstSeenAt),
            lastSeenAt = Instant.ofEpochMilli(entity.lastSeenAt),
            lastAttemptAt = entity.lastAttemptAt?.let(Instant::ofEpochMilli),
            completedAt = entity.completedAt?.let(Instant::ofEpochMilli),
        )

    fun toEntity(policy: ExternalWifiPolicy) =
        ExternalWifiPolicyEntity(
            id = policy.id.value,
            accountScopeId = policy.accountScopeId.value,
            displayName = policy.displayName,
            normalizedSsid = policy.normalizedSsid,
            normalizedBssid = policy.normalizedBssid,
            normalizedBssidKey = policy.normalizedBssid.orEmpty(),
            treatAsMetered = policy.treatAsMetered,
            enabled = policy.enabled,
            createdAt = policy.createdAt.toEpochMilli(),
            updatedAt = policy.updatedAt.toEpochMilli(),
        )

    fun toModel(entity: ExternalWifiPolicyEntity) =
        ExternalWifiPolicy(
            id = ExternalWifiPolicyId(entity.id),
            accountScopeId = AccountScopeId(entity.accountScopeId),
            displayName = entity.displayName,
            normalizedSsid = entity.normalizedSsid,
            normalizedBssid = entity.normalizedBssid,
            treatAsMetered = entity.treatAsMetered,
            enabled = entity.enabled,
            createdAt = Instant.ofEpochMilli(entity.createdAt),
            updatedAt = Instant.ofEpochMilli(entity.updatedAt),
        )

    fun toEntity(checkpoint: ScanCheckpoint) =
        ScanCheckpointEntity(
            ruleId = checkpoint.ruleId.value,
            mediaStoreVersion = checkpoint.mediaStoreVersion,
            generation = checkpoint.generation,
            fullScanToken = checkpoint.fullScanToken,
            lastCompletedAt = checkpoint.lastCompletedAt?.toEpochMilli(),
            updatedAt = checkpoint.updatedAt.toEpochMilli(),
        )

    fun toModel(entity: ScanCheckpointEntity) =
        ScanCheckpoint(
            ruleId = BackupRuleId(entity.ruleId),
            mediaStoreVersion = entity.mediaStoreVersion,
            generation = entity.generation,
            fullScanToken = entity.fullScanToken,
            lastCompletedAt = entity.lastCompletedAt?.let(Instant::ofEpochMilli),
            updatedAt = Instant.ofEpochMilli(entity.updatedAt),
        )

    fun toModel(entity: BackupStateCountEntity) = BackupStateCount(enumValueOf(entity.lifecycleState), entity.count)
}
