using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Media;

public enum MediaVariant
{
    Original,
    Thumbnail,
    ImageLow,
    ImageMedium,
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
    public int DeliveryLeaseSeconds { get; init; } = 120;
    public int DeliveryLeaseRenewalSeconds { get; init; } = 30;
    public int GenerationLeaseSeconds { get; init; } = 120;
    public int JobHeartbeatSeconds { get; init; } = 10;
    public int CacheTtlHours { get; init; } = 24;
}

public sealed record MediaJobView(
    Guid JobId,
    string Status,
    int? ProgressPercent,
    long? ProcessedDurationMs,
    long? TotalDurationMs,
    int? QueuePosition,
    int RetryAfterSeconds,
    string? ContentUrl);

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
        _ => false,
    };

    public static DerivativeType ToDerivativeType(string? mimeType, MediaVariant variant) => variant switch
    {
        MediaVariant.Thumbnail when IsPdf(mimeType) => DerivativeType.PdfThumbnail,
        MediaVariant.Thumbnail => DerivativeType.Thumbnail,
        MediaVariant.ImageLow => DerivativeType.ImageLow,
        MediaVariant.ImageMedium => DerivativeType.ImageMedium,
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
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        return $"{baseName}{suffix}.webp";
    }

    public static int ProfileVersion(MediaVariant variant, int thumbnail, int image) => variant switch
    {
        MediaVariant.Thumbnail => thumbnail,
        MediaVariant.ImageLow or MediaVariant.ImageMedium => image,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    public static string PublishedVariant(DerivativeType derivativeType) => derivativeType switch
    {
        DerivativeType.Thumbnail or DerivativeType.PdfThumbnail => "thumbnail",
        DerivativeType.ImageLow => "image-low",
        DerivativeType.ImageMedium => "image-medium",
        _ => throw new ArgumentOutOfRangeException(nameof(derivativeType)),
    };

    private static bool IsImage(string? mimeType) => mimeType is
        "image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif" or "image/heic" or "image/heif";

    private static bool IsVideo(string? mimeType) => mimeType is
        "video/mp4" or "video/quicktime" or "video/webm" or "video/x-matroska";

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
}
