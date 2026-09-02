package com.kurastorage.core.model

import java.time.Instant

enum class UserActivityType {
    UPLOAD,
    MOVE,
    EDIT,
    SHARE,
    DELETE,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String): UserActivityType = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class ActivityTargetType {
    FILE,
    FOLDER,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String): ActivityTargetType = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class ActivityEditKind {
    TEXT_SAVE,
    VERSION_RESTORE,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String): ActivityEditKind = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class ActivityShareAction {
    CREATED,
    UPDATED,
    REVOKED,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String): ActivityShareAction = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class ActivityDeleteKind {
    TRASHED,
    PURGED,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String): ActivityDeleteKind = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

sealed interface ActivityDetail {
    data class Upload(
        val resultingFileVersion: Long,
    ) : ActivityDetail

    data class Move(
        val sourceParentName: String,
        val destinationParentName: String,
    ) : ActivityDetail

    data class Edit(
        val resultingFileVersion: Long,
        val kind: ActivityEditKind,
    ) : ActivityDetail

    data class Share(
        val recipientDisplayName: String,
        val permission: SharePermission,
        val action: ActivityShareAction,
    ) : ActivityDetail

    data class Delete(
        val kind: ActivityDeleteKind,
    ) : ActivityDetail

    data object Unsupported : ActivityDetail
}

data class ActivityItem(
    val type: UserActivityType,
    val occurredAt: Instant,
    val actorDisplayName: String,
    val actorDeviceName: String?,
    val targetEntryId: String?,
    val targetType: ActivityTargetType,
    val targetName: String,
    val ownerDisplayName: String,
    val detail: ActivityDetail,
) {
    val stableKey: String =
        listOf(occurredAt, type, actorDisplayName, targetName, ownerDisplayName, detail)
            .joinToString("|")
}

data class ActivityPage(
    val items: List<ActivityItem>,
    val nextCursor: String?,
)
