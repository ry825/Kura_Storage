@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod", "LongParameterList")

package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.MaterialTheme
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
    onConnection: () -> Unit,
    onMediaSettings: () -> Unit,
    onBackupSettings: () -> Unit,
    onActivity: () -> Unit,
    onTrash: () -> Unit,
    onLogout: () -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxWidth().testTag("settings-list"),
        contentPadding = PaddingValues(KuraTheme.spacing.md),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
    ) {
        item {
            Text("Account, connection, storage, and background activity.", style = MaterialTheme.typography.bodyLarge)
        }
        item { KuraSectionHeader("Connection") }
        item {
            KuraListRow(
                headline = "Connection status",
                supportingText = "Check local direct or ZeroTier connectivity",
                onClick = onConnection,
            )
        }
        item { KuraSectionHeader("Backup and data") }
        item {
            KuraListRow(
                headline = "Automatic backup",
                supportingText = "Status, rules, and allowed Wi-Fi",
                onClick = onBackupSettings,
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
                    supportingText = "Available when server cache management is configured",
                    enabled = false,
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
        item { KuraSectionHeader("Account") }
        item {
            KuraDestructiveButton(
                label = "Log out",
                onClick = onLogout,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
