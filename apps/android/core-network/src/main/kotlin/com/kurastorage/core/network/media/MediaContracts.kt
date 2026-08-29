package com.kurastorage.core.network.media

import kotlinx.serialization.Serializable

data class OriginalMetadataDto(
    val contentLength: Long,
    val mimeType: String,
    val acceptsRanges: Boolean,
)

@Serializable
data class MediaAcceptedResponseDto(
    val status: String,
    val jobId: String,
    val jobStatusUrl: String,
    val retryAfterSeconds: Int,
)

@Serializable
data class MediaJobDto(
    val jobId: String,
    val status: String,
    val progressPercent: Int?,
    val processedDurationMs: Long?,
    val totalDurationMs: Long?,
    val queuePosition: Int?,
    val retryable: Boolean,
    val retryAfterSeconds: Int,
    val contentUrl: String?,
)
