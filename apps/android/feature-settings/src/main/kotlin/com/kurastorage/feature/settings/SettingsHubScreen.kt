@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod", "LongParameterList")

package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.components.KuraDestructiveButton
import com.kurastorage.core.ui.components.KuraListRow
import com.kurastorage.core.ui.components.KuraSectionHeader

@Composable
fun SettingsHubScreen(
    isAdmin: Boolean,
    accountStatus: String,
    connectionStatus: String,
    onConnection: () -> Unit,
    onMediaSettings: () -> Unit,
    onBackupSettings: () -> Unit,
    onWifiSettings: () -> Unit,
    onCacheManagement: () -> Unit,
    onActivity: () -> Unit,
    onTrash: () -> Unit,
    onLogout: () -> Unit,
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        color = MaterialTheme.colorScheme.background,
        contentColor = MaterialTheme.colorScheme.onBackground,
    ) {
        LazyColumn(
            modifier = Modifier.fillMaxWidth().testTag("settings-list"),
            contentPadding = PaddingValues(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
        ) {
            item {
                Text(
                    "Account, connection, storage, and background activity.",
                    style = MaterialTheme.typography.bodyLarge,
                )
            }
            item { KuraSectionHeader("Account") }
            item {
                KuraListRow(
                    headline = "Signed-in account",
                    supportingText = "$accountStatus · Server identity and permissions for this session",
                )
            }
            item { KuraSectionHeader("Connection") }
            item {
                KuraListRow(
                    headline = "Connection status",
                    supportingText = "$connectionStatus · Check route, TLS, and server reachability",
                    onClick = onConnection,
                )
            }
            item { KuraSectionHeader("Backup and data") }
            item {
                KuraListRow(
                    headline = "Automatic backup",
                    supportingText = "View current progress, waiting reasons, rules, and one-way policy",
                    onClick = onBackupSettings,
                )
            }
            item {
                KuraListRow(
                    headline = "Trusted Wi-Fi",
                    supportingText = "Review allowed external Wi-Fi and permission state",
                    onClick = onWifiSettings,
                )
            }
            item {
                KuraListRow(
                    headline = "Media quality and data use",
                    supportingText = "Choose the initial viewing quality for each connection",
                    onClick = onMediaSettings,
                )
            }
            item { KuraSectionHeader("Storage and history") }
            if (isAdmin) {
                item {
                    KuraListRow(
                        headline = "Trash and storage",
                        supportingText = "Review retained items and storage warnings",
                        onClick = onTrash,
                    )
                }
                item {
                    KuraListRow(
                        headline = "Cache management",
                        supportingText = "View ready cache usage and request safe cleanup",
                        onClick = onCacheManagement,
                    )
                }
            }
            item {
                KuraListRow(
                    headline = "Activity",
                    supportingText = "Review account and file operations",
                    onClick = onActivity,
                )
            }
            item { KuraSectionHeader("Session") }
            item {
                KuraDestructiveButton(
                    label = "Log out",
                    onClick = onLogout,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }
}
