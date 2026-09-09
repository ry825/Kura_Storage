package com.kurastorage.core.data.media

import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.RetryableThumbnailJob
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.Clock
import java.time.Instant
import java.time.ZoneId

class ThumbnailRetryCoordinatorTest {
    @Test
    fun `coalesces duplicate jobs honors retry after and stops after three attempts`() =
        runTest {
            val clock = MutableClock()
            val repository = RecordingRepository()
            val coordinator = ThumbnailRetryCoordinator(repository, clock)

            coordinator.retryEligibleJobs()
            coordinator.retryEligibleJobs()
            assertEquals(listOf("opaque-job"), repository.retried)

            repeat(3) {
                clock.millis += 10_000
                coordinator.retryEligibleJobs()
            }
            assertEquals(listOf("opaque-job", "opaque-job", "opaque-job"), repository.retried)

            coordinator.clear()
            coordinator.retryEligibleJobs()
            assertEquals(listOf("opaque-job", "opaque-job", "opaque-job", "opaque-job"), repository.retried)
        }

    private class RecordingRepository : MediaRepository {
        val retried = mutableListOf<String>()

        override suspend fun inspectOriginal(fileId: String): OriginalMetadata = error("not used")

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot {
            retried += jobId
            error("not used")
        }

        override suspend fun retryableThumbnailJobs() =
            listOf(RetryableThumbnailJob("opaque-job", retryAfterSeconds = 5), RetryableThumbnailJob("opaque-job", 5))

        override suspend fun openContent(fileId: String, variant: MediaVariant, range: String?) = error("not used")
    }

    private class MutableClock(var millis: Long = 0L) : Clock() {
        override fun getZone(): ZoneId = ZoneId.of("UTC")
        override fun withZone(zone: ZoneId): Clock = this
        override fun instant(): Instant = Instant.ofEpochMilli(millis)
    }
}
