package com.kurastorage.core.data.backup

import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.LocalBackupRule

private const val MINIMUM_BATTERY_PERCENT = 0
private const val MAXIMUM_BATTERY_PERCENT = 100

enum class BaseNetworkTransport {
    WIFI,
    ETHERNET,
    MOBILE,
    NONE,
}

enum class BackupExecutionMode {
    AUTO_BACKUP_ALLOWED,
    MANUAL_ONLY,
    BLOCKED,
}

data class BackupConnectionSnapshot(
    val status: ConnectionStatus,
    val generation: Long,
    val baseTransport: BaseNetworkTransport,
    val boundNetworkId: String? = null,
)

data class BackupPowerSnapshot(
    val batteryPercent: Int,
    val charging: Boolean,
) {
    init {
        require(batteryPercent in MINIMUM_BATTERY_PERCENT..MAXIMUM_BATTERY_PERCENT)
    }
}

data class BackupPolicyContext(
    val rule: LocalBackupRule,
    val sourcePermissionGranted: Boolean,
    val authenticated: Boolean,
    val connection: BackupConnectionSnapshot,
    val power: BackupPowerSnapshot,
    val currentWifi: CurrentWifiResult,
    val externalWifiPolicies: List<ExternalWifiPolicy>,
)

data class BackupPolicyDecision(
    val mode: BackupExecutionMode,
    val waitReason: BackupWaitReason,
    val route: ConnectionRoute? = null,
    val boundNetworkId: String? = null,
    val connectionGeneration: Long,
) {
    val allowed: Boolean get() = mode == BackupExecutionMode.AUTO_BACKUP_ALLOWED
}

fun interface ExternalWifiMatcher {
    fun matches(
        policy: ExternalWifiPolicy,
        current: ConnectedWifi,
    ): Boolean
}

class NetworkPolicyEvaluator(
    private val wifiMatcher: ExternalWifiMatcher,
) {
    @Suppress("ReturnCount", "CyclomaticComplexMethod")
    fun evaluate(context: BackupPolicyContext): BackupPolicyDecision {
        val connection = context.connection

        fun blocked(reason: BackupWaitReason) =
            BackupPolicyDecision(BackupExecutionMode.BLOCKED, reason, connectionGeneration = connection.generation)

        if (!context.rule.enabled || context.rule.pausedAt != null) return blocked(BackupWaitReason.NONE)
        if (!context.sourcePermissionGranted) return blocked(BackupWaitReason.SOURCE_PERMISSION)
        if (!context.authenticated) return blocked(BackupWaitReason.AUTHENTICATION)
        if (context.power.batteryPercent < context.rule.minimumBatteryPercent) {
            return blocked(BackupWaitReason.BATTERY)
        }
        if (
            context.rule.initialRunCompletedAt == null &&
            context.rule.requiresChargingForInitialRun &&
            !context.power.charging
        ) {
            return blocked(BackupWaitReason.CHARGING)
        }
        if (connection.baseTransport == BaseNetworkTransport.MOBILE) {
            return BackupPolicyDecision(
                BackupExecutionMode.MANUAL_ONLY,
                BackupWaitReason.NETWORK,
                connectionGeneration = connection.generation,
            )
        }
        if (connection.baseTransport == BaseNetworkTransport.NONE) return blocked(BackupWaitReason.NETWORK)
        val connected = connection.status as? ConnectionStatus.Connected ?: return blocked(BackupWaitReason.NETWORK)
        if (connected.storage != StorageAvailability.AVAILABLE) return blocked(BackupWaitReason.STORAGE)
        return when (connected.route) {
            ConnectionRoute.LOCAL_DIRECT -> localDirect(connection)
            ConnectionRoute.REMOTE_SECURE -> remoteSecure(context)
        }
    }

    private fun localDirect(connection: BackupConnectionSnapshot): BackupPolicyDecision {
        val networkId = connection.boundNetworkId
        return if (
            connection.baseTransport in setOf(BaseNetworkTransport.WIFI, BaseNetworkTransport.ETHERNET) &&
            !networkId.isNullOrBlank()
        ) {
            BackupPolicyDecision(
                BackupExecutionMode.AUTO_BACKUP_ALLOWED,
                BackupWaitReason.NONE,
                ConnectionRoute.LOCAL_DIRECT,
                networkId,
                connection.generation,
            )
        } else {
            BackupPolicyDecision(
                BackupExecutionMode.BLOCKED,
                BackupWaitReason.NETWORK,
                connectionGeneration = connection.generation,
            )
        }
    }

    @Suppress("ReturnCount")
    private fun remoteSecure(context: BackupPolicyContext): BackupPolicyDecision {
        val connection = context.connection
        if (context.rule.networkMode == BackupNetworkMode.LOCAL_DIRECT_ONLY) {
            return BackupPolicyDecision(
                BackupExecutionMode.MANUAL_ONLY,
                BackupWaitReason.ALLOWED_WIFI,
                connectionGeneration = connection.generation,
            )
        }
        if (connection.baseTransport != BaseNetworkTransport.WIFI) {
            return BackupPolicyDecision(
                BackupExecutionMode.MANUAL_ONLY,
                BackupWaitReason.NETWORK,
                connectionGeneration = connection.generation,
            )
        }
        val wifi =
            (context.currentWifi as? CurrentWifiResult.Available)?.wifi
                ?: return BackupPolicyDecision(
                    BackupExecutionMode.BLOCKED,
                    BackupWaitReason.ALLOWED_WIFI,
                    connectionGeneration = connection.generation,
                )
        val allowed = context.externalWifiPolicies.any { wifiMatcher.matches(it, wifi) }
        return if (allowed) {
            BackupPolicyDecision(
                BackupExecutionMode.AUTO_BACKUP_ALLOWED,
                BackupWaitReason.NONE,
                ConnectionRoute.REMOTE_SECURE,
                connectionGeneration = connection.generation,
            )
        } else {
            BackupPolicyDecision(
                BackupExecutionMode.BLOCKED,
                BackupWaitReason.ALLOWED_WIFI,
                connectionGeneration = connection.generation,
            )
        }
    }
}
