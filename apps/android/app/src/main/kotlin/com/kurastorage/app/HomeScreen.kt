@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "MagicNumber",
)

package com.kurastorage.app

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraListRow
import com.kurastorage.core.ui.components.KuraSectionHeader
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.formatting.formatFileSize
import com.kurastorage.core.ui.icons.KuraFileType
import com.kurastorage.core.ui.icons.KuraFileTypeIcon
import com.kurastorage.core.ui.icons.KuraLogo
import com.kurastorage.feature.files.AdminStoragePanel
import com.kurastorage.feature.files.AdminStorageState
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun HomeScreen(
    connection: ConnectionStatus.Connected?,
    state: HomeUiState = HomeUiState(recentLoading = false, backupLoading = false),
    adminStorageState: AdminStorageState = AdminStorageState(loading = false),
    onRefreshAdminStorage: () -> Unit = {},
    onRefreshRecent: () -> Unit = {},
    onFiles: () -> Unit,
    onShared: () -> Unit = {},
    onSearch: () -> Unit = {},
    onRecent: () -> Unit = {},
    onCategory: (SearchFileCategory) -> Unit = {},
    onOpenRecent: (RecentFileItem) -> Unit = {},
    onActivity: () -> Unit = {},
    onFavorites: () -> Unit = {},
    onTags: () -> Unit = {},
    onTrash: () -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxWidth().testTag("home-list"),
        contentPadding =
            androidx.compose.foundation.layout
                .PaddingValues(KuraTheme.spacing.md),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.lg),
    ) {
        item {
            Row(
                horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                KuraLogo(size = 52.dp)
                Column {
                    Text("KuraStorage", style = MaterialTheme.typography.headlineSmall)
                    Text("Your files and backups at a glance", style = MaterialTheme.typography.bodyMedium)
                }
            }
        }
        item {
            KuraSectionHeader("Current status")
            StatusCards(connection, state)
        }
        if (adminStorageState.visible) {
            item {
                AdminStoragePanel(adminStorageState, onRefreshAdminStorage, onTrash)
            }
        }
        item {
            KuraSectionHeader("Go to")
            PrimaryDestinations(onFiles, onShared, onRecent)
        }
        item {
            KuraSectionHeader("Browse by category")
            CategoryDestinations(onCategory)
        }
        item { KuraSectionHeader("Recently opened", action = { HomeTextAction("See all", onRecent) }) }
        when {
            state.recentLoading -> item { CircularProgressIndicator() }
            state.recentError ->
                item {
                    KuraStatusPanel(
                        title = "Recent files unavailable",
                        message = "Other home sections are still available.",
                        status = KuraStatus.WARNING,
                        action = { HomeTextAction("Try again", onRefreshRecent) },
                    )
                }
            state.recentItems.isEmpty() ->
                item {
                    KuraStatusPanel(
                        title = "No recent files",
                        message = "Files you open will appear here.",
                        status = KuraStatus.NEUTRAL,
                    )
                }
            else ->
                items(state.recentItems, key = { it.id }) { recent ->
                    val metadata = recent.metadata
                    KuraListRow(
                        headline = metadata.name,
                        supportingText = "${formatHomeInstant(recent.openedAt)} · ${formatFileSize(metadata.size)}",
                        onClick = { onOpenRecent(recent) },
                        leading = {
                            KuraFileTypeIcon(
                                KuraFileType.from(metadata.mimeType, metadata.entryType == FileEntryType.FOLDER),
                            )
                        },
                    )
                }
        }
        item {
            KuraSectionHeader("More")
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                KuraListRow("Favorites", onClick = onFavorites)
                KuraListRow("Tags", onClick = onTags)
                KuraListRow("Activity", onClick = onActivity)
                KuraListRow("Trash", onClick = onTrash)
                KuraListRow("Search all files", onClick = onSearch)
            }
        }
    }
}

@Composable
private fun StatusCards(
    connection: ConnectionStatus.Connected?,
    state: HomeUiState,
) {
    val largeText = LocalDensity.current.fontScale >= 1.5f
    BoxWithConstraints {
        val twoColumns = maxWidth >= 600.dp && !largeText
        if (twoColumns) {
            Row(horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                ConnectionCard(connection, Modifier.weight(1f))
                BackupCard(state, Modifier.weight(1f))
            }
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                ConnectionCard(connection)
                BackupCard(state)
            }
        }
    }
}

@Composable
private fun ConnectionCard(
    connection: ConnectionStatus.Connected?,
    modifier: Modifier = Modifier,
) {
    val routeLabel =
        when (connection?.route) {
            ConnectionRoute.LOCAL_DIRECT -> "Local direct"
            ConnectionRoute.REMOTE_SECURE -> "ZeroTier"
            null -> "Connection unavailable"
        }
    val storageAvailable = connection?.storage == StorageAvailability.AVAILABLE
    KuraCard(modifier = modifier) {
        Text("Connection", style = MaterialTheme.typography.titleMedium)
        KuraStatusBadge(routeLabel, if (connection == null) KuraStatus.ERROR else KuraStatus.SUCCESS)
        Text(
            if (storageAvailable) "Storage is available" else "Storage is unavailable",
            style = MaterialTheme.typography.bodyMedium,
        )
    }
}

@Composable
private fun BackupCard(
    state: HomeUiState,
    modifier: Modifier = Modifier,
) {
    KuraCard(modifier = modifier) {
        Text("Automatic backup", style = MaterialTheme.typography.titleMedium)
        when {
            state.backupLoading -> CircularProgressIndicator()
            state.backupError -> KuraStatusBadge("Status unavailable", KuraStatus.WARNING)
            else -> {
                val summary = state.backupSummary
                val status =
                    when {
                        summary == null -> KuraStatus.NEUTRAL
                        summary.failedCount > 0 -> KuraStatus.ERROR
                        summary.uploadingCount > 0 -> KuraStatus.INFO
                        summary.pendingCount > 0 -> KuraStatus.WARNING
                        else -> KuraStatus.SUCCESS
                    }
                KuraStatusBadge(summary?.statusLabel ?: "No backup activity", status)
                Text("Pending: ${summary?.pendingCount ?: 0}", style = MaterialTheme.typography.bodyMedium)
                Text(
                    "Last completed: ${summary?.lastCompletedAt?.let(::formatHomeInstant) ?: "Never"}",
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        }
    }
}

@Composable
private fun PrimaryDestinations(
    onFiles: () -> Unit,
    onShared: () -> Unit,
    onRecent: () -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
        DestinationCard("My files", "Browse folders and files", onFiles)
        DestinationCard("Shared", "Open items shared with you", onShared)
        DestinationCard("Recent files", "Continue where you left off", onRecent)
    }
}

@Composable
private fun DestinationCard(
    title: String,
    description: String,
    onClick: () -> Unit,
) {
    KuraCard(onClick = onClick) {
        Text(title, style = MaterialTheme.typography.titleMedium)
        Text(description, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun CategoryDestinations(onCategory: (SearchFileCategory) -> Unit) {
    val categories =
        listOf(
            SearchFileCategory.IMAGE to "Photos",
            SearchFileCategory.VIDEO to "Videos",
            SearchFileCategory.AUDIO to "Audio",
            SearchFileCategory.DOCUMENT to "Documents",
        )
    val largeText = LocalDensity.current.fontScale >= 1.5f
    BoxWithConstraints {
        if (maxWidth >= 600.dp && !largeText) {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                categories.chunked(2).forEach { row ->
                    Row(horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                        row.forEach { (category, label) ->
                            KuraCard(modifier = Modifier.weight(1f), onClick = { onCategory(category) }) {
                                Text(label, style = MaterialTheme.typography.titleMedium)
                            }
                        }
                    }
                }
            }
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                categories.forEach { (category, label) -> KuraListRow(label, onClick = { onCategory(category) }) }
            }
        }
    }
}

@Composable
private fun HomeTextAction(
    label: String,
    onClick: () -> Unit,
) {
    androidx.compose.material3.TextButton(onClick = onClick) { Text(label) }
}

private fun formatHomeInstant(value: java.time.Instant): String =
    DateTimeFormatter
        .ofPattern("yyyy-MM-dd HH:mm")
        .withZone(ZoneId.systemDefault())
        .format(value)
