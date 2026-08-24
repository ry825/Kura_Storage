package com.kurastorage.core.data

import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.ShareCandidate
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.ShareMember
import com.kurastorage.core.model.SharePage
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ShareScope
import com.kurastorage.core.network.CreateShareMemberDto
import com.kurastorage.core.network.CreateShareRequestDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.SetShareMemberRequestDto
import com.kurastorage.core.network.ShareCandidateDto
import com.kurastorage.core.network.ShareItemDto
import com.kurastorage.core.network.SharePageDto
import com.kurastorage.core.network.SharingApi
import java.time.Instant
import java.util.UUID

interface SharingRepository {
    suspend fun candidates(): List<ShareCandidate>

    suspend fun create(
        targetEntryId: String,
        members: Map<String, SharePermission>,
    ): ShareItem

    suspend fun list(
        scope: ShareScope,
        targetType: FileEntryType? = null,
        page: Int = 1,
        pageSize: Int = DEFAULT_PAGE_SIZE,
    ): SharePage

    suspend fun detail(shareId: String): ShareItem

    suspend fun setMember(
        shareId: String,
        userId: String,
        permission: SharePermission,
    ): ShareItem

    suspend fun removeMember(
        shareId: String,
        userId: String,
    )

    suspend fun delete(shareId: String)

    companion object {
        const val DEFAULT_PAGE_SIZE = 100
    }
}

class DefaultSharingRepository(
    private val api: SharingApi,
    private val executor: AuthenticatedRequestExecutor,
) : SharingRepository {
    override suspend fun candidates() = authenticated { api.listCandidates(it) }.map(ShareCandidateDto::toModel)

    override suspend fun create(
        targetEntryId: String,
        members: Map<String, SharePermission>,
    ): ShareItem {
        requireUuid(targetEntryId)
        require(members.isNotEmpty())
        val request =
            CreateShareRequestDto(
                targetEntryId,
                members.map { (userId, permission) ->
                    requireUuid(userId)
                    requireWirePermission(permission)
                    CreateShareMemberDto(userId, permission.name)
                },
            )
        return authenticated { api.createShare(it, request) }.toModel()
    }

    override suspend fun list(
        scope: ShareScope,
        targetType: FileEntryType?,
        page: Int,
        pageSize: Int,
    ) = authenticated {
        api.listShares(it, scope.name.lowercase(), targetType?.name, page, pageSize)
    }.toModel()

    override suspend fun detail(shareId: String): ShareItem {
        requireUuid(shareId)
        return authenticated { api.getShare(it, shareId) }.toModel()
    }

    override suspend fun setMember(
        shareId: String,
        userId: String,
        permission: SharePermission,
    ): ShareItem {
        requireUuid(shareId)
        requireUuid(userId)
        requireWirePermission(permission)
        return refreshAfterFailure(shareId) {
            authenticated { api.setMember(it, shareId, userId, SetShareMemberRequestDto(permission.name)) }.toModel()
        }
    }

    override suspend fun removeMember(
        shareId: String,
        userId: String,
    ) {
        requireUuid(shareId)
        requireUuid(userId)
        refreshAfterFailure(shareId) { authenticated { api.removeMember(it, shareId, userId) } }
        runCatching { detail(shareId) }
    }

    override suspend fun delete(shareId: String) {
        requireUuid(shareId)
        refreshAfterFailure(shareId) { authenticated { api.deleteShare(it, shareId) } }
        runCatching { list(ShareScope.OWNED) }
    }

    private suspend fun <T> refreshAfterFailure(
        shareId: String,
        block: suspend () -> T,
    ): T =
        try {
            block()
        } catch (failure: KuraStorageException.Api) {
            runCatching { detail(shareId) }
            throw failure
        } catch (failure: KuraStorageException.Network) {
            runCatching { detail(shareId) }
            throw failure
        }

    private suspend fun <T> authenticated(call: suspend (String) -> NetworkCallResult<T>): T =
        executor.execute { token ->
            when (val result = call(token)) {
                is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
            }
        }
}

class SharePager(
    private val loadPage: suspend (page: Int) -> SharePage,
) {
    private var current: SharePage? = null

    suspend fun refresh(): SharePage = loadPage(1).also { current = it }

    @Suppress("ReturnCount")
    suspend fun loadNext(): SharePage {
        val existing = current ?: return refresh()
        if (!existing.hasNextPage) return existing
        val next = loadPage(existing.page + 1)
        return existing
            .copy(
                items = (existing.items + next.items).distinctBy(ShareItem::id),
                page = next.page,
                totalCount = next.totalCount,
            ).also { current = it }
    }
}

private fun ShareCandidateDto.toModel(): ShareCandidate {
    requireUuid(userId)
    require(displayName.isNotBlank())
    return ShareCandidate(userId, displayName)
}

private fun ShareItemDto.toModel(): ShareItem {
    requireUuid(id)
    requireUuid(targetEntryId)
    requireUuid(owner.id)
    require(owner.displayName.isNotBlank())
    require(name.isNotBlank())
    val parsedPermission = SharePermission.fromWire(permission)
    return ShareItem(
        id,
        targetEntryId,
        FileEntryType.entries.firstOrNull { it.name == entryType }
            ?: throw IllegalArgumentException("Unknown share entry type"),
        name,
        OwnerSummary(owner.id, owner.displayName),
        parsedPermission,
        members.map { member ->
            requireUuid(member.userId)
            require(member.displayName.isNotBlank())
            ShareMember(member.userId, member.displayName, SharePermission.fromWire(member.permission))
        },
        Instant.parse(createdAt),
        Instant.parse(updatedAt),
    )
}

private fun SharePageDto.toModel(): SharePage {
    require(page >= 1 && pageSize in 1..MAX_PAGE_SIZE && totalCount >= 0)
    return SharePage(items.map(ShareItemDto::toModel), page, pageSize, totalCount)
}

private fun requireWirePermission(permission: SharePermission) {
    require(permission != SharePermission.UNKNOWN) { "Unknown share permission" }
}

private fun requireUuid(value: String) {
    require(UUID.fromString(value).toString().equals(value, ignoreCase = true)) { "Invalid UUID" }
}

private const val MAX_PAGE_SIZE = 500
