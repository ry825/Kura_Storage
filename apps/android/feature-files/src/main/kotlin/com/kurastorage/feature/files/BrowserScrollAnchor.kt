package com.kurastorage.feature.files

import androidx.lifecycle.SavedStateHandle
import com.kurastorage.core.model.FileEntry

data class BrowserScrollAnchor(
    val entryId: String?,
    val index: Int,
    val offset: Int,
) {
    init {
        require(index >= 0)
        require(offset >= 0)
    }

    fun resolveIndex(layoutEntryIds: List<String?>): Int {
        if (layoutEntryIds.isEmpty()) return 0
        val stableIndex = entryId?.let { id -> layoutEntryIds.indexOfFirst { it == id }.takeIf { it >= 0 } }
        return stableIndex ?: index.coerceIn(layoutEntryIds.indices)
    }
}

internal class BrowserScrollAnchorStore(
    private val savedStateHandle: SavedStateHandle?,
) {
    private val anchors = restore()

    fun snapshot(): Map<String, BrowserScrollAnchor> = anchors.toMap()

    fun put(
        contextKey: String,
        anchor: BrowserScrollAnchor,
    ): Map<String, BrowserScrollAnchor> {
        anchors[contextKey] = anchor
        persist()
        return snapshot()
    }

    private fun restore(): LinkedHashMap<String, BrowserScrollAnchor> {
        val keys: List<String> = savedStateHandle?.get<ArrayList<String>>(KEYS) ?: emptyList()
        val ids: List<String> = savedStateHandle?.get<ArrayList<String>>(IDS) ?: emptyList()
        val indices: IntArray = savedStateHandle?.get<IntArray>(INDICES) ?: intArrayOf()
        val offsets: IntArray = savedStateHandle?.get<IntArray>(OFFSETS) ?: intArrayOf()
        val restored = linkedMapOf<String, BrowserScrollAnchor>()
        keys.indices.forEach { index ->
            if (index < ids.size && index < indices.size && index < offsets.size) {
                restored[keys[index]] =
                    BrowserScrollAnchor(
                        ids[index].takeIf(String::isNotEmpty),
                        indices[index].coerceAtLeast(0),
                        offsets[index].coerceAtLeast(0),
                    )
            }
        }
        return restored
    }

    private fun persist() {
        savedStateHandle?.set(KEYS, ArrayList(anchors.keys))
        savedStateHandle?.set(IDS, ArrayList(anchors.values.map { it.entryId.orEmpty() }))
        savedStateHandle?.set(INDICES, anchors.values.map { it.index }.toIntArray())
        savedStateHandle?.set(OFFSETS, anchors.values.map { it.offset }.toIntArray())
    }

    private companion object {
        const val KEYS = "file_browser_scroll_keys"
        const val IDS = "file_browser_scroll_ids"
        const val INDICES = "file_browser_scroll_indices"
        const val OFFSETS = "file_browser_scroll_offsets"
    }
}

internal fun browserScrollContextKey(
    folderId: String?,
    trashMode: Boolean,
    displayMode: BrowserDisplayMode,
): String = "${if (trashMode) "trash" else "files"}|${displayMode.name}|${folderId ?: "root"}"

internal fun browserLayoutEntryIds(
    entries: List<FileEntry>,
    trashMode: Boolean,
    displayMode: BrowserDisplayMode,
): List<String?> {
    if (displayMode == BrowserDisplayMode.GRID && !trashMode) return entries.map { it.id }
    val folders = entries.filter { it.entryType == com.kurastorage.core.model.FileEntryType.FOLDER }
    val files = entries.filter { it.entryType == com.kurastorage.core.model.FileEntryType.FILE }
    return buildList {
        if (folders.isNotEmpty()) {
            add(null)
            addAll(folders.map { it.id })
        }
        if (files.isNotEmpty()) {
            add(null)
            addAll(files.map { it.id })
        }
    }
}
