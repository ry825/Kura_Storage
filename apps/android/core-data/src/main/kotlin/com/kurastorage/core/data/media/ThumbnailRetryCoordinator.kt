package com.kurastorage.core.data.media

import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.Clock

/** Session-scoped coordinator; opaque job IDs are never persisted or exposed to UI state. */
class ThumbnailRetryCoordinator(
    private val repository: MediaRepository,
    private val clock: Clock = Clock.systemUTC(),
) {
    private val mutex = Mutex()
    private val attempts = mutableMapOf<String, Int>()
    private val notBeforeMillis = mutableMapOf<String, Long>()

    suspend fun retryEligibleJobs() =
        mutex.withLock {
            val now = clock.millis()
            repository.retryableThumbnailJobs().forEach { job ->
                val attempt = attempts[job.jobId] ?: 0
                if (attempt >= MAX_ATTEMPTS || now < (notBeforeMillis[job.jobId] ?: Long.MIN_VALUE)) {
                    return@forEach
                }
                attempts[job.jobId] = attempt + 1
                val retryDelayMillis =
                    maxOf(
                        job.retryAfterSeconds.coerceAtLeast(1) * MILLISECONDS_PER_SECOND,
                        BASE_BACKOFF_MILLISECONDS * (1L shl attempt),
                    )
                notBeforeMillis[job.jobId] = now + retryDelayMillis
                runCatching { repository.retryJob(job.jobId) }
            }
        }

    fun clear() {
        attempts.clear()
        notBeforeMillis.clear()
    }

    private companion object {
        const val MAX_ATTEMPTS = 3
        const val BASE_BACKOFF_MILLISECONDS = 2_000L
        const val MILLISECONDS_PER_SECOND = 1_000L
    }
}
