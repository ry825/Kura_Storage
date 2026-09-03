@file:Suppress("LongParameterList", "MaxLineLength")

package com.kurastorage.core.data.backup

import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import com.kurastorage.core.model.backup.LocalBackupRule
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.util.UUID

class BackupNetworkPolicyTest {
    private val evaluator =
        NetworkPolicyEvaluator { policy, wifi ->
            policy.enabled &&
                !policy.treatAsMetered &&
                !wifi.systemMetered &&
                policy.normalizedSsid == wifi.ssid &&
                (policy.normalizedBssid == null || policy.normalizedBssid == wifi.bssid)
        }

    @Test
    fun localDirectRequiresBoundWifiOrEthernetButNeverAnSsidPolicy() {
        val decision = evaluate(connection = local(BaseNetworkTransport.ETHERNET), wifi = CurrentWifiResult.InformationUnavailable)

        assertTrue(decision.allowed)
        assertEquals(ConnectionRoute.LOCAL_DIRECT, decision.route)
        assertEquals("base-network", decision.boundNetworkId)

        val unbound = evaluate(connection = local(BaseNetworkTransport.WIFI).copy(boundNetworkId = null))
        assertFalse(unbound.allowed)
        assertEquals(BackupWaitReason.NETWORK, unbound.waitReason)
    }

    @Test
    fun remoteSecureRequiresOptInWifiMatchAndRejectsMeteredOrUnregisteredWifi() {
        val allowed = evaluate(connection = remote(), policies = listOf(policy()))
        assertTrue(allowed.allowed)

        val metered =
            evaluate(
                connection = remote(),
                wifi = CurrentWifiResult.Connected(ConnectedWifi("allowed", "aa:bb:cc:dd:ee:ff", true)),
                policies = listOf(policy()),
            )
        assertEquals(BackupExecutionMode.BLOCKED, metered.mode)
        assertEquals(BackupWaitReason.ALLOWED_WIFI, metered.waitReason)

        val localOnly = evaluate(rule = rule(BackupNetworkMode.LOCAL_DIRECT_ONLY), connection = remote())
        assertEquals(BackupExecutionMode.MANUAL_ONLY, localOnly.mode)
    }

    @Test
    fun mobileWithZeroTierIsManualOnlyAndStorageOrAuthenticationFailClosed() {
        val mobile = evaluate(connection = remote(BaseNetworkTransport.MOBILE), policies = listOf(policy()))
        assertEquals(BackupExecutionMode.MANUAL_ONLY, mobile.mode)

        val unavailable =
            evaluate(
                connection =
                    remote().copy(
                        status = ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.UNAVAILABLE),
                    ),
            )
        assertEquals(BackupWaitReason.STORAGE, unavailable.waitReason)

        val unauthenticated = evaluate(connection = local(), authenticated = false)
        assertEquals(BackupWaitReason.AUTHENTICATION, unauthenticated.waitReason)
    }

    @Test
    fun powerSourcePermissionAndRuleStateAreIndependentGates() {
        assertEquals(
            BackupWaitReason.BATTERY,
            evaluate(connection = local(), power = BackupPowerSnapshot(19, true)).waitReason,
        )
        assertEquals(
            BackupWaitReason.CHARGING,
            evaluate(connection = local(), power = BackupPowerSnapshot(80, false)).waitReason,
        )
        assertEquals(
            BackupWaitReason.SOURCE_PERMISSION,
            evaluate(connection = local(), sourcePermission = false).waitReason,
        )
        assertFalse(evaluate(rule = rule().copy(enabled = false), connection = local()).allowed)
    }

    @Test
    fun connectionStateAndBaseTransportMatrixFailsClosed() {
        listOf(
            ConnectionStatus.Disconnected,
            ConnectionStatus.TlsFailure,
            ConnectionStatus.IncompatibleProtocol,
        ).forEach { status ->
            val connection = BackupConnectionSnapshot(status, 1, BaseNetworkTransport.WIFI)
            assertEquals(BackupWaitReason.NETWORK, evaluate(connection = connection).waitReason)
        }

        assertTrue(evaluate(connection = local(BaseNetworkTransport.WIFI)).allowed)
        assertTrue(evaluate(connection = local(BaseNetworkTransport.ETHERNET)).allowed)
        assertEquals(
            BackupExecutionMode.MANUAL_ONLY,
            evaluate(connection = local(BaseNetworkTransport.MOBILE)).mode,
        )
        assertEquals(
            BackupExecutionMode.BLOCKED,
            evaluate(connection = local(BaseNetworkTransport.NONE)).mode,
        )
        assertEquals(
            BackupExecutionMode.MANUAL_ONLY,
            evaluate(connection = remote(BaseNetworkTransport.ETHERNET), policies = listOf(policy())).mode,
        )

        val wrongBssid =
            CurrentWifiResult.Connected(
                ConnectedWifi("allowed", "11:22:33:44:55:66", systemMetered = false),
            )
        assertEquals(
            BackupExecutionMode.BLOCKED,
            evaluate(connection = remote(), wifi = wrongBssid, policies = listOf(policy())).mode,
        )
        assertEquals(
            BackupExecutionMode.BLOCKED,
            evaluate(
                connection = remote(),
                wifi = CurrentWifiResult.PermissionRequired(setOf("permission")),
                policies = listOf(policy()),
            ).mode,
        )
    }

    private fun evaluate(
        rule: LocalBackupRule = rule(),
        connection: BackupConnectionSnapshot,
        power: BackupPowerSnapshot = BackupPowerSnapshot(80, true),
        wifi: CurrentWifiResult = CurrentWifiResult.Connected(ConnectedWifi("allowed", "aa:bb:cc:dd:ee:ff", false)),
        policies: List<ExternalWifiPolicy> = emptyList(),
        authenticated: Boolean = true,
        sourcePermission: Boolean = true,
    ) = evaluator.evaluate(
        BackupPolicyContext(rule, sourcePermission, authenticated, connection, power, wifi, policies),
    )

    private fun local(transport: BaseNetworkTransport = BaseNetworkTransport.WIFI) =
        BackupConnectionSnapshot(
            ConnectionStatus.Connected(ConnectionRoute.LOCAL_DIRECT, StorageAvailability.AVAILABLE),
            generation = 7,
            baseTransport = transport,
            boundNetworkId = "base-network",
        )

    private fun remote(transport: BaseNetworkTransport = BaseNetworkTransport.WIFI) =
        BackupConnectionSnapshot(
            ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.AVAILABLE),
            generation = 8,
            baseTransport = transport,
        )

    private fun rule(mode: BackupNetworkMode = BackupNetworkMode.LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER) =
        LocalBackupRule(
            BackupRuleId(UUID.randomUUID().toString()),
            AccountScopeId("a".repeat(64)),
            BackupSourceType.MEDIA_IMAGES,
            "external",
            "Photos",
            UUID.randomUUID().toString(),
            true,
            mode,
            true,
            20,
            null,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
        )

    private fun policy() =
        ExternalWifiPolicy(
            ExternalWifiPolicyId(UUID.randomUUID().toString()),
            AccountScopeId("a".repeat(64)),
            "Allowed",
            "allowed",
            "aa:bb:cc:dd:ee:ff",
            false,
            true,
            Instant.EPOCH,
            Instant.EPOCH,
        )
}
