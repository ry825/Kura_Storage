package com.kurastorage.core.database.backup

import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.SyncLifecycleState
import java.time.Duration
import java.time.Instant

private const val MAX_HISTORY_ITEMS = 10_000
private const val HISTORY_RETENTION_DAYS = 90L
private val HISTORY_RETENTION = Duration.ofDays(HISTORY_RETENTION_DAYS)

class BackupTransferPersistence(
    private val database: BackupDatabaseAccess,
) {
    suspend fun enabledRules(scope: AccountScopeId): List<LocalBackupRule> =
        database.backupRuleDao().enabledByScope(scope.value).map(BackupEntityMapper::toModel)

    suspend fun externalWifiPolicies(scope: AccountScopeId) =
        database.externalWifiPolicyDao().listByScope(scope.value).map(BackupEntityMapper::toModel)

    suspend fun claim(
        scope: AccountScopeId,
        leaseOwner: String,
        now: Instant,
        duration: Duration,
        limit: Int,
    ): List<LocalSyncItem> =
        database
            .localSyncItemDao()
            .claim(scope.value, leaseOwner, now.toEpochMilli(), now.plus(duration).toEpochMilli(), limit)
            .map(BackupEntityMapper::toModel)

    suspend fun find(
        scope: AccountScopeId,
        id: LocalSyncItemId,
    ): LocalSyncItem? = database.localSyncItemDao().find(id.value, scope.value)?.let(BackupEntityMapper::toModel)

    suspend fun store(item: LocalSyncItem) {
        database.inTransaction {
            val current = requireNotNull(database.localSyncItemDao().find(item.id.value, item.accountScopeId.value))
            require(current.accountScopeId == item.accountScopeId.value && current.ruleId == item.ruleId.value)
            database.localSyncItemDao().upsertAll(listOf(BackupEntityMapper.toEntity(item)))
        }
    }

    suspend fun mutate(
        scope: AccountScopeId,
        id: LocalSyncItemId,
        transform: (LocalSyncItem) -> LocalSyncItem,
    ): LocalSyncItem =
        database.inTransaction {
            val current =
                requireNotNull(database.localSyncItemDao().find(id.value, scope.value))
                    .let(BackupEntityMapper::toModel)
            val updated = transform(current)
            require(updated.id == current.id && updated.accountScopeId == current.accountScopeId)
            database.localSyncItemDao().upsertAll(listOf(BackupEntityMapper.toEntity(updated)))
            updated
        }

    suspend fun releaseForWait(
        item: LocalSyncItem,
        reason: BackupWaitReason,
    ) = store(
        item.copy(
            lifecycleState = SyncLifecycleState.PENDING,
            waitReason = reason,
            leaseOwner = null,
            leaseExpiresAt = null,
        ),
    )

    suspend fun fail(
        item: LocalSyncItem,
        reason: BackupFailureReason,
    ) = store(
        item.copy(
            lifecycleState = SyncLifecycleState.FAILED,
            waitReason = BackupWaitReason.NONE,
            failureReason = reason,
            leaseOwner = null,
            leaseExpiresAt = null,
            nextAttemptAt = null,
        ),
    )

    suspend fun cleanupHistory(
        scope: AccountScopeId,
        now: Instant,
    ): Int =
        database.localSyncItemDao().cleanupHistory(
            scope.value,
            now.minus(HISTORY_RETENTION).toEpochMilli(),
            MAX_HISTORY_ITEMS,
        )
}
