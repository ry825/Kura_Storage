package com.kurastorage.core.database.backup

import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.ScanCheckpoint
import com.kurastorage.core.model.backup.SyncLifecycleState
import java.time.Instant
import java.util.UUID

private const val MAX_SCAN_BATCH_SIZE = 500

data class PersistedScanDocument(
    val localDocumentKey: String,
    val identityDiscriminator: String,
    val relativePath: String,
    val displayName: String,
    val size: Long,
    val modifiedAtMillis: Long,
    val checksum: String?,
)

data class ScanPersistenceCandidate(
    val providerKey: String,
    val identityDiscriminator: String,
    val localDocumentKey: String,
    val sourceLocator: String,
    val relativePath: String,
    val displayName: String,
    val size: Long,
    val modifiedAtMillis: Long,
    val checksum: String,
    val sourceFingerprint: String,
)

class BackupScanPersistence(
    private val database: BackupDatabaseAccess,
) {
    suspend fun checkpoint(ruleId: BackupRuleId): ScanCheckpoint? =
        database.scanCheckpointDao().find(ruleId.value)?.let(BackupEntityMapper::toModel)

    suspend fun existing(
        rule: LocalBackupRule,
        providerKey: String,
    ): PersistedScanDocument? {
        val mapping = database.sourceIdentityMappingDao().find(rule.id.value, providerKey) ?: return null
        val item =
            database.localSyncItemDao().findByDocumentKey(
                rule.id.value,
                rule.accountScopeId.value,
                mapping.localDocumentKey,
            )
        return PersistedScanDocument(
            localDocumentKey = mapping.localDocumentKey,
            identityDiscriminator = mapping.identityDiscriminator,
            relativePath = item?.relativePath.orEmpty(),
            displayName = item?.displayName.orEmpty(),
            size = item?.size ?: -1,
            modifiedAtMillis = item?.modifiedAt ?: -1,
            checksum = item?.checksum,
        )
    }

    suspend fun applyBatch(
        rule: LocalBackupRule,
        documents: List<ScanPersistenceCandidate>,
        observedAt: Instant,
    ) {
        require(documents.size in 1..MAX_SCAN_BATCH_SIZE)
        database.inTransaction {
            val itemDao = database.localSyncItemDao()
            val identityDao = database.sourceIdentityMappingDao()
            val entities =
                documents.map { document ->
                    val priorMapping = identityDao.find(rule.id.value, document.providerKey)
                    identityDao.upsert(
                        SourceIdentityMappingEntity(
                            ruleId = rule.id.value,
                            providerKey = document.providerKey,
                            identityDiscriminator = document.identityDiscriminator,
                            localDocumentKey = document.localDocumentKey,
                            firstSeenAt = priorMapping?.firstSeenAt ?: observedAt.toEpochMilli(),
                            lastSeenAt = observedAt.toEpochMilli(),
                        ),
                    )
                    val existing =
                        itemDao.findByDocumentKey(
                            rule.id.value,
                            rule.accountScopeId.value,
                            document.localDocumentKey,
                        )
                    merge(rule, document, existing, observedAt)
                }
            itemDao.upsertAll(entities)
        }
    }

    suspend fun complete(
        rule: LocalBackupRule,
        mediaStoreVersion: String?,
        generation: Long?,
        observedAt: Instant,
        wasFullScan: Boolean,
    ) {
        database.inTransaction {
            if (wasFullScan) {
                database.localSyncItemDao().markMissingNotSeenSince(
                    rule.id.value,
                    rule.accountScopeId.value,
                    observedAt.toEpochMilli(),
                )
            }
            database.scanCheckpointDao().upsert(
                BackupEntityMapper.toEntity(
                    ScanCheckpoint(
                        ruleId = rule.id,
                        mediaStoreVersion = mediaStoreVersion,
                        generation = generation,
                        fullScanToken = null,
                        lastCompletedAt = observedAt,
                        updatedAt = observedAt,
                    ),
                ),
            )
        }
    }

    private fun merge(
        rule: LocalBackupRule,
        document: ScanPersistenceCandidate,
        existing: LocalSyncItemEntity?,
        observedAt: Instant,
    ): LocalSyncItemEntity {
        val unchanged =
            existing?.sourceFingerprint == document.sourceFingerprint &&
                existing.checksum == document.checksum
        if (existing != null && unchanged && existing.lifecycleState != SyncLifecycleState.LOCAL_MISSING.name) {
            return existing.copy(sourceLocator = document.sourceLocator, lastSeenAt = observedAt.toEpochMilli())
        }
        return LocalSyncItemEntity(
            id = existing?.id ?: UUID.randomUUID().toString(),
            accountScopeId = rule.accountScopeId.value,
            ruleId = rule.id.value,
            localDocumentKey = document.localDocumentKey,
            sourceLocator = document.sourceLocator,
            relativePath = document.relativePath,
            displayName = document.displayName,
            size = document.size,
            modifiedAt = document.modifiedAtMillis,
            checksum = document.checksum,
            sourceFingerprint = document.sourceFingerprint,
            remoteFileId = existing?.remoteFileId,
            remoteFileVersion = existing?.remoteFileVersion,
            lifecycleState = SyncLifecycleState.PENDING.name,
            waitReason = BackupWaitReason.NONE.name,
            failureReason = BackupFailureReason.NONE.name,
            retryCount = 0,
            nextAttemptAt = null,
            leaseOwner = null,
            leaseExpiresAt = null,
            uploadSessionId = null,
            idempotencyKey = null,
            confirmedOffset = 0,
            firstSeenAt = existing?.firstSeenAt ?: observedAt.toEpochMilli(),
            lastSeenAt = observedAt.toEpochMilli(),
            lastAttemptAt = existing?.lastAttemptAt,
            completedAt = existing?.completedAt,
        )
    }
}
