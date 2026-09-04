@file:Suppress(
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "ktlint:standard:function-naming",
    "FunctionNaming",
)

package com.kurastorage.feature.sharing

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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
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
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ShareScope
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraFileEntryRow
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel

@Composable
fun SharingScreen(
    state: SharingListState,
    onBack: () -> Unit,
    onScope: (ShareScope) -> Unit,
    onType: (FileEntryType?) -> Unit,
    onRefresh: () -> Unit,
    onLoadMore: () -> Unit,
    onOpenTarget: (ShareItem) -> Unit,
    onManage: (ShareItem) -> Unit,
) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                TextButton(onClick = onBack) { Text("Back") }
                Text("Shared", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.headlineSmall)
                TextButton(onClick = onRefresh, enabled = !state.loading) { Text("Refresh") }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterButton("Received", state.scope == ShareScope.RECEIVED) { onScope(ShareScope.RECEIVED) }
                FilterButton("Owned", state.scope == ShareScope.OWNED) { onScope(ShareScope.OWNED) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterButton("All", state.targetType == null) { onType(null) }
                FilterButton("Folders", state.targetType == FileEntryType.FOLDER) { onType(FileEntryType.FOLDER) }
                FilterButton("Files", state.targetType == FileEntryType.FILE) { onType(FileEntryType.FILE) }
            }
            if (state.loading) LinearProgressIndicator(Modifier.fillMaxWidth().testTag("sharing-loading"))
            state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            if (!state.loading && state.items.isEmpty()) Text("No shared items.", Modifier.testTag("sharing-empty"))
            LazyColumn(Modifier.weight(1f)) {
                items(state.items, key = ShareItem::id) { share ->
                    KuraFileEntryRow(
                        name = share.name,
                        entryType = share.entryType,
                        mimeType = null,
                        ownerName = share.owner.displayName,
                        permission = share.permission,
                        permissionSource = if (state.scope == ShareScope.OWNED) PermissionSource.OWNER else PermissionSource.DIRECT,
                        updatedAt = share.updatedAt,
                        status = FileEntryStatus.ACTIVE,
                        contextLine =
                            if (share.entryType == FileEntryType.FOLDER) {
                                "Applies to this folder and descendants"
                            } else {
                                "Applies only to this file"
                            },
                        onClick = { onOpenTarget(share) },
                        modifier = Modifier.testTag("shared-entry-${share.id}"),
                        trailing = {
                            if (share.canManage) TextButton(onClick = { onManage(share) }) { Text("Manage") }
                        },
                    )
                }
                if (state.canLoadMore) item { Button(onClick = onLoadMore) { Text("Load more") } }
            }
        }
    }
}

@Composable
private fun FilterButton(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
) {
    if (selected) Button(onClick = onClick) { Text(label) } else OutlinedButton(onClick = onClick) { Text(label) }
}

@Composable
fun SharingSettingsScreen(
    state: SharingSettingsState,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onCandidate: (String) -> Unit,
    onPermission: (SharePermission) -> Unit,
    onSubmitMember: () -> Unit,
    onChangePermission: (String, SharePermission) -> Unit,
    onRemoveMember: (String) -> Unit,
    onDeleteShare: () -> Unit,
    onConfirm: () -> Unit,
    onDismissConfirmation: () -> Unit,
) {
    var candidateQuery by remember { mutableStateOf("") }
    val visibleCandidates =
        state.candidates.filter { candidate ->
            candidate.displayName.contains(candidateQuery.trim(), ignoreCase = true) &&
                state.share?.members?.none { it.userId == candidate.userId } != false
        }
    Column(
        Modifier
            .fillMaxSize()
            .windowInsetsPadding(WindowInsets.safeDrawing)
            .verticalScroll(rememberScrollState())
            .padding(KuraTheme.spacing.md),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            TextButton(onClick = onBack) { Text("Back") }
            Text("Sharing settings", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.headlineSmall)
            TextButton(onClick = onRefresh, enabled = !state.loading && !state.submitting) { Text("Refresh") }
        }
        KuraCard {
            Text(state.targetName, style = MaterialTheme.typography.titleLarge)
            Text(if (state.targetType == FileEntryType.FOLDER) "Folder share" else "File share")
            Text("Owner: ${state.share?.owner?.displayName ?: "Confirmed when sharing is loaded"}")
        }
        KuraStatusPanel(
            title = if (state.targetType == FileEntryType.FOLDER) "Inherited scope" else "File-only scope",
            message =
                if (state.targetType == FileEntryType.FOLDER) {
                    "These permissions apply to this folder and its descendants."
                } else {
                    "These permissions apply only to this file. Inherited access cannot be weakened here."
                },
            status = KuraStatus.INFO,
        )
        if (state.loading || state.submitting) LinearProgressIndicator(Modifier.fillMaxWidth())
        state.message?.let { Text(it, color = MaterialTheme.colorScheme.primary) }
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        if (state.accessLost) {
            Text("This share is no longer available. Return to the latest shared list.")
            return@Column
        }
        Text("Current family members", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
        state.share?.members?.forEach { member ->
            Column(Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
                Text(member.displayName)
                Text("Permission: ${member.permission}")
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                    maxItemsInEachRow = 2,
                ) {
                    state.availablePermissions.forEach { permission ->
                        TextButton(
                            onClick = { onChangePermission(member.userId, permission) },
                            enabled = !state.submitting && member.permission != permission,
                        ) { Text(permission.name) }
                    }
                }
                TextButton(onClick = { onRemoveMember(member.userId) }, enabled = !state.submitting) { Text("Remove") }
            }
            HorizontalDivider()
        }
        Text("Add family member", style = MaterialTheme.typography.titleMedium)
        OutlinedTextField(
            value = candidateQuery,
            onValueChange = { candidateQuery = it },
            enabled = !state.submitting,
            label = { Text("Search family") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth().testTag("share-candidate-search"),
        )
        visibleCandidates.forEach { candidate ->
            FilterButton(candidate.displayName, state.selectedUserId == candidate.userId) { onCandidate(candidate.userId) }
        }
        Text("Permission", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleMedium)
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(4.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
            maxItemsInEachRow = 2,
        ) {
            state.availablePermissions.forEach { permission ->
                FilterButton(permission.name, state.selectedPermission == permission) { onPermission(permission) }
            }
        }
        Text(permissionDescription(state.selectedPermission))
        Button(
            onClick = onSubmitMember,
            enabled = state.selectedUserId != null && !state.submitting,
            modifier = Modifier.testTag("submit-share-member"),
        ) { Text(if (state.share == null) "Create share" else "Add member") }
        if (state.share != null) {
            OutlinedButton(onClick = onDeleteShare, enabled = !state.submitting) { Text("Remove entire share") }
        }
    }
    state.confirmation?.let { confirmation ->
        val message =
            when (confirmation) {
                Confirmation.REMOVE_MEMBER ->
                    if (state.targetType == FileEntryType.FOLDER) {
                        "Remove this family member from this folder share and its descendants?"
                    } else {
                        "Remove this family member from this file share?"
                    }
                Confirmation.DELETE_SHARE ->
                    if (state.targetType == FileEntryType.FOLDER) {
                        "Remove this share for every member, including inherited access to descendants?"
                    } else {
                        "Remove this file share for every member?"
                    }
                Confirmation.GRANT_MANAGER -> "Grant Manager permission? This person can manage sharing."
            }
        AlertDialog(
            onDismissRequest = onDismissConfirmation,
            title = { Text("Confirm sharing change") },
            text = { Text(message) },
            confirmButton = {
                Button(onClick = onConfirm, enabled = !state.submitting, modifier = Modifier.testTag("confirm-sharing-change")) {
                    Text("Confirm")
                }
            },
            dismissButton = { TextButton(onClick = onDismissConfirmation, enabled = !state.submitting) { Text("Cancel") } },
        )
    }
}

private fun permissionDescription(permission: SharePermission): String =
    when (permission) {
        SharePermission.VIEWER -> "Viewer: can view and download content."
        SharePermission.CONTRIBUTOR -> "Contributor: can add files and folders to a shared folder."
        SharePermission.EDITOR -> "Editor: can rename, move, edit, and move content to Trash."
        SharePermission.MANAGER -> "Manager: can also add, change, and remove sharing members."
        SharePermission.UNKNOWN -> "Unknown permission cannot be saved."
    }
