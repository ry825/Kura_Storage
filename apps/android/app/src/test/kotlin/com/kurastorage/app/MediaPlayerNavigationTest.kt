package com.kurastorage.app

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class MediaPlayerNavigationTest {
    @Test
    fun `supported video and audio route to their player without putting names or MIME in route`() {
        val contexts = MediaNavigationContextStore()
        val video = file("video-id", "private clip.mp4", "video/mp4")
        val audio = file("audio-id", "private track.mp3", "audio/mpeg")

        val videoRoute = checkNotNull(mediaRoute(video, listOf(video, audio), contexts))
        val audioRoute = checkNotNull(mediaRoute(audio, listOf(video, audio), contexts))

        assertTrue(videoRoute.startsWith("media/video/"))
        assertTrue(audioRoute.startsWith("media/audio/"))
        assertTrue(videoRoute.endsWith("/video-id"))
        assertTrue(audioRoute.endsWith("/audio-id"))
        assertTrue("private" !in videoRoute && "video/mp4" !in videoRoute)
    }

    @Test
    fun `unsupported and inactive files do not route to a player`() {
        val contexts = MediaNavigationContextStore()
        assertNull(mediaRoute(file("text", "note.txt", "text/plain"), emptyList(), contexts))
        assertNull(
            mediaRoute(
                file("missing", "clip.mp4", "video/mp4").copy(status = FileEntryStatus.MISSING),
                emptyList(),
                contexts,
            ),
        )
    }

    private fun file(
        id: String,
        name: String,
        mime: String,
    ) = FileEntry(
        id,
        null,
        name,
        FileEntryType.FILE,
        mime,
        100,
        FileEntryStatus.ACTIVE,
        1,
        null,
        Instant.EPOCH,
        Instant.EPOCH,
    )
}
