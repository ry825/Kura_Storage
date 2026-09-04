package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedCallResult
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.AdminMediaCacheStatus
import com.kurastorage.core.model.media.MediaCleanupFailureCode
import com.kurastorage.core.model.media.MediaCleanupRun
import com.kurastorage.core.model.media.MediaCleanupRunStatus
import com.kurastorage.core.model.media.MediaCleanupTrigger
import com.kurastorage.core.network.AdminMediaCacheApi
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.media.AdminMediaCacheStatusDto
import com.kurastorage.core.network.media.MediaCleanupRunDto
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.Instant
import java.time.format.DateTimeParseException
import java.util.UUID

interface AdminMediaCacheRepository {
    suspend fun get(): AdminMediaCacheStatus

    /** Reuses the same key after an unknown network outcome until the server acknowledges it. */
    suspend fun requestCleanup(): MediaCleanupRun

    fun hasUnknownCleanupOutcome(): Boolean
}

class DefaultAdminMediaCacheRepository(
    private val api: AdminMediaCacheApi,
    private val executor: AuthenticatedRequestExecutor,
    private val newIdempotencyKey: () -> String = { UUID.randomUUID().toString() },
) : AdminMediaCacheRepository {
    private val requestMutex = Mutex()
    private var unresolvedKey: String? = null

    override suspend fun get(): AdminMediaCacheStatus = authenticated(api::getMediaCache).toModel()

    override suspend fun requestCleanup(): MediaCleanupRun =
        requestMutex.withLock {
            val key = unresolvedKey ?: newIdempotencyKey().also(::validateIdempotencyKey)
            try {
                authenticated { token -> api.requestMediaCacheCleanup(token, key) }
                    .toModel()
                    .also { unresolvedKey = null }
            } catch (failure: KuraStorageException.Network) {
                unresolvedKey = key
                throw failure
            } catch (failure: KuraStorageException) {
                unresolvedKey = null
                throw failure
            }
        }

    override fun hasUnknownCleanupOutcome(): Boolean = unresolvedKey != null

    private suspend fun <T> authenticated(call: suspend (String) -> NetworkCallResult<T>): T =
        executor.execute { token ->
            when (val result = call(token)) {
                is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
            }
        }
}

internal fun AdminMediaCacheStatusDto.toModel(): AdminMediaCacheStatus {
    val byteValues =
        listOf(
            cacheBytes,
            imageLowBytes,
            imageMediumBytes,
            videoLowBytes,
            videoMediumBytes,
            highWatermarkBytes,
            lowWatermarkBytes,
        )
    val counts = listOf(queuedJobCount, runningJobCount, failedJobCount, pendingRunCount, runningRunCount)
    if (byteValues.any { it < 0 } || counts.any { it < 0 }) invalidCacheResponse()
    if (highWatermarkBytes == 0L || lowWatermarkBytes > highWatermarkBytes) invalidCacheResponse()
    val calculatedBytes =
        try {
            listOf(imageLowBytes, imageMediumBytes, videoLowBytes, videoMediumBytes).fold(0L, Math::addExact)
        } catch (_: ArithmeticException) {
            invalidCacheResponse()
        }
    if (calculatedBytes != cacheBytes) invalidCacheResponse()
    return AdminMediaCacheStatus(
        cacheBytes,
        imageLowBytes,
        imageMediumBytes,
        videoLowBytes,
        videoMediumBytes,
        highWatermarkBytes,
        lowWatermarkBytes,
        queuedJobCount,
        runningJobCount,
        failedJobCount,
        pendingRunCount,
        runningRunCount,
        lastCleanupRun?.toModel(),
    )
}

internal fun MediaCleanupRunDto.toModel(): MediaCleanupRun {
    if (runCatching { UUID.fromString(id) }.isFailure) invalidCacheResponse()
    if (listOf(examinedCount.toLong(), deletedCount.toLong(), releasedBytes, failureCount.toLong()).any { it < 0 }) {
        invalidCacheResponse()
    }
    if (remainingCacheBytes?.let { it < 0 } == true) invalidCacheResponse()
    if (deletedCount > examinedCount || failureCount > examinedCount) invalidCacheResponse()
    return MediaCleanupRun(
        id = id,
        trigger = MediaCleanupTrigger.entries.firstOrNull { it.name == trigger } ?: MediaCleanupTrigger.UNKNOWN,
        status = MediaCleanupRunStatus.entries.firstOrNull { it.name == status } ?: MediaCleanupRunStatus.UNKNOWN,
        requestedAt = parseInstant(requestedAt),
        startedAt = startedAt?.let(::parseInstant),
        completedAt = completedAt?.let(::parseInstant),
        examinedCount = examinedCount,
        deletedCount = deletedCount,
        releasedBytes = releasedBytes,
        failureCount = failureCount,
        remainingCacheBytes = remainingCacheBytes,
        failureCode =
            failureCode?.let { value ->
                MediaCleanupFailureCode.entries.firstOrNull { it.name == value } ?: MediaCleanupFailureCode.UNKNOWN
            },
    )
}

private fun parseInstant(value: String): Instant =
    try {
        Instant.parse(value)
    } catch (_: DateTimeParseException) {
        invalidCacheResponse()
    }

private fun validateIdempotencyKey(value: String) {
    if (runCatching { UUID.fromString(value) }.isFailure) invalidCacheResponse()
}

private fun invalidCacheResponse(): Nothing = throw KuraStorageException.InvalidServerResponse()
