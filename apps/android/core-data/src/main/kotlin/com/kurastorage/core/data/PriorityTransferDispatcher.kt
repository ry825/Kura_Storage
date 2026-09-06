package com.kurastorage.core.data

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

enum class TransferPriority {
    MANUAL,
    BACKUP,
}

data class TransferDispatcherOptions(
    val maximumParallelTransfers: Int = DEFAULT_MAXIMUM_PARALLEL_TRANSFERS,
) {
    init {
        require(maximumParallelTransfers in 1..MAXIMUM_PARALLEL_TRANSFERS)
    }

    companion object {
        const val DEFAULT_MAXIMUM_PARALLEL_TRANSFERS = 2
        const val MAXIMUM_PARALLEL_TRANSFERS = 8
    }
}

class PriorityTransferDispatcher(
    options: TransferDispatcherOptions = TransferDispatcherOptions(),
) {
    private val maximumParallelTransfers = options.maximumParallelTransfers
    private val mutex = Mutex()
    private val manualWaiters = ArrayDeque<CompletableDeferred<Unit>>()
    private val backupWaiters = ArrayDeque<CompletableDeferred<Unit>>()
    private var activeTransfers = 0

    suspend fun <T> run(
        priority: TransferPriority,
        operation: suspend () -> T,
    ): T {
        val ticket = CompletableDeferred<Unit>()
        mutex.withLock {
            if (activeTransfers < maximumParallelTransfers && manualWaiters.isEmpty()) {
                activeTransfers++
                ticket.complete(Unit)
            } else {
                queue(priority).addLast(ticket)
            }
        }

        var acquired = false
        try {
            ticket.await()
            acquired = true
            return operation()
        } finally {
            if (acquired) {
                release()
            } else {
                removeCancelledOrReleased(ticket)
            }
        }
    }

    private suspend fun removeCancelledOrReleased(ticket: CompletableDeferred<Unit>) {
        mutex.withLock {
            val removed = manualWaiters.remove(ticket) || backupWaiters.remove(ticket)
            if (!removed && ticket.isCompleted) handOffOrRelease()
        }
    }

    private suspend fun release() {
        mutex.withLock { handOffOrRelease() }
    }

    private fun handOffOrRelease() {
        val next = manualWaiters.removeFirstOrNull() ?: backupWaiters.removeFirstOrNull()
        if (next == null) {
            check(activeTransfers > 0)
            activeTransfers--
        } else {
            next.complete(Unit)
        }
    }

    private fun queue(priority: TransferPriority): ArrayDeque<CompletableDeferred<Unit>> =
        when (priority) {
            TransferPriority.MANUAL -> manualWaiters
            TransferPriority.BACKUP -> backupWaiters
        }
}
