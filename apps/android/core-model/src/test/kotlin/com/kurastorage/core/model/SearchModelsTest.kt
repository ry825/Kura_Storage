package com.kurastorage.core.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class SearchModelsTest {
    @Test
    fun `search input validates and normalizes the server contract`() {
        val normalized = checkNotNull(SearchInput(query = "  Caf\u00e9  ").validate().value)
        assertEquals("caf\u00e9", normalized.query)
        assertEquals(SearchMatchMode.CONTAINS, normalized.matchMode)

        val prefix = checkNotNull(SearchInput(query = "\ud83d\udcc1").validate().value)
        assertEquals(SearchMatchMode.PREFIX, prefix.matchMode)

        assertEquals(SearchValidationError.QUERY_REQUIRED, SearchInput().validate().error)
        assertEquals(SearchValidationError.INVALID_QUERY, SearchInput(query = " ").validate().error)
        assertEquals(
            SearchValidationError.INVALID_QUERY,
            SearchInput(query = "a".repeat(201)).validate().error,
        )
        assertEquals(
            SearchValidationError.INVALID_FILTER,
            SearchInput(entryType = FileEntryType.FOLDER, minSize = 1).validate().error,
        )
        assertEquals(
            SearchValidationError.INVALID_FILTER,
            SearchInput(updatedFrom = Instant.MAX, updatedTo = Instant.MIN).validate().error,
        )
        assertEquals(
            SearchValidationError.INVALID_FILTER,
            SearchInput(status = FileEntryStatus.TRASHED).validate().error,
        )
    }

    @Test
    fun `search and recent metadata reuse sharing models and fail closed`() {
        val item =
            SearchResultItem(
                id = "00000000-0000-4000-8000-000000000001",
                entryType = FileEntryType.FILE,
                name = "report.pdf",
                mimeType = "application/pdf",
                fileCategory = SearchFileCategory.DOCUMENT,
                size = 20,
                status = FileEntryStatus.ACTIVE,
                updatedAt = Instant.EPOCH,
                owner = OwnerSummary("00000000-0000-4000-8000-000000000002", "Owner"),
                permission = SharePermission.VIEWER,
                permissionSource = PermissionSource.DIRECT,
                shareTargetId = "00000000-0000-4000-8000-000000000003",
            )

        assertTrue(item.capabilities.canDownload)
        assertFalse(item.capabilities.canRename)
        assertFalse(item.copy(status = FileEntryStatus.MISSING).capabilities.canDownload)
        assertFalse(item.copy(permission = SharePermission.UNKNOWN).capabilities.canDownload)

        val page = SearchPage(listOf(item), page = 1, pageSize = 1, totalCount = 2)
        assertTrue(page.hasNextPage)
        assertFalse(page.copy(page = 2).hasNextPage)

        val recent = RecentFileItem(item, Instant.parse("2026-08-25T00:00:00Z"))
        assertEquals(item.id, recent.id)
        assertEquals(item.owner, recent.owner)
    }

    @Test
    fun `wire enum conversion keeps unknown values explicit`() {
        assertEquals(SearchFileCategory.IMAGE, SearchFileCategory.fromWire("IMAGE"))
        assertEquals(SearchFileCategory.UNKNOWN, SearchFileCategory.fromWire("FUTURE"))
        assertEquals(FileEntryType.UNKNOWN, FileEntryType.fromWire("FUTURE"))
        assertEquals(FileEntryStatus.UNKNOWN, FileEntryStatus.fromWire("FUTURE"))
    }
}
