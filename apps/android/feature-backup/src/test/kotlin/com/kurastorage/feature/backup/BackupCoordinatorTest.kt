package com.kurastorage.feature.backup

import com.kurastorage.core.data.backup.ScanTrigger
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupRuleId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.UUID

class BackupCoordinatorTest {
    @Test
    fun largeBatchThresholdRequiresForegroundWithoutAlwaysOnService() {
        assertFalse(BackupForegroundEstimate(99, 100L * 1024 * 1024 - 1).requiresForeground)
        assertTrue(BackupForegroundEstimate(100, 1).requiresForeground)
        assertTrue(BackupForegroundEstimate(1, 100L * 1024 * 1024).requiresForeground)
        assertFalse(BackupBackgroundCapability().usesAlwaysOnForegroundService)
    }

    @Test
    fun allTriggersConvergeOnUniqueScanAndAccountTransferNames() {
        val enqueuer = RecordingEnqueuer()
        val coordinator = BackupCoordinator(enqueuer)
        val rule = BackupRuleId(UUID.randomUUID().toString())

        coordinator.onAppStarted(SCOPE, listOf(rule, rule))
        coordinator.onContentChanged(SCOPE, rule)
        coordinator.onPendingAdded(SCOPE, rule)
        coordinator.onAllowedConnection(SCOPE, listOf(rule))
        coordinator.runNow(SCOPE, listOf(rule))
        coordinator.scheduleSafPeriodic(SCOPE, rule)

        assertEquals(
            listOf(
                ScanTrigger.APP_START,
                ScanTrigger.CONTENT_CHANGED,
                ScanTrigger.PENDING_ADDED,
                ScanTrigger.ALLOWED_CONNECTION,
                ScanTrigger.MANUAL,
            ),
            enqueuer.scans.map { it.third },
        )
        assertEquals(5, enqueuer.transfers.size)
        assertEquals(listOf(rule), enqueuer.periodic)
        assertFalse(coordinator.backgroundCapability.usesAlwaysOnForegroundService)
        assertTrue(coordinator.backgroundCapability.requiresAppRestartAfterForceStop)
    }

    private companion object {
        val SCOPE = AccountScopeId("a".repeat(64))
    }
}

private class RecordingEnqueuer : BackupWorkEnqueuer {
    val scans = mutableListOf<Triple<AccountScopeId, BackupRuleId, ScanTrigger>>()
    val periodic = mutableListOf<BackupRuleId>()
    val transfers = mutableListOf<AccountScopeId>()

    override fun enqueueScan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
        trigger: ScanTrigger,
    ) {
        scans += Triple(scope, ruleId, trigger)
    }

    override fun enqueuePeriodicSafScan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
    ) {
        periodic += ruleId
    }

    override fun enqueueTransfer(scope: AccountScopeId) {
        transfers += scope
    }
}
