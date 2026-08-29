package com.kurastorage.core.model

import java.text.Normalizer
import java.time.Instant
import java.util.Locale
import java.util.UUID

enum class SearchFileCategory {
    IMAGE,
    VIDEO,
    AUDIO,
    DOCUMENT,
    ARCHIVE,
    OTHER,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String?): SearchFileCategory = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class SearchMatchMode { NONE, PREFIX, CONTAINS }

enum class SearchValidationError { QUERY_REQUIRED, INVALID_QUERY, INVALID_FILTER }

data class SearchInput(
    val query: String? = null,
    val entryType: FileEntryType? = null,
    val fileCategory: SearchFileCategory? = null,
    val status: FileEntryStatus? = null,
    val updatedFrom: Instant? = null,
    val updatedTo: Instant? = null,
    val minSize: Long? = null,
    val maxSize: Long? = null,
    val ownerUserId: String? = null,
    val shareTargetId: String? = null,
    val tagIds: List<String> = emptyList(),
    val page: Int = 1,
    val pageSize: Int = DEFAULT_PAGE_SIZE,
) {
    @Suppress("ComplexCondition", "CyclomaticComplexMethod", "LongMethod", "ReturnCount")
    fun validate(): SearchInputValidation {
        val trimmed = query?.trim()
        if (trimmed != null && trimmed.isEmpty()) {
            return SearchInputValidation(error = SearchValidationError.INVALID_QUERY)
        }
        val normalized = trimmed?.let { Normalizer.normalize(it, Normalizer.Form.NFC).lowercase(Locale.ROOT) }
        val codePoints = normalized?.codePointCount(0, normalized.length) ?: 0
        if (codePoints > MAXIMUM_QUERY_CODE_POINTS) {
            return SearchInputValidation(error = SearchValidationError.INVALID_QUERY)
        }
        val invalidEnum =
            entryType == FileEntryType.UNKNOWN ||
                fileCategory == SearchFileCategory.UNKNOWN ||
                status !in VALID_SEARCH_STATUSES &&
                status != null
        val invalidRange =
            updatedFrom?.let { from -> updatedTo?.let(from::isAfter) } == true ||
                minSize?.let { it < 0 } == true ||
                maxSize?.let { it < 0 } == true ||
                minSize?.let { minimum -> maxSize?.let { maximum -> minimum > maximum } } == true
        val invalidFolderFilter =
            entryType == FileEntryType.FOLDER &&
                (fileCategory != null || minSize != null || maxSize != null)
        val invalidId =
            !validUuid(ownerUserId) ||
                !validUuid(shareTargetId) ||
                tagIds.any { !isCanonicalUuid(it) } ||
                tagIds.size > MAXIMUM_SEARCH_TAGS ||
                tagIds.distinct().size != tagIds.size
        val invalidPage =
            page < 1 ||
                pageSize !in 1..MAXIMUM_PAGE_SIZE ||
                (page.toLong() - 1) * pageSize > Int.MAX_VALUE
        if (invalidEnum || invalidRange || invalidFolderFilter || invalidId || invalidPage) {
            return SearchInputValidation(error = SearchValidationError.INVALID_FILTER)
        }
        val hasFilter =
            entryType != null ||
                fileCategory != null ||
                status != null ||
                updatedFrom != null ||
                updatedTo != null ||
                minSize != null ||
                maxSize != null ||
                ownerUserId != null ||
                shareTargetId != null ||
                tagIds.isNotEmpty()
        if (normalized == null && !hasFilter) {
            return SearchInputValidation(error = SearchValidationError.QUERY_REQUIRED)
        }
        return SearchInputValidation(
            value =
                ValidatedSearchInput(
                    query = normalized,
                    matchMode =
                        when (codePoints) {
                            0 -> SearchMatchMode.NONE
                            1, 2 -> SearchMatchMode.PREFIX
                            else -> SearchMatchMode.CONTAINS
                        },
                    entryType = entryType,
                    fileCategory = fileCategory,
                    status = status,
                    updatedFrom = updatedFrom,
                    updatedTo = updatedTo,
                    minSize = minSize,
                    maxSize = maxSize,
                    ownerUserId = ownerUserId,
                    shareTargetId = shareTargetId,
                    tagIds = tagIds,
                    page = page,
                    pageSize = pageSize,
                ),
        )
    }

    private fun validUuid(value: String?): Boolean =
        value == null || runCatching { UUID.fromString(value) }.getOrNull()?.toString() == value.lowercase(Locale.ROOT)

    companion object {
        const val DEFAULT_PAGE_SIZE = 50
        const val MAXIMUM_PAGE_SIZE = 100
        const val MAXIMUM_QUERY_CODE_POINTS = 200
        private val VALID_SEARCH_STATUSES =
            setOf(FileEntryStatus.ACTIVE, FileEntryStatus.MISSING_CANDIDATE, FileEntryStatus.MISSING)
    }
}

data class SearchInputValidation(
    val value: ValidatedSearchInput? = null,
    val error: SearchValidationError? = null,
)

data class ValidatedSearchInput(
    val query: String?,
    val matchMode: SearchMatchMode,
    val entryType: FileEntryType?,
    val fileCategory: SearchFileCategory?,
    val status: FileEntryStatus?,
    val updatedFrom: Instant?,
    val updatedTo: Instant?,
    val minSize: Long?,
    val maxSize: Long?,
    val ownerUserId: String?,
    val shareTargetId: String?,
    val tagIds: List<String>,
    val page: Int,
    val pageSize: Int,
)

data class SearchResultItem(
    val id: String,
    val entryType: FileEntryType,
    val name: String,
    val mimeType: String?,
    val fileCategory: SearchFileCategory?,
    val size: Long,
    val status: FileEntryStatus,
    val updatedAt: Instant,
    val owner: OwnerSummary,
    val permission: SharePermission,
    val permissionSource: PermissionSource,
    val shareTargetId: String?,
) {
    val capabilities: FilePermissionCapabilities
        get() {
            val base = filePermissionCapabilities(permission, permissionSource)
            val readable =
                status == FileEntryStatus.ACTIVE &&
                    permission != SharePermission.UNKNOWN &&
                    permissionSource != PermissionSource.UNKNOWN &&
                    entryType != FileEntryType.UNKNOWN
            return if (readable) {
                base
            } else {
                base.copy(
                    canDownload = false,
                    canCreate = false,
                    canRename = false,
                    canMove = false,
                    canTrash = false,
                    canManageShare = false,
                    canManageTrash = false,
                )
            }
        }
}

data class SearchPage(
    val items: List<SearchResultItem>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}

data class RecentFileItem(
    val metadata: SearchResultItem,
    val openedAt: Instant,
) {
    val id: String get() = metadata.id
    val owner: OwnerSummary get() = metadata.owner
}

data class RecentFilePage(
    val items: List<RecentFileItem>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}
