package com.kurastorage.core.database.backup

import androidx.room.Dao
import androidx.room.Delete
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow

@Dao
interface BackupRuleDao {
    @Query("SELECT * FROM backup_rules WHERE account_scope_id = :scopeId ORDER BY created_at, id")
    fun observeByScope(scopeId: String): Flow<List<BackupRuleEntity>>

    @Query("SELECT * FROM backup_rules WHERE id = :id AND account_scope_id = :scopeId")
    suspend fun find(
        id: String,
        scopeId: String,
    ): BackupRuleEntity?

    @Upsert
    suspend fun upsert(rule: BackupRuleEntity)

    @Query(
        "UPDATE backup_rules SET enabled = :enabled, updated_at = :updatedAt " +
            "WHERE id = :id AND account_scope_id = :scopeId",
    )
    suspend fun setEnabled(
        id: String,
        scopeId: String,
        enabled: Boolean,
        updatedAt: Long,
    ): Int

    @Delete
    suspend fun delete(rule: BackupRuleEntity)
}

@Dao
@Suppress("LongParameterList", "TooManyFunctions")
interface LocalSyncItemDao {
    @Query(
        "SELECT * FROM local_sync_items WHERE account_scope_id = :scopeId " +
            "ORDER BY first_seen_at, id",
    )
    fun observeByScope(scopeId: String): Flow<List<LocalSyncItemEntity>>

    @Query("SELECT * FROM local_sync_items WHERE id = :id AND account_scope_id = :scopeId")
    suspend fun find(
        id: String,
        scopeId: String,
    ): LocalSyncItemEntity?

    @Query(
        "SELECT * FROM local_sync_items WHERE rule_id = :ruleId AND account_scope_id = :scopeId " +
            "AND local_document_key = :localDocumentKey",
    )
    suspend fun findByDocumentKey(
        ruleId: String,
        scopeId: String,
        localDocumentKey: String,
    ): LocalSyncItemEntity?

    @Upsert
    suspend fun upsertAll(items: List<LocalSyncItemEntity>)

    @Query(
        "SELECT * FROM local_sync_items WHERE account_scope_id = :scopeId " +
            "AND lifecycle_state = 'PENDING' " +
            "AND (next_attempt_at IS NULL OR next_attempt_at <= :now) " +
            "AND (lease_expires_at IS NULL OR lease_expires_at <= :now) " +
            "ORDER BY first_seen_at, id LIMIT :limit",
    )
    suspend fun findClaimCandidates(
        scopeId: String,
        now: Long,
        limit: Int,
    ): List<LocalSyncItemEntity>

    @Query(
        "UPDATE local_sync_items SET lease_owner = :leaseOwner, lease_expires_at = :leaseExpiresAt, " +
            "lifecycle_state = 'COMPARING', wait_reason = 'NONE', last_attempt_at = :now " +
            "WHERE id = :id AND lifecycle_state = 'PENDING' " +
            "AND (lease_expires_at IS NULL OR lease_expires_at <= :now)",
    )
    suspend fun claimCandidate(
        id: String,
        leaseOwner: String,
        leaseExpiresAt: Long,
        now: Long,
    ): Int

    @Transaction
    suspend fun claim(
        scopeId: String,
        leaseOwner: String,
        now: Long,
        leaseExpiresAt: Long,
        limit: Int,
    ): List<LocalSyncItemEntity> {
        require(limit in 1..MAX_CLAIM_LIMIT)
        return findClaimCandidates(scopeId, now, limit).mapNotNull { candidate ->
            if (claimCandidate(candidate.id, leaseOwner, leaseExpiresAt, now) == 1) {
                find(candidate.id, scopeId)
            } else {
                null
            }
        }
    }

    @Query(
        "UPDATE local_sync_items SET lifecycle_state = :toState, wait_reason = :waitReason, " +
            "failure_reason = :failureReason, lease_owner = NULL, lease_expires_at = NULL " +
            "WHERE id = :id AND account_scope_id = :scopeId AND lifecycle_state = :fromState",
    )
    suspend fun updateState(
        id: String,
        scopeId: String,
        fromState: String,
        toState: String,
        waitReason: String,
        failureReason: String,
    ): Int

    @Transaction
    suspend fun transition(
        id: String,
        scopeId: String,
        toState: String,
        waitReason: String,
        failureReason: String,
    ): Boolean {
        val item = find(id, scopeId) ?: return false
        val from = LocalSyncStateMachine.parse(item.lifecycleState)
        val to = LocalSyncStateMachine.parse(toState)
        LocalSyncStateMachine.requireTransition(from, to)
        return updateState(id, scopeId, from.name, to.name, waitReason, failureReason) == 1
    }

    @Query(
        "UPDATE local_sync_items SET lifecycle_state = 'PENDING', wait_reason = 'NONE', " +
            "lease_owner = NULL, lease_expires_at = NULL WHERE lease_expires_at <= :now " +
            "AND lifecycle_state IN ('COMPARING', 'READY_TO_UPLOAD', 'UPLOADING') " +
            "AND upload_session_id IS NULL",
    )
    suspend fun recoverExpiredWithoutSession(now: Long): Int

    @Query(
        "UPDATE local_sync_items SET lifecycle_state = 'PENDING', wait_reason = 'SERVER_RECONCILIATION', " +
            "lease_owner = NULL, lease_expires_at = NULL WHERE lease_expires_at <= :now " +
            "AND lifecycle_state IN ('COMPARING', 'READY_TO_UPLOAD', 'UPLOADING') " +
            "AND upload_session_id IS NOT NULL",
    )
    suspend fun recoverExpiredWithSession(now: Long): Int

    @Transaction
    @Suppress("MaxLineLength")
    suspend fun recoverExpiredLeases(now: Long): Int = recoverExpiredWithoutSession(now) + recoverExpiredWithSession(now)

    @Query(
        "SELECT lifecycle_state, COUNT(*) AS count FROM local_sync_items " +
            "WHERE account_scope_id = :scopeId GROUP BY lifecycle_state",
    )
    fun observeStateCounts(scopeId: String): Flow<List<BackupStateCountEntity>>

    @Query(
        "SELECT * FROM local_sync_items WHERE account_scope_id = :scopeId AND lifecycle_state = 'FAILED' " +
            "ORDER BY last_attempt_at DESC, id LIMIT :limit",
    )
    suspend fun failures(
        scopeId: String,
        limit: Int,
    ): List<LocalSyncItemEntity>

    @Query(
        "UPDATE local_sync_items SET lifecycle_state = 'LOCAL_MISSING', wait_reason = 'NONE', " +
            "failure_reason = 'NONE', lease_owner = NULL, lease_expires_at = NULL " +
            "WHERE rule_id = :ruleId AND account_scope_id = :scopeId AND last_seen_at < :scanStartedAt " +
            "AND lifecycle_state != 'LOCAL_MISSING'",
    )
    suspend fun markMissingNotSeenSince(
        ruleId: String,
        scopeId: String,
        scanStartedAt: Long,
    ): Int
}

private const val MAX_CLAIM_LIMIT = 100

@Dao
interface ExternalWifiPolicyDao {
    @Query(
        "SELECT * FROM external_wifi_policies WHERE account_scope_id = :scopeId " +
            "ORDER BY display_name COLLATE NOCASE, id",
    )
    fun observeByScope(scopeId: String): Flow<List<ExternalWifiPolicyEntity>>

    @Query("SELECT COUNT(*) FROM external_wifi_policies WHERE account_scope_id = :scopeId")
    suspend fun count(scopeId: String): Int

    @Query("SELECT * FROM external_wifi_policies WHERE id = :id AND account_scope_id = :scopeId")
    suspend fun find(
        id: String,
        scopeId: String,
    ): ExternalWifiPolicyEntity?

    @Query(
        "SELECT * FROM external_wifi_policies WHERE account_scope_id = :scopeId " +
            "AND normalized_ssid = :normalizedSsid AND normalized_bssid_key = :normalizedBssidKey",
    )
    suspend fun findByNetwork(
        scopeId: String,
        normalizedSsid: String,
        normalizedBssidKey: String,
    ): ExternalWifiPolicyEntity?

    @Upsert
    suspend fun upsert(policy: ExternalWifiPolicyEntity)

    @Query("DELETE FROM external_wifi_policies WHERE id = :id AND account_scope_id = :scopeId")
    suspend fun delete(
        id: String,
        scopeId: String,
    ): Int
}

@Dao
interface ScanCheckpointDao {
    @Query("SELECT * FROM scan_checkpoints WHERE rule_id = :ruleId")
    suspend fun find(ruleId: String): ScanCheckpointEntity?

    @Upsert
    suspend fun upsert(checkpoint: ScanCheckpointEntity)
}

@Dao
interface SourceIdentityMappingDao {
    @Query("SELECT * FROM source_identity_mappings WHERE rule_id = :ruleId AND provider_key = :providerKey")
    suspend fun find(
        ruleId: String,
        providerKey: String,
    ): SourceIdentityMappingEntity?

    @Upsert
    suspend fun upsert(mapping: SourceIdentityMappingEntity)
}
