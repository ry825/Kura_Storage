@file:Suppress("ComplexCondition", "CyclomaticComplexMethod", "LongMethod", "MaxLineLength", "ReturnCount")

package com.kurastorage.core.data

import com.kurastorage.core.model.ActivityDeleteKind
import com.kurastorage.core.model.ActivityDetail
import com.kurastorage.core.model.ActivityEditKind
import com.kurastorage.core.model.ActivityItem
import com.kurastorage.core.model.ActivityPage
import com.kurastorage.core.model.ActivityShareAction
import com.kurastorage.core.model.ActivityTargetType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.UserActivityType
import com.kurastorage.core.network.ActivityApi
import com.kurastorage.core.network.ActivityItemDto
import com.kurastorage.core.network.ActivityPageDto
import com.kurastorage.core.network.NetworkCallResult
import java.text.Normalizer
import java.time.Instant
import java.time.format.DateTimeParseException
import java.util.UUID

interface ActivityRepository {
    suspend fun list(
        type: UserActivityType? = null,
        cursor: String? = null,
        pageSize: Int = DEFAULT_ACTIVITY_PAGE_SIZE,
    ): ActivityPage
}

class DefaultActivityRepository(
    private val api: ActivityApi,
    private val executor: AuthenticatedRequestExecutor,
) : ActivityRepository {
    override suspend fun list(
        type: UserActivityType?,
        cursor: String?,
        pageSize: Int,
    ): ActivityPage {
        require(type != UserActivityType.UNKNOWN)
        require(pageSize in 1..MAXIMUM_ACTIVITY_PAGE_SIZE)
        require(cursor == null || cursor.isNotBlank() && cursor.length <= MAXIMUM_CURSOR_LENGTH)
        return executor
            .execute { token -> api.listActivities(token, type?.name, cursor, pageSize).authenticated() }
            .toStrictModel(pageSize)
    }
}

class ActivityPager(
    private val repository: ActivityRepository,
    private val pageSize: Int = DEFAULT_ACTIVITY_PAGE_SIZE,
) {
    private var current: ActivityPage? = null
    private var type: UserActivityType? = null

    suspend fun refresh(filter: UserActivityType? = type): ActivityPage =
        repository.list(filter, null, pageSize).also {
            current = it
            type = filter
        }

    suspend fun loadNext(): ActivityPage {
        val existing = current ?: return refresh()
        val cursor = existing.nextCursor ?: return existing
        val next = repository.list(type, cursor, pageSize)
        if (next.nextCursor == cursor || next.items.isEmpty() && next.nextCursor != null) invalidActivityResponse()
        return ActivityPage(existing.items + next.items, next.nextCursor).also { current = it }
    }
}

private fun <T> NetworkCallResult<T>.authenticated(): AuthenticatedCallResult<T> =
    when (this) {
        is NetworkCallResult.Success -> AuthenticatedCallResult.Success(value)
        NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
    }

private fun ActivityPageDto.toStrictModel(expectedPageSize: Int): ActivityPage {
    if (items.size > expectedPageSize || nextCursor?.let { it.isBlank() || it.length > MAXIMUM_CURSOR_LENGTH } == true) {
        invalidActivityResponse()
    }
    val mapped = items.map(ActivityItemDto::toStrictModel)
    if (mapped.zipWithNext().any { (a, b) -> a.occurredAt < b.occurredAt }) {
        invalidActivityResponse()
    }
    return ActivityPage(mapped, nextCursor)
}

private fun ActivityItemDto.toStrictModel(): ActivityItem {
    val activityType = UserActivityType.fromWire(type)
    if (activityType == UserActivityType.UNKNOWN) {
        return ActivityItem(
            activityType,
            activityStrictInstant(occurredAt),
            strictSnapshot(actorDisplayName, MAXIMUM_USER_NAME_LENGTH),
            actorDeviceName?.let { strictSnapshot(it, MAXIMUM_USER_NAME_LENGTH) },
            null,
            ActivityTargetType.UNKNOWN,
            strictSnapshot(targetName, MAXIMUM_ENTRY_NAME_LENGTH),
            strictSnapshot(ownerDisplayName, MAXIMUM_USER_NAME_LENGTH),
            ActivityDetail.Unsupported,
        )
    }
    val target = ActivityTargetType.fromWire(targetType)
    if (target == ActivityTargetType.UNKNOWN) invalidActivityResponse()
    val targetId = targetEntryId?.let(::activityStrictUuid)
    return ActivityItem(
        activityType,
        activityStrictInstant(occurredAt),
        strictSnapshot(actorDisplayName, MAXIMUM_USER_NAME_LENGTH),
        actorDeviceName?.let { strictSnapshot(it, MAXIMUM_USER_NAME_LENGTH) },
        targetId,
        target,
        strictSnapshot(targetName, MAXIMUM_ENTRY_NAME_LENGTH),
        strictSnapshot(ownerDisplayName, MAXIMUM_USER_NAME_LENGTH),
        strictDetail(activityType),
    )
}

private fun ActivityItemDto.strictDetail(type: UserActivityType): ActivityDetail {
    val move = sourceParentName != null || destinationParentName != null
    val version = resultingFileVersion
    val edit = editKind != null
    val share = recipientDisplayName != null || sharePermission != null || shareAction != null
    val delete = deleteKind != null
    return when (type) {
        UserActivityType.UPLOAD -> {
            if (version == null || version < 1 || move || edit || share || delete) invalidActivityResponse()
            ActivityDetail.Upload(version)
        }
        UserActivityType.MOVE -> {
            val source = sourceParentName
            val destination = destinationParentName
            if (source == null || destination == null || version != null || edit || share || delete) invalidActivityResponse()
            ActivityDetail.Move(
                strictSnapshot(source, MAXIMUM_ENTRY_NAME_LENGTH),
                strictSnapshot(destination, MAXIMUM_ENTRY_NAME_LENGTH),
            )
        }
        UserActivityType.EDIT -> {
            val kind = editKind?.let(ActivityEditKind::fromWire)
            if (version == null ||
                version < 1 ||
                kind == null ||
                kind == ActivityEditKind.UNKNOWN ||
                move ||
                share ||
                delete
            ) {
                invalidActivityResponse()
            }
            ActivityDetail.Edit(version, kind)
        }
        UserActivityType.SHARE -> {
            val recipient = recipientDisplayName
            val permission = sharePermission?.let(SharePermission::fromWire)
            val action = shareAction?.let(ActivityShareAction::fromWire)
            if (recipient == null ||
                permission == null ||
                permission == SharePermission.UNKNOWN ||
                action == null ||
                action == ActivityShareAction.UNKNOWN ||
                move ||
                version != null ||
                edit ||
                delete
            ) {
                invalidActivityResponse()
            }
            ActivityDetail.Share(strictSnapshot(recipient, MAXIMUM_USER_NAME_LENGTH), permission, action)
        }
        UserActivityType.DELETE -> {
            val kind = deleteKind?.let(ActivityDeleteKind::fromWire)
            if (kind == null || kind == ActivityDeleteKind.UNKNOWN || move || version != null || edit || share) invalidActivityResponse()
            ActivityDetail.Delete(kind)
        }
        UserActivityType.UNKNOWN -> ActivityDetail.Unsupported
    }
}

private fun strictSnapshot(
    value: String,
    maximumLength: Int,
): String {
    if (value.isBlank() ||
        value.length > maximumLength ||
        value != value.trim() ||
        !Normalizer.isNormalized(value, Normalizer.Form.NFC) ||
        value.any(Char::isISOControl)
    ) {
        invalidActivityResponse()
    }
    return value
}

private fun activityStrictInstant(value: String): Instant =
    try {
        Instant.parse(value)
    } catch (_: DateTimeParseException) {
        invalidActivityResponse()
    }

private fun activityStrictUuid(value: String): String =
    runCatching { UUID.fromString(value).toString() }.getOrNull()?.takeIf { it == value.lowercase() }
        ?: invalidActivityResponse()

private fun invalidActivityResponse(): Nothing = throw KuraStorageException.InvalidServerResponse()

const val DEFAULT_ACTIVITY_PAGE_SIZE = 50
const val MAXIMUM_ACTIVITY_PAGE_SIZE = 100
private const val MAXIMUM_CURSOR_LENGTH = 128
private const val MAXIMUM_USER_NAME_LENGTH = 128
private const val MAXIMUM_ENTRY_NAME_LENGTH = 255
