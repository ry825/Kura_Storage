package com.kurastorage.app

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.kurastorage.core.model.FileEntry
import java.util.UUID

class MediaNavigationContextStore {
    private data class Context(
        val sourceDestinationKey: String,
        val scopeId: String,
        val initialFileId: String,
        val orderedFileIds: List<String>,
        var currentFileId: String,
    )

    data class ReturnTarget(
        val sourceDestinationKey: String,
        val scopeId: String,
        val fileId: String,
        val openDetails: Boolean,
    )

    private val contexts = mutableMapOf<String, Context>()
    var requestedDetailsId: String? by mutableStateOf(null)
        private set
    private var requestedReturnTarget: ReturnTarget? by mutableStateOf(null)
        private set

    @Synchronized
    fun register(
        entries: List<FileEntry>,
        sourceDestinationKey: String,
        scopeId: String,
    ): String = registerIds(entries.map(FileEntry::id), sourceDestinationKey, scopeId)

    @Synchronized
    fun registerIds(
        fileIds: List<String>,
        sourceDestinationKey: String = "test-source",
        scopeId: String = "test-scope",
        initialFileId: String? = null,
    ): String =
        UUID.randomUUID().toString().also { contextId ->
            val distinct = fileIds.distinct()
            if (distinct.isNotEmpty()) {
                val initial = initialFileId?.takeIf { it in distinct } ?: distinct.first()
                contexts[contextId] = Context(sourceDestinationKey, scopeId, initial, distinct, initial)
            }
        }

    @Synchronized
    fun fileIds(contextId: String): List<String> = contexts[contextId]?.orderedFileIds.orEmpty()

    @Synchronized
    fun updateCurrent(
        contextId: String,
        fileId: String,
    ) {
        contexts[contextId]?.takeIf { fileId in it.orderedFileIds }?.currentFileId = fileId
    }

    @Synchronized
    fun requestReturn(
        contextId: String,
        openDetails: Boolean = false,
    ) {
        val context = contexts[contextId] ?: return
        requestedReturnTarget =
            ReturnTarget(
                context.sourceDestinationKey,
                context.scopeId,
                context.currentFileId,
                openDetails,
            )
        requestedDetailsId = context.currentFileId.takeIf { openDetails }
    }

    @Synchronized
    fun returnTarget(
        sourceDestinationKey: String,
        scopeId: String,
    ): ReturnTarget? =
        requestedReturnTarget?.takeIf {
            it.sourceDestinationKey == sourceDestinationKey && it.scopeId == scopeId
        }

    @Synchronized
    fun consumeReturn(
        sourceDestinationKey: String,
        scopeId: String,
    ) {
        if (returnTarget(sourceDestinationKey, scopeId) != null) requestedReturnTarget = null
    }

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
        requestedReturnTarget = null
    }
}
