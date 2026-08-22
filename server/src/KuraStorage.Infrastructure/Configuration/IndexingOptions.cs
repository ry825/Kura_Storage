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
}
