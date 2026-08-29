@file:Suppress("TooManyFunctions")

package com.kurastorage.feature.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FavoritePager
import com.kurastorage.core.data.OrganizationRepository
import com.kurastorage.core.model.EntryOrganizationState
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FavoriteItem
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.model.TagNameValidationError
import com.kurastorage.core.model.validateTagName
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class OrganizationUiError(
    val message: String,
    val requestId: String? = null,
)

data class FavoritesUiState(
    val items: List<FavoriteItem> = emptyList(),
    val loading: Boolean = true,
    val refreshing: Boolean = false,
    val loadingMore: Boolean = false,
    val canLoadMore: Boolean = false,
    val error: OrganizationUiError? = null,
)

class FavoritesViewModel(
    private val repository: OrganizationRepository,
    private val loadDetail: suspend (String) -> FileEntry,
) : ViewModel() {
    private val mutableState = MutableStateFlow(FavoritesUiState())
    val state: StateFlow<FavoritesUiState> = mutableState.asStateFlow()
    private val pager = FavoritePager(repository)
    private var generation = 0

    init {
        refresh(true)
    }

    fun refresh(initial: Boolean = false) {
        val current = mutableState.value
        val blockedInitialLoad = !initial && current.loading
        val busy = current.refreshing || current.loadingMore
        if (blockedInitialLoad || busy) return
        val request = ++generation
        mutableState.update { it.copy(loading = initial, refreshing = !initial, error = null) }
        viewModelScope.launch {
            runCatching { pager.refresh() }
                .onSuccess { page ->
                    if (request ==
                        generation
                    ) {
                        mutableState.value =
                            FavoritesUiState(
                                page.items,
                                canLoadMore = page.hasNextPage,
                                loading = false,
                            )
                    }
                }.onFailure { failure ->
                    if (request ==
                        generation
                    ) {
                        mutableState.update {
                            it.copy(
                                loading = false,
                                refreshing = false,
                                error = failure.organizationError(),
                            )
                        }
                    }
                }
        }
    }

    fun loadMore() {
        val current = mutableState.value
        val busy = current.loading || current.refreshing || current.loadingMore
        if (busy || !current.canLoadMore) return
        val request = generation
        mutableState.update { it.copy(loadingMore = true, error = null) }
        viewModelScope.launch {
            runCatching { pager.loadNext() }
                .onSuccess { page ->
                    if (request ==
                        generation
                    ) {
                        mutableState.update {
                            it.copy(
                                items = page.items,
                                loadingMore = false,
                                canLoadMore = page.hasNextPage,
                            )
                        }
                    }
                }.onFailure { failure ->
                    if (request == generation) {
                        mutableState.update {
                            it.copy(loadingMore = false, error = failure.organizationError())
                        }
                    }
                }
        }
    }

    fun open(
        item: FavoriteItem,
        onOpen: (FileEntry) -> Unit,
    ) {
        if (item.metadata.status != FileEntryStatus.ACTIVE) return
        viewModelScope.launch {
            runCatching { loadDetail(item.id) }
                .onSuccess { latest ->
                    if (latest.status == FileEntryStatus.ACTIVE) onOpen(latest) else refresh()
                }.onFailure { failure ->
                    if (failure is KuraStorageException.Api && failure.error.code == ErrorCode.FILE_NOT_FOUND) {
                        refresh()
                    } else {
                        mutableState.update { it.copy(error = failure.organizationError()) }
                    }
                }
        }
    }
}

enum class TagDialog { CREATE, RENAME, DELETE }

data class TagsUiState(
    val tags: List<TagItem> = emptyList(),
    val loading: Boolean = true,
    val dialog: TagDialog? = null,
    val selected: TagItem? = null,
    val input: String = "",
    val validationError: String? = null,
    val pendingTagId: String? = null,
    val error: OrganizationUiError? = null,
)

class TagsViewModel(
    private val repository: OrganizationRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(TagsUiState())
    val state: StateFlow<TagsUiState> = mutableState.asStateFlow()
    private var operation: Job? = null
    private var generation = 0

    init {
        refresh()
    }

    fun refresh() {
        operation?.cancel()
        val request = ++generation
        mutableState.update { it.copy(loading = true, error = null) }
        operation =
            viewModelScope.launch {
                runCatching { repository.listTags() }
                    .onSuccess { tags ->
                        if (request == generation) {
                            mutableState.value = TagsUiState(tags = tags, loading = false)
                        }
                    }.onFailure { failure ->
                        if (request == generation) {
                            mutableState.update { state ->
                                state.copy(loading = false, error = failure.organizationError())
                            }
                        }
                    }
            }
    }

    fun create() {
        mutableState.update {
            it.copy(
                dialog = TagDialog.CREATE,
                selected = null,
                input = "",
                validationError = null,
            )
        }
    }

    fun rename(tag: TagItem) =
        mutableState.update {
            it.copy(dialog = TagDialog.RENAME, selected = tag, input = tag.name, validationError = null)
        }

    fun delete(tag: TagItem) =
        mutableState.update { it.copy(dialog = TagDialog.DELETE, selected = tag, input = "", validationError = null) }

    fun input(value: String) = mutableState.update { it.copy(input = value, validationError = null) }

    fun dismiss() {
        if (mutableState.value.pendingTagId == null) mutableState.update { it.copy(dialog = null, selected = null) }
    }

    fun confirm() {
        val state = mutableState.value
        if (state.pendingTagId != null) return
        val validated = if (state.dialog == TagDialog.DELETE) null else validateTagName(state.input)
        if (validated?.value == null && state.dialog != TagDialog.DELETE) {
            mutableState.update { it.copy(validationError = validated?.error.message()) }
            return
        }
        val pending = state.selected?.id ?: "create"
        val request = ++generation
        mutableState.update { it.copy(pendingTagId = pending, error = null) }
        operation =
            viewModelScope.launch {
                runCatching {
                    when (state.dialog) {
                        TagDialog.CREATE -> repository.createTag(checkNotNull(validated?.value).value)
                        TagDialog.RENAME ->
                            repository.renameTag(
                                checkNotNull(state.selected).id,
                                checkNotNull(validated?.value).value,
                            )
                        TagDialog.DELETE -> repository.deleteTag(checkNotNull(state.selected).id)
                        null -> return@launch
                    }
                    repository.listTags()
                }.onSuccess { tags ->
                    if (request == generation) mutableState.value = TagsUiState(tags = tags, loading = false)
                }.onFailure { failure ->
                    if (request == generation) {
                        mutableState.update {
                            it.copy(pendingTagId = null, error = failure.organizationError())
                        }
                    }
                }
            }
    }
}

data class EntryOrganizationUiState(
    val entry: FileEntry? = null,
    val organization: EntryOrganizationState? = null,
    val availableTags: List<TagItem> = emptyList(),
    val loading: Boolean = true,
    val pendingFavorite: Boolean = false,
    val pendingTagIds: Set<String> = emptySet(),
    val error: OrganizationUiError? = null,
) {
    val canAttach: Boolean get() = entry?.status == FileEntryStatus.ACTIVE
}

class EntryOrganizationViewModel(
    private val entryId: String,
    private val repository: OrganizationRepository,
    private val loadDetail: suspend (String) -> FileEntry,
) : ViewModel() {
    private val mutableState = MutableStateFlow(EntryOrganizationUiState())
    val state: StateFlow<EntryOrganizationUiState> = mutableState.asStateFlow()
    private var generation = 0

    init {
        refresh()
    }

    fun refresh() {
        val current = mutableState.value
        if (current.pendingFavorite || current.pendingTagIds.isNotEmpty()) return
        val request = ++generation
        mutableState.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            runCatching {
                Triple(
                    loadDetail(entryId),
                    repository.state(entryId),
                    repository.listTags(),
                )
            }.onSuccess { (entry, organization, tags) ->
                if (request == generation) {
                    mutableState.value =
                        EntryOrganizationUiState(
                            entry,
                            organization,
                            tags,
                            loading = false,
                        )
                }
            }.onFailure { failure ->
                if (request == generation) {
                    mutableState.update {
                        it.copy(loading = false, error = failure.organizationError())
                    }
                }
            }
        }
    }

    fun toggleFavorite() {
        val organization = mutableState.value.organization ?: return
        if (mutableState.value.pendingFavorite ||
            !mutableState.value.canAttach &&
            !organization.isFavorite
        ) {
            return
        }
        mutableState.update { it.copy(pendingFavorite = true, error = null) }
        viewModelScope.launch {
            runCatching { repository.setFavorite(entryId, !organization.isFavorite) }
                .onSuccess { updated ->
                    mutableState.update {
                        it.copy(organization = updated, pendingFavorite = false)
                    }
                }.onFailure { failure ->
                    mutableState.update {
                        it.copy(pendingFavorite = false, error = failure.organizationError())
                    }
                }
        }
    }

    fun toggleTag(tag: TagItem) {
        val state = mutableState.value
        val organization = state.organization
        if (organization == null || tag.id in state.pendingTagIds) return
        val attached = organization.tags.any { it.id == tag.id }
        if (!attached && !state.canAttach) return
        mutableState.update { it.copy(pendingTagIds = it.pendingTagIds + tag.id, error = null) }
        viewModelScope.launch {
            runCatching { repository.setTag(entryId, tag.id, !attached) }
                .onSuccess { updated ->
                    mutableState.update {
                        it.copy(
                            organization = updated,
                            pendingTagIds = it.pendingTagIds - tag.id,
                        )
                    }
                }.onFailure { failure ->
                    mutableState.update {
                        it.copy(
                            pendingTagIds = it.pendingTagIds - tag.id,
                            error = failure.organizationError(),
                        )
                    }
                }
        }
    }
}

private fun TagNameValidationError?.message(): String =
    when (this) {
        TagNameValidationError.EMPTY -> "Enter a tag name."
        TagNameValidationError.TOO_LONG -> "Tag names must contain at most 50 characters."
        TagNameValidationError.CONTROL_CHARACTER -> "Tag names cannot contain control characters."
        null -> "The tag name is invalid."
    }

private fun Throwable.organizationError(): OrganizationUiError =
    when (this) {
        is KuraStorageException.Api -> organizationApiError()
        is KuraStorageException.Network ->
            OrganizationUiError("The result is unknown. Refresh and try again.")
        is KuraStorageException.InvalidServerResponse ->
            OrganizationUiError("The server response requires an app update.")
        else ->
            OrganizationUiError("The request could not be completed.")
    }

private fun KuraStorageException.Api.organizationApiError(): OrganizationUiError {
    val message =
        when (error.code) {
            ErrorCode.TAG_NAME_CONFLICT -> "A tag with this name already exists."
            ErrorCode.TAG_LIMIT_EXCEEDED -> "You can create at most 200 tags."
            ErrorCode.ENTRY_TAG_LIMIT_EXCEEDED -> "An item can have at most 20 tags."
            ErrorCode.TAG_NOT_FOUND,
            ErrorCode.FILE_NOT_FOUND,
            -> "This item is no longer available. Refresh and try again."
            else -> "The server rejected the request. Refresh and try again."
        }
    return OrganizationUiError(message, error.requestId)
}
