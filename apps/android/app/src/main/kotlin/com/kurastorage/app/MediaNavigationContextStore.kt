package com.kurastorage.app

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.kurastorage.core.model.FileEntry
import java.util.UUID

class MediaNavigationContextStore {
    private val contexts = mutableMapOf<String, List<String>>()
    var requestedDetailsId: String? by mutableStateOf(null)
        private set

    @Synchronized
    fun register(entries: List<FileEntry>): String =
        UUID.randomUUID().toString().also { contextId ->
            contexts[contextId] = entries.map(FileEntry::id).distinct()
        }

    @Synchronized
    fun fileIds(contextId: String): List<String> = contexts[contextId].orEmpty()

    fun requestDetails(fileId: String) {
        requestedDetailsId = fileId
    }

    fun consumeDetails() {
        requestedDetailsId = null
    }

    @Synchronized
    fun clear() {
        contexts.clear()
        requestedDetailsId = null
    }
}
