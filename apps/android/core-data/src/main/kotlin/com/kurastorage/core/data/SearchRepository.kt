@file:Suppress(
    "ComplexCondition",
    "CyclomaticComplexMethod",
    "ReturnCount",
    "TooManyFunctions",
)

package com.kurastorage.core.data

import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.RecentFilePage
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.SearchPage
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ValidatedSearchInput
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.RecentFileItemDto
import com.kurastorage.core.network.RecentFilePageDto
import com.kurastorage.core.network.SearchApi
import com.kurastorage.core.network.SearchPageDto
import com.kurastorage.core.network.SearchRequestDto
import com.kurastorage.core.network.SearchResultItemDto
import java.time.Instant
import java.util.UUID

interface SearchRepository {
    suspend fun search(input: SearchInput): SearchPage
}

class DefaultSearchRepository(
    private val api: SearchApi,
    private val executor: AuthenticatedRequestExecutor,
) : SearchRepository {
    override suspend fun search(input: SearchInput): SearchPage {
        val validated = input.validate().value ?: throw IllegalArgumentException("Invalid search input")
        val request = validated.toRequest()
        return executor
            .execute { token -> api.search(token, request).toAuthenticatedResult() }
            .toModel(request.page, request.pageSize)
    }
}

interface RecentFileRepository {
    suspend fun list(
        page: Int = 1,
        pageSize: Int = SearchInput.DEFAULT_PAGE_SIZE,
    ): RecentFilePage

    suspend fun record(fileId: String): RecentRecordOutcome
}

sealed interface RecentRecordOutcome {
    data object Confirmed : RecentRecordOutcome

    data class Reconciled(
        val page: RecentFilePage,
    ) : RecentRecordOutcome
}

class DefaultRecentFileRepository(
    private val api: SearchApi,
    private val executor: AuthenticatedRequestExecutor,
) : RecentFileRepository {
    override suspend fun list(
        page: Int,
        pageSize: Int,
    ): RecentFilePage {
        require(page >= 1 && pageSize in 1..SearchInput.MAXIMUM_PAGE_SIZE)
        return executor
            .execute { token -> api.listRecentFiles(token, page, pageSize).toAuthenticatedResult() }
            .toModel(page, pageSize)
    }

    override suspend fun record(fileId: String): RecentRecordOutcome {
        requireUuid(fileId)
        return try {
            executor.execute { token -> api.recordRecentFile(token, fileId).toAuthenticatedResult() }
            RecentRecordOutcome.Confirmed
        } catch (_: KuraStorageException.Network) {
            RecentRecordOutcome.Reconciled(list())
        }
    }
}

class SearchPager(
    private val repository: SearchRepository,
    input: SearchInput,
) {
    private val fixedInput = input.copy(page = 1)
    private var current: SearchPage? = null

    suspend fun refresh(): SearchPage = repository.search(fixedInput).also { current = it }

    suspend fun loadNext(): SearchPage {
        val existing = current ?: return refresh()
        if (!existing.hasNextPage) return existing
        val next = repository.search(fixedInput.copy(page = existing.page + 1))
        val existingIds = existing.items.mapTo(mutableSetOf()) { it.id }
        if (next.items.any { !existingIds.add(it.id) }) invalidResponse()
        return existing
            .copy(items = existing.items + next.items, page = next.page, totalCount = next.totalCount)
            .also { current = it }
    }
}

class RecentFilePager(
    private val repository: RecentFileRepository,
    private val pageSize: Int = SearchInput.DEFAULT_PAGE_SIZE,
) {
    private var current: RecentFilePage? = null

    suspend fun refresh(): RecentFilePage = repository.list(page = 1, pageSize = pageSize).also { current = it }

    suspend fun loadNext(): RecentFilePage {
        val existing = current ?: return refresh()
        if (!existing.hasNextPage) return existing
        val next = repository.list(existing.page + 1, pageSize)
        val existingIds = existing.items.mapTo(mutableSetOf()) { it.id }
        if (next.items.any { !existingIds.add(it.id) }) invalidResponse()
        return existing
            .copy(items = existing.items + next.items, page = next.page, totalCount = next.totalCount)
            .also { current = it }
    }
}

private fun ValidatedSearchInput.toRequest() =
    SearchRequestDto(
        query = query,
        entryType = entryType?.name,
        fileCategory = fileCategory?.name,
        status = status?.name,
        updatedFrom = updatedFrom?.toString(),
        updatedTo = updatedTo?.toString(),
        minSize = minSize,
        maxSize = maxSize,
        ownerUserId = ownerUserId,
        shareTargetId = shareTargetId,
        page = page,
        pageSize = pageSize,
    )

private fun <T> NetworkCallResult<T>.toAuthenticatedResult(): AuthenticatedCallResult<T> =
    when (this) {
        is NetworkCallResult.Success -> AuthenticatedCallResult.Success(value)
        NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
    }

private fun SearchPageDto.toModel(
    expectedPage: Int,
    expectedPageSize: Int,
): SearchPage {
    validatePage(page, pageSize, totalCount, items.size, expectedPage, expectedPageSize)
    val mapped = items.map(SearchResultItemDto::toStrictModel)
    if (mapped.map { it.id }.toSet().size != mapped.size) invalidResponse()
    return SearchPage(mapped, page, pageSize, totalCount)
}

private fun RecentFilePageDto.toModel(
    expectedPage: Int,
    expectedPageSize: Int,
): RecentFilePage {
    validatePage(page, pageSize, totalCount, items.size, expectedPage, expectedPageSize)
    val mapped = items.map(RecentFileItemDto::toStrictModel)
    if (mapped.map { it.id }.toSet().size != mapped.size) invalidResponse()
    return RecentFilePage(mapped, page, pageSize, totalCount)
}

private fun SearchResultItemDto.toStrictModel() =
    strictMetadata(
        id,
        entryType,
        name,
        mimeType,
        fileCategory,
        size,
        status,
        updatedAt,
        owner.id,
        owner.displayName,
        permission,
        permissionSource,
        shareTargetId,
    )

private fun RecentFileItemDto.toStrictModel() =
    RecentFileItem(
        strictMetadata(
            id,
            entryType,
            name,
            mimeType,
            fileCategory,
            size,
            status,
            updatedAt,
            owner.id,
            owner.displayName,
            permission,
            permissionSource,
            shareTargetId,
        ),
        parseInstant(openedAt),
    )

@Suppress("LongParameterList")
private fun strictMetadata(
    id: String,
    entryTypeWire: String,
    name: String,
    mimeType: String?,
    categoryWire: String?,
    size: Long,
    statusWire: String,
    updatedAtWire: String,
    ownerId: String,
    ownerName: String,
    permissionWire: String,
    sourceWire: String,
    shareTargetId: String?,
): SearchResultItem {
    val entryType = FileEntryType.fromWire(entryTypeWire)
    val category = categoryWire?.let(SearchFileCategory::fromWire)
    val status = FileEntryStatus.fromWire(statusWire)
    val source = PermissionSource.fromWire(sourceWire)
    val permission =
        when {
            source == PermissionSource.OWNER && permissionWire == "OWNER" -> SharePermission.MANAGER
            source != PermissionSource.OWNER -> SharePermission.fromWire(permissionWire)
            else -> SharePermission.UNKNOWN
        }
    if (
        entryType == FileEntryType.UNKNOWN ||
        category == SearchFileCategory.UNKNOWN ||
        status !in setOf(FileEntryStatus.ACTIVE, FileEntryStatus.MISSING_CANDIDATE, FileEntryStatus.MISSING) ||
        permission == SharePermission.UNKNOWN ||
        source == PermissionSource.UNKNOWN ||
        name.isBlank() ||
        ownerName.isBlank() ||
        size < 0 ||
        (entryType == FileEntryType.FILE && category == null) ||
        (entryType == FileEntryType.FOLDER && category != null) ||
        (source == PermissionSource.OWNER && shareTargetId != null) ||
        (source != PermissionSource.OWNER && shareTargetId == null) ||
        (source != PermissionSource.OWNER && permissionWire == "OWNER")
    ) {
        invalidResponse()
    }
    return SearchResultItem(
        requireUuid(id),
        entryType,
        name,
        mimeType,
        category,
        size,
        status,
        parseInstant(updatedAtWire),
        OwnerSummary(requireUuid(ownerId), ownerName),
        permission,
        source,
        shareTargetId?.let(::requireUuid),
    )
}

@Suppress("LongParameterList")
private fun validatePage(
    page: Int,
    pageSize: Int,
    totalCount: Int,
    itemCount: Int,
    expectedPage: Int,
    expectedPageSize: Int,
) {
    val offset = (page.toLong() - 1) * pageSize
    if (
        page != expectedPage ||
        pageSize != expectedPageSize ||
        totalCount < 0 ||
        itemCount > pageSize ||
        offset > totalCount ||
        offset + itemCount > totalCount ||
        (itemCount == 0 && offset < totalCount)
    ) {
        invalidResponse()
    }
}

private fun requireUuid(value: String): String =
    runCatching { UUID.fromString(value).toString() }
        .getOrNull()
        ?.takeIf { it == value.lowercase() }
        ?: invalidResponse()

private fun parseInstant(value: String): Instant = runCatching { Instant.parse(value) }.getOrNull() ?: invalidResponse()

private fun invalidResponse(): Nothing = throw KuraStorageException.InvalidServerResponse()
