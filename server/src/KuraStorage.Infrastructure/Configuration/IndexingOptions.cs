using System.ComponentModel.DataAnnotations;

namespace KuraStorage.Infrastructure.Configuration;

public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    public bool Enabled { get; init; }

    [Range(10, 5000)]
    public int BatchSize { get; init; } = 500;

    [Range(1, 1440)]
    public int MissingConfirmationDelayMinutes { get; init; } = 5;

    [Range(1, 168)]
    public int StagingRetentionHours { get; init; } = 24;

    [Range(5, 10080)]
    public int FullRescanIntervalMinutes { get; init; } = 360;

    public bool RunOnStartup { get; init; } = true;

    [Range(50, 10000)]
    public int EventDebounceMilliseconds { get; init; } = 500;

    [Range(100, 30000)]
    public int MovePairingWindowMilliseconds { get; init; } = 1000;

    [Range(128, 65536)]
    public int EventQueueCapacity { get; init; } = 4096;

    [Range(1, 3600)]
    public int RetryBackoffSeconds { get; init; } = 30;
}
