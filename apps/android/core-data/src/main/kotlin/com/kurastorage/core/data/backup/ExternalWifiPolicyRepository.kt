package com.kurastorage.core.data.backup

import com.kurastorage.core.database.backup.BackupEntityMapper
import com.kurastorage.core.database.backup.ExternalWifiPolicyDao
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.time.Clock
import java.time.Instant
import java.util.Locale
import java.util.UUID

private const val MAX_SSID_CODE_POINTS = 32

data class ConnectedWifi(
    val ssid: String,
    val bssid: String?,
    val systemMetered: Boolean,
)

sealed interface CurrentWifiResult {
    data class Available(
        val wifi: ConnectedWifi,
    ) : CurrentWifiResult

    data class PermissionRequired(
        val permissions: Set<String>,
    ) : CurrentWifiResult

    data object PermissionPermanentlyDenied : CurrentWifiResult

    data object LocationServicesDisabled : CurrentWifiResult

    data object NotConnected : CurrentWifiResult

    data object Unavailable : CurrentWifiResult
}

fun interface CurrentWifiSource {
    fun read(): CurrentWifiResult
}

object WifiIdentifierNormalizer {
    private val bssidPattern = Regex("(?:[0-9a-f]{2}:){5}[0-9a-f]{2}")
    private val unavailableBssids = setOf("00:00:00:00:00:00", "02:00:00:00:00:00", "ff:ff:ff:ff:ff:ff")

    fun normalizeSsid(raw: String): String {
        val normalized = raw.trim().removeSurrounding("\"").normalizeControls()
        require(normalized.isNotBlank() && !normalized.equals("<unknown ssid>", ignoreCase = true))
        require(normalized.codePointCount(0, normalized.length) <= MAX_SSID_CODE_POINTS)
        return normalized
    }

    fun normalizeBssid(raw: String?): String? {
        if (raw == null) return null
        val normalized = raw.trim().lowercase(Locale.ROOT).replace('-', ':')
        require(bssidPattern.matches(normalized) && normalized !in unavailableBssids)
        return normalized
    }

    private fun String.normalizeControls(): String {
        require(none(Char::isISOControl))
        return this
    }
}

interface ExternalWifiPolicyRepository {
    fun observe(accountScopeId: AccountScopeId): Flow<List<ExternalWifiPolicy>>

    fun currentWifi(): CurrentWifiResult

    suspend fun registerCurrent(
        accountScopeId: AccountScopeId,
        wifi: ConnectedWifi,
        displayName: String,
        restrictToBssid: Boolean,
        treatAsMetered: Boolean,
    ): ExternalWifiPolicy

    suspend fun save(
        accountScopeId: AccountScopeId,
        policy: ExternalWifiPolicy,
    )

    suspend fun delete(
        accountScopeId: AccountScopeId,
        policyId: ExternalWifiPolicyId,
    )

    fun matchesCurrentWifi(
        policy: ExternalWifiPolicy,
        current: ConnectedWifi,
    ): Boolean
}

class RoomExternalWifiPolicyRepository(
    private val dao: ExternalWifiPolicyDao,
    private val wifiSource: CurrentWifiSource,
    private val clock: Clock = Clock.systemUTC(),
) : ExternalWifiPolicyRepository {
    override fun observe(accountScopeId: AccountScopeId): Flow<List<ExternalWifiPolicy>> =
        dao.observeByScope(accountScopeId.value).map { policies -> policies.map(BackupEntityMapper::toModel) }

    override fun currentWifi(): CurrentWifiResult = wifiSource.read()

    override suspend fun registerCurrent(
        accountScopeId: AccountScopeId,
        wifi: ConnectedWifi,
        displayName: String,
        restrictToBssid: Boolean,
        treatAsMetered: Boolean,
    ): ExternalWifiPolicy {
        val now = Instant.now(clock)
        val normalizedSsid = WifiIdentifierNormalizer.normalizeSsid(wifi.ssid)
        val normalizedBssid =
            if (restrictToBssid) {
                requireNotNull(WifiIdentifierNormalizer.normalizeBssid(wifi.bssid)) {
                    "Current Wi-Fi BSSID is unavailable"
                }
            } else {
                null
            }
        require(dao.findByNetwork(accountScopeId.value, normalizedSsid, normalizedBssid.orEmpty()) == null) {
            "External Wi-Fi policy is already registered"
        }
        require(dao.count(accountScopeId.value) < MAX_POLICIES) { "External Wi-Fi policy limit reached" }
        val policy =
            ExternalWifiPolicy(
                id = ExternalWifiPolicyId(UUID.randomUUID().toString()),
                accountScopeId = accountScopeId,
                displayName = displayName.trim(),
                normalizedSsid = normalizedSsid,
                normalizedBssid = normalizedBssid,
                treatAsMetered = treatAsMetered || wifi.systemMetered,
                enabled = true,
                createdAt = now,
                updatedAt = now,
            )
        dao.upsert(BackupEntityMapper.toEntity(policy))
        return policy
    }

    override suspend fun save(
        accountScopeId: AccountScopeId,
        policy: ExternalWifiPolicy,
    ) {
        require(policy.accountScopeId == accountScopeId)
        requireNotNull(dao.find(policy.id.value, accountScopeId.value))
        require(WifiIdentifierNormalizer.normalizeSsid(policy.normalizedSsid) == policy.normalizedSsid)
        require(WifiIdentifierNormalizer.normalizeBssid(policy.normalizedBssid) == policy.normalizedBssid)
        val duplicate =
            dao.findByNetwork(
                accountScopeId.value,
                policy.normalizedSsid,
                policy.normalizedBssid.orEmpty(),
            )
        require(duplicate == null || duplicate.id == policy.id.value) {
            "External Wi-Fi policy is already registered"
        }
        dao.upsert(BackupEntityMapper.toEntity(policy.copy(updatedAt = Instant.now(clock))))
    }

    override suspend fun delete(
        accountScopeId: AccountScopeId,
        policyId: ExternalWifiPolicyId,
    ) {
        require(dao.delete(policyId.value, accountScopeId.value) == 1) {
            "External Wi-Fi policy was not found in the active account scope"
        }
    }

    override fun matchesCurrentWifi(
        policy: ExternalWifiPolicy,
        current: ConnectedWifi,
    ): Boolean {
        val eligible = policy.enabled && !policy.treatAsMetered && !current.systemMetered
        val ssid = runCatching { WifiIdentifierNormalizer.normalizeSsid(current.ssid) }.getOrNull()
        val bssid = runCatching { WifiIdentifierNormalizer.normalizeBssid(current.bssid) }.getOrNull()
        return eligible &&
            ssid != null &&
            policy.normalizedSsid == ssid &&
            (policy.normalizedBssid == null || policy.normalizedBssid == bssid)
    }

    private companion object {
        const val MAX_POLICIES = 50
    }
}
