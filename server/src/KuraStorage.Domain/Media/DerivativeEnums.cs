namespace KuraStorage.Domain.Media;

public enum DerivativeType
{
    Thumbnail,
    PdfThumbnail,
    ImageLow,
    ImageMedium,
    VideoLow,
    VideoMedium,
}

public enum DerivativeStatus
{
    Pending,
    Running,
    Ready,
    Failed,
    BlockedSourceMissing,
    Deleting,
}

public enum MediaJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum DerivativeLeaseType
{
    Generation,
    Delivery,
}

public readonly record struct DerivativeLogicalKey(
    Guid SourceFileId,
    long SourceVersion,
    DerivativeType DerivativeType,
    int ProfileVersion);
