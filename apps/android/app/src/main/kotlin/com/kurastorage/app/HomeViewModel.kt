package com.kurastorage.app

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.RecentFileRepository
import com.kurastorage.core.data.StorageCapacityRepository
import com.kurastorage.core.data.backup.BackupProgressSnapshot
import com.kurastorage.core.data.backup.BackupStateRepository
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.StorageCapacityStatus
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.SyncLifecycleState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.Instant

private const val HOME_RECENT_LIMIT = 4

data class HomeBackupSummary(
    val lastCompletedAt: Instant?,
    val pendingCount: Int,
    val uploadingCount: Int,
    val failedCount: Int,
) {
    val statusLabel: String
        get() =
            when {
                failedCount > 0 -> "Needs attention"
                uploadingCount > 0 -> "Uploading"
                pendingCount > 0 -> "Pending"
                lastCompletedAt != null -> "All backed up"
                else -> "Ready"
            }

    companion object {
        fun from(progress: BackupProgressSnapshot): HomeBackupSummary =
            HomeBackupSummary(
                lastCompletedAt = progress.lastCompletedAt,
                pendingCount =
                    listOf(
                        SyncLifecycleState.DISCOVERED,
                        SyncLifecycleState.PENDING,
                        SyncLifecycleState.COMPARING,
                        SyncLifecycleState.READY_TO_UPLOAD,
                    ).sumOf { progress.stateCounts[it] ?: 0 },
                uploadingCount = progress.stateCounts[SyncLifecycleState.UPLOADING] ?: 0,
                failedCount = progress.stateCounts[SyncLifecycleState.FAILED] ?: 0,
            )
    }
}

data class HomeUiState(
    val recentLoading: Boolean = true,
    val recentItems: List<RecentFileItem> = emptyList(),
    val recentError: Boolean = false,
    val backupLoading: Boolean = true,
    val backupSummary: HomeBackupSummary? = null,
    val backupError: Boolean = false,
    val capacityLoading: Boolean = true,
    val capacity: StorageCapacityStatus? = null,
    val capacityError: Boolean = false,
)

class HomeViewModel(
    private val recentFiles: RecentFileRepository,
    private val storageCapacity: StorageCapacityRepository,
    backupState: BackupStateRepository,
    accountScopeId: AccountScopeId,
    connectionRecovery: kotlinx.coroutines.flow.Flow<Unit> = emptyFlow(),
) : ViewModel() {
    private val mutableState = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = mutableState.asStateFlow()

    init {
        refreshRecent()
        refreshCapacity()
        viewModelScope.launch {
            connectionRecovery.collect {
                if (mutableState.value.capacityError) refreshCapacity()
            }
        }
        viewModelScope.launch {
            backupState
                .observeProgress(accountScopeId)
                .catch {
                    mutableState.update { state -> state.copy(backupLoading = false, backupError = true) }
                }.collect { progress ->
                    mutableState.update {
                        it.copy(
                            backupLoading = false,
                            backupSummary = HomeBackupSummary.from(progress),
                            backupError = false,
                        )
                    }
                }
        }
    }

    fun refreshRecent() {
        if (mutableState.value.recentLoading && mutableState.value.recentItems.isNotEmpty()) return
        mutableState.update { it.copy(recentLoading = true, recentError = false) }
        viewModelScope.launch {
            runCatching { recentFiles.list(page = 1, pageSize = HOME_RECENT_LIMIT) }
                .onSuccess { page ->
                    mutableState.update {
                        it.copy(
                            recentLoading = false,
                            recentItems = page.items.take(HOME_RECENT_LIMIT),
                            recentError = false,
                        )
                    }
                }.onFailure {
                    mutableState.update { state -> state.copy(recentLoading = false, recentError = true) }
                }
        }
    }

    fun refreshCapacity() {
        if (mutableState.value.capacityLoading && mutableState.value.capacity != null) return
        mutableState.update { it.copy(capacityLoading = true, capacityError = false) }
        viewModelScope.launch {
            runCatching { storageCapacity.get() }
                .onSuccess { capacity ->
                    mutableState.update {
                        it.copy(capacityLoading = false, capacity = capacity, capacityError = false)
                    }
                }.onFailure {
                    mutableState.update { state ->
                        state.copy(capacityLoading = false, capacityError = true)
                    }
                }
        }
    }
}
