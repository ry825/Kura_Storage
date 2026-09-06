package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedCallResult
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MAX_RETRY_AFTER_SECONDS
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaPositionMs
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.ThumbnailJobSummary
import com.kurastorage.core.model.media.VariantMetadata
import com.kurastorage.core.network.media.MediaAcceptedResponseDto
import com.kurastorage.core.network.media.MediaApi
import com.kurastorage.core.network.media.MediaContentNetworkResult
import com.kurastorage.core.network.media.MediaJobDto
import com.kurastorage.core.network.media.MediaMetadataNetworkResult
import com.kurastorage.core.network.media.ThumbnailJobSummaryDto
import okhttp3.Headers
import okhttp3.Response
import okhttp3.ResponseBody
import java.io.Closeable
import java.io.OutputStream
import java.time.Instant

interface MediaRepository {
    suspend fun inspectOriginal(fileId: String): OriginalMetadata

    suspend fun inspectVariant(
        fileId: String,
        variant: MediaVariant,
    ): MediaMetadataResult {
        require(variant == MediaVariant.ORIGINAL)
        val original = inspectOriginal(fileId)
        return MediaMetadataResult.Ready(
            VariantMetadata(variant, original.size, original.mimeType, original.acceptsRanges),
        )
    }

    suspend fun job(jobId: String): MediaJobSnapshot

    suspend fun retryJob(jobId: String): MediaJobSnapshot

    suspend fun thumbnailJobSummary(): ThumbnailJobSummary {
        error("Thumbnail job summary is not implemented by this test double")
    }

    suspend fun openContent(
        fileId: String,
        variant: MediaVariant,
        range: String? = null,
    ): MediaContentResult
}

sealed interface MediaMetadataResult {
    data class Ready(
        val metadata: VariantMetadata,
    ) : MediaMetadataResult

    data class Generating(
        val job: MediaJobSnapshot,
    ) : MediaMetadataResult
}

sealed interface MediaContentResult {
    data class Ready(
        val content: ReadyMediaContent,
    ) : MediaContentResult

    data class Generating(
        val job: MediaJobSnapshot,
    ) : MediaContentResult
}

class ReadyMediaContent internal constructor(
    private val response: Response,
) : Closeable {
    val statusCode: Int = response.code
    val headers: Headers = response.headers
    val body: ResponseBody = response.body
    val contentLength: Long? = response.body.contentLength().takeIf { it >= 0 }

    fun copyTo(
        output: OutputStream,
        maximumBytes: Long,
        onChunk: () -> Unit = {},
    ): Long {
        require(maximumBytes >= 0)
        val expected = contentLength
        if (expected != null && expected > maximumBytes) invalidResponse()
        var received = 0L
        val buffer = ByteArray(COPY_BUFFER_BYTES)
        body.byteStream().use { input ->
            while (true) {
                onChunk()
                val read = input.read(buffer)
                if (read == -1) break
                received += read
                if (received > maximumBytes) invalidResponse()
                output.write(buffer, 0, read)
            }
        }
        if (expected != null && received != expected) invalidResponse()
        return received
    }

    override fun close() = response.close()
}

class DefaultMediaRepository(
    private val api: MediaApi,
    private val executor: AuthenticatedRequestExecutor,
) : MediaRepository {
    override suspend fun inspectOriginal(fileId: String): OriginalMetadata =
        when (val result = inspectVariant(fileId, MediaVariant.ORIGINAL)) {
            is MediaMetadataResult.Ready ->
                OriginalMetadata(result.metadata.size, result.metadata.mimeType, result.metadata.acceptsRanges)
            is MediaMetadataResult.Generating -> invalidResponse()
        }

    override suspend fun inspectVariant(
        fileId: String,
        variant: MediaVariant,
    ): MediaMetadataResult =
        executor.execute { token ->
            api.headContent(token, fileId, variant).toAuthenticatedResult { result ->
                when (result) {
                    is MediaMetadataNetworkResult.Ready -> {
                        val dto = result.metadata
                        if (dto.contentLength < 0 || dto.mimeType.isBlank() || !dto.acceptsRanges) invalidResponse()
                        MediaMetadataResult.Ready(
                            VariantMetadata(variant, ByteCount(dto.contentLength), dto.mimeType, dto.acceptsRanges),
                        )
                    }
                    is MediaMetadataNetworkResult.Generating ->
                        MediaMetadataResult.Generating(result.accepted.toSnapshot(variant))
                }
            }
        }

    override suspend fun job(jobId: String): MediaJobSnapshot =
        executor.execute { token -> api.mediaJob(token, jobId).toAuthenticatedResult(MediaJobDto::toSnapshot) }

    override suspend fun retryJob(jobId: String): MediaJobSnapshot =
        executor.execute { token -> api.retryMediaJob(token, jobId).toAuthenticatedResult(MediaJobDto::toSnapshot) }

    override suspend fun thumbnailJobSummary(): ThumbnailJobSummary =
        executor.execute { token ->
            api.thumbnailJobSummary(token).toAuthenticatedResult(ThumbnailJobSummaryDto::toSummary)
        }

    override suspend fun openContent(
        fileId: String,
        variant: MediaVariant,
        range: String?,
    ): MediaContentResult =
        executor.execute { token ->
            api.openContent(token, fileId, variant, range).toAuthenticatedResult { result ->
                when (result) {
                    is MediaContentNetworkResult.Ready -> MediaContentResult.Ready(ReadyMediaContent(result.response))
                    is MediaContentNetworkResult.Generating ->
                        MediaContentResult.Generating(result.accepted.toSnapshot(variant))
                }
            }
        }
}

private inline fun <Input, Output> com.kurastorage.core.network.NetworkCallResult<Input>.toAuthenticatedResult(
    transform: (Input) -> Output,
): AuthenticatedCallResult<Output> =
    when (this) {
        is com.kurastorage.core.network.NetworkCallResult.Success -> AuthenticatedCallResult.Success(transform(value))
        com.kurastorage.core.network.NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
    }

private fun MediaJobDto.toSnapshot(): MediaJobSnapshot {
    val processed = processedDurationMs
    val total = totalDurationMs
    val queue = queuePosition
    val valuesValid =
        (progressPercent == null || progressPercent in MIN_PROGRESS_PERCENT..MAX_PROGRESS_PERCENT) &&
            (processed == null || processed >= 0) &&
            (total == null || total >= 0) &&
            (queue == null || queue > 0)
    val mappedStatus = MediaJobStatus.fromWireValue(status).takeIf { valuesValid } ?: MediaJobStatus.UNKNOWN
    return MediaJobSnapshot(
        jobId = jobId,
        status = mappedStatus,
        progressPercent = progressPercent?.takeIf { valuesValid },
        processedDurationMs = processed?.takeIf { valuesValid }?.let(::MediaPositionMs),
        totalDurationMs = total?.takeIf { valuesValid }?.let(::MediaPositionMs),
        queuePosition = queue?.takeIf { valuesValid },
        retryAfterSeconds = retryAfterSeconds.coerceIn(0, MAX_RETRY_AFTER_SECONDS),
        retryable = mappedStatus == MediaJobStatus.FAILED && retryable,
        contentUrl = null,
    )
}

private fun MediaAcceptedResponseDto.toSnapshot(variant: MediaVariant): MediaJobSnapshot {
    if (
        status != MediaJobStatus.GENERATING.name ||
        jobId.isBlank() ||
        jobStatusUrl != "/api/v1/media-jobs/$jobId"
    ) {
        invalidResponse()
    }
    val defaultRetry = if (variant in VIDEO_VARIANTS) VIDEO_RETRY_SECONDS else IMAGE_RETRY_SECONDS
    return MediaJobSnapshot(
        jobId = jobId,
        status = MediaJobStatus.GENERATING,
        progressPercent = null,
        processedDurationMs = null,
        totalDurationMs = null,
        queuePosition = null,
        retryAfterSeconds = retryAfterSeconds.takeIf { it > 0 }?.coerceAtMost(MAX_RETRY_AFTER_SECONDS) ?: defaultRetry,
        retryable = false,
    )
}

private fun ThumbnailJobSummaryDto.toSummary(): ThumbnailJobSummary {
    if (queuedCount < 0 || runningCount < 0 || failedCount < 0) invalidResponse()
    val observed = runCatching { Instant.parse(observedAt) }.getOrElse { invalidResponse() }
    return ThumbnailJobSummary(queuedCount, runningCount, failedCount, observed)
}

private fun invalidResponse(): Nothing = throw KuraStorageException.InvalidServerResponse()

private val VIDEO_VARIANTS = setOf(MediaVariant.VIDEO_LOW, MediaVariant.VIDEO_MEDIUM)
private const val IMAGE_RETRY_SECONDS = 2
private const val VIDEO_RETRY_SECONDS = 3
private const val COPY_BUFFER_BYTES = 64 * 1024
private const val MIN_PROGRESS_PERCENT = 0
private const val MAX_PROGRESS_PERCENT = 100
