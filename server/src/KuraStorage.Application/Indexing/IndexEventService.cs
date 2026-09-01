using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Indexing;

public sealed class IndexEventService(
    IIndexCatalogRepository catalog,
    IManagedFileSystemSnapshotReader snapshotReader,
    IStorageGuard storageGuard,
    ISystemClock clock,
    IFileRepository? mutationRepository = null,
    FileVersionService? fileVersions = null) : IIndexEventService
{
    private static readonly Meter Meter = new("KuraStorage.Indexing");
    private static readonly Counter<long> EventResults = Meter.CreateCounter<long>("kurastorage.index.event.results");

    public async Task<IndexEventResult> ReconcileAsync(
        IndexChangeEvent change,
        CancellationToken cancellationToken)
    {
        if (change.Kind is IndexChangeKind.Overflow or IndexChangeKind.WatcherStopped)
        {
            return Record(IndexEventResult.RescanRequired, change.Kind);
        }

        if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
        {
            return Record(IndexEventResult.Deferred, change.Kind);
        }

        if (!TryParseManagedPath(change.RelativePath, out var path, out var ownerUserId))
        {
            return Record(IndexEventResult.Ignored, change.Kind);
        }

        try
        {
            var observed = await snapshotReader.InspectAsync(path, cancellationToken);
            IndexEventResult result;
            if (observed is null)
            {
                result = await ReconcileAbsentAsync(ownerUserId, path, cancellationToken);
            }
            else if (observed.IsolationReason is not null)
            {
                result = IndexEventResult.Ignored;
            }
            else
            {
                result = await ReconcilePresentAsync(change, observed, cancellationToken);
            }

            if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
            {
                return Record(IndexEventResult.RescanRequired, change.Kind);
            }

            return Record(result, change.Kind);
        }
        catch (IndexCatalogConcurrencyException)
        {
            return Record(IndexEventResult.RescanRequired, change.Kind);
        }
        catch (IOException)
        {
            return Record(IndexEventResult.Deferred, change.Kind);
        }
    }

    private async Task<IndexEventResult> ReconcilePresentAsync(
        IndexChangeEvent change,
        ObservedStorageEntry observed,
        CancellationToken cancellationToken)
    {
        var existing = await catalog.FindEntryByPathAsync(
            observed.OwnerUserId,
            observed.RelativePath.Value,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.EntryType != observed.EntryType ||
                await HasIncompleteOperationAsync(existing, cancellationToken))
            {
                return IndexEventResult.Deferred;
            }

            if (mutationRepository is not null && fileVersions is not null)
            {
                await using var mutationLock = await mutationRepository.AcquireMutationLocksAsync(
                    [existing.Id], cancellationToken);
                existing = await catalog.FindEntryByPathAsync(
                    observed.OwnerUserId, observed.RelativePath.Value, cancellationToken);
                if (existing is null || existing.EntryType != observed.EntryType ||
                    await HasIncompleteOperationAsync(existing, cancellationToken))
                {
                    return IndexEventResult.Deferred;
                }

                var previousVersion = existing.FileVersion;
                ApplyObservation(existing, observed, change.ContentMayHaveChanged);
                if (existing.FileVersion != previousVersion)
                {
                    try
                    {
                        _ = await fileVersions.EnsureCurrentAsync(
                            existing,
                            FileVersionChangeKind.ExternalChange,
                            Guid.NewGuid(),
                            null,
                            null,
                            cancellationToken);
                    }
                    catch (IOException)
                    {
                        _ = await mutationRepository.ReloadAsync(existing, CancellationToken.None);
                        return IndexEventResult.Deferred;
                    }
                }

                await catalog.SaveChangesAsync(cancellationToken);
            }
            else
            {
                ApplyObservation(existing, observed, change.ContentMayHaveChanged);
                await catalog.SaveChangesAsync(cancellationToken);
            }
            return IndexEventResult.Applied;
        }

        if (change.Kind == IndexChangeKind.Move &&
            TryParseManagedPath(change.PreviousRelativePath, out _, out var previousOwner) &&
            previousOwner == observed.OwnerUserId)
        {
            var moved = await TryApplyMoveAsync(change.PreviousRelativePath!, observed, cancellationToken);
            if (moved is not null)
            {
                return moved.Value;
            }
        }

        var root = await catalog.FindRootAsync(observed.OwnerUserId, cancellationToken);
        var parent = await catalog.FindEntryByPathAsync(
            observed.OwnerUserId,
            observed.ParentRelativePath.Value,
            cancellationToken);
        if (root is null || parent is null || parent.EntryType != FileEntryType.Folder ||
            parent.Status != FileEntryStatus.Active)
        {
            return IndexEventResult.Deferred;
        }

        var entry = observed.EntryType == FileEntryType.Folder
            ? FileEntry.CreateFolder(
                Guid.NewGuid(), observed.OwnerUserId, parent.Id, observed.Name,
                observed.RelativePath, clock.UtcNow)
            : FileEntry.CreateFile(
                Guid.NewGuid(), observed.OwnerUserId, parent.Id, observed.Name,
                observed.RelativePath, observed.MimeType, observed.Size, clock.UtcNow);
        entry.ApplySourceObservation(
            observed.Size,
            observed.MimeType,
            observed.SourceModifiedAt,
            observed.SourceFileKey,
            clock.UtcNow,
            contentChanged: false);
        if (mutationRepository is not null && fileVersions is not null)
        {
            await using var mutationLock = await mutationRepository.AcquireMutationLocksAsync(
                [entry.Id], cancellationToken);
            _ = await fileVersions.EnsureCurrentAsync(
                entry,
                FileVersionChangeKind.ExternalChange,
                Guid.NewGuid(),
                null,
                null,
                cancellationToken);
        }

        catalog.Add(entry);
        await catalog.SaveChangesAsync(cancellationToken);
        return IndexEventResult.Applied;
    }

    private async Task<IndexEventResult?> TryApplyMoveAsync(
        string previousRelativePath,
        ObservedStorageEntry observed,
        CancellationToken cancellationToken)
    {
        var previous = await catalog.FindEntryByPathAsync(
            observed.OwnerUserId,
            previousRelativePath,
            cancellationToken);
        var parent = await catalog.FindEntryByPathAsync(
            observed.OwnerUserId,
            observed.ParentRelativePath.Value,
            cancellationToken);
        if (previous is null)
        {
            return null;
        }

        if (previous.Status != FileEntryStatus.Active || previous.EntryType != observed.EntryType ||
            parent is null || parent.EntryType != FileEntryType.Folder || parent.Status != FileEntryStatus.Active ||
            await HasIncompleteOperationAsync(previous, cancellationToken))
        {
            return IndexEventResult.Deferred;
        }

        _ = await IndexReconciliationPrimitives.RelocateAsync(
            previous,
            parent,
            observed.Name.Value,
            observed.RelativePath.Value,
            catalog,
            clock.UtcNow,
            cancellationToken);

        ApplyObservation(previous, observed, contentMayHaveChanged: false);
        await catalog.SaveChangesAsync(cancellationToken);
        return IndexEventResult.Applied;
    }

    private async Task<IndexEventResult> ReconcileAbsentAsync(
        Guid ownerUserId,
        RelativeStoragePath path,
        CancellationToken cancellationToken)
    {
        var existing = await catalog.FindEntryByPathAsync(ownerUserId, path.Value, cancellationToken);
        if (existing is null || existing.ParentId is null || existing.Status == FileEntryStatus.Trashed)
        {
            return IndexEventResult.Ignored;
        }

        if (await HasIncompleteOperationAsync(existing, cancellationToken))
        {
            return IndexEventResult.Deferred;
        }

        // An event is only one observation. It may create a candidate, but never confirms MISSING.
        if (existing.Status == FileEntryStatus.Active)
        {
            existing.MarkMissingCandidate(Guid.NewGuid(), clock.UtcNow);
            await catalog.SaveChangesAsync(cancellationToken);
            return IndexEventResult.Applied;
        }

        return IndexEventResult.Deferred;
    }

    private Task<bool> HasIncompleteOperationAsync(FileEntry entry, CancellationToken cancellationToken) =>
        catalog.HasIncompleteOperationAsync(
            entry.OwnerUserId,
            entry.Id,
            entry.RelativePath,
            cancellationToken);

    private void ApplyObservation(
        FileEntry entry,
        ObservedStorageEntry observed,
        bool contentMayHaveChanged)
    {
        IndexReconciliationPrimitives.ApplyPresent(
            entry,
            observed.Size,
            observed.MimeType,
            observed.SourceModifiedAt,
            observed.SourceFileKey,
            clock.UtcNow,
            contentMayHaveChanged);
    }

    private static bool TryParseManagedPath(
        string? value,
        out RelativeStoragePath path,
        out Guid ownerUserId)
    {
        path = default;
        ownerUserId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value) || !RelativeStoragePath.TryCreate(value, out path))
        {
            return false;
        }

        var segments = path.Value.Split('/');
        return segments.Length >= 4 && segments[0] == "users" && segments[2] == "files" &&
               Guid.TryParseExact(segments[1], "N", out ownerUserId);
    }

    private static IndexEventResult Record(IndexEventResult result, IndexChangeKind kind)
    {
        EventResults.Add(1, new KeyValuePair<string, object?>("result", result.ToString().ToUpperInvariant()),
            new KeyValuePair<string, object?>("kind", kind.ToString().ToUpperInvariant()));
        return result;
    }
}
