@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "TooManyFunctions",
)

package com.kurastorage.feature.files

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.SupportedTextMimeTypes
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadState
import com.kurastorage.core.model.filePermissionCapabilities
import com.kurastorage.core.model.media.SupportedMediaMimeTypes
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.LoadingState
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraCardVariant
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.icons.KuraFileType
import com.kurastorage.core.ui.icons.KuraFileTypeIcon
import androidx.compose.foundation.lazy.grid.items as gridItems

@Composable
fun FileBrowserScreen(
    state: FileBrowserState,
    trashMode: Boolean,
    onOpen: (FileEntry) -> Unit,
    onShowDetails: (FileEntry) -> Unit,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onCreateFolder: (String) -> Unit,
    onChooseUpload: () -> Unit,
    onChooseDownload: (FileEntry) -> Unit,
    onTrash: (FileEntry) -> Unit,
    onRestore: (FileEntry) -> Unit,
    onBeginPermanentDelete: (FileEntry) -> Unit = {},
    onConfirmPermanentDelete: () -> Unit = {},
    onCancelPermanentDelete: () -> Unit = {},
    onRecheckMissing: (FileEntry) -> Unit = {},
    onBeginMissingIndexDelete: (FileEntry) -> Unit = {},
    onConfirmMissingIndexDelete: () -> Unit = {},
    onCancelMissingIndexDelete: () -> Unit = {},
    onRename: (FileEntry) -> Unit = {},
    onRenameInput: (String) -> Unit = {},
    onSubmitRename: () -> Unit = {},
    onDismissRename: () -> Unit = {},
    onMove: (FileEntry) -> Unit = {},
    onOpenMoveFolder: (FileEntry) -> Unit = {},
    onBackMoveFolder: () -> Unit = {},
    onLoadMoreMoveFolders: () -> Unit = {},
    onConfirmMove: () -> Unit = {},
    onDismissMove: () -> Unit = {},
    onRefreshPlacement: () -> Unit = {},
    onDetailDisplayed: (FileEntry) -> Unit = {},
    onDismissDetail: () -> Unit,
    onCancelTransfer: () -> Unit,
    onRetryTransfer: () -> Unit,
    onOpenDownload: (String) -> Unit,
    adminStorageState: AdminStorageState = AdminStorageState(loading = false),
    onRefreshAdminStorage: () -> Unit = {},
    onOpenTrashFromWarning: () -> Unit = {},
    onShare: (FileEntry) -> Unit = {},
    onOrganization: (String) -> Unit = {},
    onOpenMedia: (FileEntry) -> Unit = {},
    onOpenText: (FileEntry) -> Unit = {},
    onSearch: () -> Unit = {},
    thumbnail: @Composable (FileEntry, Modifier) -> Unit = { entry, modifier ->
        Box(modifier, contentAlignment = Alignment.Center) {
            KuraFileTypeIcon(
                type = KuraFileType.from(entry.mimeType, entry.entryType == FileEntryType.FOLDER),
                contentDescription = fileTypeLabel(entry),
            )
        }
    },
) {
    var showCreate by remember { mutableStateOf(false) }
    var pendingTrash by remember { mutableStateOf<FileEntry?>(null) }
    var pendingRestore by remember { mutableStateOf<FileEntry?>(null) }
    var gridMode by rememberSaveable { mutableStateOf(false) }
    if (state.loading && state.entries.isEmpty()) return LoadingState("Loading files")
    if (state.error != null && state.entries.isEmpty() && state.transfer == null) {
        return ErrorState(state.error.message, state.error.requestId, onRefresh)
    }
    val visibleEntries = state.entries
    val currentCapabilities =
        state.currentFolder?.let {
            filePermissionCapabilities(it.permission, it.permissionSource)
        } ?: filePermissionCapabilities(
            if (state.personalRoot) SharePermission.MANAGER else SharePermission.UNKNOWN,
            if (state.personalRoot) PermissionSource.OWNER else PermissionSource.UNKNOWN,
        )
    val folderTitle =
        when {
            trashMode -> "Trash"
            state.currentFolder != null -> state.currentFolder.name
            state.personalRoot -> "My files"
            else -> "Shared folder"
        }
    val breadcrumbTrail =
        state.breadcrumbs.let { breadcrumbs ->
            val currentFolder = state.currentFolder
            if (!trashMode && currentFolder != null && breadcrumbs.none { it.id == currentFolder.id }) {
                breadcrumbs + BrowserBreadcrumb(currentFolder.id, currentFolder.name)
            } else {
                breadcrumbs
            }
        }
    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(horizontal = KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            BrowserHeader(
                title = folderTitle,
                trashMode = trashMode,
                gridMode = gridMode,
                onBack = onBack,
                onRefresh = onRefresh,
                onSearch = onSearch,
                onList = { gridMode = false },
                onGrid = { gridMode = true },
            )
            Text(
                "Location: ${if (trashMode) "Trash" else breadcrumbTrail.joinToString(" / ") { it.label }}",
                modifier = Modifier.testTag("file-breadcrumb"),
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            AdminStoragePanel(adminStorageState, onRefreshAdminStorage, onOpenTrashFromWarning)
            if (!trashMode && currentCapabilities.canCreate) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                    OutlinedButton(onClick = { showCreate = true }, modifier = Modifier.weight(1f)) { Text("New folder") }
                    FloatingActionButton(
                        onClick = onChooseUpload,
                        modifier = Modifier.testTag("upload-fab"),
                    ) { Text("Upload") }
                }
            }
            if (!trashMode && !state.personalRoot) {
                state.currentFolder?.let { folder ->
                    KuraCard {
                        Text("Owner: ${folder.owner.displayName}")
                        Text("Permission: ${folder.permission} (${folder.permissionSource})")
                        folder.shareTargetId?.let {
                            Text("Shared from folder: ${shareTargetLabel(it, state.currentFolder)}")
                        }
                    }
                }
            }
            state.placementResult?.let {
                KuraStatusPanel("Updated", it, KuraStatus.SUCCESS, Modifier.testTag("placement-result"))
            }
            state.error?.let {
                KuraStatusPanel(
                    title = if (it.code == ErrorCode.RECOVERY_REQUIRED) "Recovery required" else "Files could not be updated",
                    message = it.message,
                    status = KuraStatus.ERROR,
                    action = { TextButton(onClick = onRefresh) { Text("Try again") } },
                )
            }
            if (visibleEntries.isEmpty()) {
                Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) {
                    KuraStatusPanel(
                        title = if (trashMode) "Trash is empty." else "This folder is empty.",
                        message = "There is nothing to show here yet.",
                        status = KuraStatus.NEUTRAL,
                    )
                }
            } else if (gridMode && !trashMode) {
                LazyVerticalGrid(
                    columns = GridCells.Adaptive(144.dp),
                    modifier = Modifier.weight(1f).testTag("file-grid"),
                    contentPadding = PaddingValues(bottom = KuraTheme.spacing.sm),
                    verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
                    horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
                ) {
                    gridItems(visibleEntries, key = { it.id }) { entry ->
                        FileGridItem(entry, onOpen, onShowDetails, thumbnail)
                    }
                    if (state.canLoadMore) item(key = "load-more") { Button(onClick = onLoadMore) { Text("Load more") } }
                }
            } else {
                val folders = visibleEntries.filter { it.entryType == FileEntryType.FOLDER }
                val files = visibleEntries.filter { it.entryType == FileEntryType.FILE }
                LazyColumn(
                    modifier = Modifier.weight(1f).testTag("file-list"),
                    contentPadding = PaddingValues(bottom = KuraTheme.spacing.sm),
                    verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
                ) {
                    if (folders.isNotEmpty()) {
                        item(key = "folder-section") {
                            Text("Folders", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleLarge)
                        }
                        items(folders, key = { "folder-${it.id}" }) { entry ->
                            FileListItem(entry, trashMode, state.currentFolder, onOpen, onShowDetails, thumbnail)
                        }
                    }
                    if (files.isNotEmpty()) {
                        item(key = "file-section") {
                            Text("Files", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleLarge)
                        }
                        items(files, key = { "file-${it.id}" }) { entry ->
                            FileListItem(entry, trashMode, state.currentFolder, onOpen, onShowDetails, thumbnail)
                        }
                    }
                    if (state.canLoadMore) item(key = "load-more") { Button(onClick = onLoadMore) { Text("Load more") } }
                }
            }
            TransferPanel(state.transfer, onCancelTransfer, onRetryTransfer, onOpenDownload)
        }
    }
    if (showCreate) {
        NameDialog(
            onDismiss = { showCreate = false },
            onCreate = {
                onCreateFolder(it)
                showCreate = false
            },
        )
    }
    state.selected?.let { entry ->
        LaunchedEffect(entry.id) { onDetailDisplayed(entry) }
        AlertDialog(
            onDismissRequest = onDismissDetail,
            title = { Text(entry.name) },
            text = {
                Column(
                    modifier = Modifier.verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    KuraCard {
                        Row(
                            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            KuraFileTypeIcon(
                                KuraFileType.from(entry.mimeType, entry.entryType == FileEntryType.FOLDER),
                                contentDescription = fileTypeLabel(entry),
                            )
                            Column {
                                Text(fileTypeLabel(entry), style = MaterialTheme.typography.titleMedium)
                                Text("MIME type: ${entry.mimeType ?: "Unknown"}", style = MaterialTheme.typography.bodySmall)
                                missingStatusText(entry)?.let { KuraStatusBadge(it, missingStatusStyle(entry.status)) }
                                entry.missingLastCheckedAt?.let { Text("最終確認: $it", style = MaterialTheme.typography.bodySmall) }
                            }
                        }
                    }
                    if (entry.isUnsupportedFile()) {
                        KuraStatusPanel(
                            title = "Unsupported file",
                            message = unsupportedReason(entry),
                            status = KuraStatus.WARNING,
                        )
                    }
                    KuraCard {
                        Text("File information", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
                        Text("Owner: ${entry.owner.displayName}")
                        Text("Permission: ${entry.permission} (${entry.permissionSource})")
                        Text("Name: ${entry.name}")
                        Text(
                            if (entry.entryType == FileEntryType.FILE) {
                                "Size: ${formatBytes(entry.size)}"
                            } else {
                                "Items: unavailable"
                            },
                        )
                        Text("Created: ${entry.createdAt}")
                        Text("Updated: ${entry.updatedAt}")
                        Text("Storage: ${if (trashMode) "KuraStorage Trash" else "Dedicated KuraStorage storage"}")
                        if (entry.permissionSource == PermissionSource.INHERITED) {
                            Text("Shared from folder: ${shareTargetLabel(entry.shareTargetId, state.currentFolder)}")
                        }
                        entry.missingDetectedAt?.let { Text("検出日時: $it") }
                        if (trashMode) {
                            entry.trashedAt?.let { Text("Moved to Trash: $it") }
                            Text(state.retention?.text ?: "Automatic deletion time is unavailable.")
                        }
                    }
                    state.historySyncError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                    KuraCard {
                        Text("Available actions", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
                        Text(detailActionSummary(entry, trashMode))
                    }
                    if (!trashMode &&
                        entry.status in
                        setOf(
                            FileEntryStatus.ACTIVE,
                            FileEntryStatus.MISSING_CANDIDATE,
                            FileEntryStatus.MISSING,
                        )
                    ) {
                        OutlinedButton(
                            onClick = { onOrganization(entry.id) },
                            modifier = Modifier.testTag("organize-entry"),
                        ) {
                            Text("Favorites and tags")
                        }
                    }
                    if (
                        entry.status == FileEntryStatus.ACTIVE &&
                        (entry.permission == SharePermission.UNKNOWN || entry.permissionSource == PermissionSource.UNKNOWN)
                    ) {
                        Text("Some actions are unavailable because current permission information is unknown.")
                    }
                }
            },
            confirmButton = {
                val capabilities = filePermissionCapabilities(entry.permission, entry.permissionSource)
                if (trashMode) {
                    if (capabilities.canManageTrash) {
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = {
                                pendingRestore = entry
                                onDismissDetail()
                            }) { Text("Restore") }
                            Button(
                                onClick = { onBeginPermanentDelete(entry) },
                                colors =
                                    ButtonDefaults.buttonColors(
                                        containerColor = MaterialTheme.colorScheme.error,
                                        contentColor = MaterialTheme.colorScheme.onError,
                                    ),
                                modifier = Modifier.testTag("delete-permanently"),
                            ) { Text("Delete permanently") }
                        }
                    } else {
                        Text("Only the owner can manage trash.")
                    }
                } else if (entry.status == FileEntryStatus.MISSING && capabilities.canManageTrash) {
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        OutlinedButton(
                            onClick = { onRecheckMissing(entry) },
                            enabled = entry.id !in state.missingActionIds,
                            modifier = Modifier.testTag("recheck-missing"),
                        ) { Text(if (entry.id in state.missingActionIds) "確認中…" else "再確認") }
                        Button(
                            onClick = { onBeginMissingIndexDelete(entry) },
                            enabled = entry.id !in state.missingActionIds,
                            modifier = Modifier.testTag("delete-missing-index"),
                        ) { Text("一覧から削除") }
                    }
                } else if (entry.status == FileEntryStatus.MISSING_CANDIDATE && capabilities.canManageTrash) {
                    TextButton(
                        onClick = { onRecheckMissing(entry) },
                        enabled = entry.id !in state.missingActionIds,
                    ) { Text(if (entry.id in state.missingActionIds) "確認中…" else "再確認") }
                } else if (entry.status == FileEntryStatus.UNKNOWN) {
                    Text("アプリの更新が必要です", color = MaterialTheme.colorScheme.error)
                } else {
                    Column {
                        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                            if (entry.isMediaPreview()) {
                                TextButton(onClick = { onOpenMedia(entry) }) { Text("Open") }
                            }
                            if (
                                entry.entryType == FileEntryType.FILE &&
                                entry.status == FileEntryStatus.ACTIVE &&
                                SupportedTextMimeTypes.isSupported(entry.mimeType)
                            ) {
                                TextButton(onClick = { onOpenText(entry) }) { Text("Open text") }
                            }
                            if (entry.entryType == FileEntryType.FILE && capabilities.canDownload) {
                                TextButton(onClick = {
                                    onChooseDownload(entry)
                                    onDismissDetail()
                                }) { Text("Download") }
                            }
                            if (capabilities.canRename) TextButton(onClick = { onRename(entry) }) { Text("Rename") }
                            if (capabilities.canMove) Button(onClick = { onMove(entry) }) { Text("Move") }
                        }
                        if (capabilities.canManageShare) {
                            TextButton(onClick = { onShare(entry) }) { Text("Sharing settings") }
                        }
                    }
                }
            },
            dismissButton = {
                if (!trashMode &&
                    entry.status == FileEntryStatus.ACTIVE &&
                    filePermissionCapabilities(entry.permission, entry.permissionSource).canTrash
                ) {
                    TextButton(onClick = {
                        pendingTrash = entry
                        onDismissDetail()
                    }) { Text("Move to trash") }
                }
            },
        )
    }
    pendingTrash?.let { entry ->
        ConfirmDialog(
            title = "Move to trash?",
            onDismiss = { pendingTrash = null },
            onConfirm = {
                onTrash(entry)
                pendingTrash = null
            },
        )
    }
    pendingRestore?.let { entry ->
        ConfirmDialog(
            title = "Restore this item?",
            onDismiss = { pendingRestore = null },
            onConfirm = {
                onRestore(entry)
                pendingRestore = null
            },
        )
    }
    state.permanentDelete?.let { deletion ->
        PermanentDeleteDialog(
            state = deletion,
            onDismiss = onCancelPermanentDelete,
            onConfirm = onConfirmPermanentDelete,
            onRefresh = onRefresh,
        )
    }
    state.missingIndexDelete?.let { deletion ->
        MissingIndexDeleteDialog(
            state = deletion,
            onDismiss = onCancelMissingIndexDelete,
            onConfirm = onConfirmMissingIndexDelete,
            onRefresh = onRefresh,
        )
    }
    state.rename?.let { rename ->
        RenameDialog(
            state = rename,
            onInput = onRenameInput,
            onDismiss = onDismissRename,
            onSubmit = onSubmitRename,
            onRefresh = onRefreshPlacement,
        )
    }
    state.movePicker?.let { picker ->
        MovePickerDialog(
            state = picker,
            onOpenFolder = onOpenMoveFolder,
            onBack = onBackMoveFolder,
            onLoadMore = onLoadMoreMoveFolders,
            onConfirm = onConfirmMove,
            onDismiss = onDismissMove,
            onRefresh = onRefreshPlacement,
        )
    }
}

@Composable
private fun BrowserHeader(
    title: String,
    trashMode: Boolean,
    gridMode: Boolean,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onSearch: () -> Unit,
    onList: () -> Unit,
    onGrid: () -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TextButton(onClick = onBack, modifier = Modifier.semantics { contentDescription = "Back" }) { Text("Back") }
            Text(title, modifier = Modifier.weight(1f).kuraHeading(), style = MaterialTheme.typography.headlineSmall)
            TextButton(onClick = onRefresh, modifier = Modifier.semantics { contentDescription = "Refresh files" }) {
                Text("Refresh")
            }
        }
        if (!trashMode) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                OutlinedButton(onClick = onSearch, modifier = Modifier.weight(1f)) { Text("Search") }
                OutlinedButton(onClick = onList, enabled = gridMode, modifier = Modifier.weight(1f)) { Text("List") }
                OutlinedButton(onClick = onGrid, enabled = !gridMode, modifier = Modifier.weight(1f)) { Text("Grid") }
            }
        }
    }
}

@Composable
private fun FileListItem(
    entry: FileEntry,
    trashMode: Boolean,
    currentFolder: FileEntry?,
    onOpen: (FileEntry) -> Unit,
    onShowDetails: (FileEntry) -> Unit,
    thumbnail: @Composable (FileEntry, Modifier) -> Unit,
) {
    KuraCard(
        modifier = Modifier.testTag("entry-${entry.id}"),
        variant = if (entry.status == FileEntryStatus.ACTIVE) KuraCardVariant.DEFAULT else KuraCardVariant.WARNING,
        onClick = { onOpen(entry) },
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            thumbnail(entry, Modifier.size(56.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xxs)) {
                Text(
                    "${if (entry.entryType == FileEntryType.FOLDER) "Folder" else "File"}: ${entry.name}",
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.titleMedium,
                )
                Text(entryMetadata(entry), style = MaterialTheme.typography.bodySmall)
                Text("Owner: ${entry.owner.displayName} • Permission: ${entry.permission}", style = MaterialTheme.typography.bodySmall)
                if (entry.shareTargetId != null || entry.permissionSource == PermissionSource.INHERITED) {
                    Text(
                        "Shared from: ${shareTargetLabel(entry.shareTargetId, currentFolder)}",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                missingStatusText(entry)?.let {
                    KuraStatusBadge(it, missingStatusStyle(entry.status), Modifier.testTag("entry-status-${entry.id}"))
                }
            }
            if (!trashMode) {
                TextButton(
                    onClick = { onShowDetails(entry) },
                    modifier = Modifier.semantics { contentDescription = "More actions for ${entry.name}" },
                ) { Text("Actions") }
            } else {
                Text(if (entry.entryType == FileEntryType.FILE) formatBytes(entry.size) else "Folder")
            }
        }
    }
}

@Composable
private fun FileGridItem(
    entry: FileEntry,
    onOpen: (FileEntry) -> Unit,
    onShowDetails: (FileEntry) -> Unit,
    thumbnail: @Composable (FileEntry, Modifier) -> Unit,
) {
    KuraCard(
        modifier = Modifier.testTag("grid-entry-${entry.id}"),
        variant = if (entry.status == FileEntryStatus.ACTIVE) KuraCardVariant.DEFAULT else KuraCardVariant.WARNING,
        onClick = { onOpen(entry) },
    ) {
        thumbnail(entry, Modifier.fillMaxWidth().heightIn(min = 88.dp, max = 128.dp))
        Text(entry.name, maxLines = 2, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.titleMedium)
        Text(entryMetadata(entry), maxLines = 2, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodySmall)
        if (entry.shareTargetId != null || entry.permissionSource == PermissionSource.INHERITED) {
            KuraStatusBadge("Shared", KuraStatus.INFO)
        }
        missingStatusText(entry)?.let { KuraStatusBadge(it, missingStatusStyle(entry.status)) }
        TextButton(
            onClick = { onShowDetails(entry) },
            modifier = Modifier.semantics { contentDescription = "More actions for ${entry.name}" },
        ) { Text("Actions") }
    }
}

private fun entryMetadata(entry: FileEntry): String =
    if (entry.entryType == FileEntryType.FOLDER) {
        "Folder • Items unavailable • Updated ${entry.updatedAt}"
    } else {
        "${fileTypeLabel(entry)} • ${formatBytes(entry.size)} • Updated ${entry.updatedAt}"
    }

private fun fileTypeLabel(entry: FileEntry): String =
    if (entry.entryType == FileEntryType.FOLDER) {
        "Folder"
    } else {
        KuraFileType.from(entry.mimeType, false).accessibilityLabel
    }

private fun missingStatusStyle(status: FileEntryStatus): KuraStatus =
    when (status) {
        FileEntryStatus.MISSING_CANDIDATE -> KuraStatus.WARNING
        FileEntryStatus.MISSING, FileEntryStatus.UNKNOWN -> KuraStatus.ERROR
        else -> KuraStatus.NEUTRAL
    }

private fun missingStatusText(entry: FileEntry): String? =
    when (entry.status) {
        FileEntryStatus.MISSING -> "ファイルが見つかりません"
        FileEntryStatus.MISSING_CANDIDATE -> "ファイルを確認中"
        FileEntryStatus.UNKNOWN -> "アプリの更新が必要です"
        else -> null
    }

private fun FileEntry.isMediaPreview(): Boolean {
    val activeFile = entryType == FileEntryType.FILE && status == FileEntryStatus.ACTIVE
    return activeFile &&
        (
            SupportedMediaMimeTypes.isPhoto(mimeType) ||
                SupportedMediaMimeTypes.isPdf(mimeType) ||
                SupportedMediaMimeTypes.isVideo(mimeType) ||
                SupportedMediaMimeTypes.isAudio(mimeType)
        )
}

private fun FileEntry.isUnsupportedFile(): Boolean =
    entryType == FileEntryType.FILE &&
        status == FileEntryStatus.ACTIVE &&
        !isMediaPreview() &&
        !SupportedTextMimeTypes.isSupported(mimeType)

private fun unsupportedReason(entry: FileEntry): String =
    if (entry.mimeType.isNullOrBlank()) {
        "The file type is unknown, so KuraStorage will not try to open it in the app. Download it only if you trust the file."
    } else {
        "${entry.mimeType} cannot be opened in KuraStorage. You can download it and choose a compatible external app."
    }

@Suppress("ReturnCount")
private fun detailActionSummary(
    entry: FileEntry,
    trashMode: Boolean,
): String {
    val capabilities = filePermissionCapabilities(entry.permission, entry.permissionSource)
    if (entry.status == FileEntryStatus.UNKNOWN) return "No actions are available until the app understands this file state."
    if (entry.status == FileEntryStatus.MISSING) {
        return if (capabilities.canManageTrash) {
            "Recheck or remove the missing index entry."
        } else {
            "No destructive actions are allowed."
        }
    }
    if (entry.status == FileEntryStatus.MISSING_CANDIDATE) return "Recheck the file before any content operation."
    if (trashMode) {
        return if (capabilities.canManageTrash) {
            "Restore or permanently delete this item."
        } else {
            "Only the owner can manage Trash."
        }
    }
    val actions =
        buildList {
            if (entry.isMediaPreview() || SupportedTextMimeTypes.isSupported(entry.mimeType)) add("Open")
            if (entry.entryType == FileEntryType.FILE && capabilities.canDownload) add("Download")
            if (capabilities.canRename) add("Rename")
            if (capabilities.canMove) add("Move")
            if (capabilities.canManageShare) add("Share")
            if (capabilities.canTrash) add("Move to Trash")
        }
    return actions.takeIf { it.isNotEmpty() }?.joinToString() ?: "No actions are available with the current permission."
}

@Composable
private fun MissingIndexDeleteDialog(
    state: MissingIndexDeleteState,
    onDismiss: () -> Unit,
    onConfirm: () -> Unit,
    onRefresh: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = { if (!state.submitting && !state.resultUnknown) onDismiss() },
        title = { Text("一覧から削除しますか？") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("対象: ${state.target.name}")
                Text("元ファイルはHDD上に存在しません。")
                Text("管理情報、共有情報、同期情報、サムネイル、プレビューキャッシュを削除します。")
                Text("KuraStorageの索引だけを削除します。HDD上のファイルは削除しません。")
                if (state.target.entryType == FileEntryType.FOLDER) {
                    Text("欠損している配下項目も一覧から削除されます。")
                }
                if (state.resultUnknown) {
                    Text("結果を確認できませんでした。一覧を更新してServerの状態を確認してください。")
                }
                state.error?.let {
                    Text(it.message, color = MaterialTheme.colorScheme.error)
                    TextButton(onClick = onRefresh, enabled = !state.submitting) { Text("一覧を更新") }
                }
                if (state.submitting) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        },
        confirmButton = {
            Button(
                onClick = onConfirm,
                enabled = !state.submitting && !state.resultUnknown,
                modifier = Modifier.testTag("confirm-delete-missing-index"),
            ) { Text(if (state.submitting) "削除中…" else "索引だけ削除") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !state.submitting && !state.resultUnknown) { Text("キャンセル") }
        },
    )
}

@Composable
fun AdminStoragePanel(
    state: AdminStorageState,
    onRefresh: () -> Unit,
    onOpenTrash: () -> Unit,
) {
    val status = state.status
    when {
        state.error ->
            Column(Modifier.testTag("admin-storage-error"), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text("Storage status could not be refreshed.", color = MaterialTheme.colorScheme.error)
                TextButton(onClick = onRefresh) { Text("Retry storage status") }
            }
        status?.storage == "UNAVAILABLE" ->
            Column(Modifier.testTag("admin-storage-unavailable"), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text("Storage is unavailable.", color = MaterialTheme.colorScheme.error)
                TextButton(onClick = onRefresh) { Text("Refresh storage status") }
            }
        status?.capacityWarning == true ->
            KuraCard(modifier = Modifier.testTag("capacity-warning"), variant = KuraCardVariant.WARNING) {
                Text("Storage capacity warning", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
                Text("Available: ${formatBytes(status.availableBytes)}")
                Text("Warning threshold: ${formatBytes(status.capacityWarningThresholdBytes)}")
                Text("Trash estimate: ${formatBytes(status.trashBytes)}")
                Text("Expired trash roots: ${status.expiredTrashRootCount}")
                val latest = status.lastPurgeRun
                Text("Latest cleanup: ${latest?.status ?: "not available"}")
                latest?.let {
                    Text("Cleanup examined/deleted/errors: ${it.examinedRootCount}/${it.deletedRootCount}/${it.errorCount}")
                    Text("Cleanup released: ${formatBytes(it.releasedBytes)}")
                }
                Text("The 30-day retention period is not shortened. Delete unneeded trash manually or expand storage.")
                Row(horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                    Button(onClick = onOpenTrash) { Text("Open Trash") }
                    TextButton(onClick = onRefresh) { Text("Refresh") }
                }
            }
    }
}

@Composable
private fun PermanentDeleteDialog(
    state: PermanentDeleteState,
    onDismiss: () -> Unit,
    onConfirm: () -> Unit,
    onRefresh: () -> Unit,
) {
    val refreshOnly = state.error?.code in setOf(ErrorCode.FILE_NOT_FOUND, ErrorCode.IDEMPOTENCY_CONFLICT)
    val refreshRecommended =
        state.resultUnknown ||
            state.error?.code in
            setOf(ErrorCode.FILE_NOT_FOUND, ErrorCode.IDEMPOTENCY_CONFLICT, ErrorCode.RECOVERY_REQUIRED)
    AlertDialog(
        onDismissRequest = { if (!state.submitting) onDismiss() },
        title = { Text("Delete ${state.target.name} permanently?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("This operation cannot be undone.")
                if (state.target.entryType == FileEntryType.FOLDER) {
                    Text("The folder and everything inside it will be permanently deleted.")
                }
                state.error?.let { error ->
                    Text(error.message, color = MaterialTheme.colorScheme.error)
                    if (refreshRecommended) {
                        TextButton(onClick = onRefresh, enabled = !state.submitting) { Text("Refresh to confirm") }
                    }
                }
                if (state.submitting) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        },
        confirmButton = {
            Button(
                onClick = onConfirm,
                enabled = !state.submitting && !refreshOnly,
                colors =
                    ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError,
                    ),
                modifier = Modifier.testTag("confirm-permanent-delete"),
            ) {
                Text(
                    if (state.submitting) {
                        "Deleting…"
                    } else if (state.resultUnknown) {
                        "Retry same request"
                    } else if (refreshOnly) {
                        "Refresh list first"
                    } else {
                        "Delete permanently"
                    },
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !state.submitting && !state.resultUnknown) {
                Text("Cancel")
            }
        },
    )
}

@Composable
private fun RenameDialog(
    state: RenameState,
    onInput: (String) -> Unit,
    onDismiss: () -> Unit,
    onSubmit: () -> Unit,
    onRefresh: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = { if (!state.submitting) onDismiss() },
        title = { Text("Rename ${state.target.name}") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = state.input,
                    onValueChange = onInput,
                    enabled = !state.submitting,
                    label = { Text("New name") },
                    singleLine = true,
                    modifier = Modifier.testTag("rename-input"),
                )
                state.error?.let { error ->
                    Text(error.message, color = MaterialTheme.colorScheme.error)
                    if (error.resultUnknown) TextButton(onClick = onRefresh) { Text("Refresh to confirm") }
                }
                if (state.submitting) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        },
        confirmButton = {
            Button(onClick = onSubmit, enabled = !state.submitting) {
                Text(if (state.submitting) "Renaming…" else "Rename")
            }
        },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !state.submitting) { Text("Cancel") } },
    )
}

@Composable
private fun MovePickerDialog(
    state: MovePickerState,
    onOpenFolder: (FileEntry) -> Unit,
    onBack: () -> Unit,
    onLoadMore: () -> Unit,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    onRefresh: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = { if (!state.submitting) onDismiss() },
        title = { Text("Move ${state.target.name}") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Server folder selection", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
                Text("My files / ${state.currentFolderName}", modifier = Modifier.testTag("move-breadcrumb"))
                Text("Destination: ${state.currentFolderName}")
                KuraStatusBadge(
                    if (state.destinationWritable) "Writable destination" else "Read-only destination",
                    if (state.destinationWritable) KuraStatus.SUCCESS else KuraStatus.WARNING,
                )
                if (state.currentFolderId == state.target.parentId) {
                    Text("This is the current folder.", color = MaterialTheme.colorScheme.secondary)
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = onBack, enabled = state.canGoBack && !state.loading) { Text("Up") }
                    if (state.canLoadMore) {
                        TextButton(onClick = onLoadMore, enabled = !state.loading && !state.submitting) {
                            Text("Load more")
                        }
                    }
                }
                if (state.loading) {
                    LinearProgressIndicator(Modifier.fillMaxWidth())
                } else if (state.folders.isEmpty()) {
                    Text("No folders here.")
                } else {
                    LazyColumn(Modifier.fillMaxWidth().heightIn(max = 280.dp)) {
                        items(state.folders, key = { it.id }) { folder ->
                            TextButton(
                                onClick = { onOpenFolder(folder) },
                                enabled = state.canOpen(folder) && !state.submitting,
                                modifier = Modifier.testTag("move-folder-${folder.id}"),
                            ) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
                                    verticalAlignment = Alignment.CenterVertically,
                                ) {
                                    KuraFileTypeIcon(KuraFileType.FOLDER, contentDescription = "Folder")
                                    Text(
                                        if (state.canOpen(folder)) {
                                            "Folder: ${folder.name}"
                                        } else {
                                            "Folder: ${folder.name} (unavailable)"
                                        },
                                    )
                                }
                            }
                        }
                    }
                }
                state.error?.let { error ->
                    Text(error.message, color = MaterialTheme.colorScheme.error)
                    if (error.resultUnknown || error.code == com.kurastorage.core.model.ErrorCode.FILE_NOT_FOUND) {
                        TextButton(onClick = onRefresh) { Text("Refresh list") }
                    }
                }
                if (state.submitting) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        },
        confirmButton = {
            Button(onClick = onConfirm, enabled = state.canConfirm, modifier = Modifier.testTag("move-confirm")) {
                Text(if (state.submitting) "Moving…" else "Move here")
            }
        },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !state.submitting) { Text("Cancel") } },
    )
}

@Composable
private fun TransferPanel(
    event: TransferEvent?,
    onCancel: () -> Unit,
    onRetry: () -> Unit,
    onOpenDownload: (String) -> Unit,
) {
    var confirmCancel by remember { mutableStateOf(false) }
    if (event != null) {
        KuraCard(
            modifier =
                Modifier
                    .testTag("transfer-status")
                    .semantics { stateDescription = transferStateDescription(event) },
        ) {
            Text("Transfer status", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
            when (event) {
                is TransferEvent.Progress -> {
                    val fraction = event.totalBytes?.takeIf { it > 0 }?.let { event.transferredBytes.toFloat() / it }
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text("Downloading")
                        if (fraction == null) LinearProgressIndicator() else LinearProgressIndicator({ fraction })
                        Text("${event.transferredBytes} / ${event.totalBytes ?: "?"} bytes")
                        TextButton(onClick = onCancel) { Text("Cancel") }
                    }
                }
                is TransferEvent.UploadStatus -> {
                    val operation = event.operation
                    val fraction =
                        if (operation.size > 0) operation.confirmedOffset.toFloat() / operation.size else 0f
                    Column(
                        Modifier.fillMaxWidth().testTag("upload-status"),
                        verticalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        LinearProgressIndicator({ fraction.coerceIn(0f, 1f) }, Modifier.fillMaxWidth())
                        Text(operation.fileName, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text("${operation.confirmedOffset} / ${operation.size} bytes")
                        Text(event.message ?: operation.state.uploadLabel())
                        if (event.canRetry) {
                            Button(onClick = onRetry, modifier = Modifier.testTag("resume-upload")) {
                                Text("Resume from confirmed position")
                            }
                        }
                        if (operation.state in
                            setOf(
                                UploadState.PREPARING,
                                UploadState.CREATING_SESSION,
                                UploadState.UPLOADING,
                                UploadState.PAUSED,
                            )
                        ) {
                            TextButton(onClick = { confirmCancel = true }, modifier = Modifier.testTag("cancel-upload")) {
                                Text("Cancel upload")
                            }
                        }
                    }
                }
                is TransferEvent.Failed -> {
                    Text("Transfer failed.", color = MaterialTheme.colorScheme.error)
                    Text("The server result was not assumed successful. Retry to recheck or resume the same transfer.")
                    if (event.partialFileRemoved == false) Text("The partial download could not be removed.")
                    Button(onClick = onRetry) { Text("Retry transfer") }
                }
                is TransferEvent.UploadCompleted -> Text("Upload completed.")
                is TransferEvent.DownloadCompleted -> {
                    Text("Download completed.")
                    Button(onClick = { onOpenDownload(event.destinationUri) }) { Text("Open") }
                }
            }
        }
    }
    if (confirmCancel) {
        AlertDialog(
            onDismissRequest = { confirmCancel = false },
            title = { Text("Cancel upload?") },
            text = { Text("The resumable server session and its temporary data will be removed.") },
            confirmButton = {
                Button(
                    onClick = {
                        confirmCancel = false
                        onCancel()
                    },
                    modifier = Modifier.testTag("confirm-cancel-upload"),
                ) { Text("Cancel upload") }
            },
            dismissButton = { TextButton(onClick = { confirmCancel = false }) { Text("Keep uploading") } },
        )
    }
}

private fun transferStateDescription(event: TransferEvent): String =
    when (event) {
        is TransferEvent.Progress -> "Download in progress"
        is TransferEvent.UploadStatus -> event.operation.state.uploadLabel()
        is TransferEvent.Failed -> "Transfer failed"
        is TransferEvent.UploadCompleted -> "Upload completed"
        is TransferEvent.DownloadCompleted -> "Download completed"
    }

private fun UploadState.uploadLabel() =
    when (this) {
        UploadState.PREPARING -> "Preparing and calculating the SHA-256 hash"
        UploadState.CREATING_SESSION -> "Waiting for a resumable upload session"
        UploadState.UPLOADING -> "Uploading"
        UploadState.PAUSED -> "Upload paused; received bytes are safe on the server"
        UploadState.VERIFYING -> "Verifying the completed file"
        UploadState.COMPLETED -> "Upload completed"
        UploadState.CANCELLED -> "Upload cancelled"
        UploadState.FAILED -> "Upload needs attention"
    }

private fun shareTargetLabel(
    shareTargetId: String?,
    currentFolder: FileEntry?,
): String =
    currentFolder
        ?.takeIf { folder ->
            shareTargetId != null && (folder.id == shareTargetId || folder.shareTargetId == shareTargetId)
        }?.name
        ?: "Shared item"

@Composable
private fun ConfirmDialog(
    title: String,
    onDismiss: () -> Unit,
    onConfirm: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        confirmButton = { Button(onClick = onConfirm) { Text("Confirm") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

@Composable
private fun NameDialog(
    onDismiss: () -> Unit,
    onCreate: (String) -> Unit,
) {
    var name by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Create folder") },
        text = { OutlinedTextField(name, { name = it }, label = { Text("Name") }) },
        confirmButton = { Button(onClick = { onCreate(name) }, enabled = name.isNotBlank()) { Text("Create") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
