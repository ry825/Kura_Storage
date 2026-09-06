using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Media;

public enum MediaVariant
{
    Original,
    Thumbnail,
    ImageLow,
    ImageMedium,
    VideoLow,
    VideoMedium,
}

public enum MediaDisposition
{
    Inline,
    Attachment,
}

public enum MediaRequestStatus
{
    Ready,
    Generating,
    Failed,
}

public sealed record MediaContentRequest(
    Guid ActorUserId,
    Guid FileId,
    string? Variant,
    string? Disposition);

public sealed record MediaContent(
    Guid DerivativeId,
    RelativeStoragePath Path,
    long Size,
    string ContentType,
    string DownloadName,
    MediaDisposition Disposition,
    Guid LeaseOwnerToken,
    Stream Stream);

public sealed record MediaRequestResult(
    MediaRequestStatus Status,
    MediaContent? Content,
    Guid? JobId,
    string? ErrorCode,
    int RetryAfterSeconds = 2);

public enum MediaFailureKind
{
    BadRequest,
    NotFound,
    Conflict,
    StorageUnavailable,
}

public sealed record MediaFailure(string Code, MediaFailureKind Kind);

public sealed class MediaResult<T>
{
    private MediaResult(T? value, MediaFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public MediaFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static MediaResult<T> Success(T value) => new(value, null);

    public static MediaResult<T> Fail(string code, MediaFailureKind kind) => new(default, new(code, kind));
}

public sealed class MediaRuntimeOptions
{
    public int ImageWaitMilliseconds { get; init; } = 2000;
    public int JobPollMilliseconds { get; init; } = 500;
    public int ThumbnailProfileVersion { get; init; } = 1;
    public int ImageProfileVersion { get; init; } = 1;
    public int VideoProfileVersion { get; init; } = 1;
    public int DeliveryLeaseSeconds { get; init; } = 120;
    public int DeliveryLeaseRenewalSeconds { get; init; } = 30;
    public int GenerationLeaseSeconds { get; init; } = 120;
    public int JobHeartbeatSeconds { get; init; } = 10;
    public int CacheTtlHours { get; init; } = 24;
    public int MaximumConcurrentThumbnailJobs { get; init; } = 2;
}

public sealed record MediaJobView(
    Guid JobId,
    string Status,
    int? ProgressPercent,
    long? ProcessedDurationMs,
    long? TotalDurationMs,
    int? QueuePosition,
    bool Retryable,
    int RetryAfterSeconds,
    string? ContentUrl);

public sealed record ThumbnailJobSummaryView(
    long QueuedCount,
    long RunningCount,
    long FailedCount,
    DateTimeOffset ObservedAt);

public static class MediaContractRules
{
    public static bool TryParseVariant(string? value, out MediaVariant variant)
    {
        variant = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "original" => MediaVariant.Original,
            "thumbnail" => MediaVariant.Thumbnail,
            "image-low" => MediaVariant.ImageLow,
            "image-medium" => MediaVariant.ImageMedium,
            "video-low" => MediaVariant.VideoLow,
            "video-medium" => MediaVariant.VideoMedium,
            _ => (MediaVariant)(-1),
        };
        return Enum.IsDefined(variant);
    }

    public static bool TryParseDisposition(string? value, out MediaDisposition disposition)
    {
        disposition = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "inline" => MediaDisposition.Inline,
            "attachment" => MediaDisposition.Attachment,
            _ => (MediaDisposition)(-1),
        };
        return Enum.IsDefined(disposition);
    }

    public static bool Supports(string? mimeType, MediaVariant variant) => variant switch
    {
        MediaVariant.Original => true,
        MediaVariant.Thumbnail => IsImage(mimeType) || IsVideo(mimeType) || IsPdf(mimeType),
        MediaVariant.ImageLow or MediaVariant.ImageMedium => IsImage(mimeType),
        MediaVariant.VideoLow or MediaVariant.VideoMedium => IsVideo(mimeType),
        _ => false,
    };

    public static DerivativeType ToDerivativeType(string? mimeType, MediaVariant variant) => variant switch
    {
        MediaVariant.Thumbnail when IsPdf(mimeType) => DerivativeType.PdfThumbnail,
        MediaVariant.Thumbnail => DerivativeType.Thumbnail,
        MediaVariant.ImageLow => DerivativeType.ImageLow,
        MediaVariant.ImageMedium => DerivativeType.ImageMedium,
        MediaVariant.VideoLow => DerivativeType.VideoLow,
        MediaVariant.VideoMedium => DerivativeType.VideoMedium,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    public static string DownloadName(string sourceName, MediaVariant variant)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceName);
        var suffix = variant switch
        {
            MediaVariant.Thumbnail => "_thumbnail",
            MediaVariant.ImageLow => "_low",
            MediaVariant.ImageMedium => "_medium",
            MediaVariant.VideoLow => "_low",
            MediaVariant.VideoMedium => "_medium",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var extension = variant is MediaVariant.VideoLow or MediaVariant.VideoMedium ? "mp4" : "webp";
        return $"{baseName}{suffix}.{extension}";
    }

    public static int ProfileVersion(MediaVariant variant, int thumbnail, int image, int video) => variant switch
    {
        MediaVariant.Thumbnail => thumbnail,
        MediaVariant.ImageLow or MediaVariant.ImageMedium => image,
        MediaVariant.VideoLow or MediaVariant.VideoMedium => video,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    public static string PublishedVariant(DerivativeType derivativeType) => derivativeType switch
    {
        DerivativeType.Thumbnail or DerivativeType.PdfThumbnail => "thumbnail",
        DerivativeType.ImageLow => "image-low",
        DerivativeType.ImageMedium => "image-medium",
        DerivativeType.VideoLow => "video-low",
        DerivativeType.VideoMedium => "video-medium",
        _ => throw new ArgumentOutOfRangeException(nameof(derivativeType)),
    };

    public static string ContentType(DerivativeType derivativeType) => derivativeType switch
    {
        DerivativeType.Thumbnail or DerivativeType.PdfThumbnail or
            DerivativeType.ImageLow or DerivativeType.ImageMedium => "image/webp",
        DerivativeType.VideoLow or DerivativeType.VideoMedium => "video/mp4",
        _ => throw new ArgumentOutOfRangeException(nameof(derivativeType)),
    };

    private static bool IsImage(string? mimeType) => mimeType is
        "image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif" or "image/heic" or "image/heif";

    private static bool IsVideo(string? mimeType) => mimeType is
        "video/mp4" or "video/quicktime" or "video/webm" or "video/x-matroska" or "video/3gpp";

    private static bool IsPdf(string? mimeType) => mimeType == "application/pdf";
}

public static class MediaErrorCodes
{
    public const string VariantUnsupported = "MEDIA_VARIANT_UNSUPPORTED";
    public const string SourceNotActive = "MEDIA_SOURCE_NOT_ACTIVE";
    public const string GenerationFailed = "MEDIA_GENERATION_FAILED";
    public const string RetryNotAllowed = "MEDIA_RETRY_NOT_ALLOWED";
    public const string ToolUnavailable = "MEDIA_TOOL_UNAVAILABLE";
    public const string WorkerUnavailable = "MEDIA_WORKER_UNAVAILABLE";
    public const string CompletionUnknown = "MEDIA_COMPLETION_UNKNOWN";
}

public sealed class MediaCompletionStateUnknownException(Exception innerException)
    : Exception("The derivative publication database state is unknown.", innerException);
