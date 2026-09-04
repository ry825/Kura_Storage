package com.kurastorage.core.model.media

import java.time.Instant

enum class MediaCleanupTrigger {
    SCHEDULED,
    MANUAL,
    UNKNOWN,
}

enum class MediaCleanupRunStatus {
    PENDING,
    RUNNING,
    COMPLETED,
    FAILED,
    UNKNOWN,
}

enum class MediaCleanupFailureCode {
    STORAGE_UNAVAILABLE,
    PARTIAL_DELETE_FAILURE,
    CLEANUP_FAILED,
    UNKNOWN,
}

data class MediaCleanupRun(
    val id: String,
    val trigger: MediaCleanupTrigger,
    val status: MediaCleanupRunStatus,
    val requestedAt: Instant,
    val startedAt: Instant?,
    val completedAt: Instant?,
    val examinedCount: Int,
    val deletedCount: Int,
    val releasedBytes: Long,
    val failureCount: Int,
    val remainingCacheBytes: Long?,
    val failureCode: MediaCleanupFailureCode?,
) {
    val terminal: Boolean
        get() = status == MediaCleanupRunStatus.COMPLETED || status == MediaCleanupRunStatus.FAILED
}

data class AdminMediaCacheStatus(
    val cacheBytes: Long,
    val imageLowBytes: Long,
    val imageMediumBytes: Long,
    val videoLowBytes: Long,
    val videoMediumBytes: Long,
    val highWatermarkBytes: Long,
    val lowWatermarkBytes: Long,
    val queuedJobCount: Int,
    val runningJobCount: Int,
    val failedJobCount: Int,
    val pendingRunCount: Int,
    val runningRunCount: Int,
    val lastCleanupRun: MediaCleanupRun?,
) {
    val imageBytes: Long get() = imageLowBytes + imageMediumBytes
    val videoBytes: Long get() = videoLowBytes + videoMediumBytes
}
