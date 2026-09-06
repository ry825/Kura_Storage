package com.kurastorage.feature.files

import androidx.lifecycle.SavedStateHandle
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.Instant

class BrowserScrollAnchorTest {
    @Test
    fun `stable id wins after insertion and missing id clamps saved index`() {
        val entries = listOf(entry("new"), entry("before"), entry("anchor"), entry("after"))

        val layoutIds = browserLayoutEntryIds(entries, false, BrowserDisplayMode.GRID)
        assertEquals(2, BrowserScrollAnchor("anchor", 1, 17).resolveIndex(layoutIds))
        assertEquals(3, BrowserScrollAnchor("removed", 99, 17).resolveIndex(layoutIds))
        assertEquals(0, BrowserScrollAnchor("removed", 99, 17).resolveIndex(emptyList()))
    }

    @Test
    fun `folder and display contexts survive saved state recreation independently`() {
        val handle = SavedStateHandle()
        val store = BrowserScrollAnchorStore(handle)
        val grid = browserScrollContextKey("folder", false, BrowserDisplayMode.GRID)
        val list = browserScrollContextKey("folder", false, BrowserDisplayMode.LIST)
        store.put(grid, BrowserScrollAnchor("grid-entry", 8, 12))
        store.put(list, BrowserScrollAnchor("list-entry", 3, 4))

        val restored = BrowserScrollAnchorStore(handle).snapshot()

        assertEquals(BrowserScrollAnchor("grid-entry", 8, 12), restored[grid])
        assertEquals(BrowserScrollAnchor("list-entry", 3, 4), restored[list])
    }

    @Test
    fun `list anchors account for section rows while grid anchors use entry order`() {
        val entries = listOf(entry("file"), entry("folder").copy(entryType = FileEntryType.FOLDER))

        val listIds = browserLayoutEntryIds(entries, false, BrowserDisplayMode.LIST)
        val gridIds = browserLayoutEntryIds(entries, false, BrowserDisplayMode.GRID)

        assertEquals(listOf(null, "folder", null, "file"), listIds)
        assertEquals(3, BrowserScrollAnchor("file", 0, 0).resolveIndex(listIds))
        assertEquals(0, BrowserScrollAnchor("file", 3, 0).resolveIndex(gridIds))
    }

    private fun entry(id: String) =
        FileEntry(
            id,
            "root",
            "$id.txt",
            FileEntryType.FILE,
            "text/plain",
            1,
            FileEntryStatus.ACTIVE,
            1,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
        )
}
