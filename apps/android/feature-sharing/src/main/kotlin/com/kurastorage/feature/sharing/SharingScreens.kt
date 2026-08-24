@file:Suppress(
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "ktlint:standard:function-naming",
    "FunctionNaming",
)

package com.kurastorage.feature.sharing

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
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
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ShareScope

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
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onBack) { Text("Back") }
            Button(onClick = onRefresh, enabled = !state.loading) { Text("Refresh") }
        }
        Text("Shared", style = MaterialTheme.typography.headlineSmall)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
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
                Column(
                    Modifier.fillMaxWidth().clickable { onOpenTarget(share) }.padding(vertical = 10.dp),
                    verticalArrangement = Arrangement.spacedBy(3.dp),
                ) {
                    Text("${share.entryType}: ${share.name}", style = MaterialTheme.typography.titleMedium)
                    Text("Owner: ${share.owner.displayName}")
                    Text("Permission: ${share.permission}")
                    Text(
                        if (share.entryType ==
                            FileEntryType.FOLDER
                        ) {
                            "Applies to this folder and descendants"
                        } else {
                            "Applies only to this file"
                        },
                    )
                    if (share.canManage) TextButton(onClick = { onManage(share) }) { Text("Sharing settings") }
                }
                HorizontalDivider()
            }
            if (state.canLoadMore) item { Button(onClick = onLoadMore) { Text("Load more") } }
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
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onBack) { Text("Back") }
            Button(onClick = onRefresh, enabled = !state.loading && !state.submitting) { Text("Refresh") }
        }
        Text("Sharing settings", style = MaterialTheme.typography.headlineSmall)
        Text(state.targetName, style = MaterialTheme.typography.titleLarge)
        Text(
            if (state.targetType ==
                FileEntryType.FOLDER
            ) {
                "Permissions are inherited by descendants."
            } else {
                "Permissions apply only to this file."
            },
        )
        if (state.loading || state.submitting) LinearProgressIndicator(Modifier.fillMaxWidth())
        state.message?.let { Text(it, color = MaterialTheme.colorScheme.primary) }
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        if (state.accessLost) {
            Text("This share is no longer available. Return to the latest shared list.")
            return@Column
        }
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
        state.candidates
            .filter { candidate -> state.share?.members?.none { it.userId == candidate.userId } != false }
            .forEach { candidate ->
                FilterButton(candidate.displayName, state.selectedUserId == candidate.userId) { onCandidate(candidate.userId) }
            }
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(4.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
            maxItemsInEachRow = 2,
        ) {
            state.availablePermissions.forEach { permission ->
                FilterButton(permission.name, state.selectedPermission == permission) { onPermission(permission) }
            }
        }
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
                Confirmation.REMOVE_MEMBER -> "Remove this family member from the share?"
                Confirmation.DELETE_SHARE -> "Remove this share for every member?"
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
