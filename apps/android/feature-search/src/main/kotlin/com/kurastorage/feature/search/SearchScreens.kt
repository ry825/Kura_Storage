@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@file:Suppress(
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "MagicNumber",
    "MatchingDeclarationName",
    "MaxLineLength",
    "TooManyFunctions",
    "ktlint:standard:function-naming",
)

package com.kurastorage.feature.search

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.ui.EmptyState
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.LoadingState
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraFileEntryRow
import java.time.Duration
import java.time.Instant

data class SearchFilterOption(
    val id: String,
    val label: String,
)

@Composable
@Suppress("CyclomaticComplexMethod")
fun SearchScreen(
    state: SearchUiState,
    onBack: () -> Unit,
    onInput: (SearchInput) -> Unit,
    onSearch: () -> Unit,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onOpen: (SearchResultItem) -> Unit,
    ownerOptions: List<SearchFilterOption> = emptyList(),
    shareOptions: List<SearchFilterOption> = emptyList(),
    tagOptions: List<TagItem> = emptyList(),
    onManageTags: () -> Unit = {},
    categoryMode: SearchFileCategory? = null,
    onClear: () -> Unit = {},
    onFavorites: () -> Unit = {},
) {
    var updatedFrom by remember(state.input.updatedFrom) {
        mutableStateOf(
            state.input.updatedFrom
                ?.toString()
                .orEmpty(),
        )
    }
    var updatedTo by remember(state.input.updatedTo) {
        mutableStateOf(
            state.input.updatedTo
                ?.toString()
                .orEmpty(),
        )
    }
    var minimumSize by remember(state.input.minSize) {
        mutableStateOf(
            state.input.minSize
                ?.toString()
                .orEmpty(),
        )
    }
    var maximumSize by remember(state.input.maxSize) {
        mutableStateOf(
            state.input.maxSize
                ?.toString()
                .orEmpty(),
        )
    }
    val parsedUpdatedFrom = updatedFrom.takeIf(String::isNotBlank)?.let(::parseInstantOrNull)
    val parsedUpdatedTo = updatedTo.takeIf(String::isNotBlank)?.let(::parseInstantOrNull)
    val parsedMinimumSize = minimumSize.takeIf(String::isNotBlank)?.toLongOrNull()
    val parsedMaximumSize = maximumSize.takeIf(String::isNotBlank)?.toLongOrNull()
    val rangesValid =
        (updatedFrom.isBlank() || parsedUpdatedFrom != null) &&
            (updatedTo.isBlank() || parsedUpdatedTo != null) &&
            (minimumSize.isBlank() || parsedMinimumSize != null) &&
            (maximumSize.isBlank() || parsedMaximumSize != null)
    val submitSearch = {
        onInput(
            state.input.copy(
                updatedFrom = parsedUpdatedFrom,
                updatedTo = parsedUpdatedTo,
                minSize = parsedMinimumSize,
                maxSize = parsedMaximumSize,
            ),
        )
        onSearch()
    }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        LazyColumn(
            Modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .padding(horizontal = KuraTheme.spacing.md)
                .testTag("search-results"),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            item {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    TextButton(onClick = onBack) { Text("Back") }
                    Text(
                        categoryMode?.displayName()?.let { "$it files" } ?: "Search",
                        modifier = Modifier.kuraHeading(),
                        style = MaterialTheme.typography.headlineSmall,
                    )
                    TextButton(onClick = onRefresh, enabled = state.hasSearched && !state.refreshing) { Text("Refresh") }
                }
            }
            if (categoryMode != null) {
                item {
                    Text("Browse by category", style = MaterialTheme.typography.titleMedium)
                    EnumFilterRow(
                        "Category",
                        listOf(
                            SearchFileCategory.IMAGE,
                            SearchFileCategory.VIDEO,
                            SearchFileCategory.AUDIO,
                            SearchFileCategory.DOCUMENT,
                        ),
                        state.input.fileCategory,
                    ) { selected ->
                        onInput(state.input.copy(fileCategory = selected, entryType = FileEntryType.FILE))
                    }
                }
            }
            item {
                OutlinedTextField(
                    value = state.input.query.orEmpty(),
                    onValueChange = { onInput(state.input.copy(query = it)) },
                    label = { Text("Search term") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { if (rangesValid) submitSearch() }),
                    modifier = Modifier.fillMaxWidth().testTag("search-query"),
                )
            }
            item { SearchEnumFilters(state.input, onInput, showCategory = categoryMode == null) }
            item {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    RangeField("Updated from", updatedFrom, { updatedFrom = it }, Modifier.testTag("updated-from"))
                    RangeField("Updated to", updatedTo, { updatedTo = it }, Modifier.testTag("updated-to"))
                    RangeField("Minimum bytes", minimumSize, { minimumSize = it }, Modifier.testTag("minimum-size"))
                    RangeField("Maximum bytes", maximumSize, { maximumSize = it }, Modifier.testTag("maximum-size"))
                    OutlinedButton(
                        onClick = {
                            onInput(
                                state.input.copy(
                                    updatedFrom = parsedUpdatedFrom,
                                    updatedTo = parsedUpdatedTo,
                                    minSize = parsedMinimumSize,
                                    maxSize = parsedMaximumSize,
                                ),
                            )
                        },
                        enabled = rangesValid,
                    ) { Text("Apply ranges") }
                }
                if (!rangesValid) Text("Enter valid ISO-8601 dates and whole-byte sizes.", color = MaterialTheme.colorScheme.error)
            }
            item {
                OptionFilters("Owner", ownerOptions, state.input.ownerUserId) {
                    onInput(state.input.copy(ownerUserId = it))
                }
            }
            item {
                OptionFilters("Shared from", shareOptions, state.input.shareTargetId) {
                    onInput(state.input.copy(shareTargetId = it))
                }
            }
            item {
                Column {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text("Tags")
                        TextButton(onClick = onManageTags) { Text("Manage tags") }
                    }
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        tagOptions.forEach { tag ->
                            val selected = tag.id in state.input.tagIds
                            FilterChip(
                                selected = selected,
                                onClick = {
                                    val ids = if (selected) state.input.tagIds - tag.id else state.input.tagIds + tag.id
                                    onInput(state.input.copy(tagIds = ids))
                                },
                                enabled = selected || state.input.tagIds.size < 10,
                                label = { Text(tag.name) },
                            )
                        }
                    }
                }
            }
            state.validationError?.let { error -> item { Text(error, color = MaterialTheme.colorScheme.error) } }
            item {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                    OutlinedButton(
                        onClick = {
                            updatedFrom = ""
                            updatedTo = ""
                            minimumSize = ""
                            maximumSize = ""
                            onClear()
                        },
                        enabled = !state.loading,
                        modifier = Modifier.weight(1f),
                    ) { Text("Clear") }
                    Button(
                        onClick = submitSearch,
                        enabled = !state.loading && rangesValid,
                        modifier = Modifier.weight(1f).testTag("search-submit"),
                    ) { Text("Search") }
                }
            }
            item {
                OutlinedButton(onClick = onFavorites, modifier = Modifier.fillMaxWidth()) {
                    Text("Browse favorites")
                }
            }
            searchResults(state, onRefresh, onLoadMore, onOpen, shareOptions)
        }
    }
}

@Composable
private fun SearchEnumFilters(
    input: SearchInput,
    onInput: (SearchInput) -> Unit,
    showCategory: Boolean,
) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        EnumFilterRow("Entry", listOf(null, FileEntryType.FILE, FileEntryType.FOLDER), input.entryType) {
            onInput(input.copy(entryType = it, fileCategory = if (it == FileEntryType.FOLDER) null else input.fileCategory))
        }
        if (showCategory) {
            EnumFilterRow(
                "Category",
                listOf(null) + SearchFileCategory.entries.filter { it != SearchFileCategory.UNKNOWN },
                input.fileCategory,
            ) {
                onInput(input.copy(fileCategory = it, entryType = if (it == null) input.entryType else FileEntryType.FILE))
            }
        }
        EnumFilterRow(
            "Status",
            listOf(null, FileEntryStatus.ACTIVE, FileEntryStatus.MISSING_CANDIDATE, FileEntryStatus.MISSING),
            input.status,
        ) { onInput(input.copy(status = it)) }
    }
}

@Composable
private fun <T> EnumFilterRow(
    label: String,
    values: List<T?>,
    selected: T?,
    onSelect: (T?) -> Unit,
) {
    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("$label:", modifier = Modifier.padding(top = 8.dp))
        values.forEach { value ->
            FilterChip(selected = selected == value, onClick = { onSelect(value) }, label = { Text(value?.toString() ?: "Any") })
        }
    }
}

@Composable
private fun RangeField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        singleLine = true,
        modifier = modifier.fillMaxWidth(0.48f),
    )
}

@Composable
private fun OptionFilters(
    label: String,
    options: List<SearchFilterOption>,
    selectedId: String?,
    onSelect: (String?) -> Unit,
) {
    if (options.isEmpty()) return
    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("$label:", modifier = Modifier.padding(top = 8.dp))
        FilterChip(selected = selectedId == null, onClick = { onSelect(null) }, label = { Text("Any") })
        options.forEach { option ->
            FilterChip(selected = selectedId == option.id, onClick = { onSelect(option.id) }, label = { Text(option.label) })
        }
    }
}

private fun LazyListScope.searchResults(
    state: SearchUiState,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onOpen: (SearchResultItem) -> Unit,
    shareOptions: List<SearchFilterOption>,
) {
    when {
        state.loading -> item { LoadingState("Searching") }
        state.error != null && state.items.isEmpty() ->
            item { ErrorState(state.error.message, state.error.requestId, onRefresh) }
        state.hasSearched && state.items.isEmpty() -> item { EmptyState("No matching files or folders.") }
        else -> {
            items(state.items, key = { it.id }) { item -> SearchResultRow(item, onOpen, shareOptions) }
            if (state.error != null) item { Text(state.error.message, color = MaterialTheme.colorScheme.error) }
            if (state.canLoadMore) {
                item {
                    Button(
                        onClick = onLoadMore,
                        enabled = !state.loadingMore,
                        modifier = Modifier.testTag("search-load-more"),
                    ) {
                        Text(if (state.loadingMore) "Loading" else "Load more")
                    }
                }
            }
        }
    }
}

private fun parseInstantOrNull(value: String): Instant? = runCatching { Instant.parse(value) }.getOrNull()

@Composable
private fun SearchResultRow(
    item: SearchResultItem,
    onOpen: (SearchResultItem) -> Unit,
    shareOptions: List<SearchFilterOption>,
) {
    val active = item.status == FileEntryStatus.ACTIVE && item.entryType != FileEntryType.UNKNOWN
    KuraFileEntryRow(
        name = item.name,
        entryType = item.entryType,
        mimeType = item.mimeType,
        ownerName = item.owner.displayName,
        permission = item.permission,
        permissionSource = item.permissionSource,
        updatedAt = item.updatedAt,
        status = item.status,
        size = item.size.takeIf { item.entryType == FileEntryType.FILE },
        sharedFrom = item.shareTargetId?.let { shareLabel(it, shareOptions) },
        contextLine = item.fileCategory?.displayName(),
        enabled = active,
        onClick = { onOpen(item) },
        modifier = Modifier.testTag("search-result-${item.id}"),
    )
}

@Composable
fun RecentFilesScreen(
    state: RecentFilesUiState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onOpen: (RecentFileItem) -> Unit,
    shareOptions: List<SearchFilterOption> = emptyList(),
) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                TextButton(onClick = onBack) { Text("Back") }
                Text("Recent files", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.headlineSmall)
                TextButton(onClick = onRefresh, enabled = !state.refreshing) { Text("Refresh") }
            }
            when {
                state.loading -> LoadingState("Loading recent files")
                state.error != null && state.items.isEmpty() -> ErrorState(state.error.message, state.error.requestId, onRefresh)
                state.items.isEmpty() -> EmptyState("No recently opened files.")
                else ->
                    LazyColumn(Modifier.weight(1f).testTag("recent-results"), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        state.items.groupBy { recentGroup(it.openedAt) }.forEach { (group, groupItems) ->
                            item(key = "recent-group-$group") {
                                Text(group, modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
                            }
                            items(groupItems, key = { it.id }) { item ->
                                val metadata = item.metadata
                                KuraFileEntryRow(
                                    name = metadata.name,
                                    entryType = metadata.entryType,
                                    mimeType = metadata.mimeType,
                                    ownerName = item.owner.displayName,
                                    permission = metadata.permission,
                                    permissionSource = metadata.permissionSource,
                                    updatedAt = metadata.updatedAt,
                                    status = metadata.status,
                                    size = metadata.size,
                                    sharedFrom = metadata.shareTargetId?.let { shareLabel(it, shareOptions) },
                                    contextLine = "Opened ${item.openedAt}",
                                    onClick = { onOpen(item) },
                                    modifier = Modifier.testTag("recent-result-${item.id}"),
                                )
                            }
                        }
                        if (state.error != null) item { Text(state.error.message, color = MaterialTheme.colorScheme.error) }
                        if (state.canLoadMore) {
                            item {
                                Button(onClick = onLoadMore, enabled = !state.loadingMore) {
                                    Text(if (state.loadingMore) "Loading" else "Load more")
                                }
                            }
                        }
                    }
            }
        }
    }
}

private fun recentGroup(openedAt: Instant): String {
    val age = Duration.between(openedAt, Instant.now())
    return when {
        age.isNegative || age < Duration.ofDays(1) -> "Today"
        age < Duration.ofDays(7) -> "This week"
        age < Duration.ofDays(30) -> "This month"
        else -> "Older"
    }
}

private fun SearchFileCategory.displayName(): String =
    when (this) {
        SearchFileCategory.IMAGE -> "Photo"
        SearchFileCategory.VIDEO -> "Video"
        SearchFileCategory.AUDIO -> "Audio"
        SearchFileCategory.DOCUMENT -> "Document"
        SearchFileCategory.ARCHIVE -> "Archive"
        SearchFileCategory.OTHER -> "Other"
        SearchFileCategory.UNKNOWN -> "Unknown"
    }

private fun shareLabel(
    shareTargetId: String,
    options: List<SearchFilterOption>,
): String = options.firstOrNull { it.id == shareTargetId }?.label ?: "Shared item"
