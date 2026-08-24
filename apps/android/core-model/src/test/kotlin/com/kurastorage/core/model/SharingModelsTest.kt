@file:Suppress("CyclomaticComplexMethod", "MaxLineLength")

package com.kurastorage.core.model

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SharingModelsTest {
    @Test
    fun `permission capabilities match owner viewer contributor editor manager and unknown`() {
        val owner = filePermissionCapabilities(SharePermission.MANAGER, PermissionSource.OWNER)
        assertTrue(owner.canCreate && owner.canRename && owner.canManageShare && owner.canManageTrash)

        val viewer = filePermissionCapabilities(SharePermission.VIEWER, PermissionSource.DIRECT)
        assertTrue(viewer.canDownload)
        assertFalse(viewer.canCreate || viewer.canRename || viewer.canManageShare)

        val contributor = filePermissionCapabilities(SharePermission.CONTRIBUTOR, PermissionSource.INHERITED)
        assertTrue(contributor.canDownload && contributor.canCreate)
        assertFalse(contributor.canRename)

        val editor = filePermissionCapabilities(SharePermission.EDITOR, PermissionSource.DIRECT)
        assertTrue(editor.canCreate && editor.canRename && editor.canMove && editor.canTrash)
        assertFalse(editor.canManageShare || editor.canManageTrash)

        val manager = filePermissionCapabilities(SharePermission.MANAGER, PermissionSource.INHERITED)
        assertTrue(manager.canManageShare)
        assertFalse(manager.canManageTrash)

        val unknown = filePermissionCapabilities(SharePermission.UNKNOWN, PermissionSource.UNKNOWN)
        assertTrue(unknown.canDownload)
        assertFalse(unknown.canCreate || unknown.canRename || unknown.canMove || unknown.canTrash || unknown.canManageShare)

        val unknownPermission = filePermissionCapabilities(SharePermission.UNKNOWN, PermissionSource.OWNER)
        assertTrue(unknownPermission.canDownload)
        assertFalse(unknownPermission.canCreate || unknownPermission.canManageShare || unknownPermission.canManageTrash)

        val unknownSource = filePermissionCapabilities(SharePermission.MANAGER, PermissionSource.UNKNOWN)
        assertTrue(unknownSource.canDownload)
        assertFalse(unknownSource.canCreate || unknownSource.canRename || unknownSource.canTrash || unknownSource.canManageShare)
    }

    @Test
    fun `unknown wire enums map to explicit unknown`() {
        assertTrue(SharePermission.fromWire("VIEWER") == SharePermission.VIEWER)
        assertTrue(SharePermission.fromWire("CONTRIBUTOR") == SharePermission.CONTRIBUTOR)
        assertTrue(SharePermission.fromWire("EDITOR") == SharePermission.EDITOR)
        assertTrue(SharePermission.fromWire("MANAGER") == SharePermission.MANAGER)
        assertTrue(SharePermission.fromWire("FUTURE") == SharePermission.UNKNOWN)
        assertTrue(PermissionSource.fromWire("OWNER") == PermissionSource.OWNER)
        assertTrue(PermissionSource.fromWire("DIRECT") == PermissionSource.DIRECT)
        assertTrue(PermissionSource.fromWire("INHERITED") == PermissionSource.INHERITED)
        assertTrue(PermissionSource.fromWire(null) == PermissionSource.UNKNOWN)
    }
}
