@file:Suppress(
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "MagicNumber",
    "MaxLineLength",
    "ktlint:standard:function-naming",
)

package com.kurastorage.feature.search

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.FavoriteItem
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.ui.EmptyState
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.LoadingState
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraFileEntryRow
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.components.KuraStatusPanel

@Composable
fun FavoritesScreen(
    state: FavoritesUiState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onOpen: (FavoriteItem) -> Unit,
    shareOptions: List<SearchFilterOption> = emptyList(),
) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Header(
                "Favorites",
                onBack,
                onRefresh,
                !state.loading && !state.refreshing && !state.loadingMore,
            )
            when {
                state.loading -> LoadingState("Loading favorites")
                state.error != null && state.items.isEmpty() -> ErrorState(state.error.message, state.error.requestId, onRefresh)
                state.items.isEmpty() -> EmptyState("No favorite files or folders.")
                else ->
                    LazyColumn(Modifier.weight(1f).testTag("favorites-list"), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(state.items, key = { it.id }) { item ->
                            FavoriteRow(item, onOpen, shareOptions)
                        }
                        state.error?.let { error -> item { Text(error.message, color = MaterialTheme.colorScheme.error) } }
                        if (state.canLoadMore) {
                            item {
                                Button(
                                    onLoadMore,
                                    enabled = !state.loadingMore,
                                ) { Text(if (state.loadingMore) "Loading" else "Load more") }
                            }
                        }
                    }
            }
        }
    }
}

@Composable
private fun FavoriteRow(
    item: FavoriteItem,
    onOpen: (FavoriteItem) -> Unit,
    shareOptions: List<SearchFilterOption>,
) {
    val active = item.metadata.status == FileEntryStatus.ACTIVE
    val metadata = item.metadata
    KuraFileEntryRow(
        name = metadata.name,
        entryType = metadata.entryType,
        mimeType = metadata.mimeType,
        ownerName = metadata.owner.displayName,
        permission = metadata.permission,
        permissionSource = metadata.permissionSource,
        updatedAt = metadata.updatedAt,
        status = metadata.status,
        size = metadata.size.takeIf { metadata.entryType == com.kurastorage.core.model.FileEntryType.FILE },
        sharedFrom = metadata.shareTargetId?.let { id -> shareOptions.firstOrNull { it.id == id }?.label ?: "Shared item" },
        contextLine = "Favorited ${item.favoritedAt}",
        enabled = active,
        onClick = { onOpen(item) },
        modifier = Modifier.testTag("favorite-${item.id}"),
    )
}

@Composable
@Suppress("CyclomaticComplexMethod")
fun TagsScreen(
    state: TagsUiState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onCreate: () -> Unit,
    onRename: (TagItem) -> Unit,
    onDelete: (TagItem) -> Unit,
    onInput: (String) -> Unit,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Header("Tags", onBack, onRefresh, !state.loading)
            KuraStatusPanel(
                title = "Personal tags",
                message = "Up to 200 tags can be created. Each file or folder can use up to 20 tags.",
                status = KuraStatus.NEUTRAL,
            )
            Button(onClick = onCreate, enabled = state.pendingTagId == null, modifier = Modifier.fillMaxWidth().testTag("tag-create")) {
                Text("Create tag")
            }
            when {
                state.loading -> LoadingState("Loading tags")
                state.error != null && state.tags.isEmpty() -> ErrorState(state.error.message, state.error.requestId, onRefresh)
                state.tags.isEmpty() -> EmptyState("No tags.")
                else ->
                    LazyColumn(Modifier.weight(1f).testTag("tags-list")) {
                        items(state.tags, key = { it.id }) { tag ->
                            KuraCard {
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                    Text(tag.name, style = MaterialTheme.typography.titleMedium)
                                    Row {
                                        TextButton(onClick = { onRename(tag) }) { Text("Rename") }
                                        TextButton(onClick = { onDelete(tag) }) { Text("Delete") }
                                    }
                                }
                            }
                        }
                    }
            }
            state.error?.let { Text(it.message, color = MaterialTheme.colorScheme.error) }
        }
    }
    state.dialog?.let { dialog ->
        AlertDialog(
            onDismissRequest = { if (state.pendingTagId == null) onDismiss() },
            title = {
                Text(
                    when (dialog) {
                        TagDialog.CREATE -> "Create tag"
                        TagDialog.RENAME -> "Rename tag"
                        TagDialog.DELETE -> "Delete tag"
                    },
                )
            },
            text = {
                Column {
                    if (dialog == TagDialog.DELETE) {
                        Text("Delete this tag from every file and folder?")
                    } else {
                        OutlinedTextField(
                            state.input,
                            onInput,
                            enabled = state.pendingTagId == null,
                            label = { Text("Tag name") },
                            modifier = Modifier.testTag("tag-name"),
                        )
                    }
                    state.validationError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                    if (state.pendingTagId != null) LinearProgressIndicator(Modifier.fillMaxWidth())
                }
            },
            confirmButton = {
                TextButton(onClick = onConfirm, enabled = state.pendingTagId == null) {
                    Text(
                        if (dialog ==
                            TagDialog.DELETE
                        ) {
                            "Delete"
                        } else {
                            "Save"
                        },
                    )
                }
            },
            dismissButton = { TextButton(onClick = onDismiss, enabled = state.pendingTagId == null) { Text("Cancel") } },
        )
    }
}

@Composable
@Suppress("CyclomaticComplexMethod")
fun EntryOrganizationScreen(
    state: EntryOrganizationUiState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onToggleFavorite: () -> Unit,
    onToggleTag: (TagItem) -> Unit,
    onManageTags: () -> Unit,
) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Header(
                "Organize",
                onBack,
                onRefresh,
                !state.loading && !state.pendingFavorite && state.pendingTagIds.isEmpty(),
            )
            when {
                state.loading -> LoadingState("Loading organization")
                state.error != null && state.organization == null -> ErrorState(state.error.message, state.error.requestId, onRefresh)
                else -> {
                    state.entry?.let { EntrySummary(it) }
                    val organization = state.organization
                    KuraStatusBadge(
                        if (organization?.isFavorite == true) "Favorite" else "Not favorite",
                        if (organization?.isFavorite == true) KuraStatus.SUCCESS else KuraStatus.NEUTRAL,
                    )
                    if (state.pendingFavorite || state.pendingTagIds.isNotEmpty()) {
                        LinearProgressIndicator(Modifier.fillMaxWidth())
                        Text("Saving organization…")
                    }
                    OutlinedButton(
                        onClick = onToggleFavorite,
                        enabled =
                            !state.pendingFavorite && (state.canAttach || organization?.isFavorite == true),
                    ) {
                        Text(if (organization?.isFavorite == true) "Remove favorite" else "Add favorite")
                    }
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text("Tags", style = MaterialTheme.typography.titleMedium)
                        TextButton(onClick = onManageTags) { Text("Manage tags") }
                    }
                    if (!state.canAttach) {
                        Text(
                            "Unavailable items only allow removing existing organization data.",
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                    LazyColumn(Modifier.weight(1f).testTag("entry-tags")) {
                        items(state.availableTags, key = { it.id }) { tag ->
                            val attached = organization?.tags?.any { it.id == tag.id } == true
                            FilterChip(
                                selected = attached,
                                onClick = { onToggleTag(tag) },
                                enabled = tag.id !in state.pendingTagIds && (state.canAttach || attached),
                                label = { Text(tag.name) },
                            )
                        }
                    }
                    state.error?.let { Text(it.message, color = MaterialTheme.colorScheme.error) }
                }
            }
        }
    }
}

@Composable
private fun EntrySummary(entry: FileEntry) {
    KuraCard {
        Text(entry.name, style = MaterialTheme.typography.titleLarge)
        Text("${entry.entryType} • ${entry.status} • ${entry.owner.displayName}")
        Text("${entry.permission} (${entry.permissionSource})")
    }
}

@Composable
private fun Header(
    title: String,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    refreshEnabled: Boolean,
) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        TextButton(onClick = onBack) { Text("Back") }
        Text(title, modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.headlineSmall)
        TextButton(onClick = onRefresh, enabled = refreshEnabled) { Text("Refresh") }
    }
}
