package com.kurastorage.core.network.media

import kotlinx.serialization.Serializable

@Serializable
data class MediaCleanupRunDto(
    val id: String,
    val trigger: String,
    val status: String,
    val requestedAt: String,
    val startedAt: String? = null,
    val completedAt: String? = null,
    val examinedCount: Int,
    val deletedCount: Int,
    val releasedBytes: Long,
    val failureCount: Int,
    val remainingCacheBytes: Long? = null,
    val failureCode: String? = null,
)

@Serializable
data class AdminMediaCacheStatusDto(
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
    val lastCleanupRun: MediaCleanupRunDto? = null,
)
