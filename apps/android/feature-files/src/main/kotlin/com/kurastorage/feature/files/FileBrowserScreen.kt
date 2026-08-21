@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
)

package com.kurastorage.feature.files

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.LinearProgressIndicator
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
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.ui.EmptyState
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.LoadingState

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
    onDismissDetail: () -> Unit,
    onCancelTransfer: () -> Unit,
    onRetryTransfer: () -> Unit,
    onOpenDownload: (String) -> Unit,
    adminStorageState: AdminStorageState = AdminStorageState(loading = false),
    onRefreshAdminStorage: () -> Unit = {},
    onOpenTrashFromWarning: () -> Unit = {},
) {
    var showCreate by remember { mutableStateOf(false) }
    var pendingTrash by remember { mutableStateOf<FileEntry?>(null) }
    var pendingRestore by remember { mutableStateOf<FileEntry?>(null) }
    if (state.loading && state.entries.isEmpty()) return LoadingState("Loading files")
    if (state.error != null && state.entries.isEmpty() && state.transfer == null) {
        return ErrorState(state.error.message, state.error.requestId, onRefresh)
    }
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        AdminStoragePanel(adminStorageState, onRefreshAdminStorage, onOpenTrashFromWarning)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onBack) { Text("Back") }
            Button(onClick = onRefresh) { Text("Refresh") }
        }
        if (!trashMode) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { showCreate = true }) { Text("New folder") }
                Button(onClick = onChooseUpload) { Text("Upload") }
            }
        }
        Text(if (trashMode) "Trash" else "My files", style = MaterialTheme.typography.headlineSmall)
        state.placementResult?.let { Text(it, color = MaterialTheme.colorScheme.primary) }
        state.error?.let { Text(it.message, color = MaterialTheme.colorScheme.error) }
        if (state.entries.isEmpty()) {
            EmptyState(if (trashMode) "Trash is empty." else "This folder is empty.")
        } else {
            LazyColumn(Modifier.weight(1f)) {
                items(state.entries, key = { it.id }) { entry ->
                    Row(
                        Modifier.fillMaxWidth().clickable { onOpen(entry) }.padding(vertical = 6.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text("${if (entry.entryType == FileEntryType.FOLDER) "Folder" else "File"}: ${entry.name}")
                        if (!trashMode) {
                            TextButton(onClick = { onShowDetails(entry) }) { Text("Actions") }
                        } else {
                            Text(if (entry.entryType == FileEntryType.FILE) "${entry.size} B" else "")
                        }
                    }
                }
                if (state.canLoadMore) {
                    item { Button(onClick = onLoadMore) { Text("Load more") } }
                }
            }
        }
        TransferPanel(state.transfer, onCancelTransfer, onRetryTransfer, onOpenDownload)
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
        AlertDialog(
            onDismissRequest = onDismissDetail,
            title = { Text(entry.name) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text("${entry.entryType} • ${entry.size} bytes\nUpdated ${entry.updatedAt}")
                    if (trashMode) Text(state.retention?.text ?: "Automatic deletion time is unavailable.")
                }
            },
            confirmButton = {
                if (trashMode) {
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
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        if (entry.entryType == FileEntryType.FILE) {
                            TextButton(onClick = {
                                onChooseDownload(entry)
                                onDismissDetail()
                            }) { Text("Download") }
                        }
                        TextButton(onClick = { onRename(entry) }) { Text("Rename") }
                        Button(onClick = { onMove(entry) }) { Text("Move") }
                    }
                }
            },
            dismissButton = {
                if (!trashMode) {
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
            Column(
                Modifier.fillMaxWidth().testTag("capacity-warning").padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                Text("Storage capacity warning", style = MaterialTheme.typography.titleMedium)
                Text("Available: ${formatBytes(status.availableBytes)}")
                Text("Warning threshold: ${formatBytes(status.capacityWarningThresholdBytes)}")
                Text("Trash estimate: ${formatBytes(status.trashBytes)}")
                Text("Expired trash roots: ${status.expiredTrashRootCount}")
                val latest = status.lastPurgeRun
                Text("Latest cleanup: ${latest?.status ?: "not available"}")
                latest?.let {
                    Text(
                        "Cleanup examined/deleted/errors: " +
                            "${it.examinedRootCount}/${it.deletedRootCount}/${it.errorCount}",
                    )
                    Text("Cleanup released: ${formatBytes(it.releasedBytes)}")
                }
                Text("The 30-day retention period is not shortened. Delete unneeded trash manually or expand storage.")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
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
                Text("Destination: ${state.currentFolderName}")
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
    when (event) {
        is TransferEvent.Progress -> {
            val fraction = event.totalBytes?.takeIf { it > 0 }?.let { event.transferredBytes.toFloat() / it }
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                if (fraction == null) LinearProgressIndicator() else LinearProgressIndicator({ fraction })
                Text("${event.transferredBytes} / ${event.totalBytes ?: "?"} bytes")
                TextButton(onClick = onCancel) { Text("Cancel") }
            }
        }
        is TransferEvent.Failed -> {
            Text("Transfer failed.", color = MaterialTheme.colorScheme.error)
            if (event.partialFileRemoved == false) Text("The partial download could not be removed.")
            Button(onClick = onRetry) { Text("Retry transfer") }
        }
        is TransferEvent.UploadCompleted -> Text("Upload completed.")
        is TransferEvent.DownloadCompleted -> {
            Text("Download completed.")
            Button(onClick = { onOpenDownload(event.destinationUri) }) { Text("Open") }
        }
        null -> Unit
    }
}

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
