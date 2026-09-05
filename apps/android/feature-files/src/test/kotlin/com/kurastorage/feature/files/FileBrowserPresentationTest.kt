package com.kurastorage.feature.files

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class FileBrowserPresentationTest {
    @Test
    fun filesDefaultToGridWhileTrashDefaultsToList() {
        assertEquals(BrowserDisplayMode.GRID, defaultBrowserDisplayMode(trashMode = false))
        assertEquals(BrowserDisplayMode.LIST, defaultBrowserDisplayMode(trashMode = true))
    }

    @Test
    fun personalOwnerMetadataOmitsObviousOwnerAndPermission() {
        val metadata = entryMetadata(file(), personalRoot = true, sharedFrom = null)

        assertTrue(metadata.primary.startsWith("Text file • 1 KB"))
        assertTrue(metadata.secondary.isEmpty())
    }

    @Test
    fun sharedMetadataUsesFriendlyPermissionAndSource() {
        val metadata =
            entryMetadata(
                file(permission = SharePermission.VIEWER, source = PermissionSource.INHERITED),
                personalRoot = false,
                sharedFrom = "Family photos",
            )

        assertEquals(listOf("Shared from: Family photos", "Read only"), metadata.secondary)
    }

    @Test
    fun rawPermissionsMapToUserFacingLabels() {
        assertEquals("Read only", permissionLabel(SharePermission.VIEWER))
        assertEquals("Can edit", permissionLabel(SharePermission.EDITOR))
        assertEquals("Can manage", permissionLabel(SharePermission.MANAGER))
        assertEquals("Unavailable", permissionLabel(SharePermission.UNKNOWN))
    }

    private fun file(
        permission: SharePermission = SharePermission.MANAGER,
        source: PermissionSource = PermissionSource.OWNER,
    ) = FileEntry(
        id = "file-1",
        parentId = null,
        name = "notes.txt",
        entryType = FileEntryType.FILE,
        mimeType = "text/plain",
        size = 1024,
        status = FileEntryStatus.ACTIVE,
        fileVersion = 1,
        trashedAt = null,
        createdAt = Instant.parse("2026-09-05T00:00:00Z"),
        updatedAt = Instant.parse("2026-09-05T00:00:00Z"),
        owner = OwnerSummary("owner-1", "Taylor"),
        permission = permission,
        permissionSource = source,
    )
}
