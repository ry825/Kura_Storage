@file:Suppress("ComplexCondition", "MaxLineLength")

package com.kurastorage.feature.activity

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.ActivityPager
import com.kurastorage.core.data.ActivityRepository
import com.kurastorage.core.model.ActivityItem
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.UserActivityType
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ActivityUiError(
    val message: String,
    val category: ErrorCategory,
    val requestId: String? = null,
)

data class ActivityUiState(
    val items: List<ActivityItem> = emptyList(),
    val filter: UserActivityType? = null,
    val loading: Boolean = false,
    val refreshing: Boolean = false,
    val loadingMore: Boolean = false,
    val canLoadMore: Boolean = false,
    val error: ActivityUiError? = null,
)

class ActivityViewModel(
    private val repository: ActivityRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(ActivityUiState())
    val state: StateFlow<ActivityUiState> = mutableState.asStateFlow()
    private var pager = ActivityPager(repository)
    private var request: Job? = null
    private var generation = 0L

    init {
        refresh(initial = true)
    }

    fun selectFilter(filter: UserActivityType?) {
        if (filter == UserActivityType.UNKNOWN || filter == mutableState.value.filter) return
        generation++
        request?.cancel()
        pager = ActivityPager(repository)
        mutableState.value = ActivityUiState(filter = filter)
        refresh(initial = true)
    }

    fun refresh(initial: Boolean = false) {
        if (mutableState.value.loading || mutableState.value.refreshing || mutableState.value.loadingMore) return
        generation++
        val activeGeneration = generation
        request?.cancel()
        mutableState.update { it.copy(loading = initial, refreshing = !initial, error = null) }
        request =
            viewModelScope.launch {
                runCatching { pager.refresh(mutableState.value.filter) }
                    .onSuccess { page ->
                        if (generation == activeGeneration) {
                            mutableState.update {
                                it.copy(items = page.items, loading = false, refreshing = false, canLoadMore = page.nextCursor != null)
                            }
                        }
                    }.onFailure { failure ->
                        if (generation == activeGeneration && failure !is kotlinx.coroutines.CancellationException) {
                            mutableState.update { it.copy(loading = false, refreshing = false, error = failure.toUiError()) }
                        }
                    }
            }
    }

    fun loadMore() {
        val value = mutableState.value
        if (value.loading || value.refreshing || value.loadingMore || !value.canLoadMore) return
        val activeGeneration = generation
        mutableState.update { it.copy(loadingMore = true, error = null) }
        request =
            viewModelScope.launch {
                runCatching { pager.loadNext() }
                    .onSuccess { page ->
                        if (generation == activeGeneration) {
                            mutableState.update { it.copy(items = page.items, loadingMore = false, canLoadMore = page.nextCursor != null) }
                        }
                    }.onFailure { failure ->
                        if (generation == activeGeneration && failure !is kotlinx.coroutines.CancellationException) {
                            mutableState.update { it.copy(loadingMore = false, error = failure.toUiError()) }
                        }
                    }
            }
    }
}

private fun Throwable.toUiError(): ActivityUiError =
    when (this) {
        is KuraStorageException.Api -> ActivityUiError(apiMessage(error.category), error.category, error.requestId)
        is KuraStorageException.Network -> ActivityUiError("You appear to be offline. Refresh when connected.", ErrorCategory.CONNECTION)
        is KuraStorageException.InvalidServerResponse ->
            ActivityUiError(
                "The activity response requires an app update.",
                ErrorCategory.UNKNOWN,
            )
        else -> ActivityUiError("The activity could not be loaded.", ErrorCategory.UNKNOWN)
    }

private fun apiMessage(category: ErrorCategory): String =
    when (category) {
        ErrorCategory.AUTHENTICATION -> "Sign in again to continue."
        ErrorCategory.AUTHORIZATION -> "Activity access is no longer available."
        ErrorCategory.STORAGE -> "Storage is currently unavailable."
        ErrorCategory.CONNECTION -> "You appear to be offline. Refresh when connected."
        ErrorCategory.VALIDATION -> "The activity request is invalid."
        ErrorCategory.CONFLICT -> "Activity changed. Refresh and try again."
        ErrorCategory.UNKNOWN -> "The activity could not be loaded."
    }
