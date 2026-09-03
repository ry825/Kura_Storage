package com.kurastorage.core.data.backup

import com.kurastorage.core.database.backup.BackupTransferPersistence
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.LocalSyncItem
import java.time.Duration
import java.time.Instant

class RoomBackupTransferStore(
    private val persistence: BackupTransferPersistence,
) : BackupTransferStore {
    override suspend fun enabledRules(scope: AccountScopeId) = persistence.enabledRules(scope)

    override suspend fun claim(
        scope: AccountScopeId,
        leaseOwner: String,
        now: Instant,
        duration: Duration,
        limit: Int,
    ) = persistence.claim(scope, leaseOwner, now, duration, limit)

    override suspend fun save(item: LocalSyncItem) = persistence.store(item)

    override suspend fun cleanupHistory(
        scope: AccountScopeId,
        now: Instant,
    ) = persistence.cleanupHistory(scope, now)
}
