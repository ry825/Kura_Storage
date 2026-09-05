package com.kurastorage.app

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class EntryDestinationResolverTest {
    @Test
    fun `resolver classifies every supported destination and fails closed`() {
        assertEquals(
            EntryDestination.FOLDER,
            EntryDestinationResolver.resolve(file("folder", FileEntryType.FOLDER, null)),
        )
        assertEquals(EntryDestination.PHOTO, EntryDestinationResolver.resolve(file("photo", mime = "image/jpeg")))
        assertEquals(EntryDestination.VIDEO, EntryDestinationResolver.resolve(file("video", mime = "video/mp4")))
        assertEquals(EntryDestination.AUDIO, EntryDestinationResolver.resolve(file("audio", mime = "audio/mpeg")))
        assertEquals(EntryDestination.PDF, EntryDestinationResolver.resolve(file("pdf", mime = "application/pdf")))
        assertEquals(
            EntryDestination.TEXT,
            EntryDestinationResolver.resolve(file("text", mime = "text/plain; charset=utf-8")),
        )
        assertEquals(
            EntryDestination.DETAILS,
            EntryDestinationResolver.resolve(file("binary", mime = "application/octet-stream")),
        )
        assertEquals(
            EntryDestination.DETAILS,
            EntryDestinationResolver.resolve(
                file("missing", mime = "image/jpeg").copy(status = FileEntryStatus.MISSING),
            ),
        )
    }

    @Test
    fun `favorite photo route registers only active photos in visible order`() {
        val contexts = MediaNavigationContextStore()
        val selected = file("photo-2", mime = "image/jpeg")
        val favorites =
            listOf(
                metadata("video", "video/mp4"),
                metadata("photo-1", "image/webp"),
                metadata("missing-photo", "image/jpeg", FileEntryStatus.MISSING),
                metadata("photo-2", "image/jpeg"),
                metadata("audio", "audio/mpeg"),
            )

        val route = favoriteEntryRoute(selected, favorites, contexts)
        val contextId = route.split('/')[2]

        assertTrue(route.startsWith("media/photo/"))
        assertTrue(route.endsWith("/photo-2"))
        assertEquals(listOf("photo-1", "photo-2"), contexts.fileIds(contextId))
    }

    @Test
    fun `favorite video and audio routes preserve their own visible order`() {
        val contexts = MediaNavigationContextStore()
        val favorites =
            listOf(
                metadata("audio-1", "audio/ogg"),
                metadata("video-1", "video/mp4"),
                metadata("audio-2", "audio/mpeg"),
                metadata("video-2", "video/webm"),
            )

        val videoRoute = favoriteEntryRoute(file("video-2", mime = "video/webm"), favorites, contexts)
        val audioRoute = favoriteEntryRoute(file("audio-2", mime = "audio/mpeg"), favorites, contexts)

        assertEquals(listOf("video-1", "video-2"), contexts.fileIds(videoRoute.split('/')[2]))
        assertEquals(listOf("audio-1", "audio-2"), contexts.fileIds(audioRoute.split('/')[2]))
    }

    @Test
    fun `favorite destinations use direct viewers and details fallback`() {
        val contexts = MediaNavigationContextStore()

        assertTrue(
            favoriteEntryRoute(file("video", mime = "video/mp4"), emptyList(), contexts)
                .startsWith("media/video/"),
        )
        assertTrue(
            favoriteEntryRoute(file("audio", mime = "audio/mpeg"), emptyList(), contexts)
                .startsWith("media/audio/"),
        )
        assertEquals(
            "media/pdf/pdf",
            favoriteEntryRoute(file("pdf", mime = "application/pdf"), emptyList(), contexts),
        )
        assertEquals("text/editor/text", favoriteEntryRoute(file("text", mime = "text/plain"), emptyList(), contexts))
        assertEquals(
            "shared-entry/folder/FOLDER",
            favoriteEntryRoute(file("folder", FileEntryType.FOLDER), emptyList(), contexts),
        )
        assertEquals(
            "shared-entry/binary/FILE",
            favoriteEntryRoute(file("binary", mime = "application/octet-stream"), emptyList(), contexts),
        )
    }

    @Test
    fun `context loss and lifecycle clear safely fall back to an empty context`() {
        val contexts = MediaNavigationContextStore()
        val contextId = contexts.registerIds(listOf("first", "second"))

        assertTrue(contexts.fileIds("lost-after-process-death").isEmpty())
        contexts.clear()
        assertTrue(contexts.fileIds(contextId).isEmpty())
    }

    private fun file(
        id: String,
        type: FileEntryType = FileEntryType.FILE,
        mime: String? = null,
    ) = FileEntry(
        id = id,
        parentId = null,
        name = id,
        entryType = type,
        mimeType = mime,
        size = 1,
        status = FileEntryStatus.ACTIVE,
        fileVersion = 1,
        trashedAt = null,
        createdAt = NOW,
        updatedAt = NOW,
    )

    private fun metadata(
        id: String,
        mime: String,
        status: FileEntryStatus = FileEntryStatus.ACTIVE,
    ) = SearchResultItem(
        id,
        FileEntryType.FILE,
        id,
        mime,
        SearchFileCategory.OTHER,
        1,
        status,
        NOW,
        OwnerSummary("owner", "Owner"),
        SharePermission.MANAGER,
        PermissionSource.OWNER,
        null,
    )

    private companion object {
        val NOW: Instant = Instant.parse("2026-09-05T00:00:00Z")
    }
}
