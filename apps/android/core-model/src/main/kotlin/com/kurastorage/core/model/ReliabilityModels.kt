package com.kurastorage.core.model

/** A user-safe aggregate for a batch trash operation; it intentionally carries no entry identity. */
data class BulkTrashSummary(
    val trashedCount: Int,
    val failedCount: Int,
) {
    init {
        require(trashedCount >= 0)
        require(failedCount >= 0)
    }

    val attemptedCount: Int get() = trashedCount + failedCount
}

enum class BulkTrashItemOutcome {
    TRASHED,
    FAILED,
}

@JvmInline
value class PhotoNavigationRequestToken(
    val value: Long,
) {
    init {
        require(value >= 0)
    }
}

/** Session-local generations only; file, job, user, and error identities must not become UI keys. */
data class ThumbnailFailureNoticeKey(
    val sessionGeneration: Long,
    val summaryGeneration: Long,
) {
    init {
        require(sessionGeneration >= 0)
        require(summaryGeneration >= 0)
    }
}

@JvmInline
value class ThumbnailRetryKey(
    val value: Long,
) {
    init {
        require(value >= 0)
    }
}

enum class VideoPlaybackError {
    CODEC_UNSUPPORTED,
    NETWORK,
    RANGE,
    AUTHENTICATION,
    AUTHORIZATION,
    CONTENT_CORRUPT,
    SERVER,
    UNKNOWN,
}
