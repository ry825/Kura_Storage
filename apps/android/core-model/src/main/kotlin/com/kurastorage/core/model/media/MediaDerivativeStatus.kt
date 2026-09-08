package com.kurastorage.core.model.media

import java.time.Instant

data class MediaDerivativeStatus(
    val profileVersion: Int,
    val photoCount: Long,
    val readyCount: Long,
    val pendingCount: Long,
    val runningCount: Long,
    val failedCount: Long,
    val blockedCount: Long,
    val missingCount: Long,
    val readyBytes: Long,
    val duplicateCount: Long,
    val orphanCount: Long,
    val observedAt: Instant,
) {
    val coverage: Float
        get() = if (photoCount == 0L) 1f else (readyCount.toDouble() / photoCount).coerceIn(0.0, 1.0).toFloat()
}
