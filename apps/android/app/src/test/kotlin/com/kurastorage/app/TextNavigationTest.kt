package com.kurastorage.app

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.Instant

class TextNavigationTest {
    @Test
    fun `only active supported text files receive an editor route`() {
        assertEquals("text/editor/plain", textRoute(file("plain", "text/plain; charset=UTF-8")))
        assertEquals("text/editor/yaml", textRoute(file("yaml", "application/yaml")))
        assertNull(textRoute(file("html", "text/html")))
        assertNull(textRoute(file("missing", "text/plain").copy(status = FileEntryStatus.MISSING)))
        assertNull(textRoute(file("folder", "text/plain").copy(entryType = FileEntryType.FOLDER)))
    }

    @Test
    fun `a replacement session receives a distinct editor ViewModel key`() {
        assertEquals("text-editor-file-session-a", textEditorViewModelKey("file", "session-a"))
        assertNotEquals(textEditorViewModelKey("file", "session-a"), textEditorViewModelKey("file", "session-b"))
    }

    private fun file(
        id: String,
        mimeType: String,
    ) = FileEntry(
        id = id,
        parentId = "root",
        name = "$id.txt",
        entryType = FileEntryType.FILE,
        mimeType = mimeType,
        size = 1,
        status = FileEntryStatus.ACTIVE,
        fileVersion = 1,
        trashedAt = null,
        createdAt = Instant.EPOCH,
        updatedAt = Instant.EPOCH,
    )
}
