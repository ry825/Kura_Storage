package com.kurastorage.app

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SupportedTextMimeTypes
import com.kurastorage.core.model.media.SupportedMediaMimeTypes
import com.kurastorage.core.ui.AppDestination

internal enum class EntryDestination {
    FOLDER,
    PHOTO,
    VIDEO,
    AUDIO,
    PDF,
    TEXT,
    DETAILS,
}

internal data class EntryNavigationCandidate(
    val id: String,
    val entryType: FileEntryType,
    val status: FileEntryStatus,
    val mimeType: String?,
)

internal object EntryDestinationResolver {
    fun resolve(entry: FileEntry): EntryDestination = resolve(entry.entryType, entry.status, entry.mimeType)

    fun resolve(entry: SearchResultItem): EntryDestination = resolve(entry.entryType, entry.status, entry.mimeType)

    fun resolve(candidate: EntryNavigationCandidate): EntryDestination =
        resolve(
            candidate.entryType,
            candidate.status,
            candidate.mimeType,
        )

    private fun resolve(
        entryType: FileEntryType,
        status: FileEntryStatus,
        mimeType: String?,
    ): EntryDestination =
        when {
            status != FileEntryStatus.ACTIVE || entryType == FileEntryType.UNKNOWN -> EntryDestination.DETAILS
            entryType == FileEntryType.FOLDER -> EntryDestination.FOLDER
            entryType != FileEntryType.FILE -> EntryDestination.DETAILS
            SupportedMediaMimeTypes.isPhoto(mimeType) -> EntryDestination.PHOTO
            SupportedMediaMimeTypes.isVideo(mimeType) -> EntryDestination.VIDEO
            SupportedMediaMimeTypes.isAudio(mimeType) -> EntryDestination.AUDIO
            SupportedMediaMimeTypes.isPdf(mimeType) -> EntryDestination.PDF
            SupportedTextMimeTypes.isSupported(mimeType) -> EntryDestination.TEXT
            else -> EntryDestination.DETAILS
        }
}

internal fun directEntryRoute(
    entry: FileEntry,
    candidates: List<EntryNavigationCandidate>,
    contexts: MediaNavigationContextStore,
): String? =
    when (val destination = EntryDestinationResolver.resolve(entry)) {
        EntryDestination.PHOTO,
        EntryDestination.VIDEO,
        EntryDestination.AUDIO,
        -> {
            val orderedIds =
                candidates
                    .filter { EntryDestinationResolver.resolve(it) == destination }
                    .map(EntryNavigationCandidate::id)
                    .let { ids -> if (entry.id in ids) ids else ids + entry.id }
            val contextId = contexts.registerIds(orderedIds)
            val route =
                when (destination) {
                    EntryDestination.PHOTO -> AppDestination.PHOTO_VIEWER.route
                    EntryDestination.VIDEO -> AppDestination.VIDEO_PLAYER.route
                    EntryDestination.AUDIO -> AppDestination.AUDIO_PLAYER.route
                    EntryDestination.FOLDER,
                    EntryDestination.PDF,
                    EntryDestination.TEXT,
                    EntryDestination.DETAILS,
                    -> error("Unsupported media destination")
                }
            "$route/$contextId/${entry.id}"
        }
        EntryDestination.PDF -> "${AppDestination.PDF_VIEWER.route}/${entry.id}"
        EntryDestination.TEXT -> "${AppDestination.TEXT_EDITOR.route}/${entry.id}"
        EntryDestination.FOLDER,
        EntryDestination.DETAILS,
        -> null
    }

internal fun favoriteEntryRoute(
    entry: FileEntry,
    favorites: List<SearchResultItem>,
    contexts: MediaNavigationContextStore,
): String =
    directEntryRoute(entry, favorites.map(SearchResultItem::navigationCandidate), contexts)
        ?: entryRoute(entry.id, entry.entryType)

internal fun FileEntry.navigationCandidate() = EntryNavigationCandidate(id, entryType, status, mimeType)

internal fun SearchResultItem.navigationCandidate() = EntryNavigationCandidate(id, entryType, status, mimeType)
