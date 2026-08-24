package com.kurastorage.core.model

import java.time.Instant

enum class SharePermission {
    UNKNOWN,
    VIEWER,
    CONTRIBUTOR,
    EDITOR,
    MANAGER,
    ;

    val strength: Int get() = ordinal

    companion object {
        fun fromWire(value: String?): SharePermission = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class PermissionSource {
    UNKNOWN,
    OWNER,
    DIRECT,
    INHERITED,
    ;

    companion object {
        fun fromWire(value: String?): PermissionSource = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

data class OwnerSummary(
    val id: String,
    val displayName: String,
) {
    companion object {
        val UNKNOWN = OwnerSummary("", "Unknown owner")
    }
}

data class FilePermissionCapabilities(
    val canDownload: Boolean,
    val canCreate: Boolean,
    val canRename: Boolean,
    val canMove: Boolean,
    val canTrash: Boolean,
    val canManageShare: Boolean,
    val canManageTrash: Boolean,
)

fun filePermissionCapabilities(
    permission: SharePermission,
    source: PermissionSource,
): FilePermissionCapabilities {
    val trusted = permission != SharePermission.UNKNOWN && source != PermissionSource.UNKNOWN
    val owner = trusted && source == PermissionSource.OWNER
    val strength = permission.strength
    return FilePermissionCapabilities(
        canDownload = true,
        canCreate = trusted && (owner || strength >= SharePermission.CONTRIBUTOR.strength),
        canRename = trusted && (owner || strength >= SharePermission.EDITOR.strength),
        canMove = trusted && (owner || strength >= SharePermission.EDITOR.strength),
        canTrash = trusted && (owner || strength >= SharePermission.EDITOR.strength),
        canManageShare = trusted && (owner || strength >= SharePermission.MANAGER.strength),
        canManageTrash = owner,
    )
}

data class ShareCandidate(
    val userId: String,
    val displayName: String,
)

data class ShareMember(
    val userId: String,
    val displayName: String,
    val permission: SharePermission,
)

data class ShareItem(
    val id: String,
    val targetEntryId: String,
    val entryType: FileEntryType,
    val name: String,
    val owner: OwnerSummary,
    val permission: SharePermission,
    val members: List<ShareMember>,
    val createdAt: Instant,
    val updatedAt: Instant,
) {
    val canManage: Boolean get() = permission == SharePermission.MANAGER
}

data class SharePage(
    val items: List<ShareItem>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}

enum class ShareScope { OWNED, RECEIVED }
