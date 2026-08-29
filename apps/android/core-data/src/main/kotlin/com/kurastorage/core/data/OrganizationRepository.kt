@file:Suppress("TooManyFunctions")

package com.kurastorage.core.data

import com.kurastorage.core.model.EntryOrganizationState
import com.kurastorage.core.model.FavoriteItem
import com.kurastorage.core.model.FavoritePage
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.MAXIMUM_TAGS_PER_ENTRY
import com.kurastorage.core.model.MAXIMUM_TAGS_PER_USER
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.model.validateTagName
import com.kurastorage.core.network.EntryOrganizationStateDto
import com.kurastorage.core.network.FavoriteItemDto
import com.kurastorage.core.network.FavoritePageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.OrganizationApi
import com.kurastorage.core.network.SearchResultItemDto
import com.kurastorage.core.network.TagItemDto
import com.kurastorage.core.network.TagNameRequestDto

interface OrganizationRepository {
    suspend fun listFavorites(
        page: Int = 1,
        pageSize: Int = SearchInput.DEFAULT_PAGE_SIZE,
    ): FavoritePage

    suspend fun setFavorite(
        entryId: String,
        favorite: Boolean,
    ): EntryOrganizationState

    suspend fun listTags(): List<TagItem>

    suspend fun createTag(name: String): TagItem

    suspend fun renameTag(
        tagId: String,
        name: String,
    ): TagItem

    suspend fun deleteTag(tagId: String)

    suspend fun state(entryId: String): EntryOrganizationState

    suspend fun setTag(
        entryId: String,
        tagId: String,
        attached: Boolean,
    ): EntryOrganizationState
}

class DefaultOrganizationRepository(
    private val api: OrganizationApi,
    private val executor: AuthenticatedRequestExecutor,
) : OrganizationRepository {
    override suspend fun listFavorites(
        page: Int,
        pageSize: Int,
    ): FavoritePage {
        require(page >= 1 && pageSize in 1..SearchInput.MAXIMUM_PAGE_SIZE)
        return executor
            .execute { token -> api.listFavorites(token, page, pageSize).authenticated() }
            .toModel(page, pageSize)
    }

    override suspend fun setFavorite(
        entryId: String,
        favorite: Boolean,
    ): EntryOrganizationState {
        strictUuid(entryId)
        return reconcile(entryId) { token ->
            if (favorite) {
                api.addFavorite(token, entryId).authenticated()
            } else {
                api.removeFavorite(token, entryId).authenticated()
            }
        }
    }

    override suspend fun listTags(): List<TagItem> =
        executor
            .execute { token -> api.listTags(token).authenticated() }
            .map(TagItemDto::toModel)
            .also(::validateTags)

    override suspend fun createTag(name: String): TagItem {
        val validated = requireNotNull(validateTagName(name).value) { "Invalid tag name" }
        return executor
            .execute { token -> api.createTag(token, TagNameRequestDto(validated.value)).authenticated() }
            .toModel()
    }

    override suspend fun renameTag(
        tagId: String,
        name: String,
    ): TagItem {
        strictUuid(tagId)
        val validated = requireNotNull(validateTagName(name).value) { "Invalid tag name" }
        return executor
            .execute { token ->
                api.renameTag(token, tagId, TagNameRequestDto(validated.value)).authenticated()
            }.toModel()
    }

    override suspend fun deleteTag(tagId: String) {
        strictUuid(tagId)
        executor.execute { token -> api.deleteTag(token, tagId).authenticated() }
    }

    override suspend fun state(entryId: String): EntryOrganizationState {
        strictUuid(entryId)
        return executor.execute { token -> api.getEntryOrganization(token, entryId).authenticated() }.toModel()
    }

    override suspend fun setTag(
        entryId: String,
        tagId: String,
        attached: Boolean,
    ): EntryOrganizationState {
        strictUuid(entryId)
        strictUuid(tagId)
        return reconcile(entryId) { token ->
            if (attached) {
                api.attachTag(token, entryId, tagId).authenticated()
            } else {
                api.detachTag(token, entryId, tagId).authenticated()
            }
        }
    }

    private suspend fun reconcile(
        entryId: String,
        mutation: suspend (String) -> AuthenticatedCallResult<Unit>,
    ): EntryOrganizationState {
        try {
            executor.execute(mutation)
        } catch (_: KuraStorageException.Network) {
            // The write may have reached the server. Never synthesize local success.
        }
        return state(entryId)
    }
}

class FavoritePager(
    private val repository: OrganizationRepository,
    private val pageSize: Int = SearchInput.DEFAULT_PAGE_SIZE,
) {
    private var current: FavoritePage? = null

    suspend fun refresh(): FavoritePage = repository.listFavorites(1, pageSize).also { current = it }

    suspend fun loadNext(): FavoritePage {
        val existing = current
        return when {
            existing == null -> refresh()
            !existing.hasNextPage -> existing
            else -> appendNextPage(existing)
        }
    }

    private suspend fun appendNextPage(existing: FavoritePage): FavoritePage {
        val next = repository.listFavorites(existing.page + 1, pageSize)
        val ids = existing.items.mapTo(mutableSetOf()) { it.id }
        if (next.items.any { !ids.add(it.id) }) invalidSearchResponse()
        return existing
            .copy(
                items = existing.items + next.items,
                page = next.page,
                totalCount = next.totalCount,
            ).also { current = it }
    }
}

private fun FavoritePageDto.toModel(
    expectedPage: Int,
    expectedPageSize: Int,
): FavoritePage {
    validateSearchPage(page, pageSize, totalCount, items.size, expectedPage, expectedPageSize)
    val mapped = items.map(FavoriteItemDto::toModel)
    if (mapped.map { it.id }.toSet().size != mapped.size) invalidSearchResponse()
    return FavoritePage(mapped, page, pageSize, totalCount)
}

private fun FavoriteItemDto.toModel() =
    FavoriteItem(
        SearchResultItemDto(
            id,
            entryType,
            name,
            mimeType,
            fileCategory,
            size,
            status,
            updatedAt,
            owner,
            permission,
            permissionSource,
            shareTargetId,
        ).toStrictModel(),
        strictInstant(favoritedAt),
    )

private fun TagItemDto.toModel(): TagItem {
    val validated = validateTagName(name).value ?: invalidSearchResponse()
    if (validated.value != name) invalidSearchResponse()
    return TagItem(strictUuid(id), name)
}

private fun EntryOrganizationStateDto.toModel(): EntryOrganizationState {
    val mapped = tags.map(TagItemDto::toModel)
    validateTags(mapped, MAXIMUM_TAGS_PER_ENTRY)
    return EntryOrganizationState(isFavorite, mapped)
}

private fun validateTags(
    tags: List<TagItem>,
    maximum: Int = MAXIMUM_TAGS_PER_USER,
) {
    if (tags.size > maximum || tags.map { it.id }.toSet().size != tags.size) invalidSearchResponse()
}

private fun <T> NetworkCallResult<T>.authenticated(): AuthenticatedCallResult<T> =
    when (this) {
        is NetworkCallResult.Success -> AuthenticatedCallResult.Success(value)
        NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
    }
