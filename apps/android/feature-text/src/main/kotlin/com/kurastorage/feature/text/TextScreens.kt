@file:Suppress(
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "MagicNumber",
    "ktlint:standard:function-naming",
    "FunctionNaming",
)

package com.kurastorage.feature.text

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.FileVersionChangeKind

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TextEditorScreen(
    state: TextEditorUiState,
    onBack: () -> Unit,
    onRequestExit: () -> Boolean,
    onDismissDiscard: () -> Unit,
    onDiscard: () -> Unit,
    onBeginEdit: () -> Unit,
    onDraftChange: (String) -> Unit,
    onSave: () -> Unit,
    onReloadConflict: () -> Unit,
    onSaveAsCopy: (String) -> Unit,
    onHistory: () -> Unit,
    onReload: () -> Unit = {},
) {
    fun exit() {
        if (!onRequestExit()) onBack()
    }
    BackHandler(onBack = ::exit)
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(state.file?.name ?: "Text file") },
                navigationIcon = { TextButton(onClick = ::exit) { Text("Back") } },
                actions = { TextButton(onClick = onHistory, enabled = state.document != null) { Text("History") } },
            )
        },
    ) { padding ->
        when {
            state.phase == TextEditorPhase.LOADING ->
                Column(Modifier.fillMaxSize().padding(padding), verticalArrangement = Arrangement.Center) {
                    CircularProgressIndicator(modifier = Modifier.testTag("text-loading"))
                    Text("Loading text…")
                }
            state.phase == TextEditorPhase.ERROR && state.document == null ->
                Column(Modifier.fillMaxSize().padding(padding).padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("Unable to load or save this text.", color = MaterialTheme.colorScheme.error)
                    state.errorCode?.let { Text(it.name) }
                    state.requestId?.let { Text("Request ID: $it") }
                    Button(onClick = onReload) { Text("Retry") }
                }
            else ->
                EditorContent(
                    state = state,
                    modifier = Modifier.fillMaxSize().padding(padding).padding(16.dp),
                    onBeginEdit = onBeginEdit,
                    onDraftChange = onDraftChange,
                    onSave = onSave,
                    onReloadConflict = onReloadConflict,
                    onSaveAsCopy = onSaveAsCopy,
                )
        }
    }
    if (state.showDiscardConfirmation) {
        AlertDialog(
            onDismissRequest = onDismissDiscard,
            title = { Text("Discard unsaved changes?") },
            text = { Text("Your edited text has not been saved.") },
            confirmButton = {
                TextButton(onClick = {
                    onDiscard()
                    onBack()
                }) { Text("Discard") }
            },
            dismissButton = { TextButton(onClick = onDismissDiscard) { Text("Keep editing") } },
        )
    }
}

@Composable
private fun EditorContent(
    state: TextEditorUiState,
    modifier: Modifier,
    onBeginEdit: () -> Unit,
    onDraftChange: (String) -> Unit,
    onSave: () -> Unit,
    onReloadConflict: () -> Unit,
    onSaveAsCopy: (String) -> Unit,
) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("Version ${state.document?.fileVersion ?: "–"}")
            Text(if (state.canEdit) "Can edit" else "Read only", modifier = Modifier.testTag("text-permission"))
        }
        if (state.phase in setOf(TextEditorPhase.EDITING, TextEditorPhase.SAVING, TextEditorPhase.CONFLICT, TextEditorPhase.ERROR)) {
            OutlinedTextField(
                value = state.draft,
                onValueChange = onDraftChange,
                enabled = state.phase != TextEditorPhase.SAVING,
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .testTag("text-editor")
                        .semantics { contentDescription = "Text file content editor" },
                label = { Text("Content") },
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { onSave() }),
                minLines = 12,
            )
        } else {
            Text(
                text = state.document?.content.orEmpty(),
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .verticalScroll(rememberScrollState())
                        .testTag("text-viewer")
                        .semantics { contentDescription = "Text file content" },
            )
        }
        if (!state.draftPersisted) {
            Text("This large draft cannot be restored after process recreation.", color = MaterialTheme.colorScheme.error)
        }
        if (state.phase == TextEditorPhase.ERROR) {
            Text("The text was not saved. Correct the issue or retry.", color = MaterialTheme.colorScheme.error)
            state.errorCode?.let { Text(it.name) }
            state.requestId?.let { Text("Request ID: $it") }
        }
        if (state.phase == TextEditorPhase.CONFLICT) {
            Text("A newer version exists. Reload the server version or save a separate copy.", color = MaterialTheme.colorScheme.error)
            state.diff.filter { it.kind != LineDiffKind.SAME }.take(20).forEach { line ->
                Text(diffLabel(line), style = MaterialTheme.typography.bodySmall)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = onReloadConflict, modifier = Modifier.testTag("reload-conflict")) { Text("Reload latest") }
                OutlinedButton(onClick = { onSaveAsCopy(state.draft) }, modifier = Modifier.testTag("save-as-copy")) {
                    Text("Save as copy")
                }
            }
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (state.canEdit && state.phase !in setOf(TextEditorPhase.EDITING, TextEditorPhase.SAVING)) {
                    Button(onClick = onBeginEdit, modifier = Modifier.testTag("begin-edit")) { Text("Edit") }
                }
                if (state.canEdit && state.phase in setOf(TextEditorPhase.EDITING, TextEditorPhase.SAVING, TextEditorPhase.ERROR)) {
                    Button(
                        onClick = onSave,
                        enabled = state.dirty && state.phase != TextEditorPhase.SAVING,
                        modifier = Modifier.testTag("save-text"),
                    ) { Text(if (state.phase == TextEditorPhase.SAVING) "Saving…" else "Save") }
                }
                if (state.phase == TextEditorPhase.SAVED) Text("Saved", modifier = Modifier.testTag("text-saved"))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun VersionHistoryScreen(
    state: VersionHistoryUiState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onPreview: (Long) -> Unit,
    onDismissPreview: () -> Unit,
    onRequestRestore: (Long) -> Unit,
    onDismissRestore: () -> Unit,
    onConfirmRestore: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Version history") },
                navigationIcon = { TextButton(onClick = onBack) { Text("Back") } },
                actions = { TextButton(onClick = onRefresh) { Text("Refresh") } },
            )
        },
    ) { padding ->
        when {
            state.loading -> CircularProgressIndicator(Modifier.padding(padding).padding(24.dp).testTag("history-loading"))
            state.errorCode != null && state.items.isEmpty() ->
                Column(Modifier.padding(padding).padding(24.dp)) {
                    Text("History unavailable", color = MaterialTheme.colorScheme.error)
                    Text(state.errorCode.name)
                    state.requestId?.let { Text("Request ID: $it") }
                }
            state.items.isEmpty() -> Text("No versions yet", Modifier.padding(padding).padding(24.dp).testTag("history-empty"))
            else ->
                LazyColumn(Modifier.fillMaxSize().padding(padding).testTag("history-list")) {
                    if (state.errorCode != null) {
                        item {
                            Text(
                                if (state.restoreConflict) {
                                    "The current version changed. Refresh before restoring."
                                } else {
                                    "The history request failed: ${state.errorCode.name}"
                                },
                                color = MaterialTheme.colorScheme.error,
                                modifier = Modifier.padding(16.dp),
                            )
                        }
                    }
                    items(state.items, key = { it.version }) { version ->
                        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text("Version ${version.version}", style = MaterialTheme.typography.titleMedium)
                            Text(changeKindLabel(version.changeKind))
                            Text("${version.actorDisplayName} · ${version.createdAt}")
                            Text("${version.size} bytes · ${version.sha256.take(12)}…")
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                TextButton(onClick = { onPreview(version.version) }) { Text("Preview") }
                                Button(onClick = { onRequestRestore(version.version) }, enabled = state.canRestore) { Text("Restore") }
                            }
                        }
                        HorizontalDivider()
                    }
                    if (state.hasNextPage) {
                        item {
                            TextButton(
                                onClick = onLoadMore,
                                enabled = !state.loadingMore,
                                modifier = Modifier.testTag("history-load-more"),
                            ) {
                                Text(if (state.loadingMore) "Loading…" else "Load more")
                            }
                        }
                    }
                }
        }
    }
    state.preview?.let { preview ->
        AlertDialog(
            onDismissRequest = onDismissPreview,
            title = { Text("Version ${preview.fileVersion}") },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState())) {
                    Text(preview.content, modifier = Modifier.testTag("history-preview"))
                    state.previewDiff.orEmpty().filter { it.kind != LineDiffKind.SAME }.take(20).forEach {
                        Text(diffLabel(it), style = MaterialTheme.typography.bodySmall)
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { onRequestRestore(preview.fileVersion) }, enabled = state.canRestore) {
                    Text("Restore this version")
                }
            },
            dismissButton = { TextButton(onClick = onDismissPreview) { Text("Close") } },
        )
    }
    state.restoreConfirmationVersion?.let { version ->
        AlertDialog(
            onDismissRequest = onDismissRestore,
            title = { Text("Restore version $version?") },
            text = { Text("The current content will be retained as an older version.") },
            confirmButton = {
                TextButton(onClick = onConfirmRestore, enabled = !state.restoring, modifier = Modifier.testTag("confirm-restore")) {
                    Text(if (state.restoring) "Restoring…" else "Restore")
                }
            },
            dismissButton = { TextButton(onClick = onDismissRestore) { Text("Cancel") } },
        )
    }
}

private fun changeKindLabel(kind: FileVersionChangeKind): String =
    when (kind) {
        FileVersionChangeKind.UPLOAD -> "Upload"
        FileVersionChangeKind.TEXT_EDIT -> "Text edit"
        FileVersionChangeKind.EXTERNAL_CHANGE -> "External change"
        FileVersionChangeKind.RESTORE -> "Restore"
        FileVersionChangeKind.UNKNOWN -> "Unknown change"
    }

private fun diffLabel(line: LineDiff): String =
    when (line.kind) {
        LineDiffKind.SAME -> "Line ${line.lineNumber}: unchanged"
        LineDiffKind.CHANGED -> "Line ${line.lineNumber}: − ${line.current.orEmpty()} / + ${line.proposed.orEmpty()}"
        LineDiffKind.ADDED -> "Line ${line.lineNumber}: + ${line.proposed.orEmpty()}"
        LineDiffKind.REMOVED -> "Line ${line.lineNumber}: − ${line.current.orEmpty()}"
    }
