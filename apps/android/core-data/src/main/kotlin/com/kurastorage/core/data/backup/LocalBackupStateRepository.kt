package com.kurastorage.core.data.backup

import com.kurastorage.core.database.backup.BackupEntityMapper
import com.kurastorage.core.database.backup.LocalSyncItemDao
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupStateCount
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.SyncLifecycleState
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.time.Clock
import java.time.Duration
import java.time.Instant

private const val MAX_LEASE_OWNER_LENGTH = 128

enum class LocalDatabaseRecoveryDirective {
    RESCAN_AND_COMPARE,
}

class LocalBackupStateRepository(
    private val dao: LocalSyncItemDao,
    private val clock: Clock = Clock.systemUTC(),
) {
    fun observeItems(accountScopeId: AccountScopeId): Flow<List<LocalSyncItem>> =
        dao.observeByScope(accountScopeId.value).map { items -> items.map(BackupEntityMapper::toModel) }

    fun observeCounts(accountScopeId: AccountScopeId): Flow<List<BackupStateCount>> =
        dao.observeStateCounts(accountScopeId.value).map { counts -> counts.map(BackupEntityMapper::toModel) }

    suspend fun upsert(items: List<LocalSyncItem>) = dao.upsertAll(items.map(BackupEntityMapper::toEntity))

    suspend fun claim(
        accountScopeId: AccountScopeId,
        leaseOwner: String,
        limit: Int,
        leaseDuration: Duration,
    ): List<LocalSyncItem> {
        require(leaseOwner.isNotBlank() && leaseOwner.length <= MAX_LEASE_OWNER_LENGTH)
        require(!leaseDuration.isNegative && !leaseDuration.isZero)
        val now = Instant.now(clock)
        return dao
            .claim(
                scopeId = accountScopeId.value,
                leaseOwner = leaseOwner,
                now = now.toEpochMilli(),
                leaseExpiresAt = now.plus(leaseDuration).toEpochMilli(),
                limit = limit,
            ).map(BackupEntityMapper::toModel)
    }

    suspend fun transition(
        accountScopeId: AccountScopeId,
        itemId: LocalSyncItemId,
        to: SyncLifecycleState,
        waitReason: BackupWaitReason = BackupWaitReason.NONE,
        failureReason: BackupFailureReason = BackupFailureReason.NONE,
    ): Boolean =
        dao.transition(
            itemId.value,
            accountScopeId.value,
            to.name,
            waitReason.name,
            failureReason.name,
        )

    suspend fun recoverExpiredLeases(): Int = dao.recoverExpiredLeases(Instant.now(clock).toEpochMilli())

    @Suppress("MaxLineLength")
    fun onDatabaseRecreatedAfterCorruption(): LocalDatabaseRecoveryDirective = LocalDatabaseRecoveryDirective.RESCAN_AND_COMPARE
}
