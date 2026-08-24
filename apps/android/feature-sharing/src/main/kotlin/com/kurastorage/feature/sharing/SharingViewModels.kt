@file:Suppress("TooManyFunctions", "ReturnCount", "MaxLineLength", "MagicNumber")

package com.kurastorage.feature.sharing

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.SharePager
import com.kurastorage.core.data.SharingRepository
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.ShareCandidate
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ShareScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SharingListState(
    val loading: Boolean = true,
    val scope: ShareScope = ShareScope.RECEIVED,
    val targetType: FileEntryType? = null,
    val items: List<ShareItem> = emptyList(),
    val canLoadMore: Boolean = false,
    val error: String? = null,
)

class SharingListViewModel(
    private val repository: SharingRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(SharingListState())
    val state: StateFlow<SharingListState> = mutableState.asStateFlow()
    private var pager = newPager()

    init {
        refresh()
    }

    fun selectScope(scope: ShareScope) {
        if (scope == mutableState.value.scope) return
        mutableState.update { it.copy(scope = scope) }
        resetAndRefresh()
    }

    fun selectTargetType(targetType: FileEntryType?) {
        if (targetType == mutableState.value.targetType) return
        mutableState.update { it.copy(targetType = targetType) }
        resetAndRefresh()
    }

    fun refresh() = load { pager.refresh() }

    fun loadMore() {
        if (!mutableState.value.canLoadMore || mutableState.value.loading) return
        load { pager.loadNext() }
    }

    private fun resetAndRefresh() {
        pager = newPager()
        refresh()
    }

    private fun newPager() =
        SharePager { page ->
            val current = mutableState.value
            repository.list(current.scope, current.targetType, page)
        }

    private fun load(block: suspend () -> com.kurastorage.core.model.SharePage) {
        mutableState.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            runCatching { block() }
                .onSuccess { page ->
                    mutableState.update {
                        it.copy(loading = false, items = page.items, canLoadMore = page.hasNextPage)
                    }
                }.onFailure { failure ->
                    mutableState.update { it.copy(loading = false, error = failure.userMessage()) }
                }
        }
    }
}

enum class Confirmation { REMOVE_MEMBER, DELETE_SHARE, GRANT_MANAGER }

data class SharingSettingsState(
    val loading: Boolean = true,
    val targetEntryId: String,
    val targetType: FileEntryType,
    val targetName: String,
    val share: ShareItem? = null,
    val candidates: List<ShareCandidate> = emptyList(),
    val selectedUserId: String? = null,
    val selectedPermission: SharePermission = SharePermission.VIEWER,
    val submitting: Boolean = false,
    val confirmation: Confirmation? = null,
    val pendingMemberUserId: String? = null,
    val accessLost: Boolean = false,
    val message: String? = null,
    val error: String? = null,
) {
    val availablePermissions: List<SharePermission>
        get() =
            SharePermission.entries.filter {
                it != SharePermission.UNKNOWN && (targetType == FileEntryType.FOLDER || it != SharePermission.CONTRIBUTOR)
            }
}

class SharingSettingsViewModel(
    private val repository: SharingRepository,
    targetEntryId: String,
    targetType: FileEntryType,
    targetName: String,
    private val shareId: String? = null,
) : ViewModel() {
    private val mutableState = MutableStateFlow(SharingSettingsState(false, targetEntryId, targetType, targetName))
    val state: StateFlow<SharingSettingsState> = mutableState.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        if (mutableState.value.submitting) return
        mutableState.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            runCatching {
                val candidates = repository.candidates()
                val share = shareId?.let { repository.detail(it) }
                share to candidates
            }.onSuccess { (share, candidates) ->
                mutableState.update { it.copy(loading = false, share = share, candidates = candidates) }
            }.onFailure(::handleFailure)
        }
    }

    fun selectCandidate(userId: String) = mutableState.update { it.copy(selectedUserId = userId) }

    fun selectPermission(permission: SharePermission) {
        if (permission !in mutableState.value.availablePermissions) return
        mutableState.update { it.copy(selectedPermission = permission) }
    }

    fun submitSelectedMember() {
        val current = mutableState.value
        val userId = current.selectedUserId ?: return
        if (current.submitting) return
        if (current.selectedPermission == SharePermission.MANAGER) {
            mutableState.update {
                it.copy(confirmation = Confirmation.GRANT_MANAGER, pendingMemberUserId = userId)
            }
            return
        }
        submitMember(userId)
    }

    fun requestMemberRemoval(userId: String) {
        if (mutableState.value.submitting) return
        mutableState.update { it.copy(confirmation = Confirmation.REMOVE_MEMBER, pendingMemberUserId = userId) }
    }

    fun requestShareDeletion() {
        if (mutableState.value.submitting) return
        mutableState.update { it.copy(confirmation = Confirmation.DELETE_SHARE) }
    }

    fun dismissConfirmation() = mutableState.update { it.copy(confirmation = null, pendingMemberUserId = null) }

    fun confirm() {
        val current = mutableState.value
        when (current.confirmation) {
            Confirmation.GRANT_MANAGER -> current.pendingMemberUserId?.let(::submitMember)
            Confirmation.REMOVE_MEMBER -> current.pendingMemberUserId?.let(::removeMember)
            Confirmation.DELETE_SHARE -> deleteShare()
            null -> Unit
        }
    }

    fun changeMemberPermission(
        userId: String,
        permission: SharePermission,
    ) {
        if (permission !in mutableState.value.availablePermissions || mutableState.value.submitting) return
        mutableState.update { it.copy(selectedPermission = permission, selectedUserId = userId) }
        if (permission == SharePermission.MANAGER) {
            mutableState.update { it.copy(confirmation = Confirmation.GRANT_MANAGER, pendingMemberUserId = userId) }
        } else {
            submitMember(userId)
        }
    }

    private fun submitMember(userId: String) {
        val current = mutableState.value
        val permission = current.selectedPermission
        submit {
            val updated =
                current.share?.let { repository.setMember(it.id, userId, permission) }
                    ?: repository.create(current.targetEntryId, mapOf(userId to permission))
            mutableState.update {
                it.copy(
                    share = updated,
                    selectedUserId = null,
                    confirmation = null,
                    pendingMemberUserId = null,
                    message = "Sharing settings updated.",
                )
            }
        }
    }

    private fun removeMember(userId: String) =
        submit {
            val id = mutableState.value.share?.id ?: return@submit
            repository.removeMember(id, userId)
            val refreshed =
                try {
                    repository.detail(id)
                } catch (failure: KuraStorageException.Api) {
                    if (failure.error.statusCode == 404) null else throw failure
                }
            mutableState.update {
                it.copy(
                    share = refreshed,
                    confirmation = null,
                    pendingMemberUserId = null,
                    accessLost = refreshed == null,
                    message = if (refreshed == null) "Access removed." else "Member removed.",
                )
            }
        }

    private fun deleteShare() =
        submit {
            val id = mutableState.value.share?.id ?: return@submit
            repository.delete(id)
            mutableState.update { it.copy(share = null, confirmation = null, accessLost = true, message = "Share removed.") }
        }

    private fun submit(block: suspend () -> Unit) {
        if (mutableState.value.submitting) return
        mutableState.update { it.copy(submitting = true, error = null) }
        viewModelScope.launch {
            runCatching { block() }
                .onSuccess { mutableState.update { it.copy(submitting = false) } }
                .onFailure(::handleFailure)
        }
    }

    private fun handleFailure(failure: Throwable) {
        val lost = failure is KuraStorageException.Api && failure.error.statusCode == 404
        mutableState.update {
            it.copy(loading = false, submitting = false, confirmation = null, accessLost = lost, error = failure.userMessage())
        }
    }
}

private fun Throwable.userMessage(): String =
    when (this) {
        is KuraStorageException.Api -> "Request failed (${error.code}).${error.requestId?.let { " Request: $it" } ?: ""}"
        is KuraStorageException.Network -> "The server response could not be confirmed. Refresh before retrying."
        else -> "The sharing response was invalid. Refresh or update the app."
    }
