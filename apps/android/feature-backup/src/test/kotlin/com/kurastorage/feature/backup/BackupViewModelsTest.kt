package com.kurastorage.feature.backup

import com.kurastorage.core.data.backup.BackupProgressSnapshot
import com.kurastorage.core.data.backup.BackupRuleRepository
import com.kurastorage.core.data.backup.BackupSourceAccess
import com.kurastorage.core.data.backup.BackupStateRepository
import com.kurastorage.core.data.backup.ConnectedWifi
import com.kurastorage.core.data.backup.CreateBackupRuleCommand
import com.kurastorage.core.data.backup.CurrentWifiResult
import com.kurastorage.core.data.backup.ExternalWifiPolicyRepository
import com.kurastorage.core.data.backup.ScanTrigger
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.SyncLifecycleState
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant
import java.util.UUID

@OptIn(ExperimentalCoroutinesApi::class)
class BackupViewModelsTest {
    private val dispatcher = StandardTestDispatcher()
    private val scope = AccountScopeId("a".repeat(64))

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `overview keeps account scoped state and coalesces run pause and retry operations`() =
        runTest(dispatcher) {
            val ruleRepository = FakeRules(scope)
            val failed = item(scope, SyncLifecycleState.FAILED)
            val stateRepository = FakeState(failed)
            val work = RecordingWork()
            val model = BackupOverviewViewModel(scope, ruleRepository, stateRepository, BackupCoordinator(work))
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                1,
                model.state.value.progress
                    ?.stateCounts
                    ?.get(SyncLifecycleState.FAILED),
            )
            model.runNow()
            dispatcher.scheduler.advanceUntilIdle()
            assertEquals(1, work.transfers)
            assertEquals(1, work.scans)

            model.setPaused(true)
            dispatcher.scheduler.advanceUntilIdle()
            assertTrue(
                ruleRepository.rules.value
                    .single()
                    .pausedAt != null,
            )

            model.retry(failed.id)
            dispatcher.scheduler.advanceUntilIdle()
            assertEquals(failed.id, stateRepository.retried)
            assertEquals(2, work.transfers)
            assertFalse(model.state.value.actionRunning)
        }

    @Test
    fun `overview requests bounded history pages from repository`() =
        runTest(dispatcher) {
            val history = (0 until 60).map { item(scope, SyncLifecycleState.COMPLETED) }
            val stateRepository = FakeState(history)
            val model =
                BackupOverviewViewModel(
                    scope,
                    FakeRules(scope),
                    stateRepository,
                    BackupCoordinator(RecordingWork()),
                )
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(listOf(51), stateRepository.requestedLimits)
            assertEquals(50, model.state.value.visibleItems.size)
            assertTrue(model.state.value.canLoadMore)

            model.loadMore()
            dispatcher.scheduler.advanceUntilIdle()
            assertEquals(listOf(51, 101), stateRepository.requestedLimits)
            assertEquals(60, model.state.value.visibleItems.size)
            assertFalse(model.state.value.canLoadMore)
        }

    @Test
    fun `rule editor saves source destination and policy in active account only`() =
        runTest(dispatcher) {
            val repository = FakeRules(scope, emptyList())
            val model = BackupRulesViewModel(scope, repository)
            dispatcher.scheduler.advanceUntilIdle()
            model.save(
                BackupRuleInput(
                    BackupSourceType.SAF_TREE,
                    "content://provider/tree/photos",
                    "Photos",
                    UUID.randomUUID().toString(),
                    BackupNetworkMode.LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER,
                    true,
                    25,
                ),
            )
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                scope,
                repository.rules.value
                    .single()
                    .accountScopeId,
            )
            assertEquals(
                BackupSourceType.SAF_TREE,
                repository.rules.value
                    .single()
                    .sourceType,
            )
            assertEquals(
                25,
                repository.rules.value
                    .single()
                    .minimumBatteryPercent,
            )
        }

    @Test
    fun `wifi view model exposes permission fail closed and registers only current wifi`() =
        runTest(dispatcher) {
            val repository = FakeWifi()
            val model = BackupWifiViewModel(scope, repository)
            dispatcher.scheduler.advanceUntilIdle()
            assertTrue(model.state.value.currentWifi is CurrentWifiResult.PermissionRequired)

            repository.current = CurrentWifiResult.Connected(ConnectedWifi("Family", null, false))
            model.refreshCurrent()
            model.register("Home", false, false)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                "Home",
                model.state.value.policies
                    .single()
                    .displayName,
            )
            assertEquals(
                scope,
                model.state.value.policies
                    .single()
                    .accountScopeId,
            )
        }

    private class RecordingWork : BackupWorkEnqueuer {
        var scans = 0
        var transfers = 0

        override fun enqueueScan(
            scope: AccountScopeId,
            ruleId: BackupRuleId,
            trigger: ScanTrigger,
        ) {
            scans++
        }

        override fun enqueuePeriodicSafScan(
            scope: AccountScopeId,
            ruleId: BackupRuleId,
        ) = Unit

        override fun enqueueTransfer(scope: AccountScopeId) {
            transfers++
        }
    }

    private class FakeState(
        initialItems: List<LocalSyncItem>,
    ) : BackupStateRepository {
        constructor(item: LocalSyncItem) : this(listOf(item))

        private val items = MutableStateFlow(initialItems)
        val requestedLimits = mutableListOf<Int>()
        var retried: LocalSyncItemId? = null

        override fun observeItems(
            accountScopeId: AccountScopeId,
            limit: Int,
        ): Flow<List<LocalSyncItem>> {
            requestedLimits += limit
            return MutableStateFlow(items.value.take(limit))
        }

        override fun observeProgress(accountScopeId: AccountScopeId): Flow<BackupProgressSnapshot> =
            MutableStateFlow(
                BackupProgressSnapshot(
                    stateCounts = items.value.groupingBy(LocalSyncItem::lifecycleState).eachCount(),
                    ruleStateCounts = emptyMap(),
                    waitReasonCounts = mapOf(BackupWaitReason.ALLOWED_WIFI to 1),
                    lastCompletedAt = null,
                ),
            )

        override suspend fun retryFailed(
            accountScopeId: AccountScopeId,
            itemId: LocalSyncItemId,
        ): Boolean {
            retried = itemId
            return true
        }

        override suspend fun retryAllFailed(accountScopeId: AccountScopeId): Int {
            retried = items.value.single().id
            return 1
        }
    }

    private class FakeRules(
        private val scope: AccountScopeId,
        initial: List<LocalBackupRule> = listOf(rule(scope)),
    ) : BackupRuleRepository {
        val rules = MutableStateFlow(initial)

        override fun observe(accountScopeId: AccountScopeId): Flow<List<LocalBackupRule>> = rules

        override suspend fun create(
            accountScopeId: AccountScopeId,
            command: CreateBackupRuleCommand,
        ): LocalBackupRule =
            rule(scope)
                .copy(
                    sourceType = command.sourceType,
                    sourceLocator = command.sourceLocator,
                    displayName = command.displayName,
                    remoteFolderId = command.remoteFolderId,
                    networkMode = command.networkMode,
                    requiresChargingForInitialRun = command.requiresChargingForInitialRun,
                    minimumBatteryPercent = command.minimumBatteryPercent,
                ).also { rules.value += it }

        override suspend fun setEnabled(
            accountScopeId: AccountScopeId,
            ruleId: BackupRuleId,
            enabled: Boolean,
        ) {
            rules.value = rules.value.map { if (it.id == ruleId) it.copy(enabled = enabled) else it }
        }

        override suspend fun setPaused(
            accountScopeId: AccountScopeId,
            ruleId: BackupRuleId,
            paused: Boolean,
        ) {
            rules.value =
                rules.value.map {
                    if (it.id == ruleId) it.copy(pausedAt = Instant.EPOCH.takeIf { paused }) else it
                }
        }

        override suspend fun save(
            accountScopeId: AccountScopeId,
            rule: LocalBackupRule,
        ) {
            rules.value = rules.value.map { if (it.id == rule.id) rule else it }
        }

        override suspend fun delete(
            accountScopeId: AccountScopeId,
            ruleId: BackupRuleId,
        ) {
            rules.value = rules.value.filterNot { it.id == ruleId }
        }

        override suspend fun sourceAccess(rule: LocalBackupRule) = BackupSourceAccess.AVAILABLE
    }

    private class FakeWifi : ExternalWifiPolicyRepository {
        val policies = MutableStateFlow(emptyList<ExternalWifiPolicy>())
        var current: CurrentWifiResult = CurrentWifiResult.PermissionRequired(setOf("wifi"))

        override fun observe(accountScopeId: AccountScopeId): Flow<List<ExternalWifiPolicy>> = policies

        override fun currentWifi() = current

        override suspend fun registerCurrent(
            accountScopeId: AccountScopeId,
            displayName: String,
            restrictToBssid: Boolean,
            treatAsMetered: Boolean,
        ): ExternalWifiPolicy =
            ExternalWifiPolicy(
                ExternalWifiPolicyId(UUID.randomUUID().toString()),
                accountScopeId,
                displayName,
                "Family",
                null,
                treatAsMetered,
                true,
                Instant.EPOCH,
                Instant.EPOCH,
            ).also {
                policies.value +=
                    it
            }

        override suspend fun save(
            accountScopeId: AccountScopeId,
            policy: ExternalWifiPolicy,
        ) {
            policies.value =
                policies.value.map { if (it.id == policy.id) policy else it }
        }

        override suspend fun delete(
            accountScopeId: AccountScopeId,
            policyId: ExternalWifiPolicyId,
        ) {
            policies.value =
                policies.value.filterNot { it.id == policyId }
        }

        override fun matchesCurrentWifi(
            policy: ExternalWifiPolicy,
            current: ConnectedWifi,
        ) = false
    }

    private companion object {
        fun rule(scope: AccountScopeId) =
            LocalBackupRule(
                BackupRuleId(UUID.randomUUID().toString()),
                scope,
                BackupSourceType.MEDIA_IMAGES,
                "external",
                "Photos",
                UUID.randomUUID().toString(),
                true,
                BackupNetworkMode.LOCAL_DIRECT_ONLY,
                true,
                20,
                null,
                null,
                Instant.EPOCH,
                Instant.EPOCH,
            )

        fun item(
            scope: AccountScopeId,
            state: SyncLifecycleState,
        ): LocalSyncItem {
            val rule = BackupRuleId(UUID.randomUUID().toString())
            return LocalSyncItem(
                LocalSyncItemId(UUID.randomUUID().toString()),
                scope,
                rule,
                "key",
                "content://item",
                "Photos/a.jpg",
                "a.jpg",
                1,
                Instant.EPOCH,
                null,
                "fingerprint",
                null,
                null,
                state,
                BackupWaitReason.NONE,
                BackupFailureReason.RETRY_EXHAUSTED,
                10,
                null,
                null,
                null,
                null,
                null,
                0,
                Instant.EPOCH,
                Instant.EPOCH,
                Instant.EPOCH,
                null,
            )
        }
    }
}
