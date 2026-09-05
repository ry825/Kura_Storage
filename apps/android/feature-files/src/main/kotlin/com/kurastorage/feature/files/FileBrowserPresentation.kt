package com.kurastorage.feature.files

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission

enum class BrowserDisplayMode {
    GRID,
    LIST,
}

internal fun defaultBrowserDisplayMode(trashMode: Boolean): BrowserDisplayMode =
    if (trashMode) BrowserDisplayMode.LIST else BrowserDisplayMode.GRID

internal fun permissionLabel(permission: SharePermission): String =
    when (permission) {
        SharePermission.VIEWER -> "Read only"
        SharePermission.CONTRIBUTOR -> "Can add files"
        SharePermission.EDITOR -> "Can edit"
        SharePermission.MANAGER -> "Can manage"
        SharePermission.UNKNOWN -> "Unavailable"
    }

internal data class EntryMetadata(
    val primary: String,
    val secondary: List<String>,
)

internal fun entryMetadata(
    entry: FileEntry,
    personalRoot: Boolean,
    sharedFrom: String?,
): EntryMetadata {
    val primary =
        if (entry.entryType == FileEntryType.FOLDER) {
            "Folder • Updated ${entry.updatedAt}"
        } else {
            "${fileTypeLabel(entry)} • ${formatBytes(entry.size)} • Updated ${entry.updatedAt}"
        }
    val secondary =
        if (personalRoot && entry.permissionSource == PermissionSource.OWNER) {
            emptyList()
        } else {
            buildList {
                sharedFrom?.let { add("Shared from: $it") }
                add(permissionLabel(entry.permission))
                if (entry.permission != SharePermission.VIEWER && entry.permission != SharePermission.UNKNOWN) {
                    add("Writable")
                }
            }
        }
    return EntryMetadata(primary, secondary)
}
