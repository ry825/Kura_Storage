package com.kurastorage.core.data.backup

import com.kurastorage.core.database.backup.BackupEntityMapper
import com.kurastorage.core.database.backup.LocalSyncItemDao
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupStateCount
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.SyncLifecycleState
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.map
import java.time.Clock
import java.time.Duration
import java.time.Instant

private const val MAX_LEASE_OWNER_LENGTH = 128

enum class LocalDatabaseRecoveryDirective {
    RESCAN_AND_COMPARE,
}

interface BackupStateRepository {
    fun observeItems(
        accountScopeId: AccountScopeId,
        limit: Int,
    ): Flow<List<LocalSyncItem>>

    fun observeProgress(accountScopeId: AccountScopeId): Flow<BackupProgressSnapshot>

    suspend fun retryFailed(
        accountScopeId: AccountScopeId,
        itemId: LocalSyncItemId,
    ): Boolean

    suspend fun retryAllFailed(accountScopeId: AccountScopeId): Int
}

class LocalBackupStateRepository(
    private val dao: LocalSyncItemDao,
    private val clock: Clock = Clock.systemUTC(),
) : BackupStateRepository {
    override fun observeItems(
        accountScopeId: AccountScopeId,
        limit: Int,
    ): Flow<List<LocalSyncItem>> {
        require(limit in 1..MAXIMUM_HISTORY_PAGE_ITEMS)
        return dao.observeHistory(accountScopeId.value, limit).map { items -> items.map(BackupEntityMapper::toModel) }
    }

    fun observeCounts(accountScopeId: AccountScopeId): Flow<List<BackupStateCount>> =
        dao.observeStateCounts(accountScopeId.value).map { counts -> counts.map(BackupEntityMapper::toModel) }

    override fun observeProgress(accountScopeId: AccountScopeId): Flow<BackupProgressSnapshot> =
        combine(
            dao.observeStateCounts(accountScopeId.value),
            dao.observeRuleStateCounts(accountScopeId.value),
            dao.observeWaitReasonCounts(accountScopeId.value),
            dao.observeLastCompletedAt(accountScopeId.value),
        ) { totals, rules, waits, lastCompletedAt ->
            BackupProgressSnapshot(
                totals.associate { enumValueOf<SyncLifecycleState>(it.lifecycleState) to it.count },
                rules
                    .groupBy { BackupRuleId(it.ruleId) }
                    .mapValues { (_, values) ->
                        values.associate { enumValueOf<SyncLifecycleState>(it.lifecycleState) to it.count }
                    },
                waits.associate { enumValueOf<BackupWaitReason>(it.waitReason) to it.count },
                lastCompletedAt?.let(Instant::ofEpochMilli),
            )
        }

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

    override suspend fun retryFailed(
        accountScopeId: AccountScopeId,
        itemId: LocalSyncItemId,
    ): Boolean = dao.retryFailed(itemId.value, accountScopeId.value) == 1

    override suspend fun retryAllFailed(accountScopeId: AccountScopeId): Int = dao.retryAllFailed(accountScopeId.value)

    @Suppress("MaxLineLength")
    fun onDatabaseRecreatedAfterCorruption(): LocalDatabaseRecoveryDirective = LocalDatabaseRecoveryDirective.RESCAN_AND_COMPARE
}

data class BackupProgressSnapshot(
    val stateCounts: Map<SyncLifecycleState, Int>,
    val ruleStateCounts: Map<BackupRuleId, Map<SyncLifecycleState, Int>>,
    val waitReasonCounts: Map<BackupWaitReason, Int>,
    val lastCompletedAt: Instant?,
)

private const val MAXIMUM_HISTORY_PAGE_ITEMS = 10_001
