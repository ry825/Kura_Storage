namespace KuraStorage.Infrastructure.Configuration;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public string DerivativeRoot { get; init; } = "derivatives";

    public string TemporaryRoot { get; init; } = "derivative-temp";

    public int ImageWaitMilliseconds { get; init; } = 2000;

    public int ThumbnailProfileVersion { get; init; } = 1;

    public int ImageProfileVersion { get; init; } = 1;

    public int VideoProfileVersion { get; init; } = 1;

    public int ThumbnailMaxDimension { get; init; } = 512;

    public int ThumbnailWebpQuality { get; init; } = 75;

    public int JobPollMilliseconds { get; init; } = 500;

    public int JobHeartbeatSeconds { get; init; } = 10;

    public int StaleJobSeconds { get; init; } = 120;

    public int MaximumAttempts { get; init; } = 3;

    public int GenerationLeaseSeconds { get; init; } = 120;

    public int DeliveryLeaseSeconds { get; init; } = 120;

    public int DeliveryLeaseRenewalSeconds { get; init; } = 30;

    public int CacheTtlHours { get; init; } = 24;

    public long CacheHighWatermarkBytes { get; init; } = 10_737_418_240;

    public long CacheLowWatermarkBytes { get; init; } = 6_442_450_944;

    public int CleanupIntervalMinutes { get; init; } = 30;

    public int CleanupBatchSize { get; init; } = 100;

    public int TerminalJobRetentionDays { get; init; } = 7;

    public int MaximumConcurrentMediaJobs { get; init; } = 1;

    public int MaximumConcurrentVideoJobs { get; init; } = 1;

    public string VipsPath { get; init; } = "/usr/bin/vips";

    public string FfmpegPath { get; init; } = "/usr/bin/ffmpeg";

    public string FfprobePath { get; init; } = "/usr/bin/ffprobe";

    public string PdftoppmPath { get; init; } = "/usr/bin/pdftoppm";
}
