package com.kurastorage.feature.backup

import com.kurastorage.core.data.backup.BackupRuleRepository
import com.kurastorage.core.data.backup.ConnectedWifi
import com.kurastorage.core.data.backup.CreateBackupRuleCommand
import com.kurastorage.core.data.backup.ExternalWifiPolicyRepository
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import com.kurastorage.core.model.backup.LocalBackupRule
import kotlinx.coroutines.flow.Flow

private const val WORK_NAME_HASH_BYTES = 16
private const val UNSIGNED_BYTE_MASK = 0xff

class BackupRuleUseCases(
    private val repository: BackupRuleRepository,
) {
    fun observe(accountScopeId: AccountScopeId): Flow<List<LocalBackupRule>> = repository.observe(accountScopeId)

    suspend fun create(
        accountScopeId: AccountScopeId,
        command: CreateBackupRuleCommand,
    ): LocalBackupRule = repository.create(accountScopeId, command)

    suspend fun setEnabled(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
        enabled: Boolean,
    ) = repository.setEnabled(accountScopeId, ruleId, enabled)

    suspend fun save(
        accountScopeId: AccountScopeId,
        rule: LocalBackupRule,
    ) = repository.save(accountScopeId, rule)

    suspend fun delete(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
    ) = repository.delete(accountScopeId, ruleId)
}

class ExternalWifiPolicyUseCases(
    private val repository: ExternalWifiPolicyRepository,
) {
    fun observe(accountScopeId: AccountScopeId): Flow<List<ExternalWifiPolicy>> = repository.observe(accountScopeId)

    fun currentWifi() = repository.currentWifi()

    suspend fun registerCurrent(
        accountScopeId: AccountScopeId,
        wifi: ConnectedWifi,
        displayName: String,
        restrictToBssid: Boolean,
        treatAsMetered: Boolean,
    ) = repository.registerCurrent(accountScopeId, wifi, displayName, restrictToBssid, treatAsMetered)

    suspend fun save(
        accountScopeId: AccountScopeId,
        policy: ExternalWifiPolicy,
    ) = repository.save(accountScopeId, policy)

    suspend fun delete(
        accountScopeId: AccountScopeId,
        policyId: ExternalWifiPolicyId,
    ) = repository.delete(accountScopeId, policyId)
}

object BackupWorkNames {
    fun transfer(accountScopeId: AccountScopeId): String = "backup-transfer:${stableShortHash(accountScopeId.value)}"

    fun scan(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
    ): String = "backup-scan:${stableShortHash("${accountScopeId.value}:${ruleId.value}")}"

    private fun stableShortHash(value: String): String =
        java.security.MessageDigest
            .getInstance("SHA-256")
            .digest(value.toByteArray())
            .take(WORK_NAME_HASH_BYTES)
            .joinToString("") { byte -> "%02x".format(byte.toInt() and UNSIGNED_BYTE_MASK) }
}
