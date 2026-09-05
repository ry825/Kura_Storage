package com.kurastorage.core.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class TextFileModelsTest {
    @Test
    fun `direct text routing and control character boundary are conservative`() {
        assertTrue(SupportedTextMimeTypes.isDirectlyOpenable("server.log", "application/octet-stream"))
        assertTrue(SupportedTextMimeTypes.isDirectlyOpenable("page.unknown", "text/html; charset=utf-8"))
        assertFalse(SupportedTextMimeTypes.isDirectlyOpenable("archive.bin", "application/octet-stream"))
        assertTrue(SupportedTextMimeTypes.isLikelyText("line one\nline two\tvalue"))
        assertFalse(SupportedTextMimeTypes.isLikelyText("text\u0000binary"))
        assertTrue(SupportedTextMimeTypes.isLikelyText("a".repeat(49) + "\u0001"))
        assertFalse(SupportedTextMimeTypes.isLikelyText("a".repeat(48) + "\u0001\u0002"))
    }

    @Test
    fun `supported text MIME types are exact and normalized`() {
        listOf(
            "text/plain",
            "text/markdown",
            "text/csv",
            "application/json",
            "application/xml",
            "application/yaml",
        ).forEach { assertTrue(SupportedTextMimeTypes.isSupported(" $it; charset=UTF-8")) }
        assertFalse(SupportedTextMimeTypes.isSupported("text/html"))
        assertFalse(SupportedTextMimeTypes.isSupported(null))
    }

    @Test
    fun `future change kind fails closed and history paging is bounded`() {
        assertEquals(FileVersionChangeKind.UNKNOWN, FileVersionChangeKind.fromWire("FUTURE"))
        val page =
            FileVersionPage(
                items = listOf(version(3)),
                page = 1,
                pageSize = 1,
                totalCount = 2,
            )
        assertTrue(page.hasNextPage)
        assertFalse(page.copy(page = 2).hasNextPage)
    }

    @Test
    fun `only trusted editor manager or owner can save and restore`() {
        assertFalse(canEditText(SharePermission.VIEWER, PermissionSource.DIRECT))
        assertFalse(canEditText(SharePermission.CONTRIBUTOR, PermissionSource.INHERITED))
        assertTrue(canEditText(SharePermission.EDITOR, PermissionSource.DIRECT))
        assertTrue(canEditText(SharePermission.MANAGER, PermissionSource.INHERITED))
        assertTrue(canEditText(SharePermission.EDITOR, PermissionSource.OWNER))
        assertFalse(canEditText(SharePermission.MANAGER, PermissionSource.UNKNOWN))
        assertFalse(canEditText(SharePermission.UNKNOWN, PermissionSource.OWNER))
    }

    @Test
    fun `size limit counts UTF-8 bytes instead of UTF-16 characters`() {
        val exactlyOneMiB = "😀".repeat(262_144)
        assertTrue(SupportedTextMimeTypes.isWithinSizeLimit(exactlyOneMiB))
        assertFalse(SupportedTextMimeTypes.isWithinSizeLimit("${exactlyOneMiB}a"))
    }

    private fun version(value: Long) =
        FileVersionItem(
            version = value,
            size = 4,
            sha256 = "a".repeat(64),
            changeKind = FileVersionChangeKind.TEXT_EDIT,
            actorDisplayName = "Ryo",
            createdAt = Instant.parse("2026-09-01T00:00:00Z"),
        )
}
