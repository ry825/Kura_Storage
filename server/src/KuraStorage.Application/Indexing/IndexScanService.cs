using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KuraStorage.Application.Indexing;

public sealed class IndexScanService(
    IIndexCatalogRepository catalog,
    IManagedFileSystemSnapshotReader snapshotReader,
    IStorageGuard storageGuard,
    ISystemClock clock,
    IndexingOptions options,
    IIndexScanObserver? observer = null) : IIndexScanService
{
    private static readonly Meter Meter = new("KuraStorage.Indexing");
    private static readonly Histogram<double> ScanDuration = Meter.CreateHistogram<double>(
        "kurastorage.index.scan.duration",
        "s");
    private static readonly Counter<long> ScanEntries = Meter.CreateCounter<long>("kurastorage.index.scan.entries");

    public async Task<IndexScanSummary> RunAsync(
        IndexScanRequest request,
        CancellationToken cancellationToken)
    {
        await using var scanLock = await catalog.TryAcquireScanLockAsync(cancellationToken)
            ?? throw new IndexScanAlreadyRunningException();
        await EnsureStorageAvailableAsync(cancellationToken);

        var run = new IndexScanRun(Guid.NewGuid(), request.Trigger, request.Mode, clock.UtcNow);
        var startedTimestamp = Stopwatch.GetTimestamp();
        observer?.Started(run.Id, request.Trigger, request.Mode);
        if (request.Mode == IndexScanMode.Apply)
        {
            catalog.Add(run);
            await catalog.SaveChangesAsync(cancellationToken);
            await catalog.CleanupStagingAsync(
                clock.UtcNow.AddHours(-options.StagingRetentionHours),
                cancellationToken);
        }

        await using var workspace = await catalog.CreateWorkspaceAsync(run.Id, request.Mode, cancellationToken);
        try
        {
            await StageSnapshotAsync(run, workspace, cancellationToken);
            await EnsureStorageAvailableAsync(cancellationToken);
            await ReconcileObservedAsync(run, workspace, request.Mode, cancellationToken);
            await ReconcileMissingAsync(run, workspace, request.Mode, cancellationToken);
            await EnsureStorageAvailableAsync(cancellationToken);
            run.Complete(clock.UtcNow);
            if (request.Mode == IndexScanMode.Apply)
            {
                await catalog.SaveChangesAsync(cancellationToken);
            }

            await workspace.ClearAsync(cancellationToken);
            RecordMetrics(run, startedTimestamp);
            var summary = ToSummary(run);
            observer?.Completed(summary);
            return summary;
        }
        catch (OperationCanceledException) when (request.Mode == IndexScanMode.Apply)
        {
            run.Cancel(clock.UtcNow);
            await catalog.SaveChangesAsync(CancellationToken.None);
            RecordMetrics(run, startedTimestamp);
            var summary = ToSummary(run);
            observer?.Completed(summary);
            return summary;
        }
        catch (Exception exception) when (request.Mode == IndexScanMode.Apply)
        {
            var errorCode = ErrorCodeFor(exception);
            if (run.Status == IndexScanStatus.Running)
            {
                run.Fail(errorCode, clock.UtcNow);
                await catalog.SaveChangesAsync(CancellationToken.None);
                RecordMetrics(run, startedTimestamp);
            }

            observer?.Failed(run.Id, errorCode);
            throw;
        }
    }

    private async Task StageSnapshotAsync(
        IndexScanRun run,
        IIndexScanWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var batch = new List<ObservedStorageEntry>(options.BatchSize);
        await foreach (var observed in snapshotReader.EnumerateAsync(
                           new StorageSnapshotContext(run.Id, options.BatchSize),
                           cancellationToken))
        {
            batch.Add(observed);
            run.RecordEnumerated();
            if (batch.Count < options.BatchSize)
            {
                continue;
            }

            await workspace.StageAsync(batch, cancellationToken);
            batch.Clear();
            await EnsureStorageAvailableAsync(cancellationToken);
        }

        await workspace.StageAsync(batch, cancellationToken);
    }

    private async Task ReconcileObservedAsync(
        IndexScanRun run,
        IIndexScanWorkspace workspace,
        IndexScanMode mode,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        while (true)
        {
            var batch = await workspace.ListStagedAsync(cursor, options.BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var observed in batch)
            {
                await ReconcileObservedEntryAsync(run, workspace, observed, mode, cancellationToken);
                cursor = observed.RelativePath;
            }

            if (mode == IndexScanMode.Apply)
            {
                await SaveBatchAsync(run, cancellationToken);
            }

            await EnsureStorageAvailableAsync(cancellationToken);
        }
    }

    private async Task ReconcileObservedEntryAsync(
        IndexScanRun run,
        IIndexScanWorkspace workspace,
        StagedIndexEntry observed,
        IndexScanMode mode,
        CancellationToken cancellationToken)
    {
        if (observed.IsolationReason is not null)
        {
            run.RecordIsolated();
            return;
        }

        var existing = await catalog.FindEntryByPathAsync(
            observed.OwnerUserId,
            observed.RelativePath,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.EntryType != observed.EntryType ||
                await catalog.HasIncompleteOperationAsync(
                    existing.OwnerUserId,
                    existing.Id,
                    existing.RelativePath,
                    cancellationToken))
            {
                run.RecordIsolated();
                return;
            }

            var revived = existing.Status is FileEntryStatus.MissingCandidate or FileEntryStatus.Missing;
            var contentChanged = existing.EntryType == FileEntryType.File &&
                                 (existing.Size != observed.Size ||
                                  (existing.SourceModifiedAt is not null &&
                                   existing.SourceModifiedAt != observed.SourceModifiedAt));
            var metadataChanged = contentChanged || existing.MimeType != observed.MimeType ||
                                  existing.SourceFileKey != observed.SourceFileKey ||
                                  existing.SourceModifiedAt != observed.SourceModifiedAt;
            if (revived)
            {
                run.RecordRevived();
            }
            else if (metadataChanged)
            {
                run.RecordUpdated();
            }

            if (mode == IndexScanMode.Apply)
            {
                existing.ApplySourceObservation(
                    observed.Size,
                    observed.MimeType,
                    observed.SourceModifiedAt,
                    observed.SourceFileKey,
                    clock.UtcNow,
                    contentChanged);
            }

            return;
        }

        if (await catalog.FindRootAsync(observed.OwnerUserId, cancellationToken) is null)
        {
            run.RecordIsolated();
            return;
        }

        var parent = await catalog.FindEntryByPathAsync(
            observed.OwnerUserId,
            observed.ParentRelativePath,
            cancellationToken);
        var parentExistsInSnapshot = await workspace.ContainsAsync(
            observed.OwnerUserId,
            observed.ParentRelativePath,
            cancellationToken);
        if ((parent is null || parent.EntryType != FileEntryType.Folder || parent.Status != FileEntryStatus.Active) &&
            !(mode == IndexScanMode.DryRun && parentExistsInSnapshot))
        {
            run.RecordIsolated();
            return;
        }

        var moveCandidates = await workspace.FindMoveCandidatesAsync(observed, cancellationToken);
        if (moveCandidates.Count == 1)
        {
            var candidate = moveCandidates[0];
            if (await catalog.HasIncompleteOperationAsync(
                    candidate.OwnerUserId,
                    candidate.Id,
                    candidate.RelativePath,
                    cancellationToken))
            {
                run.RecordIsolated();
                return;
            }

            run.RecordMoved();
            if (mode == IndexScanMode.Apply)
            {
                var entry = await catalog.FindEntryByIdAsync(candidate.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The move candidate disappeared.");
                var oldPrefix = entry.RelativePath;
                if (entry.Name != observed.Name)
                {
                    entry.Rename(FileName.Create(observed.Name), RelativeStoragePath.Create(observed.RelativePath), clock.UtcNow);
                }

                if (entry.ParentId != parent!.Id)
                {
                    entry.MoveTo(parent.Id, RelativeStoragePath.Create(observed.RelativePath), clock.UtcNow);
                }

                if (entry.EntryType == FileEntryType.Folder)
                {
                    foreach (var descendant in await catalog.ListDescendantsAsync(
                                 entry.OwnerUserId,
                                 oldPrefix,
                                 cancellationToken))
                    {
                        var suffix = descendant.RelativePath[oldPrefix.Length..];
                        descendant.RelocateDescendant(
                            RelativeStoragePath.Create(observed.RelativePath + suffix),
                            clock.UtcNow);
                    }
                }
            }

            return;
        }

        if (moveCandidates.Count > 1)
        {
            run.RecordIsolated();
            return;
        }

        run.RecordAdded();
        if (mode == IndexScanMode.Apply)
        {
            var entry = observed.EntryType == FileEntryType.Folder
                ? FileEntry.CreateFolder(
                    Guid.NewGuid(), observed.OwnerUserId, parent!.Id, FileName.Create(observed.Name),
                    RelativeStoragePath.Create(observed.RelativePath), clock.UtcNow)
                : FileEntry.CreateFile(
                    Guid.NewGuid(), observed.OwnerUserId, parent!.Id, FileName.Create(observed.Name),
                    RelativeStoragePath.Create(observed.RelativePath), observed.MimeType, observed.Size, clock.UtcNow);
            entry.ApplySourceObservation(
                observed.Size,
                observed.MimeType,
                observed.SourceModifiedAt,
                observed.SourceFileKey,
                clock.UtcNow,
                contentChanged: false);
            catalog.Add(entry);
        }
    }

    private async Task ReconcileMissingAsync(
        IndexScanRun run,
        IIndexScanWorkspace workspace,
        IndexScanMode mode,
        CancellationToken cancellationToken)
    {
        Guid? ownerCursor = null;
        string? pathCursor = null;
        while (true)
        {
            var batch = await workspace.ListUnobservedAsync(
                ownerCursor,
                pathCursor,
                options.BatchSize,
                cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var candidate in batch)
            {
                ownerCursor = candidate.OwnerUserId;
                pathCursor = candidate.RelativePath;
                if (await snapshotReader.InspectAsync(
                        RelativeStoragePath.Create(candidate.RelativePath),
                        cancellationToken) is not null ||
                    await catalog.HasIncompleteOperationAsync(
                        candidate.OwnerUserId,
                        candidate.Id,
                        candidate.RelativePath,
                        cancellationToken))
                {
                    continue;
                }

                var entry = mode == IndexScanMode.Apply
                    ? await catalog.FindEntryByIdAsync(candidate.Id, cancellationToken)
                    : null;
                switch (candidate.Status)
                {
                    case FileEntryStatus.Active:
                        run.RecordCandidate();
                        entry?.MarkMissingCandidate(run.Id, clock.UtcNow);
                        break;
                    case FileEntryStatus.MissingCandidate:
                        var detectedAt = entry?.MissingDetectedAt ?? candidate.MissingDetectedAt;
                        var observationId = entry?.MissingObservationId ?? candidate.MissingObservationId;
                        var eligible = detectedAt is not null && observationId is not null &&
                                       clock.UtcNow >= detectedAt.Value.AddMinutes(options.MissingConfirmationDelayMinutes) &&
                                       observationId != run.Id;
                        if (eligible)
                        {
                            run.RecordMissing();
                            entry?.ConfirmMissing(
                                run.Id,
                                clock.UtcNow,
                                TimeSpan.FromMinutes(options.MissingConfirmationDelayMinutes));
                        }

                        break;
                    case FileEntryStatus.Missing:
                        entry?.RecordMissingCheck(clock.UtcNow);
                        break;
                }
            }

            if (mode == IndexScanMode.Apply)
            {
                await SaveBatchAsync(run, cancellationToken);
            }

            await EnsureStorageAvailableAsync(cancellationToken);
        }
    }

    private async Task EnsureStorageAvailableAsync(CancellationToken cancellationToken)
    {
        if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
        {
            throw new IndexStorageUnavailableException();
        }
    }

    private async Task SaveBatchAsync(IndexScanRun run, CancellationToken cancellationToken)
    {
        try
        {
            await catalog.SaveChangesAsync(cancellationToken);
        }
        catch (IndexCatalogConcurrencyException)
        {
            // A newer catalog value wins; the next event or full scan will reconcile the skipped batch.
            run.RecordError();
        }
    }

    private static string ErrorCodeFor(Exception exception) => exception switch
    {
        IndexStorageUnavailableException => "STORAGE_UNAVAILABLE",
        IndexSnapshotIncompleteException => "SNAPSHOT_INCOMPLETE",
        _ => "INDEX_SCAN_FAILED",
    };

    private static IndexScanSummary ToSummary(IndexScanRun run) =>
        new(
            run.Id, run.Status, run.EnumeratedCount, run.AddedCount, run.UpdatedCount, run.MovedCount,
            run.CandidateCount, run.MissingCount, run.RevivedCount, run.IsolatedCount, run.ErrorCount,
            run.ErrorCode);

    private static void RecordMetrics(IndexScanRun run, long startedTimestamp)
    {
        var tags = new TagList
        {
            { "trigger", run.Trigger.ToString().ToUpperInvariant() },
            { "mode", run.Mode == IndexScanMode.DryRun ? "DRY_RUN" : "APPLY" },
            { "status", run.Status == IndexScanStatus.CompletedWithWarnings ? "COMPLETED_WITH_WARNINGS" : run.Status.ToString().ToUpperInvariant() },
        };
        ScanDuration.Record(Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds, tags);
        RecordCount("enumerated", run.EnumeratedCount, tags);
        RecordCount("added", run.AddedCount, tags);
        RecordCount("updated", run.UpdatedCount, tags);
        RecordCount("moved", run.MovedCount, tags);
        RecordCount("candidate", run.CandidateCount, tags);
        RecordCount("missing", run.MissingCount, tags);
        RecordCount("revived", run.RevivedCount, tags);
        RecordCount("isolated", run.IsolatedCount, tags);
        RecordCount("error", run.ErrorCount, tags);
    }

    private static void RecordCount(string result, int count, TagList tags)
    {
        tags.Add("result", result);
        ScanEntries.Add(count, tags);
        tags.RemoveAt(tags.Count - 1);
    }
}
