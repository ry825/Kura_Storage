using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;

namespace KuraStorage.Application.Indexing;

public sealed record StorageSnapshotContext(Guid ObservationId, int BatchSize);

public sealed record ObservedStorageEntry(
    Guid OwnerUserId,
    RelativeStoragePath RelativePath,
    RelativeStoragePath ParentRelativePath,
    FileName Name,
    FileEntryType EntryType,
    long Size,
    string? MimeType,
    DateTimeOffset SourceModifiedAt,
    string? SourceFileKey,
    string? IsolationReason = null);

public sealed record IndexScanRequest(IndexScanTrigger Trigger, IndexScanMode Mode);

public sealed record IndexScanSummary(
    Guid RunId,
    IndexScanStatus Status,
    int EnumeratedCount,
    int AddedCount,
    int UpdatedCount,
    int MovedCount,
    int CandidateCount,
    int MissingCount,
    int RevivedCount,
    int IsolatedCount,
    int ErrorCount,
    string? ErrorCode);

public sealed class IndexScanAlreadyRunningException : Exception;
public sealed class IndexStorageUnavailableException : Exception;
public sealed class IndexSnapshotIncompleteException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class IndexingOptions
{
    public bool Enabled { get; init; }
    public int BatchSize { get; init; } = 500;
    public int MissingConfirmationDelayMinutes { get; init; } = 5;
    public int StagingRetentionHours { get; init; } = 24;
}
