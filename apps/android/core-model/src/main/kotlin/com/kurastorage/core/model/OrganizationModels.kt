package com.kurastorage.core.model

import java.text.Normalizer
import java.time.Instant
import java.util.UUID

data class TagItem(
    val id: String,
    val name: String,
)

data class FavoriteItem(
    val metadata: SearchResultItem,
    val favoritedAt: Instant,
) {
    val id: String get() = metadata.id
}

data class FavoritePage(
    val items: List<FavoriteItem>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}

data class EntryOrganizationState(
    val isFavorite: Boolean,
    val tags: List<TagItem>,
)

enum class TagNameValidationError { EMPTY, TOO_LONG, CONTROL_CHARACTER }

data class ValidatedTagName(
    val value: String,
)

data class TagNameValidation(
    val value: ValidatedTagName? = null,
    val error: TagNameValidationError? = null,
)

fun validateTagName(input: String): TagNameValidation {
    val normalized = Normalizer.normalize(input.trim(), Normalizer.Form.NFC)
    val error =
        when {
            normalized.isEmpty() -> TagNameValidationError.EMPTY
            normalized.codePointCount(0, normalized.length) > MAXIMUM_TAG_NAME_CODE_POINTS ->
                TagNameValidationError.TOO_LONG
            normalized.codePoints().anyMatch(Character::isISOControl) ->
                TagNameValidationError.CONTROL_CHARACTER
            else -> null
        }
    return if (error == null) {
        TagNameValidation(ValidatedTagName(normalized))
    } else {
        TagNameValidation(error = error)
    }
}

fun isCanonicalUuid(value: String): Boolean {
    val parsed = runCatching { UUID.fromString(value).toString() }.getOrNull()
    return parsed == value.lowercase()
}

const val MAXIMUM_TAG_NAME_CODE_POINTS = 50
const val MAXIMUM_TAGS_PER_USER = 200
const val MAXIMUM_TAGS_PER_ENTRY = 20
const val MAXIMUM_SEARCH_TAGS = 10
