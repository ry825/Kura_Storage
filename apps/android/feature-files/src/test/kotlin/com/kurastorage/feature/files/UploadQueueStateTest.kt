package com.kurastorage.feature.files

import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.model.UploadState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.IOException
import java.time.Instant

class UploadQueueStateTest {
    @Test
    fun `events update only their item while concurrent items remain active`() {
        val first = operation("first", "first.txt")
        val second = operation("second", "second.txt")
        val state =
            UploadQueueState()
                .enqueue("item-1", first)
                .enqueue("item-2", second)
                .applyEvent("item-1", TransferEvent.Progress(4, 10))

        assertEquals(4, state.items.getValue("item-1").transferredBytes)
        assertEquals(0, state.items.getValue("item-2").transferredBytes)
        assertEquals(TransferDisplayState.ACTIVE, state.displayState)
    }

    @Test
    fun `partial failure keeps successful result and retries only failed item`() {
        val first = operation("first", "first.txt")
        val second = operation("second", "second.txt")
        val failed = second.copy(state = UploadState.FAILED)
        val mixed =
            UploadQueueState()
                .enqueue("item-1", first)
                .enqueue("item-2", second)
                .applyEvent("item-1", TransferEvent.UploadCompleted(file("first.txt")))
                .applyEvent("item-2", TransferEvent.Failed(IOException("offline")))
                .applyEvent("item-2", TransferEvent.UploadStatus(failed, "Connection interrupted", canRetry = true))

        assertEquals(
            UploadState.COMPLETED,
            mixed.items
                .getValue("item-1")
                .operation.state,
        )
        assertEquals(
            UploadState.FAILED,
            mixed.items
                .getValue("item-2")
                .operation.state,
        )
        assertEquals(TransferDisplayState.NEEDS_ATTENTION, mixed.displayState)
        assertEquals(listOf("item-2"), mixed.retryableItems.map { it.id })

        val retrying = mixed.retry("item-2")
        assertEquals(
            UploadState.PREPARING,
            retrying.items
                .getValue("item-2")
                .operation.state,
        )
        assertEquals(first.idempotencyKey, retrying.items.getValue("item-1").operationId)
        assertEquals(second.idempotencyKey, retrying.items.getValue("item-2").operationId)
        assertEquals(TransferDisplayState.ACTIVE, retrying.displayState)
    }

    @Test
    fun `all successful items produce one consumable notice and leave no persistent completed items`() {
        val state =
            UploadQueueState()
                .enqueue("item-1", operation("first", "first.txt"))
                .enqueue("item-2", operation("second", "second.txt"))
                .applyEvent("item-1", TransferEvent.UploadCompleted(file("first.txt")))
                .applyEvent("item-2", TransferEvent.UploadCompleted(file("second.txt")))

        assertEquals(emptyMap<String, UploadQueueItem>(), state.items)
        assertEquals(2, state.completionNotice?.completedCount)
        assertEquals(TransferDisplayState.COMPLETED_NOTICE, state.displayState)

        val consumed = state.consumeCompletionNotice()
        assertNull(consumed.completionNotice)
        assertEquals(TransferDisplayState.IDLE, consumed.displayState)
        assertEquals(consumed, consumed.consumeCompletionNotice())
    }

    @Test
    fun `cancel removes only the cancelled item`() {
        val cancelled = operation("first", "first.txt").copy(state = UploadState.CANCELLED)
        val state =
            UploadQueueState()
                .enqueue("item-1", operation("first", "first.txt"))
                .enqueue("item-2", operation("second", "second.txt"))
                .applyEvent("item-1", TransferEvent.UploadStatus(cancelled, "Upload cancelled"))

        assertEquals(listOf("item-2"), state.items.keys.toList())
        assertEquals(TransferDisplayState.ACTIVE, state.displayState)
    }

    private fun operation(
        id: String,
        name: String,
    ) = UploadOperation(
        sourceUri = "content://$id",
        destinationFolderId = "folder",
        fileName = name,
        size = 10,
        contentType = "text/plain",
        idempotencyKey = "operation-$id",
    )

    private fun file(name: String) =
        FileEntry(
            id = name,
            parentId = "folder",
            name = name,
            entryType = FileEntryType.FILE,
            mimeType = "text/plain",
            size = 10,
            status = FileEntryStatus.ACTIVE,
            fileVersion = 1,
            trashedAt = null,
            createdAt = Instant.EPOCH,
            updatedAt = Instant.EPOCH,
            owner = OwnerSummary.UNKNOWN,
            permission = SharePermission.MANAGER,
            permissionSource = PermissionSource.OWNER,
        )
}
