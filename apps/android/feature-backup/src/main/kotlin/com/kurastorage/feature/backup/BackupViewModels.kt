package com.kurastorage.feature.backup

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.backup.BackupProgressSnapshot
import com.kurastorage.core.data.backup.BackupRuleRepository
import com.kurastorage.core.data.backup.BackupStateRepository
import com.kurastorage.core.data.backup.CreateBackupRuleCommand
import com.kurastorage.core.data.backup.CurrentWifiResult
import com.kurastorage.core.data.backup.ExternalWifiPolicyRepository
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

private const val HISTORY_PAGE_SIZE = 50

data class BackupOverviewState(
    val loading: Boolean = true,
    val rules: List<LocalBackupRule> = emptyList(),
    val items: List<LocalSyncItem> = emptyList(),
    val progress: BackupProgressSnapshot? = null,
    val visibleHistoryCount: Int = HISTORY_PAGE_SIZE,
    val actionRunning: Boolean = false,
    val error: String? = null,
) {
    val visibleItems: List<LocalSyncItem>
        get() = items.take(visibleHistoryCount)

    val canLoadMore: Boolean get() = items.size > visibleHistoryCount
}

@OptIn(ExperimentalCoroutinesApi::class)
class BackupOverviewViewModel(
    private val scope: AccountScopeId,
    private val rules: BackupRuleRepository,
    private val stateRepository: BackupStateRepository,
    private val coordinator: BackupCoordinator,
) : ViewModel() {
    private val mutableState = MutableStateFlow(BackupOverviewState())
    private val historyLimit = MutableStateFlow(HISTORY_PAGE_SIZE)
    val state: StateFlow<BackupOverviewState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            combine(
                rules.observe(scope),
                historyLimit.flatMapLatest { limit -> stateRepository.observeItems(scope, limit + 1) },
                stateRepository.observeProgress(scope),
            ) { ruleList, items, progress -> Triple(ruleList, items, progress) }
                .collect { (ruleList, items, progress) ->
                    mutableState.update {
                        it.copy(loading = false, rules = ruleList, items = items, progress = progress)
                    }
                }
        }
    }

    fun runNow() =
        guardedAction {
            coordinator.runNow(
                scope,
                mutableState.value.rules
                    .filter(LocalBackupRule::enabled)
                    .map(LocalBackupRule::id),
            )
        }

    fun setPaused(paused: Boolean) =
        guardedAction {
            mutableState.value.rules.forEach { rule -> rules.setPaused(scope, rule.id, paused) }
            if (!paused) {
                coordinator.runNow(
                    scope,
                    mutableState.value.rules
                        .filter(LocalBackupRule::enabled)
                        .map(LocalBackupRule::id),
                )
            }
        }

    fun retry(itemId: LocalSyncItemId) =
        guardedAction {
            val changed = stateRepository.retryFailed(scope, itemId)
            check(changed) { "Backup item is no longer retryable" }
            coordinator.enqueueTransfer(scope)
        }

    fun retryAllFailures() =
        guardedAction {
            stateRepository.retryAllFailed(scope)
            coordinator.enqueueTransfer(scope)
        }

    fun loadMore() {
        val next = (historyLimit.value + HISTORY_PAGE_SIZE).coerceAtMost(MAXIMUM_HISTORY_ITEMS)
        historyLimit.value = next
        mutableState.update { it.copy(visibleHistoryCount = next) }
    }

    fun clearError() = mutableState.update { it.copy(error = null) }

    private fun guardedAction(action: suspend () -> Unit) {
        if (mutableState.value.actionRunning) return
        mutableState.update { it.copy(actionRunning = true, error = null) }
        viewModelScope.launch {
            runCatching { action() }
                .onFailure {
                    mutableState.update { state ->
                        state.copy(error = "Backup action could not be completed.")
                    }
                }
            mutableState.update { it.copy(actionRunning = false) }
        }
    }
}

private const val MAXIMUM_HISTORY_ITEMS = 10_000

data class BackupRuleInput(
    val sourceType: BackupSourceType,
    val sourceLocator: String,
    val displayName: String,
    val remoteFolderId: String,
    val networkMode: BackupNetworkMode,
    val requiresChargingForInitialRun: Boolean,
    val minimumBatteryPercent: Int,
)

data class BackupRulesState(
    val loading: Boolean = true,
    val rules: List<LocalBackupRule> = emptyList(),
    val saving: Boolean = false,
    val error: String? = null,
)

class BackupRulesViewModel(
    private val scope: AccountScopeId,
    private val repository: BackupRuleRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(BackupRulesState())
    val state: StateFlow<BackupRulesState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            repository.observe(scope).collect { rules ->
                mutableState.update { it.copy(loading = false, rules = rules) }
            }
        }
    }

    fun save(
        input: BackupRuleInput,
        existing: LocalBackupRule? = null,
        onSaved: () -> Unit = {},
    ) = action {
        if (existing == null) {
            repository.create(
                scope,
                CreateBackupRuleCommand(
                    input.sourceType,
                    input.sourceLocator,
                    input.displayName,
                    input.remoteFolderId,
                    input.networkMode,
                    input.requiresChargingForInitialRun,
                    input.minimumBatteryPercent,
                ),
            )
        } else {
            repository.save(
                scope,
                existing.copy(
                    sourceType = input.sourceType,
                    sourceLocator = input.sourceLocator,
                    displayName = input.displayName,
                    remoteFolderId = input.remoteFolderId,
                    networkMode = input.networkMode,
                    requiresChargingForInitialRun = input.requiresChargingForInitialRun,
                    minimumBatteryPercent = input.minimumBatteryPercent,
                ),
            )
        }
        onSaved()
    }

    fun setEnabled(
        rule: LocalBackupRule,
        enabled: Boolean,
    ) = action { repository.setEnabled(scope, rule.id, enabled) }

    fun delete(ruleId: BackupRuleId) = action { repository.delete(scope, ruleId) }

    private fun action(block: suspend () -> Unit) {
        if (mutableState.value.saving) return
        mutableState.update { it.copy(saving = true, error = null) }
        viewModelScope.launch {
            runCatching { block() }
                .onFailure {
                    mutableState.update { state ->
                        state.copy(error = "Backup rule could not be saved. Check source and destination access.")
                    }
                }
            mutableState.update { it.copy(saving = false) }
        }
    }
}

data class BackupWifiState(
    val loading: Boolean = true,
    val policies: List<ExternalWifiPolicy> = emptyList(),
    val currentWifi: CurrentWifiResult = CurrentWifiResult.InformationUnavailable,
    val saving: Boolean = false,
    val error: String? = null,
)

class BackupWifiViewModel(
    private val scope: AccountScopeId,
    private val repository: ExternalWifiPolicyRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(BackupWifiState(currentWifi = repository.currentWifi()))
    val state: StateFlow<BackupWifiState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            repository.observe(scope).collect { policies ->
                mutableState.update { it.copy(loading = false, policies = policies) }
            }
        }
    }

    fun refreshCurrent() = mutableState.update { it.copy(currentWifi = repository.currentWifi(), error = null) }

    fun register(
        displayName: String,
        restrictToBssid: Boolean,
        treatAsMetered: Boolean,
    ) = action {
        repository.registerCurrent(scope, displayName, restrictToBssid, treatAsMetered)
        refreshCurrent()
    }

    fun save(policy: ExternalWifiPolicy) = action { repository.save(scope, policy) }

    fun delete(policy: ExternalWifiPolicy) = action { repository.delete(scope, policy.id) }

    private fun action(block: suspend () -> Unit) {
        if (mutableState.value.saving) return
        mutableState.update { it.copy(saving = true, error = null) }
        viewModelScope.launch {
            runCatching { block() }
                .onFailure { mutableState.update { state -> state.copy(error = "Wi-Fi policy could not be updated.") } }
            mutableState.update { it.copy(saving = false) }
        }
    }
}
