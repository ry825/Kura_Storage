@file:Suppress("ComplexCondition", "MaxLineLength")

package com.kurastorage.feature.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.RecentFilePager
import com.kurastorage.core.data.RecentFileRepository
import com.kurastorage.core.data.SearchPager
import com.kurastorage.core.data.SearchRepository
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SearchValidationError
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SearchUiError(
    val message: String,
    val category: ErrorCategory,
    val requestId: String? = null,
)

data class SearchUiState(
    val input: SearchInput = SearchInput(),
    val items: List<SearchResultItem> = emptyList(),
    val hasSearched: Boolean = false,
    val loading: Boolean = false,
    val refreshing: Boolean = false,
    val loadingMore: Boolean = false,
    val canLoadMore: Boolean = false,
    val validationError: String? = null,
    val error: SearchUiError? = null,
)

class SearchViewModel(
    private val repository: SearchRepository,
    private val loadDetail: suspend (String) -> FileEntry,
) : ViewModel() {
    private val mutableState = MutableStateFlow(SearchUiState())
    val state: StateFlow<SearchUiState> = mutableState.asStateFlow()
    private var pager: SearchPager? = null
    private var requestJob: Job? = null
    private var generation = 0L

    fun updateInput(input: SearchInput) {
        generation++
        requestJob?.cancel()
        mutableState.update {
            it.copy(input = input.copy(page = 1), loading = false, refreshing = false, loadingMore = false, validationError = null)
        }
    }

    fun search() {
        val input = mutableState.value.input.copy(page = 1)
        val validation = input.validate()
        if (validation.value == null) {
            mutableState.update { it.copy(validationError = validation.error.toMessage(), error = null) }
            return
        }
        generation++
        val activeGeneration = generation
        requestJob?.cancel()
        val activePager = SearchPager(repository, input)
        pager = activePager
        mutableState.update {
            it.copy(
                hasSearched = true,
                loading = true,
                refreshing = false,
                loadingMore = false,
                validationError = null,
                error = null,
            )
        }
        requestJob =
            viewModelScope.launch {
                runCatching { activePager.refresh() }
                    .onSuccess { page ->
                        if (generation == activeGeneration) {
                            mutableState.update {
                                it.copy(
                                    items = page.items,
                                    loading = false,
                                    canLoadMore = page.hasNextPage,
                                    error = null,
                                )
                            }
                        }
                    }.onFailure { failure ->
                        if (generation == activeGeneration && failure !is kotlinx.coroutines.CancellationException) {
                            mutableState.update { it.copy(loading = false, error = failure.toUiError()) }
                        }
                    }
            }
    }

    fun refresh() {
        if (mutableState.value.loading || mutableState.value.refreshing || mutableState.value.loadingMore) return
        val activePager = pager ?: return search()
        generation++
        val activeGeneration = generation
        requestJob?.cancel()
        mutableState.update { it.copy(refreshing = true, error = null) }
        requestJob =
            viewModelScope.launch {
                runCatching { activePager.refresh() }
                    .onSuccess { page ->
                        if (generation == activeGeneration) {
                            mutableState.update {
                                it.copy(items = page.items, refreshing = false, canLoadMore = page.hasNextPage)
                            }
                        }
                    }.onFailure { failure ->
                        if (generation == activeGeneration && failure !is kotlinx.coroutines.CancellationException) {
                            mutableState.update { it.copy(refreshing = false, error = failure.toUiError()) }
                        }
                    }
            }
    }

    fun loadMore() {
        val activePager = pager ?: return
        if (
            mutableState.value.loading ||
            mutableState.value.refreshing ||
            mutableState.value.loadingMore ||
            !mutableState.value.canLoadMore
        ) {
            return
        }
        val activeGeneration = generation
        mutableState.update { it.copy(loadingMore = true, error = null) }
        viewModelScope.launch {
            runCatching { activePager.loadNext() }
                .onSuccess { page ->
                    if (generation == activeGeneration) {
                        mutableState.update {
                            it.copy(items = page.items, loadingMore = false, canLoadMore = page.hasNextPage)
                        }
                    }
                }.onFailure { failure ->
                    if (generation == activeGeneration) {
                        mutableState.update { it.copy(loadingMore = false, error = failure.toUiError()) }
                    }
                }
        }
    }

    fun open(
        item: SearchResultItem,
        onOpen: (String, FileEntryType) -> Unit,
    ) {
        if (item.entryType == FileEntryType.UNKNOWN || item.status != FileEntryStatus.ACTIVE) return
        viewModelScope.launch {
            runCatching { loadDetail(item.id) }
                .onSuccess { latest ->
                    if (latest.status == FileEntryStatus.ACTIVE && latest.entryType != FileEntryType.UNKNOWN) {
                        onOpen(latest.id, latest.entryType)
                    } else {
                        refresh()
                    }
                }.onFailure { failure ->
                    if (failure.isNotFound()) refresh() else mutableState.update { it.copy(error = failure.toUiError()) }
                }
        }
    }
}

data class RecentFilesUiState(
    val items: List<RecentFileItem> = emptyList(),
    val loading: Boolean = false,
    val refreshing: Boolean = false,
    val loadingMore: Boolean = false,
    val canLoadMore: Boolean = false,
    val error: SearchUiError? = null,
)

class RecentFilesViewModel(
    private val repository: RecentFileRepository,
    private val loadDetail: suspend (String) -> FileEntry,
) : ViewModel() {
    private val mutableState = MutableStateFlow(RecentFilesUiState())
    val state: StateFlow<RecentFilesUiState> = mutableState.asStateFlow()
    private val pager = RecentFilePager(repository)
    private var requestJob: Job? = null
    private var generation = 0L

    init {
        refresh(initial = true)
    }

    fun refresh(initial: Boolean = false) {
        if (mutableState.value.loading || mutableState.value.refreshing || mutableState.value.loadingMore) return
        generation++
        val activeGeneration = generation
        requestJob?.cancel()
        mutableState.update { it.copy(loading = initial, refreshing = !initial, error = null) }
        requestJob =
            viewModelScope.launch {
                runCatching { pager.refresh() }
                    .onSuccess { page ->
                        if (generation == activeGeneration) {
                            mutableState.update {
                                it.copy(
                                    items = page.items,
                                    loading = false,
                                    refreshing = false,
                                    canLoadMore = page.hasNextPage,
                                )
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
        if (
            mutableState.value.loading ||
            mutableState.value.refreshing ||
            mutableState.value.loadingMore ||
            !mutableState.value.canLoadMore
        ) {
            return
        }
        mutableState.update { it.copy(loadingMore = true, error = null) }
        val activeGeneration = generation
        requestJob =
            viewModelScope.launch {
                runCatching { pager.loadNext() }
                    .onSuccess { page ->
                        if (generation == activeGeneration) {
                            mutableState.update {
                                it.copy(items = page.items, loadingMore = false, canLoadMore = page.hasNextPage)
                            }
                        }
                    }.onFailure { failure ->
                        if (generation == activeGeneration && failure !is kotlinx.coroutines.CancellationException) {
                            mutableState.update { it.copy(loadingMore = false, error = failure.toUiError()) }
                        }
                    }
            }
    }

    fun open(
        item: RecentFileItem,
        onOpen: (String, FileEntryType) -> Unit,
    ) {
        if (item.metadata.status != FileEntryStatus.ACTIVE) return
        viewModelScope.launch {
            runCatching { loadDetail(item.id) }
                .onSuccess { latest ->
                    if (latest.status == FileEntryStatus.ACTIVE && latest.entryType == FileEntryType.FILE) {
                        onOpen(latest.id, latest.entryType)
                    } else {
                        refresh()
                    }
                }.onFailure { failure ->
                    if (failure.isNotFound()) refresh() else mutableState.update { it.copy(error = failure.toUiError()) }
                }
        }
    }
}

private fun SearchValidationError?.toMessage(): String =
    when (this) {
        SearchValidationError.QUERY_REQUIRED -> "Enter a search term or choose at least one filter."
        SearchValidationError.INVALID_QUERY -> "The search term must contain 1 to 200 characters."
        SearchValidationError.INVALID_FILTER -> "The selected filters cannot be combined."
        null -> "The search input is invalid."
    }

private fun Throwable.isNotFound(): Boolean = this is KuraStorageException.Api && error.code == ErrorCode.FILE_NOT_FOUND

private fun Throwable.toUiError(): SearchUiError =
    when (this) {
        is KuraStorageException.Api -> SearchUiError(apiErrorMessage(error.category), error.category, error.requestId)
        is KuraStorageException.Network -> SearchUiError("The result is unknown. Refresh and try again.", ErrorCategory.CONNECTION)
        is KuraStorageException.InvalidServerResponse -> SearchUiError("The server response requires an app update.", ErrorCategory.UNKNOWN)
        else -> SearchUiError("The request could not be completed.", ErrorCategory.UNKNOWN)
    }

private fun apiErrorMessage(category: ErrorCategory): String =
    when (category) {
        ErrorCategory.STORAGE -> "Storage is currently unavailable."
        ErrorCategory.CONFLICT -> "The item changed. Refresh and try again."
        ErrorCategory.AUTHORIZATION -> "Access is no longer available."
        ErrorCategory.AUTHENTICATION -> "Sign in again to continue."
        ErrorCategory.VALIDATION -> "The request is invalid."
        ErrorCategory.CONNECTION -> "The result is unknown. Refresh and try again."
        ErrorCategory.UNKNOWN -> "The request could not be completed."
    }
