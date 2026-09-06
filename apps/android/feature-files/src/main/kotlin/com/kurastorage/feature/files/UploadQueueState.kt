package com.kurastorage.feature.files

import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.model.UploadState

enum class TransferDisplayState {
    ACTIVE,
    NEEDS_ATTENTION,
    COMPLETED_NOTICE,
    IDLE,
}

data class UploadCompletionNotice(
    val completedCount: Int,
)

data class UploadQueueItem(
    val id: String,
    val targetName: String,
    val operationId: String,
    val operation: UploadOperation,
    val transferredBytes: Long = operation.confirmedOffset,
    val totalBytes: Long = operation.size,
    val canRetry: Boolean = false,
    val message: String? = null,
    val error: Throwable? = null,
)

data class UploadQueueState(
    val items: Map<String, UploadQueueItem> = emptyMap(),
    val completionNotice: UploadCompletionNotice? = null,
) {
    val displayState: TransferDisplayState
        get() =
            when {
                items.values.any(UploadQueueItem::needsAttention) -> TransferDisplayState.NEEDS_ATTENTION
                items.values.any(UploadQueueItem::isActive) -> TransferDisplayState.ACTIVE
                completionNotice != null -> TransferDisplayState.COMPLETED_NOTICE
                else -> TransferDisplayState.IDLE
            }

    val retryableItems: List<UploadQueueItem>
        get() = items.values.filter { it.needsAttention() && it.canRetry }

    fun enqueue(
        itemId: String,
        operation: UploadOperation,
    ): UploadQueueState {
        require(itemId.isNotBlank())
        require(itemId !in items) { "Duplicate upload queue item ID" }
        val updated = LinkedHashMap(items)
        updated[itemId] =
            UploadQueueItem(
                id = itemId,
                targetName = operation.fileName,
                operationId = operation.idempotencyKey,
                operation = operation,
            )
        return copy(items = updated, completionNotice = null)
    }

    @Suppress("ReturnCount")
    fun applyEvent(
        itemId: String,
        event: TransferEvent,
    ): UploadQueueState {
        val current = items[itemId] ?: return this
        if (event is TransferEvent.UploadStatus && event.operation.state == UploadState.CANCELLED) {
            return remove(itemId)
        }
        val updatedItem =
            when (event) {
                is TransferEvent.Progress ->
                    current.copy(
                        transferredBytes = event.transferredBytes.coerceIn(0, event.totalBytes ?: current.totalBytes),
                        totalBytes = event.totalBytes ?: current.totalBytes,
                    )
                is TransferEvent.UploadStatus ->
                    current.copy(
                        operation = event.operation,
                        transferredBytes = event.operation.confirmedOffset,
                        totalBytes = event.operation.size,
                        canRetry = event.canRetry,
                        message = event.message,
                        error = null,
                    )
                is TransferEvent.UploadCompleted ->
                    current.copy(
                        operation = current.operation.copy(state = UploadState.COMPLETED),
                        transferredBytes = current.totalBytes,
                        canRetry = false,
                        message = "Upload completed",
                        error = null,
                    )
                is TransferEvent.Failed ->
                    current.copy(
                        operation = current.operation.copy(state = UploadState.FAILED),
                        canRetry = true,
                        message = event.error.message,
                        error = event.error,
                    )
                is TransferEvent.DownloadCompleted -> current
            }
        val updated = LinkedHashMap(items)
        updated[itemId] = updatedItem
        if (updated.isNotEmpty() && updated.values.all { it.operation.state == UploadState.COMPLETED }) {
            return UploadQueueState(
                completionNotice = UploadCompletionNotice(updated.size),
            )
        }
        return copy(items = updated)
    }

    fun retry(itemId: String): UploadQueueState {
        val current = items[itemId]?.takeIf { it.canRetry && it.needsAttention() } ?: return this
        val updated = LinkedHashMap(items)
        updated[itemId] =
            current.copy(
                operation = current.operation.copy(state = UploadState.PREPARING),
                transferredBytes = current.operation.confirmedOffset,
                canRetry = false,
                message = null,
                error = null,
            )
        return copy(items = updated, completionNotice = null)
    }

    fun reject(
        itemId: String,
        message: String,
    ): UploadQueueState {
        val current = items[itemId] ?: return this
        val updated = LinkedHashMap(items)
        updated[itemId] =
            current.copy(
                operation = current.operation.copy(state = UploadState.FAILED),
                canRetry = false,
                message = message,
                error = null,
            )
        return copy(items = updated, completionNotice = null)
    }

    fun dismiss(itemId: String): UploadQueueState = if (itemId in items) remove(itemId) else this

    @Suppress("MaxLineLength")
    fun consumeCompletionNotice(): UploadQueueState = if (completionNotice == null) this else copy(completionNotice = null)

    private fun remove(itemId: String): UploadQueueState {
        val updated = LinkedHashMap(items)
        updated.remove(itemId)
        return copy(items = updated)
    }
}

private fun UploadQueueItem.isActive(): Boolean =
    operation.state in
        setOf(
            UploadState.PREPARING,
            UploadState.CREATING_SESSION,
            UploadState.UPLOADING,
            UploadState.VERIFYING,
        )

private fun UploadQueueItem.needsAttention(): Boolean = operation.state in setOf(UploadState.PAUSED, UploadState.FAILED)
