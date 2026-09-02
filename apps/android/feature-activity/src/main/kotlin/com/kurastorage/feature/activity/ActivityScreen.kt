@file:Suppress("FunctionNaming", "LongMethod", "LongParameterList", "MagicNumber", "MaxLineLength", "ktlint:standard:function-naming")

package com.kurastorage.feature.activity

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ActivityDeleteKind
import com.kurastorage.core.model.ActivityDetail
import com.kurastorage.core.model.ActivityEditKind
import com.kurastorage.core.model.ActivityItem
import com.kurastorage.core.model.ActivityShareAction
import com.kurastorage.core.model.UserActivityType
import com.kurastorage.core.ui.EmptyState
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.LoadingState
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

@Composable
fun ActivityScreen(
    state: ActivityUiState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onFilter: (UserActivityType?) -> Unit,
    onLoadMore: () -> Unit,
    onOpenTarget: (ActivityItem) -> Unit,
) {
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) { Text("Back") }
            Text("Activity", style = MaterialTheme.typography.headlineSmall)
            TextButton(onClick = onRefresh, enabled = !state.refreshing) { Text("Refresh") }
        }
        LazyColumn(
            Modifier.weight(1f).testTag("activity-list"),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            when {
                state.loading -> {
                    item { LoadingState("Loading activity") }
                    item { ActivityFilters(state.filter, onFilter) }
                }
                state.error != null && state.items.isEmpty() -> {
                    item { ErrorState(state.error.message, state.error.requestId, onRefresh) }
                    item { ActivityFilters(state.filter, onFilter) }
                }
                state.items.isEmpty() -> {
                    item { EmptyState("No activity to show.") }
                    item { ActivityFilters(state.filter, onFilter) }
                }
                else -> {
                    item { ActivityFilters(state.filter, onFilter) }
                    itemsIndexed(state.items, key = { index, item -> "$index-${item.stableKey}" }) { _, item ->
                        ActivityRow(item, onOpenTarget)
                    }
                    state.error?.let { error -> item { Text(error.message, color = MaterialTheme.colorScheme.error) } }
                    if (state.canLoadMore) {
                        item {
                            Button(onClick = onLoadMore, enabled = !state.loadingMore, modifier = Modifier.testTag("activity-load-more")) {
                                Text(if (state.loadingMore) "Loading" else "Load more")
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ActivityFilters(
    selected: UserActivityType?,
    onFilter: (UserActivityType?) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("Filter by operation", style = MaterialTheme.typography.labelLarge)
        listOf(null, UserActivityType.UPLOAD, UserActivityType.MOVE, UserActivityType.EDIT, UserActivityType.SHARE, UserActivityType.DELETE)
            .chunked(3)
            .forEach { row ->
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    row.forEach { type ->
                        FilterChip(
                            selected = selected == type,
                            onClick = { onFilter(type) },
                            label = { Text(type?.displayName() ?: "All") },
                        )
                    }
                }
            }
    }
}

@Composable
private fun ActivityRow(
    item: ActivityItem,
    onOpenTarget: (ActivityItem) -> Unit,
) {
    val openable = item.targetEntryId != null
    val formatter = rememberActivityFormatter()
    Surface(
        tonalElevation = 1.dp,
        modifier =
            Modifier
                .fillMaxWidth()
                .heightIn(min = 72.dp)
                .clickable(enabled = openable) { onOpenTarget(item) }
                .testTag("activity-item-${item.stableKey}"),
    ) {
        Row(Modifier.padding(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.Top) {
            ActivityTypeIcon(item.type)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(item.type.displayName(), style = MaterialTheme.typography.titleMedium)
                Text(item.targetName, style = MaterialTheme.typography.bodyLarge)
                Text(item.detail.description(), style = MaterialTheme.typography.bodyMedium)
                Text("By ${item.actorDisplayName}${item.actorDeviceName?.let { " on $it" } ?: ""}")
                Text("Owner: ${item.ownerDisplayName}")
                Text(formatter.format(item.occurredAt), style = MaterialTheme.typography.bodySmall)
                if (openable) {
                    Text("Open current item", color = MaterialTheme.colorScheme.primary)
                } else {
                    Text("Snapshot only — current item is unavailable", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

@Composable
private fun ActivityTypeIcon(type: UserActivityType) {
    val label = type.displayName()
    val color =
        when (type) {
            UserActivityType.UPLOAD -> Color(0xFF1565C0)
            UserActivityType.MOVE -> Color(0xFF6A1B9A)
            UserActivityType.EDIT -> Color(0xFF00695C)
            UserActivityType.SHARE -> Color(0xFFAD5700)
            UserActivityType.DELETE -> Color(0xFFB3261E)
            UserActivityType.UNKNOWN -> MaterialTheme.colorScheme.outline
        }
    Surface(shape = CircleShape, color = color, modifier = Modifier.size(48.dp).semantics { contentDescription = "$label operation" }) {
        Box(contentAlignment = Alignment.Center) { Text(label.take(1), color = Color.White) }
    }
}

@Composable
private fun rememberActivityFormatter(): DateTimeFormatter {
    val locale = LocalLocale.current.platformLocale
    val zone = ZoneId.systemDefault()
    return remember(locale, zone) { DateTimeFormatter.ofLocalizedDateTime(FormatStyle.MEDIUM).withLocale(locale).withZone(zone) }
}

private fun UserActivityType.displayName(): String =
    when (this) {
        UserActivityType.UPLOAD -> "Upload"
        UserActivityType.MOVE -> "Move"
        UserActivityType.EDIT -> "Edit"
        UserActivityType.SHARE -> "Share"
        UserActivityType.DELETE -> "Delete"
        UserActivityType.UNKNOWN -> "Unsupported activity"
    }

private fun ActivityDetail.description(): String =
    when (this) {
        is ActivityDetail.Upload -> "Uploaded as version $resultingFileVersion"
        is ActivityDetail.Move -> "Moved from $sourceParentName to $destinationParentName"
        is ActivityDetail.Edit ->
            if (kind ==
                ActivityEditKind.VERSION_RESTORE
            ) {
                "Restored as version $resultingFileVersion"
            } else {
                "Saved as version $resultingFileVersion"
            }
        is ActivityDetail.Share -> "${action.actionText()} $recipientDisplayName as ${permission.name.lowercase()}"
        is ActivityDetail.Delete -> if (kind == ActivityDeleteKind.PURGED) "Permanently deleted" else "Moved to trash"
        ActivityDetail.Unsupported -> "This activity requires a newer app version."
    }

private fun ActivityShareAction.actionText(): String =
    when (this) {
        ActivityShareAction.CREATED -> "Shared with"
        ActivityShareAction.UPDATED -> "Updated access for"
        ActivityShareAction.REVOKED -> "Removed access for"
        ActivityShareAction.UNKNOWN -> "Changed access for"
    }
