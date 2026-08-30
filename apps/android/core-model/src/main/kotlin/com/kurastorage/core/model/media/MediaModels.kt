package com.kurastorage.core.model.media

enum class MediaQuality {
    LOW,
    MEDIUM,
    ORIGINAL,
}

enum class MediaVariant(
    val wireValue: String,
) {
    THUMBNAIL("thumbnail"),
    IMAGE_LOW("image-low"),
    IMAGE_MEDIUM("image-medium"),
    VIDEO_LOW("video-low"),
    VIDEO_MEDIUM("video-medium"),
    ORIGINAL("original"),
    ;

    companion object {
        fun fromWireValue(value: String): MediaVariant? = entries.firstOrNull { it.wireValue == value }
    }
}

enum class MediaKind {
    IMAGE,
    PDF,
    VIDEO,
    AUDIO,
}

object SupportedMediaMimeTypes {
    private val photo =
        setOf(
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif",
            "image/bmp",
            "image/heic",
            "image/heif",
        )

    private val video =
        setOf(
            "video/mp4",
            "video/webm",
            "video/quicktime",
            "video/x-matroska",
            "video/3gpp",
            "video/3gpp2",
            "video/mpeg",
        )

    private val audio =
        setOf(
            "audio/mpeg",
            "audio/mp4",
            "audio/aac",
            "audio/ogg",
            "audio/opus",
            "audio/flac",
            "audio/wav",
            "audio/3gpp",
            "audio/amr",
            "audio/amr-wb",
        )

    fun isPhoto(mimeType: String?): Boolean = normalize(mimeType) in photo

    fun isPdf(mimeType: String?): Boolean = normalize(mimeType) == "application/pdf"

    fun isVideo(mimeType: String?): Boolean = normalize(mimeType) in video

    fun isAudio(mimeType: String?): Boolean = normalize(mimeType) in audio

    private fun normalize(mimeType: String?): String? =
        mimeType
            ?.substringBefore(';')
            ?.trim()
            ?.lowercase()
            ?.takeIf(String::isNotEmpty)
}

enum class MediaJobStatus {
    GENERATING,
    READY,
    FAILED,
    CANCELLED,
    UNKNOWN,
    ;

    companion object {
        fun fromWireValue(value: String): MediaJobStatus = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

enum class PlaybackState {
    IDLE,
    BUFFERING,
    READY,
    ENDED,
    FAILED,
}

enum class NetworkQualityContext {
    LOCAL_DIRECT,
    REGISTERED_REMOTE_WIFI,
    UNREGISTERED_REMOTE_WIFI,
    REMOTE_MOBILE,
}

@JvmInline
value class ByteCount(
    val value: Long,
) {
    init {
        require(value >= 0) { "Byte count must not be negative" }
    }
}

@JvmInline
value class MediaPositionMs(
    val value: Long,
) {
    init {
        require(value >= 0) { "Media position must not be negative" }
    }
}

@JvmInline
value class PlaybackRate(
    val value: Float,
) {
    init {
        require(value in MIN_VALUE..MAX_VALUE) { "Playback rate must be between $MIN_VALUE and $MAX_VALUE" }
    }

    companion object {
        const val MIN_VALUE = 0.5f
        const val MAX_VALUE = 3.0f
    }
}

object MediaVariantResolver {
    fun resolve(
        kind: MediaKind,
        quality: MediaQuality,
    ): MediaVariant =
        when (kind) {
            MediaKind.IMAGE ->
                when (quality) {
                    MediaQuality.LOW -> MediaVariant.IMAGE_LOW
                    MediaQuality.MEDIUM -> MediaVariant.IMAGE_MEDIUM
                    MediaQuality.ORIGINAL -> MediaVariant.ORIGINAL
                }
            MediaKind.VIDEO ->
                when (quality) {
                    MediaQuality.LOW -> MediaVariant.VIDEO_LOW
                    MediaQuality.MEDIUM -> MediaVariant.VIDEO_MEDIUM
                    MediaQuality.ORIGINAL -> MediaVariant.ORIGINAL
                }
            MediaKind.AUDIO,
            MediaKind.PDF,
            -> {
                require(quality == MediaQuality.ORIGINAL) { "$kind only supports original content" }
                MediaVariant.ORIGINAL
            }
        }
}

data class MediaJobSnapshot(
    val jobId: String,
    val status: MediaJobStatus,
    val progressPercent: Int?,
    val processedDurationMs: MediaPositionMs?,
    val totalDurationMs: MediaPositionMs?,
    val queuePosition: Int?,
    val retryAfterSeconds: Int,
    val retryable: Boolean,
    val contentUrl: String? = null,
) {
    init {
        require(jobId.isNotBlank())
        require(progressPercent == null || progressPercent in MIN_PROGRESS_PERCENT..MAX_PROGRESS_PERCENT)
        require(queuePosition == null || queuePosition > 0)
        require(retryAfterSeconds in 0..MAX_RETRY_AFTER_SECONDS)
    }
}

data class OriginalMetadata(
    val size: ByteCount,
    val mimeType: String,
    val acceptsRanges: Boolean,
)

data class ReadyMediaSource(
    val fileId: String,
    val fileVersion: Long,
    val variant: MediaVariant,
) {
    init {
        require(fileId.isNotBlank())
        require(fileVersion >= 0)
    }
}

sealed interface MediaLoadState {
    data object Idle : MediaLoadState

    data object ConfirmingTransfer : MediaLoadState

    data object Loading : MediaLoadState

    data class Generating(
        val job: MediaJobSnapshot,
    ) : MediaLoadState

    data class Ready(
        val source: ReadyMediaSource,
    ) : MediaLoadState

    data class Failed(
        val error: MediaUiError,
    ) : MediaLoadState
}

enum class MediaUiError {
    AUTHENTICATION_REQUIRED,
    PERMISSION_DENIED,
    NOT_FOUND,
    FILE_CHANGED,
    DISCONNECTED,
    RANGE_INVALID,
    RESPONSE_INCOMPLETE,
    GENERATION_FAILED,
    UNSUPPORTED,
    UNKNOWN,
}

const val SHORT_SKIP_MS = 3_000L
const val LONG_SKIP_MS = 10_000L
const val MAX_RETRY_AFTER_SECONDS = 30
private const val MIN_PROGRESS_PERCENT = 0
private const val MAX_PROGRESS_PERCENT = 100
