@file:Suppress("FunctionNaming", "MagicNumber", "MaxLineLength", "ktlint:standard:function-naming")

package com.kurastorage.core.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
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
import com.kurastorage.core.ui.icons.KuraFileType
import com.kurastorage.core.ui.icons.KuraFileTypeIcon
import java.time.Instant

@Composable
@Suppress("LongParameterList")
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
    enabled: Boolean = status == FileEntryStatus.ACTIVE && entryType != FileEntryType.UNKNOWN,
    onClick: (() -> Unit)? = null,
    modifier: Modifier = Modifier,
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
            KuraFileTypeIcon(
                type = KuraFileType.from(mimeType, entryType == FileEntryType.FOLDER),
                contentDescription = if (entryType == FileEntryType.FOLDER) "Folder" else "File",
            )
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xxs)) {
                androidx.compose.material3.Text(name, maxLines = 2, overflow = TextOverflow.Ellipsis)
                androidx.compose.material3.Text("Owner: $ownerName • $permission${permissionSource?.let { " ($it)" }.orEmpty()}")
                androidx.compose.material3.Text(
                    "Updated $updatedAt${size?.let { " • ${formatEntryBytes(it)}" }.orEmpty()}",
                )
                sharedFrom?.let { androidx.compose.material3.Text("Shared from: $it") }
                contextLine?.let { androidx.compose.material3.Text(it) }
                status.presentation()?.let { (label, style) -> KuraStatusBadge(label, style) }
            }
            trailing?.invoke()
        }
    }
}

private fun FileEntryStatus.presentation(): Pair<String, KuraStatus>? =
    when (this) {
        FileEntryStatus.ACTIVE -> null
        FileEntryStatus.MISSING_CANDIDATE -> "Checking file" to KuraStatus.WARNING
        FileEntryStatus.MISSING -> "File missing" to KuraStatus.ERROR
        FileEntryStatus.TRASHED -> "In Trash" to KuraStatus.WARNING
        FileEntryStatus.UNKNOWN -> "Update required" to KuraStatus.ERROR
    }

private fun formatEntryBytes(bytes: Long): String =
    when {
        bytes >= 1_000_000_000 -> "%.1f GB".format(bytes / 1_000_000_000.0)
        bytes >= 1_000_000 -> "%.1f MB".format(bytes / 1_000_000.0)
        bytes >= 1_000 -> "%.1f KB".format(bytes / 1_000.0)
        else -> "$bytes bytes"
    }
