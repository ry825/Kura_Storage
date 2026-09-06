package com.kurastorage.core.data

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.util.concurrent.atomic.AtomicInteger

class PriorityTransferDispatcherTest {
    @Test
    fun `rejects unsafe parallel transfer limits`() {
        assertThrows(IllegalArgumentException::class.java) { TransferDispatcherOptions(0) }
        assertThrows(IllegalArgumentException::class.java) { TransferDispatcherOptions(9) }
        assertEquals(2, TransferDispatcherOptions().maximumParallelTransfers)
    }

    @Test
    fun `bounds active transfers`() =
        runTest {
            val dispatcher = PriorityTransferDispatcher(TransferDispatcherOptions(2))
            val release = CompletableDeferred<Unit>()
            val active = AtomicInteger()
            val maximum = AtomicInteger()

            val jobs =
                (0 until 6).map {
                    async {
                        dispatcher.run(TransferPriority.BACKUP) {
                            val count = active.incrementAndGet()
                            maximum.updateAndGet { previous -> maxOf(previous, count) }
                            release.await()
                            active.decrementAndGet()
                        }
                    }
                }
            testScheduler.runCurrent()
            assertEquals(2, maximum.get())
            release.complete(Unit)
            jobs.awaitAll()
        }

    @Test
    fun `waiting manual transfer is selected before waiting backup`() =
        runTest {
            val dispatcher = PriorityTransferDispatcher(TransferDispatcherOptions(1))
            val firstRelease = CompletableDeferred<Unit>()
            val order = mutableListOf<String>()
            val first = async { dispatcher.run(TransferPriority.BACKUP) { firstRelease.await() } }
            testScheduler.runCurrent()
            val backup = async { dispatcher.run(TransferPriority.BACKUP) { order += "backup" } }
            val manual = async { dispatcher.run(TransferPriority.MANUAL) { order += "manual" } }
            testScheduler.runCurrent()

            firstRelease.complete(Unit)
            awaitAll(first, backup, manual)

            assertEquals(listOf("manual", "backup"), order)
        }
}
