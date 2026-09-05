package com.kurastorage.app

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.SupportedTextMimeTypes
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class TextNavigationTest {
    @Test
    fun `active known text files route directly and unknown files require explicit override`() {
        assertEquals("text/editor/plain", textRoute(file("plain", "text/plain; charset=UTF-8")))
        assertEquals("text/editor/yaml", textRoute(file("yaml", "application/yaml")))
        assertEquals("text/editor/html", textRoute(file("html", "text/html")))
        val unknown = file("opaque", "application/octet-stream").copy(name = "opaque.bin")
        assertNull(textRoute(unknown))
        assertEquals("text/editor/opaque?allowUnsafe=true", textRoute(unknown, allowUnknown = true))
        assertNull(textRoute(file("missing", "text/plain").copy(status = FileEntryStatus.MISSING)))
        assertNull(textRoute(file("folder", "text/plain").copy(entryType = FileEntryType.FOLDER)))
        assertNull(textRoute(file("large", "text/plain").copy(size = SupportedTextMimeTypes.MAX_CONTENT_BYTES + 1L)))
    }

    @Test
    fun `a replacement session receives a distinct editor ViewModel key`() {
        assertEquals("text-editor-file-session-a", textEditorViewModelKey("file", "session-a"))
        assertNotEquals(textEditorViewModelKey("file", "session-a"), textEditorViewModelKey("file", "session-b"))
    }

    @Test
    fun `primary entry dispatch never sends media through the explicit text override`() {
        val pdf = file("pdf", "application/pdf").copy(name = "document.pdf")
        var mediaOpens = 0
        var textOpens = 0
        var fallbacks = 0

        dispatchPrimaryEntry(
            entry = pdf,
            entries = listOf(pdf),
            onOpenMedia = { _, _ ->
                mediaOpens += 1
                true
            },
            onOpenText = {
                textOpens += 1
                true
            },
            onFallback = { fallbacks += 1 },
        )

        assertEquals(1, mediaOpens)
        assertEquals(0, textOpens)
        assertEquals(0, fallbacks)
    }

    @Test
    fun `primary entry dispatch sends unknown files to details instead of text override`() {
        val binary = file("binary", "application/octet-stream").copy(name = "binary.bin")
        var textOpens = 0
        var fallback: FileEntry? = null

        dispatchPrimaryEntry(
            entry = binary,
            entries = listOf(binary),
            onOpenMedia = { _, _ -> false },
            onOpenText = {
                textOpens += 1
                true
            },
            onFallback = { fallback = it },
        )

        assertEquals(0, textOpens)
        assertEquals(binary, fallback)
    }

    @Test
    fun `top app bar and system back use the same folder-first behavior`() {
        var topExit = false
        var systemExit = false

        handleFileBack(onFolderBack = { true }, onExit = { topExit = true })
        handleFileBack(onFolderBack = { true }, onExit = { systemExit = true })

        assertFalse(topExit)
        assertFalse(systemExit)

        handleFileBack(onFolderBack = { false }, onExit = { topExit = true })
        handleFileBack(onFolderBack = { false }, onExit = { systemExit = true })

        assertTrue(topExit)
        assertTrue(systemExit)
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
