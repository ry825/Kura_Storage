using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;

namespace KuraStorage.Application.Abstractions;

public interface IManagedFileSystemSnapshotReader
{
    IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
        StorageSnapshotContext context,
        CancellationToken cancellationToken);

    Task<ObservedStorageEntry?> InspectAsync(
        Domain.Files.RelativeStoragePath path,
        CancellationToken cancellationToken);
}

public interface IIndexScanService
{
    Task<IndexScanSummary> RunAsync(IndexScanRequest request, CancellationToken cancellationToken);
}

public interface IIndexEventService
{
    Task<IndexEventResult> ReconcileAsync(IndexChangeEvent change, CancellationToken cancellationToken);
}

public interface IIndexChangeWatcher
{
    IAsyncEnumerable<IndexChangeEvent> WatchAsync(CancellationToken cancellationToken);
}

public interface IIndexRescanSignal
{
    void Request(IndexScanTrigger trigger);
    Task<IndexScanTrigger?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
    void SetReady(bool ready);
    Task WaitUntilReadyAsync(CancellationToken cancellationToken);
}

public interface IIndexScanObserver
{
    void Started(Guid runId, IndexScanTrigger trigger, IndexScanMode mode);
    void Completed(IndexScanSummary summary);
    void Failed(Guid runId, string errorCode);
}

public interface IIndexScanLock : IAsyncDisposable;

public sealed class IndexCatalogConcurrencyException : Exception;

public interface IIndexScanWorkspace : IAsyncDisposable
{
    Task StageAsync(IReadOnlyList<ObservedStorageEntry> entries, CancellationToken cancellationToken);

    Task<IReadOnlyList<StagedIndexEntry>> ListStagedAsync(
        string? afterRelativePath,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IndexedCatalogEntry>> ListUnobservedAsync(
        Guid? afterOwnerUserId,
        string? afterRelativePath,
        int take,
        CancellationToken cancellationToken);

    Task<bool> ContainsAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<IndexedCatalogEntry>> FindMoveCandidatesAsync(
        StagedIndexEntry observed,
        CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<string, IReadOnlyList<IndexedCatalogEntry>>> FindMoveCandidatesBatchAsync(
        IReadOnlyList<StagedIndexEntry> observed,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<IndexedCatalogEntry>>(StringComparer.Ordinal);
        foreach (var entry in observed)
        {
            result[entry.RelativePath] = await FindMoveCandidatesAsync(entry, cancellationToken);
        }

        return result;
    }

    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed record StagedIndexEntry(
    Guid OwnerUserId,
    string RelativePath,
    string ParentRelativePath,
    string Name,
    FileEntryType EntryType,
    long Size,
    string? MimeType,
    DateTimeOffset SourceModifiedAt,
    string? SourceFileKey,
    string? IsolationReason);

public sealed record IndexedCatalogEntry(
    Guid Id,
    Guid OwnerUserId,
    string RelativePath,
    FileEntryType EntryType,
    FileEntryStatus Status,
    string? SourceFileKey,
    long Size,
    DateTimeOffset? SourceModifiedAt,
    DateTimeOffset? MissingDetectedAt,
    Guid? MissingObservationId);

public interface IIndexCatalogRepository
{
    Task<IIndexScanLock?> TryAcquireScanLockAsync(CancellationToken cancellationToken);

    Task<IIndexScanWorkspace> CreateWorkspaceAsync(
        Guid scanId,
        IndexScanMode mode,
        CancellationToken cancellationToken);

    Task<FileEntry?> FindEntryByPathAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken);

    async Task<IReadOnlyList<FileEntry>> FindEntriesByPathsAsync(
        IReadOnlyList<IndexPathKey> paths,
        CancellationToken cancellationToken)
    {
        var result = new List<FileEntry>(paths.Count);
        foreach (var path in paths)
        {
            var entry = await FindEntryByPathAsync(path.OwnerUserId, path.RelativePath, cancellationToken);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    Task<FileEntry?> FindEntryByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task<bool> HasIncompleteOperationAsync(
        Guid ownerUserId,
        Guid entryId,
        string relativePath,
        CancellationToken cancellationToken);

    async Task<IReadOnlySet<Guid>> FindEntriesWithIncompleteOperationsAsync(
        IReadOnlyList<IndexOperationKey> entries,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (await HasIncompleteOperationAsync(
                    entry.OwnerUserId,
                    entry.EntryId,
                    entry.RelativePath,
                    cancellationToken))
            {
                result.Add(entry.EntryId);
            }
        }

        return result;
    }

    Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(
        Guid ownerUserId,
        string relativePathPrefix,
        CancellationToken cancellationToken);

    Task<(int CandidateCount, int MissingCount)> CountMissingStatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult((0, 0));

    void Add(FileEntry entry);
    void Add(IndexScanRun run);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task RecoverInterruptedRunsAsync(DateTimeOffset recoveredAt, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    Task CleanupStagingAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

public readonly record struct IndexPathKey(Guid OwnerUserId, string RelativePath);
public readonly record struct IndexOperationKey(Guid OwnerUserId, Guid EntryId, string RelativePath);
