@file:Suppress("FunctionNaming", "MagicNumber", "MaxLineLength", "ktlint:standard:function-naming")

package com.kurastorage.core.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.formatting.formatFileSize
import com.kurastorage.core.ui.icons.KuraFileType
import com.kurastorage.core.ui.icons.KuraFileTypeIcon
import java.time.Instant

@Composable
@Suppress("LongMethod", "LongParameterList")
fun KuraFileEntryRow(
    name: String,
    entryType: FileEntryType,
    mimeType: String?,
    ownerName: String,
    permission: SharePermission,
    permissionSource: PermissionSource?,
    updatedAt: Instant,
    status: FileEntryStatus = FileEntryStatus.ACTIVE,
    size: Long? = null,
    sharedFrom: String? = null,
    contextLine: String? = null,
    visual: Boolean = false,
    enabled: Boolean = status == FileEntryStatus.ACTIVE && entryType != FileEntryType.UNKNOWN,
    onClick: (() -> Unit)? = null,
    modifier: Modifier = Modifier,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable () -> Unit)? = null,
) {
    KuraCard(
        modifier = modifier.then(if (enabled) Modifier else Modifier.semantics { disabled() }),
        onClick = onClick.takeIf { enabled },
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (leading == null) {
                KuraFileTypeIcon(
                    type = KuraFileType.from(mimeType, entryType == FileEntryType.FOLDER),
                    contentDescription = if (entryType == FileEntryType.FOLDER) "Folder" else "File",
                )
            } else {
                leading()
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xxs)) {
                Text(
                    name,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    style = if (visual) MaterialTheme.typography.titleMedium else MaterialTheme.typography.bodyLarge,
                )
                if (visual) {
                    Text(
                        "${permission.userLabel()} • $ownerName${size?.let { " • ${formatFileSize(it)}" }.orEmpty()}",
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    sharedFrom?.let {
                        Text(
                            "Shared from: $it",
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Text(
                        listOfNotNull(contextLine, "Updated $updatedAt").joinToString(" • "),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                } else {
                    Text("Owner: $ownerName • ${permission.userLabel()}${permissionSource?.userLabel()?.let { " ($it)" }.orEmpty()}")
                    Text("Updated $updatedAt${size?.let { " • ${formatFileSize(it)}" }.orEmpty()}")
                    sharedFrom?.let { Text("Shared from: $it") }
                    contextLine?.let { Text(it) }
                }
                status.presentation()?.let { (label, style) -> KuraStatusBadge(label, style) }
            }
            trailing?.invoke()
        }
    }
}

private fun SharePermission.userLabel(): String =
    when (this) {
        SharePermission.VIEWER -> "Read only"
        SharePermission.CONTRIBUTOR -> "Can add"
        SharePermission.EDITOR -> "Can edit"
        SharePermission.MANAGER -> "Can manage"
        SharePermission.UNKNOWN -> "Unavailable"
    }

private fun PermissionSource.userLabel(): String? =
    when (this) {
        PermissionSource.OWNER -> "Owner"
        PermissionSource.DIRECT -> "Direct share"
        PermissionSource.INHERITED -> "Inherited share"
        PermissionSource.UNKNOWN -> null
    }

private fun FileEntryStatus.presentation(): Pair<String, KuraStatus>? =
    when (this) {
        FileEntryStatus.ACTIVE -> null
        FileEntryStatus.MISSING_CANDIDATE -> "Checking file" to KuraStatus.WARNING
        FileEntryStatus.MISSING -> "File missing" to KuraStatus.ERROR
        FileEntryStatus.TRASHED -> "In Trash" to KuraStatus.WARNING
        FileEntryStatus.UNKNOWN -> "Update required" to KuraStatus.ERROR
    }
