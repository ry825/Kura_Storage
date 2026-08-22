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

public enum IndexChangeKind
{
    Reconcile,
    Move,
    Overflow,
    WatcherStopped,
}

public sealed record IndexChangeEvent(
    IndexChangeKind Kind,
    string RelativePath,
    string? PreviousRelativePath = null,
    bool ContentMayHaveChanged = false);

public enum IndexEventResult
{
    Applied,
    Ignored,
    Deferred,
    RescanRequired,
}

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
    public int FullRescanIntervalMinutes { get; init; } = 360;
    public bool RunOnStartup { get; init; } = true;
    public int EventDebounceMilliseconds { get; init; } = 500;
    public int MovePairingWindowMilliseconds { get; init; } = 1000;
    public int EventQueueCapacity { get; init; } = 4096;
    public int RetryBackoffSeconds { get; init; } = 30;
}
