@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@file:Suppress(
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "MagicNumber",
    "MatchingDeclarationName",
    "MaxLineLength",
    "ktlint:standard:function-naming",
)

package com.kurastorage.feature.search

import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
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
import com.kurastorage.core.ui.EmptyState
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.LoadingState
import java.time.Instant

data class SearchFilterOption(
    val id: String,
    val label: String,
)

@Composable
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
    LazyColumn(
        Modifier.fillMaxSize().padding(16.dp).testTag("search-results"),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                TextButton(onClick = onBack) { Text("Back") }
                Text("Search", style = MaterialTheme.typography.headlineSmall)
                TextButton(onClick = onRefresh, enabled = state.hasSearched && !state.refreshing) { Text("Refresh") }
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
        item { SearchEnumFilters(state.input, onInput) }
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
        state.validationError?.let { error -> item { Text(error, color = MaterialTheme.colorScheme.error) } }
        item {
            Button(
                onClick = submitSearch,
                enabled = !state.loading && rangesValid,
                modifier = Modifier.fillMaxWidth().testTag("search-submit"),
            ) {
                Text("Search")
            }
        }
        searchResults(state, onRefresh, onLoadMore, onOpen, shareOptions)
    }
}

@Composable
private fun SearchEnumFilters(
    input: SearchInput,
    onInput: (SearchInput) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        EnumFilterRow("Entry", listOf(null, FileEntryType.FILE, FileEntryType.FOLDER), input.entryType) {
            onInput(input.copy(entryType = it, fileCategory = if (it == FileEntryType.FOLDER) null else input.fileCategory))
        }
        EnumFilterRow(
            "Category",
            listOf(null) + SearchFileCategory.entries.filter { it != SearchFileCategory.UNKNOWN },
            input.fileCategory,
        ) {
            onInput(input.copy(fileCategory = it, entryType = if (it == null) input.entryType else FileEntryType.FILE))
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
    Column(
        Modifier
            .fillMaxWidth()
            .clickable(enabled = active) { onOpen(item) }
            .padding(vertical = 8.dp)
            .testTag("search-result-${item.id}"),
    ) {
        Text("${item.entryType}: ${item.name}")
        Text("Owner: ${item.owner.displayName} • ${item.permission} (${item.permissionSource})")
        item.shareTargetId?.let { Text("Shared from: ${shareLabel(it, shareOptions)}") }
        Text("${item.fileCategory ?: "Folder"} • ${item.size} bytes • Updated ${item.updatedAt}")
        if (!active) Text("Unavailable: ${item.status}", color = MaterialTheme.colorScheme.error)
    }
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
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            TextButton(onClick = onBack) { Text("Back") }
            Text("Recent files", style = MaterialTheme.typography.headlineSmall)
            TextButton(onClick = onRefresh, enabled = !state.refreshing) { Text("Refresh") }
        }
        when {
            state.loading -> LoadingState("Loading recent files")
            state.error != null && state.items.isEmpty() -> ErrorState(state.error.message, state.error.requestId, onRefresh)
            state.items.isEmpty() -> EmptyState("No recently opened files.")
            else ->
                LazyColumn(Modifier.weight(1f).testTag("recent-results"), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(state.items, key = { it.id }) { item ->
                        val active = item.metadata.status == FileEntryStatus.ACTIVE
                        Column(
                            Modifier.fillMaxWidth().clickable(enabled = active) { onOpen(item) }.padding(vertical = 8.dp),
                        ) {
                            Text(item.metadata.name)
                            Text("Owner: ${item.owner.displayName} • ${item.metadata.permission} (${item.metadata.permissionSource})")
                            item.metadata.shareTargetId?.let { Text("Shared from: ${shareLabel(it, shareOptions)}") }
                            Text("Opened ${item.openedAt}")
                            if (!active) Text("Unavailable: ${item.metadata.status}", color = MaterialTheme.colorScheme.error)
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

private fun shareLabel(
    shareTargetId: String,
    options: List<SearchFilterOption>,
): String = options.firstOrNull { it.id == shareTargetId }?.label ?: "Shared item"
