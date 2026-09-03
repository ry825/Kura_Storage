@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "CyclomaticComplexMethod",
    "LongMethod",
    "LongParameterList",
    "TooManyFunctions",
    "MaxLineLength",
)

package com.kurastorage.feature.backup

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.data.backup.CurrentWifiResult
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.SyncLifecycleState
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun BackupSettingsScreen(
    onOverview: () -> Unit,
    onRules: () -> Unit,
    onWifi: () -> Unit,
    onBack: () -> Unit,
) {
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text("Automatic backup", style = MaterialTheme.typography.headlineMedium)
        Text("One-way backup adds and updates server files. Deleting a source from this device never deletes the server copy.")
        Text("Android cannot run scheduled work after you force-stop KuraStorage. Open the app again to resume scheduling.")
        LargeButton("Backup status and history", onOverview)
        LargeButton("Backup rules", onRules)
        LargeButton("Allowed external Wi-Fi", onWifi)
        OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
    }
}

@Composable
fun BackupOverviewScreen(
    state: BackupOverviewState,
    onRunNow: () -> Unit,
    onPause: (Boolean) -> Unit,
    onRetry: (LocalSyncItem) -> Unit,
    onRetryAll: () -> Unit,
    onLoadMore: () -> Unit,
    onBack: () -> Unit,
) {
    val progress = state.progress
    val paused = state.rules.isNotEmpty() && state.rules.all { it.pausedAt != null }
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Backup status", style = MaterialTheme.typography.headlineMedium)
        Text("Last success: ${formatInstant(progress?.lastCompletedAt)}")
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            StatusCount("Pending", progress?.pendingCount() ?: 0, Modifier.weight(1f))
            StatusCount("Uploading", progress?.count(SyncLifecycleState.UPLOADING) ?: 0, Modifier.weight(1f))
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            StatusCount("Succeeded", progress?.count(SyncLifecycleState.COMPLETED) ?: 0, Modifier.weight(1f))
            StatusCount("Failed", progress?.count(SyncLifecycleState.FAILED) ?: 0, Modifier.weight(1f))
        }
        progress?.primaryWaitReason()?.let {
            Text("Waiting: ${it.label()}", modifier = Modifier.testTag("backup-wait-reason"))
        } ?: Text("Policy status: ready when an allowed connection and device conditions are available.")
        state.rules.forEach { rule ->
            val counts = progress?.ruleStateCounts?.get(rule.id).orEmpty()
            Text(
                "${rule.displayName}: ${counts[SyncLifecycleState.PENDING] ?: 0} pending, " +
                    "${counts[SyncLifecycleState.UPLOADING] ?: 0} uploading, " +
                    "${counts[SyncLifecycleState.FAILED] ?: 0} failed",
            )
        }
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                onClick = onRunNow,
                enabled = !state.actionRunning && state.rules.any { it.enabled },
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
            ) {
                Text("Back up now")
            }
            OutlinedButton(
                onClick = { onPause(!paused) },
                enabled = !state.actionRunning && state.rules.isNotEmpty(),
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
            ) {
                Text(if (paused) "Resume" else "Pause")
            }
            if ((progress?.count(SyncLifecycleState.FAILED) ?: 0) > 0) {
                OutlinedButton(
                    onClick = onRetryAll,
                    enabled = !state.actionRunning,
                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                ) { Text("Retry failures") }
            }
        }
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        Text("File history", style = MaterialTheme.typography.titleLarge)
        if (!state.loading && state.items.isEmpty()) Text("No backup history yet.")
        LazyColumn(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            items(state.visibleItems, key = { it.id.value }) { item ->
                BackupHistoryRow(item, state.actionRunning, onRetry)
            }
            if (state.canLoadMore) item { OutlinedButton(onClick = onLoadMore) { Text("Load more") } }
        }
        OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
    }
}

@Composable
private fun StatusCount(
    label: String,
    count: Int,
    modifier: Modifier = Modifier,
) {
    Column(modifier.semantics { contentDescription = "$label: $count" }) {
        Text(count.toString(), style = MaterialTheme.typography.titleLarge)
        Text(label, style = MaterialTheme.typography.labelMedium)
    }
}

@Composable
private fun BackupHistoryRow(
    item: LocalSyncItem,
    busy: Boolean,
    onRetry: (LocalSyncItem) -> Unit,
) {
    Column(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Text(item.displayName, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Text(item.lifecycleState.label())
        item.waitReason.takeUnless { it == BackupWaitReason.NONE }?.let { Text("Waiting: ${it.label()}") }
        item.failureReason.takeUnless { it == BackupFailureReason.NONE }?.let {
            Text("Failure: ${it.label()}. ${it.nextAction()}", color = MaterialTheme.colorScheme.error)
        }
        Text("Last attempt: ${formatInstant(item.lastAttemptAt)} • retries: ${item.retryCount}")
        if (item.lifecycleState == SyncLifecycleState.FAILED) {
            TextButton(onClick = { onRetry(item) }, enabled = !busy, modifier = Modifier.heightIn(min = 48.dp)) {
                Text("Retry")
            }
        }
    }
}

@Composable
fun BackupRulesScreen(
    state: BackupRulesState,
    selectedSource: SelectedBackupSource?,
    selectedDestination: SelectedBackupDestination?,
    onPickSafSource: () -> Unit,
    onPickDestination: () -> Unit,
    onRequestMediaPermission: (BackupSourceType) -> Unit,
    onSave: (BackupRuleInput, LocalBackupRule?, () -> Unit) -> Unit,
    onToggle: (LocalBackupRule, Boolean) -> Unit,
    onDelete: (LocalBackupRule) -> Unit,
    onSelectionsConsumed: () -> Unit,
    onBack: () -> Unit,
) {
    var editingId by rememberSaveable { mutableStateOf<String?>(null) }
    var creating by rememberSaveable { mutableStateOf(false) }
    var deleting by remember { mutableStateOf<LocalBackupRule?>(null) }
    val editing = state.rules.firstOrNull { it.id.value == editingId }
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Backup rules", style = MaterialTheme.typography.headlineMedium)
        Text("These are one-way backup rules, not two-way sync. Device deletions never delete server files.")
        Button(onClick = {
            creating = true
            editingId = null
        }, enabled = !state.saving) { Text("Add rule") }
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        if (!state.loading && state.rules.isEmpty()) Text("No backup rules.")
        LazyColumn(Modifier.weight(1f)) {
            items(state.rules, key = { it.id.value }) { rule ->
                Column(Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
                    Text(rule.displayName, style = MaterialTheme.typography.titleMedium)
                    Text("${rule.sourceType.label()} • ${rule.networkMode.label()} • minimum battery ${rule.minimumBatteryPercent}%")
                    if (rule.sourceType == BackupSourceType.SAF_TREE) {
                        Text("If device folder access is lost, choose Edit and re-select the folder.")
                    }
                    Text("If server access changes, choose Edit and re-select an allowed destination.")
                    if (rule.pausedAt != null) Text("Paused")
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Enabled", Modifier.weight(1f))
                        Switch(checked = rule.enabled, onCheckedChange = { onToggle(rule, it) }, enabled = !state.saving)
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        TextButton(onClick = {
                            editingId = rule.id.value
                            creating = true
                        }) { Text("Edit") }
                        TextButton(onClick = { deleting = rule }) { Text("Delete") }
                    }
                }
            }
        }
        OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
    }
    if (creating) {
        RuleEditorDialog(
            existing = editing,
            selectedSource = selectedSource,
            selectedDestination = selectedDestination,
            saving = state.saving,
            onPickSafSource = onPickSafSource,
            onPickDestination = onPickDestination,
            onRequestMediaPermission = onRequestMediaPermission,
            onDismiss = {
                creating = false
                onSelectionsConsumed()
            },
            onSave = { input ->
                onSave(input, editing) {
                    creating = false
                    onSelectionsConsumed()
                }
            },
        )
    }
    deleting?.let { rule ->
        AlertDialog(
            onDismissRequest = { deleting = null },
            title = { Text("Delete backup rule?") },
            text = { Text("This removes the device rule and local history. Files already backed up on the server are not deleted.") },
            confirmButton = {
                Button(onClick = {
                    deleting = null
                    onDelete(rule)
                }) { Text("Delete rule") }
            },
            dismissButton = { TextButton(onClick = { deleting = null }) { Text("Cancel") } },
        )
    }
}

data class SelectedBackupSource(
    val uri: String,
    val displayName: String,
)

data class SelectedBackupDestination(
    val id: String,
    val displayName: String,
)

@Composable
private fun RuleEditorDialog(
    existing: LocalBackupRule?,
    selectedSource: SelectedBackupSource?,
    selectedDestination: SelectedBackupDestination?,
    saving: Boolean,
    onPickSafSource: () -> Unit,
    onPickDestination: () -> Unit,
    onRequestMediaPermission: (BackupSourceType) -> Unit,
    onDismiss: () -> Unit,
    onSave: (BackupRuleInput) -> Unit,
) {
    var sourceType by rememberSaveable { mutableStateOf(existing?.sourceType ?: BackupSourceType.MEDIA_IMAGES) }
    var sourceLocator by rememberSaveable { mutableStateOf(existing?.sourceLocator.orEmpty()) }
    var sourceName by rememberSaveable { mutableStateOf(existing?.displayName.orEmpty()) }
    var destinationId by rememberSaveable { mutableStateOf(existing?.remoteFolderId.orEmpty()) }
    var destinationName by rememberSaveable { mutableStateOf(if (existing == null) "" else "Current server folder") }
    var mode by rememberSaveable { mutableStateOf(existing?.networkMode ?: BackupNetworkMode.LOCAL_DIRECT_ONLY) }
    var battery by rememberSaveable { mutableIntStateOf(existing?.minimumBatteryPercent ?: DEFAULT_MINIMUM_BATTERY_PERCENT) }
    var charging by rememberSaveable { mutableStateOf(existing?.requiresChargingForInitialRun ?: true) }
    LaunchedEffect(selectedSource) {
        selectedSource?.let {
            sourceType = BackupSourceType.SAF_TREE
            sourceLocator = it.uri
            sourceName = it.displayName
        }
    }
    LaunchedEffect(selectedDestination) {
        selectedDestination?.let {
            destinationId = it.id
            destinationName = it.displayName
        }
    }

    fun selectMedia(type: BackupSourceType) {
        sourceType = type
        sourceLocator = "external"
        sourceName = type.label()
        onRequestMediaPermission(type)
    }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (existing == null) "Add backup rule" else "Edit backup rule") },
        text = {
            Column(
                Modifier.heightIn(max = 520.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Text("Device source")
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    listOf(BackupSourceType.MEDIA_IMAGES, BackupSourceType.MEDIA_VIDEOS, BackupSourceType.MEDIA_AUDIO).forEach { type ->
                        FilterChip(selected = sourceType == type, onClick = { selectMedia(type) }, label = { Text(type.shortLabel()) })
                    }
                }
                OutlinedButton(onClick = onPickSafSource) {
                    Text(
                        if (sourceType ==
                            BackupSourceType.SAF_TREE
                        ) {
                            sourceName
                        } else {
                            "Choose device folder"
                        },
                    )
                }
                OutlinedTextField(value = sourceName, onValueChange = { sourceName = it }, label = { Text("Rule name") })
                OutlinedButton(onClick = onPickDestination) { Text(destinationName.ifBlank { "Choose server folder" }) }
                Text("Network")
                FilterChip(selected = mode == BackupNetworkMode.LOCAL_DIRECT_ONLY, onClick = {
                    mode = BackupNetworkMode.LOCAL_DIRECT_ONLY
                }, label = { Text("Local direct only") })
                FilterChip(selected = mode == BackupNetworkMode.LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER, onClick = {
                    mode =
                        BackupNetworkMode.LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER
                }, label = { Text("Local or allowed Wi-Fi + ZeroTier") })
                OutlinedTextField(value = battery.toString(), onValueChange = {
                    battery = it.toIntOrNull()?.coerceIn(MINIMUM_BATTERY_PERCENT, MAXIMUM_BATTERY_PERCENT) ?: battery
                }, label = { Text("Minimum battery %") })
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = charging, onCheckedChange = { charging = it })
                    Text("Require charging for the initial backup")
                }
            }
        },
        confirmButton = {
            Button(
                onClick = { onSave(BackupRuleInput(sourceType, sourceLocator, sourceName, destinationId, mode, charging, battery)) },
                enabled = !saving && sourceLocator.isNotBlank() && sourceName.isNotBlank() && destinationId.isNotBlank(),
            ) { Text("Save") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

private const val MINIMUM_BATTERY_PERCENT = 0
private const val DEFAULT_MINIMUM_BATTERY_PERCENT = 20
private const val MAXIMUM_BATTERY_PERCENT = 100

@Composable
fun BackupWifiScreen(
    state: BackupWifiState,
    onRefresh: () -> Unit,
    onRequestPermission: (Set<String>) -> Unit,
    onRegister: (String, Boolean, Boolean) -> Unit,
    onSave: (ExternalWifiPolicy) -> Unit,
    onDelete: (ExternalWifiPolicy) -> Unit,
    onOpenAppSettings: () -> Unit,
    onBack: () -> Unit,
) {
    var name by rememberSaveable { mutableStateOf("") }
    var restrictBssid by rememberSaveable { mutableStateOf(false) }
    var metered by rememberSaveable { mutableStateOf(false) }
    var confirmRegistration by rememberSaveable { mutableStateOf(false) }
    var renaming by remember { mutableStateOf<ExternalWifiPolicy?>(null) }
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Allowed external Wi-Fi", style = MaterialTheme.typography.headlineMedium)
        Text("Wi-Fi matching only selects a policy. ZeroTier, TLS, server identity, and sign-in are still required.")
        WifiAvailability(state.currentWifi, onRequestPermission, onOpenAppSettings)
        OutlinedTextField(value = name, onValueChange = { name = it }, label = { Text("Display name") })
        Row(verticalAlignment = Alignment.CenterVertically) {
            Checkbox(restrictBssid, { restrictBssid = it })
            Text("Restrict to current access point")
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Checkbox(metered, { metered = it })
            Text("Treat as metered (automatic backup disabled)")
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                onClick = { confirmRegistration = true },
                enabled =
                    name.isNotBlank() && state.currentWifi is CurrentWifiResult.Connected && !state.saving,
            ) { Text("Register current Wi-Fi") }
            OutlinedButton(onClick = onRefresh) { Text("Refresh") }
        }
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        LazyColumn(Modifier.weight(1f)) {
            items(state.policies, key = { it.id.value }) { policy ->
                Column(Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
                    Text(policy.displayName, style = MaterialTheme.typography.titleMedium)
                    Text("Wi-Fi name: ${policy.normalizedSsid}")
                    Text(if (policy.normalizedBssid == null) "All access points with this Wi-Fi name" else "Restricted to one access point")
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Allowed", Modifier.weight(1f))
                        Switch(policy.enabled, { onSave(policy.copy(enabled = it)) }, enabled = !state.saving && !policy.treatAsMetered)
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Treat as metered", Modifier.weight(1f))
                        Switch(policy.treatAsMetered, {
                            onSave(policy.copy(treatAsMetered = it, enabled = if (it) false else policy.enabled))
                        }, enabled = !state.saving)
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        TextButton(onClick = { renaming = policy }, enabled = !state.saving) { Text("Rename") }
                        TextButton(onClick = { onDelete(policy) }, enabled = !state.saving) { Text("Delete") }
                    }
                }
            }
        }
        OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
    }
    if (confirmRegistration) {
        AlertDialog(
            onDismissRequest = { confirmRegistration = false },
            title = { Text("Allow this Wi-Fi?") },
            text = {
                Text(
                    "Register the currently connected Wi-Fi for automatic backup? " +
                        "Network, TLS, server identity, and sign-in checks will still apply.",
                )
            },
            confirmButton = {
                Button(onClick = {
                    confirmRegistration = false
                    onRegister(name, restrictBssid, metered)
                }) { Text("Allow current Wi-Fi") }
            },
            dismissButton = { TextButton(onClick = { confirmRegistration = false }) { Text("Cancel") } },
        )
    }
    renaming?.let { policy ->
        WifiRenameDialog(
            policy = policy,
            saving = state.saving,
            onSave = {
                onSave(policy.copy(displayName = it))
                renaming = null
            },
            onDismiss = { renaming = null },
        )
    }
}

@Composable
private fun WifiRenameDialog(
    policy: ExternalWifiPolicy,
    saving: Boolean,
    onSave: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    var displayName by rememberSaveable(policy.id.value) { mutableStateOf(policy.displayName) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Rename allowed Wi-Fi") },
        text = {
            OutlinedTextField(
                value = displayName,
                onValueChange = { displayName = it },
                label = { Text("Display name") },
            )
        },
        confirmButton = {
            Button(onClick = { onSave(displayName) }, enabled = displayName.isNotBlank() && !saving) { Text("Save") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

@Composable
private fun WifiAvailability(
    current: CurrentWifiResult,
    request: (Set<String>) -> Unit,
    settings: () -> Unit,
) {
    when (current) {
        is CurrentWifiResult.Connected -> Text("Connected Wi-Fi is available to register.")
        is CurrentWifiResult.PermissionRequired ->
            Column {
                Text(
                    "Wi-Fi permission is required only to match an allowed external network. Automatic backup remains stopped until granted.",
                )
                Button(onClick = { request(current.permissions) }) { Text("Grant Wi-Fi permission") }
            }
        CurrentWifiResult.PermissionPermanentlyDenied ->
            Column {
                Text("Wi-Fi permission was permanently denied. Automatic backup is stopped.")
                Button(onClick = settings) { Text("Open app settings") }
            }
        CurrentWifiResult.LocationServicesDisabled ->
            Text(
                "Location services must be enabled for Android to provide Wi-Fi identity. Automatic backup is stopped.",
            )
        CurrentWifiResult.NotConnectedToWifi -> Text("Connect to the Wi-Fi you want to register.")
        CurrentWifiResult.InformationUnavailable -> Text("Wi-Fi information is unavailable. Automatic backup is stopped.")
    }
}

@Composable
private fun LargeButton(
    text: String,
    onClick: () -> Unit,
) {
    Button(onClick = onClick, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text(text) }
}

private fun com.kurastorage.core.data.backup.BackupProgressSnapshot.count(state: SyncLifecycleState) = stateCounts[state] ?: 0

private fun com.kurastorage.core.data.backup.BackupProgressSnapshot.pendingCount() =
    listOf(
        SyncLifecycleState.DISCOVERED,
        SyncLifecycleState.PENDING,
        SyncLifecycleState.COMPARING,
        SyncLifecycleState.READY_TO_UPLOAD,
    ).sumOf(::count)

private fun com.kurastorage.core.data.backup.BackupProgressSnapshot.primaryWaitReason() = waitReasonCounts.maxByOrNull { it.value }?.key

private fun BackupSourceType.shortLabel() =
    when (this) {
        BackupSourceType.MEDIA_IMAGES -> "Photos"
        BackupSourceType.MEDIA_VIDEOS -> "Videos"
        BackupSourceType.MEDIA_AUDIO -> "Audio"
        BackupSourceType.SAF_TREE -> "Folder"
    }

private fun BackupSourceType.label() =
    when (this) {
        BackupSourceType.MEDIA_IMAGES -> "Photos"
        BackupSourceType.MEDIA_VIDEOS -> "Videos"
        BackupSourceType.MEDIA_AUDIO -> "Audio"
        BackupSourceType.SAF_TREE -> "Device folder"
    }

private fun BackupNetworkMode.label() =
    when (this) {
        BackupNetworkMode.LOCAL_DIRECT_ONLY -> "Local direct only"
        BackupNetworkMode.LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER -> "Local or allowed Wi-Fi + ZeroTier"
    }

private fun SyncLifecycleState.label() = name.lowercase().replace('_', ' ').replaceFirstChar(Char::uppercase)

private fun BackupWaitReason.label() =
    when (this) {
        BackupWaitReason.NONE -> "none"
        BackupWaitReason.NETWORK, BackupWaitReason.ALLOWED_WIFI -> "an allowed connection"
        BackupWaitReason.BATTERY -> "battery"
        BackupWaitReason.CHARGING -> "charging"
        BackupWaitReason.AUTHENTICATION -> "sign-in"
        BackupWaitReason.STORAGE -> "server storage"
        BackupWaitReason.SOURCE_PERMISSION -> "source permission"
        BackupWaitReason.SERVER_RECONCILIATION -> "server confirmation"
    }

private fun BackupFailureReason.label() = name.lowercase().replace('_', ' ')

private fun BackupFailureReason.nextAction() =
    when (this) {
        BackupFailureReason.PERMISSION_REVOKED, BackupFailureReason.SOURCE_UNAVAILABLE -> "Re-select the device source."
        BackupFailureReason.REMOTE_CONFLICT -> "Check the server destination and permissions."
        BackupFailureReason.SOURCE_CHANGED -> "Scan the source again."
        BackupFailureReason.RETRY_EXHAUSTED -> "Retry when the connection is stable."
        BackupFailureReason.PROTOCOL_ERROR -> "Update KuraStorage before retrying."
        BackupFailureReason.NONE -> ""
    }

private fun formatInstant(value: Instant?): String =
    value?.atZone(ZoneId.systemDefault())?.format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm")) ?: "Never"
