@file:Suppress(
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "MagicNumber",
    "TooManyFunctions",
    "ktlint:standard:function-naming",
    "FunctionNaming",
)

package com.kurastorage.feature.text

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileVersionChangeKind
import com.kurastorage.core.model.FileVersionItem
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraAdaptiveActionLayout
import com.kurastorage.core.ui.components.KuraAppScaffold
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraCardVariant
import com.kurastorage.core.ui.components.KuraConfirmationDialog
import com.kurastorage.core.ui.components.KuraPrimaryButton
import com.kurastorage.core.ui.components.KuraSecondaryButton
import com.kurastorage.core.ui.components.KuraSegmentedControl
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.components.KuraTopAppBar
import com.kurastorage.core.ui.state.KuraStateKind
import com.kurastorage.core.ui.state.KuraStateView

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
    onSaveAndExit: () -> Unit = onSave,
    onExitAfterSaveConsumed: () -> Unit = {},
    onEndEdit: () -> Unit = {},
) {
    fun exit() {
        if (!onRequestExit()) onBack()
    }
    BackHandler(onBack = ::exit)
    LaunchedEffect(state.phase, state.exitAfterSave) {
        if (state.phase == TextEditorPhase.SAVED && state.exitAfterSave) {
            onExitAfterSaveConsumed()
            onBack()
        }
    }
    KuraAppScaffold(
        topBar = {
            KuraTopAppBar(
                title = state.file?.name ?: "Text file",
                navigationIcon = { TextButton(onClick = ::exit, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") } },
                actions = {
                    TextButton(onClick = onHistory, enabled = state.document != null, modifier = Modifier.heightIn(min = 48.dp)) {
                        Text("History")
                    }
                },
            )
        },
    ) { padding ->
        when {
            state.phase == TextEditorPhase.LOADING ->
                StateBox(padding) {
                    KuraStateView(
                        kind = KuraStateKind.LOADING,
                        title = "Loading text",
                        message = "Checking the latest version and your current permission.",
                        modifier = Modifier.testTag("text-loading"),
                    )
                }
            state.phase == TextEditorPhase.ERROR && state.document == null ->
                StateBox(padding) {
                    KuraStateView(
                        kind =
                            if (state.errorCode ==
                                ErrorCode.FILE_NOT_FOUND
                            ) {
                                KuraStateKind.BLOCKING_ERROR
                            } else {
                                KuraStateKind.RECOVERABLE_ERROR
                            },
                        title =
                            if (state.errorCode ==
                                ErrorCode.FILE_NOT_FOUND
                            ) {
                                "Text is no longer available"
                            } else {
                                "Unable to load this text"
                            },
                        message = editorErrorMessage(state.errorCode),
                        requestId = state.requestId,
                        actionLabel = if (state.errorCode == ErrorCode.FILE_NOT_FOUND) null else "Retry",
                        onAction = if (state.errorCode == ErrorCode.FILE_NOT_FOUND) null else onReload,
                    )
                }
            else ->
                EditorContent(
                    state = state,
                    modifier = Modifier.fillMaxSize().padding(padding),
                    onBeginEdit = onBeginEdit,
                    onEndEdit = onEndEdit,
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
            title = { Text("Save changes before leaving?", modifier = Modifier.kuraHeading()) },
            text = { Text("Your changes to ${state.file?.name ?: "this text file"} have not been saved.") },
            confirmButton = {
                TextButton(
                    onClick = onSaveAndExit,
                    enabled = state.canEdit && state.phase != TextEditorPhase.SAVING,
                ) { Text("Save and leave") }
            },
            dismissButton = {
                Row {
                    TextButton(onClick = onDismissDiscard) { Text("Cancel") }
                    TextButton(
                        onClick = {
                            onDiscard()
                            onBack()
                        },
                    ) { Text("Discard") }
                }
            },
        )
    }
}

@Composable
private fun StateBox(
    padding: PaddingValues,
    content: @Composable () -> Unit,
) {
    Box(
        Modifier.fillMaxSize().padding(padding).padding(KuraTheme.spacing.md),
        contentAlignment = Alignment.Center,
    ) { content() }
}

@Composable
private fun EditorContent(
    state: TextEditorUiState,
    modifier: Modifier,
    onBeginEdit: () -> Unit,
    onEndEdit: () -> Unit,
    onDraftChange: (String) -> Unit,
    onSave: () -> Unit,
    onReloadConflict: () -> Unit,
    onSaveAsCopy: (String) -> Unit,
) {
    val editing = state.phase in setOf(TextEditorPhase.EDITING, TextEditorPhase.SAVING, TextEditorPhase.CONFLICT, TextEditorPhase.ERROR)
    val content = if (editing || state.dirty) state.draft else state.document?.content.orEmpty()
    val lineCount = if (content.isEmpty()) 0 else content.count { it == '\n' } + 1
    val fontScale = LocalDensity.current.fontScale
    BoxWithConstraints(modifier) {
        val editorMinimumHeight = if (maxHeight < 560.dp || fontScale >= 1.5f) 240.dp else 380.dp
        Column(
            Modifier
                .fillMaxSize()
                .verticalScroll(
                    rememberScrollState(),
                ).padding(horizontal = KuraTheme.spacing.md, vertical = KuraTheme.spacing.sm),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
        ) {
            KuraSegmentedControl(
                labels = listOf("View", "Edit"),
                selectedIndex = if (editing) 1 else 0,
                onSelected = { if (it == 1) onBeginEdit() else onEndEdit() },
                enabled = state.canEdit && state.phase !in setOf(TextEditorPhase.SAVING, TextEditorPhase.CONFLICT),
                modifier = Modifier.testTag("text-mode"),
            )
            EditorStatus(state)
            KuraCard {
                Text("File information", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
                MetadataLayout(
                    listOf(
                        "Encoding" to (state.document?.encoding ?: "Unknown"),
                        "Version" to (state.document?.fileVersion?.toString() ?: "–"),
                        "Characters" to content.length.toString(),
                        "Lines" to lineCount.toString(),
                    ),
                )
                KuraStatusBadge(
                    label = if (state.canEdit) "Editing allowed" else "Read only",
                    status = if (state.canEdit) KuraStatus.SUCCESS else KuraStatus.NEUTRAL,
                    modifier = Modifier.testTag("text-permission"),
                )
            }
            KuraCard {
                Text(if (editing) "Editor" else "Content", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
                if (editing) {
                    OutlinedTextField(
                        value = state.draft,
                        onValueChange = onDraftChange,
                        enabled = state.phase != TextEditorPhase.SAVING,
                        modifier =
                            Modifier
                                .fillMaxWidth()
                                .heightIn(min = editorMinimumHeight)
                                .testTag("text-editor")
                                .semantics { contentDescription = "Text file content editor" },
                        label = { Text("Content") },
                        textStyle = MaterialTheme.typography.bodyLarge.copy(fontFamily = FontFamily.Monospace),
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Default),
                        keyboardActions = KeyboardActions(),
                        minLines = 10,
                    )
                } else {
                    Text(
                        text = content,
                        modifier =
                            Modifier
                                .fillMaxWidth()
                                .heightIn(min = editorMinimumHeight)
                                .testTag("text-viewer")
                                .semantics { contentDescription = "Text file content" },
                        style = MaterialTheme.typography.bodyLarge.copy(fontFamily = FontFamily.Monospace),
                    )
                }
                Text("${content.length} characters", style = MaterialTheme.typography.bodySmall, modifier = Modifier.align(Alignment.End))
            }
            if (!state.draftPersisted) {
                KuraStatusPanel(
                    "Draft cannot be restored",
                    "This draft is larger than 64 KiB. Save it before the app process ends.",
                    KuraStatus.WARNING,
                )
            }
            if (state.phase == TextEditorPhase.CONFLICT) {
                ConflictPanel(state, onReloadConflict, onSaveAsCopy)
            } else if (state.phase == TextEditorPhase.ERROR) {
                KuraStatusPanel(
                    "Changes were not saved",
                    if (state.conflictReloadFailed) {
                        "The latest server version could not be retrieved. Keep this draft and retry when the connection is available."
                    } else {
                        editorErrorMessage(state.errorCode)
                    },
                    KuraStatus.ERROR,
                )
                state.errorCode?.let { Text("Code: ${it.name}", style = MaterialTheme.typography.bodySmall) }
                state.requestId?.let { Text("Request ID: $it", style = MaterialTheme.typography.bodySmall) }
            }
            if (state.canEdit && state.dirty && state.phase !in setOf(TextEditorPhase.CONFLICT, TextEditorPhase.SAVING)) {
                KuraPrimaryButton(
                    label = if (state.phase == TextEditorPhase.SAVING) "Saving…" else "Save changes",
                    onClick = onSave,
                    enabled = state.dirty && state.phase != TextEditorPhase.SAVING,
                    modifier = Modifier.fillMaxWidth().testTag("save-text"),
                )
            }
        }
    }
}

@Composable
private fun EditorStatus(state: TextEditorUiState) {
    when {
        state.phase == TextEditorPhase.SAVING ->
            KuraStateView(
                KuraStateKind.PROGRESS,
                "Saving changes",
                "The latest permission and version are being checked.",
            )
        state.phase == TextEditorPhase.SAVED ->
            KuraStatusPanel(
                "Saved",
                "Version ${state.document?.fileVersion ?: "–"} is now current.",
                KuraStatus.SUCCESS,
                Modifier.testTag("text-saved"),
            )
        state.phase == TextEditorPhase.CONFLICT ->
            KuraStatusPanel(
                "A newer version exists",
                "Compare your draft with the server version before choosing the next action.",
                KuraStatus.WARNING,
            )
        state.dirty -> KuraStatusBadge("Unsaved changes", KuraStatus.WARNING, Modifier.testTag("text-dirty"))
        !state.canEdit ->
            KuraStatusPanel(
                "Read-only access",
                "You can read this file and its history, but you cannot edit or restore versions.",
                KuraStatus.INFO,
            )
    }
}

@Composable
private fun ConflictPanel(
    state: TextEditorUiState,
    onReloadConflict: () -> Unit,
    onSaveAsCopy: (String) -> Unit,
) {
    KuraCard(variant = KuraCardVariant.WARNING) {
        Text("Bounded line comparison", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
        Text("Only the first 400 lines and 512 characters per line are compared. Force overwrite is never available.")
        if (state.diffTruncated) {
            KuraStatusPanel(
                "Comparison limit reached",
                "Some lines are outside the display limit. Reload the full latest version or save this draft as a separate copy.",
                KuraStatus.WARNING,
            )
        }
        val changed = state.diff.filter { it.kind != LineDiffKind.SAME }
        if (changed.isEmpty()) Text("No changed lines are available to display.")
        changed.take(20).forEach { line -> Text(diffLabel(line), style = MaterialTheme.typography.bodySmall) }
        if (changed.size > 20) Text("${changed.size - 20} more changed lines are not shown.", style = MaterialTheme.typography.bodySmall)
        KuraAdaptiveActionLayout(
            listOf(
                { KuraPrimaryButton("Reload latest", onReloadConflict, Modifier.fillMaxWidth().testTag("reload-conflict")) },
                { KuraSecondaryButton("Save as copy", { onSaveAsCopy(state.draft) }, Modifier.fillMaxWidth().testTag("save-as-copy")) },
            ),
        )
    }
}

@Composable
private fun MetadataLayout(values: List<Pair<String, String>>) {
    values.forEach { (label, value) ->
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
            Text(label, style = MaterialTheme.typography.labelLarge, modifier = Modifier.weight(1f))
            Text(value, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
        }
    }
}

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
    KuraAppScaffold(
        topBar = {
            KuraTopAppBar(
                title = "Version history",
                navigationIcon = { TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") } },
                actions = {
                    TextButton(
                        onClick = onRefresh,
                        enabled = !state.refreshing,
                        modifier = Modifier.heightIn(min = 48.dp),
                    ) { Text("Refresh") }
                },
            )
        },
    ) { padding ->
        when {
            state.loading ->
                StateBox(padding) {
                    KuraStateView(
                        KuraStateKind.LOADING,
                        "Loading version history",
                        "Retrieving up to 50 versions at a time.",
                        Modifier.testTag("history-loading"),
                    )
                }
            state.errorCode != null && state.items.isEmpty() ->
                StateBox(padding) {
                    KuraStateView(
                        KuraStateKind.RECOVERABLE_ERROR,
                        "History unavailable",
                        historyErrorMessage(state.errorCode, state.restoreConflict),
                        requestId = state.requestId,
                        actionLabel = "Retry",
                        onAction = onRefresh,
                    )
                }
            state.items.isEmpty() ->
                StateBox(padding) {
                    KuraStateView(
                        KuraStateKind.EMPTY,
                        "No versions yet",
                        "A version appears after upload, text save, external change, or restore.",
                        Modifier.testTag("history-empty"),
                    )
                }
            else -> VersionList(state, Modifier.fillMaxSize().padding(padding), onLoadMore, onPreview, onRequestRestore)
        }
    }
    state.preview?.let { preview ->
        AlertDialog(
            onDismissRequest = onDismissPreview,
            title = { Text("Version ${preview.fileVersion}", modifier = Modifier.kuraHeading()) },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                    KuraStatusPanel("Preview only", "Only this selected version is loaded. The current file is unchanged.", KuraStatus.INFO)
                    Text(preview.content, modifier = Modifier.testTag("history-preview"), fontFamily = FontFamily.Monospace)
                    state.previewDiff
                        .orEmpty()
                        .filter {
                            it.kind != LineDiffKind.SAME
                        }.take(20)
                        .forEach { Text(diffLabel(it), style = MaterialTheme.typography.bodySmall) }
                    if (state.previewDiffTruncated) {
                        KuraStatusPanel(
                            "Comparison limit reached",
                            "The preview is intact, but only the bounded comparison is shown.",
                            KuraStatus.WARNING,
                        )
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { onRequestRestore(preview.fileVersion) }, enabled = state.canRestore) { Text("Restore this version") }
            },
            dismissButton = { TextButton(onClick = onDismissPreview) { Text("Close preview") } },
        )
    }
    state.restoreConfirmationVersion?.let { version ->
        KuraConfirmationDialog(
            title = "Restore version $version?",
            target = "Version $version",
            impact = "Permission and the latest version will be checked again. Current content is retained as an older version.",
            confirmLabel = if (state.restoring) "Restoring…" else "Restore",
            onConfirm = onConfirmRestore,
            onDismiss = onDismissRestore,
            modifier = Modifier.testTag("confirm-restore"),
            confirmEnabled = !state.restoring,
        )
    }
}

@Composable
private fun VersionList(
    state: VersionHistoryUiState,
    modifier: Modifier,
    onLoadMore: () -> Unit,
    onPreview: (Long) -> Unit,
    onRequestRestore: (Long) -> Unit,
) {
    LazyColumn(
        modifier = modifier.testTag("history-list"),
        contentPadding = PaddingValues(KuraTheme.spacing.md),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
    ) {
        item {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                Text("Versions", style = MaterialTheme.typography.titleLarge, modifier = Modifier.kuraHeading())
                Text("Newest first · ${state.items.size} loaded")
                if (!state.canRestore) {
                    KuraStatusPanel(
                        "Read-only history",
                        "Preview is available, but restoring requires edit permission.",
                        KuraStatus.INFO,
                    )
                }
                if (state.refreshing) KuraStateView(KuraStateKind.PROGRESS, "Refreshing history", "Checking the latest versions.")
                if (state.previewLoading) KuraStateView(KuraStateKind.PROGRESS, "Loading preview", "Retrieving the selected version only.")
                if (state.errorCode != null) {
                    KuraStatusPanel(
                        "History needs attention",
                        historyErrorMessage(state.errorCode, state.restoreConflict),
                        KuraStatus.ERROR,
                    )
                    state.requestId?.let { Text("Request ID: $it", style = MaterialTheme.typography.bodySmall) }
                }
            }
        }
        items(
            state.items,
            key = FileVersionItem::version,
        ) { version -> VersionCard(version, state.canRestore, onPreview, onRequestRestore) }
        if (state.hasNextPage) {
            item {
                KuraSecondaryButton(
                    if (state.loadingMore) "Loading…" else "Load 50 more",
                    onLoadMore,
                    Modifier.fillMaxWidth().testTag("history-load-more"),
                    enabled = !state.loadingMore,
                )
            }
        }
    }
}

@Composable
private fun VersionCard(
    version: FileVersionItem,
    canRestore: Boolean,
    onPreview: (Long) -> Unit,
    onRequestRestore: (Long) -> Unit,
) {
    KuraCard {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Version ${version.version}", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f).kuraHeading())
            KuraStatusBadge(changeKindLabel(version.changeKind), changeKindStatus(version.changeKind))
        }
        Text("Created ${version.createdAt}")
        Text("By ${version.actorDisplayName}")
        Text("${version.size} bytes · SHA-256 ${version.sha256.take(12)}…", maxLines = 2, overflow = TextOverflow.Ellipsis)
        HorizontalDivider()
        KuraAdaptiveActionLayout(
            listOf(
                { KuraSecondaryButton("Preview", { onPreview(version.version) }, Modifier.fillMaxWidth()) },
                { KuraPrimaryButton("Restore", { onRequestRestore(version.version) }, Modifier.fillMaxWidth(), enabled = canRestore) },
            ),
        )
    }
}

private fun editorErrorMessage(code: ErrorCode?): String =
    when (code) {
        ErrorCode.FILE_NOT_FOUND -> "The file, permission, or session is no longer available. Return to the file list."
        ErrorCode.TEXT_SIZE_LIMIT_EXCEEDED -> "Use UTF-8 text no larger than 1 MiB, or download the file and edit it externally."
        ErrorCode.TEXT_ENCODING_INVALID -> "This content is not valid UTF-8. Download it and use an editor that supports its encoding."
        ErrorCode.FILE_VERSION_CONFLICT ->
            "A newer version exists. Reload it, compare the bounded changes, " +
                "or save your draft as a separate copy."
        ErrorCode.RATE_LIMIT_EXCEEDED -> "Wait briefly, then retry with the same draft."
        else -> "Check your connection and retry. Your current draft remains available on this screen."
    }

private fun historyErrorMessage(
    code: ErrorCode?,
    restoreConflict: Boolean,
): String =
    when {
        restoreConflict -> "The current version changed. Refresh and preview the latest content before restoring again."
        code == ErrorCode.FILE_NOT_FOUND -> "The file, permission, or session is no longer available."
        else -> "The request failed. Refresh to retrieve the authoritative history state."
    }

private fun changeKindLabel(kind: FileVersionChangeKind): String =
    when (kind) {
        FileVersionChangeKind.UPLOAD -> "Upload"
        FileVersionChangeKind.TEXT_EDIT -> "Text edit"
        FileVersionChangeKind.EXTERNAL_CHANGE -> "External change"
        FileVersionChangeKind.RESTORE -> "Restore"
        FileVersionChangeKind.UNKNOWN -> "Unknown change"
    }

private fun changeKindStatus(kind: FileVersionChangeKind): KuraStatus =
    when (kind) {
        FileVersionChangeKind.UPLOAD, FileVersionChangeKind.TEXT_EDIT, FileVersionChangeKind.RESTORE -> KuraStatus.SUCCESS
        FileVersionChangeKind.EXTERNAL_CHANGE -> KuraStatus.INFO
        FileVersionChangeKind.UNKNOWN -> KuraStatus.WARNING
    }

private fun diffLabel(line: LineDiff): String =
    when (line.kind) {
        LineDiffKind.SAME -> "Line ${line.lineNumber}: unchanged"
        LineDiffKind.CHANGED -> "Line ${line.lineNumber}: − ${line.current.orEmpty()} / + ${line.proposed.orEmpty()}"
        LineDiffKind.ADDED -> "Line ${line.lineNumber}: + ${line.proposed.orEmpty()}"
        LineDiffKind.REMOVED -> "Line ${line.lineNumber}: − ${line.current.orEmpty()}"
    }
