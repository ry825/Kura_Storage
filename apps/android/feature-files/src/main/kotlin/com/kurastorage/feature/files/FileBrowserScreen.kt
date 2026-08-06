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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
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
import androidx.compose.ui.unit.dp
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
    onDismissDetail: () -> Unit,
    onCancelTransfer: () -> Unit,
    onRetryTransfer: () -> Unit,
    onOpenDownload: (String) -> Unit,
) {
    var showCreate by remember { mutableStateOf(false) }
    var pendingTrash by remember { mutableStateOf<FileEntry?>(null) }
    var pendingRestore by remember { mutableStateOf<FileEntry?>(null) }
    if (state.loading && state.entries.isEmpty()) return LoadingState("Loading files")
    if (state.error != null && state.entries.isEmpty() && state.transfer == null) {
        return ErrorState(state.error.message, state.error.requestId, onRefresh)
    }
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
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
                        if (entry.entryType == FileEntryType.FOLDER && !trashMode) {
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
            text = { Text("${entry.entryType} • ${entry.size} bytes\nUpdated ${entry.updatedAt}") },
            confirmButton = {
                if (trashMode) {
                    Button(onClick = {
                        pendingRestore = entry
                        onDismissDetail()
                    }) { Text("Restore") }
                } else if (entry.entryType == FileEntryType.FILE) {
                    Button(onClick = {
                        onChooseDownload(entry)
                        onDismissDetail()
                    }) { Text("Download") }
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
