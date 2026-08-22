using System.Runtime.CompilerServices;
using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class IndexScanServiceTests
{
    [Fact]
    public async Task Apply_TwoIndependentAbsentScans_ProgressesCandidateToMissing()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var clock = new MutableClock(now);
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("gone.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/gone.txt"), "text/plain", 42, now);
        var catalog = new FakeCatalog([root, file]);
        var service = CreateService(catalog, new FakeSnapshot([]), clock);

        var first = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(FileEntryStatus.MissingCandidate, file.Status);
        Assert.Equal(1, first.CandidateCount);

        clock.UtcNow = now.AddMinutes(6);
        var second = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(FileEntryStatus.Missing, file.Status);
        Assert.Equal(1, second.MissingCount);
        Assert.Equal(2, catalog.Runs.Count);
    }

    [Fact]
    public async Task DryRun_ClassifiesChangesWithoutMutatingCatalogOrPersistingRun()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("gone.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/gone.txt"), "text/plain", 42, now);
        var catalog = new FakeCatalog([root, file]);
        var service = CreateService(catalog, new FakeSnapshot([]), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.DryRun),
            CancellationToken.None);

        Assert.Equal(1, summary.CandidateCount);
        Assert.Equal(FileEntryStatus.Active, file.Status);
        Assert.Empty(catalog.Runs);
    }

    [Fact]
    public async Task Apply_ObservedNewFile_AddsItBelowExistingOwnerRoot()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var observed = new ObservedStorageEntry(
            ownerId,
            RelativeStoragePath.Create($"users/{ownerId:N}/files/new.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files"),
            FileName.Create("new.txt"),
            FileEntryType.File,
            12,
            "text/plain",
            now,
            "source-key");
        var catalog = new FakeCatalog([root]);
        var service = CreateService(catalog, new FakeSnapshot([observed]), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.AddedCount);
        var added = Assert.Single(catalog.Entries, entry => entry.ParentId == root.Id);
        Assert.Equal("new.txt", added.Name);
        Assert.Equal("source-key", added.SourceFileKey);
    }

    [Fact]
    public async Task Apply_UniqueSourceIdentityMove_PreservesIdAndContentVersion()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("before.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/before.txt"), "text/plain", 12, now);
        file.ApplySourceObservation(12, "text/plain", now, "stable-key", now, contentChanged: false);
        var observed = new ObservedStorageEntry(
            ownerId,
            RelativeStoragePath.Create($"users/{ownerId:N}/files/after.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files"),
            FileName.Create("after.txt"),
            FileEntryType.File,
            12,
            "text/plain",
            now,
            "stable-key");
        var catalog = new FakeCatalog([root, file]);
        var service = CreateService(catalog, new FakeSnapshot([observed]), new MutableClock(now.AddMinutes(1)));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.MovedCount);
        Assert.Equal("after.txt", file.Name);
        Assert.Equal(1, file.FileVersion);
        Assert.Equal(FileEntryStatus.Active, file.Status);
    }

    [Fact]
    public async Task Apply_ContentMetadataChange_IncrementsVersionOnce()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("item.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/item.txt"), "text/plain", 12, now);
        file.ApplySourceObservation(12, "text/plain", now, "stable-key", now, contentChanged: false);
        var observed = new ObservedStorageEntry(
            ownerId, RelativeStoragePath.Create(file.RelativePath), RelativeStoragePath.Create(root.RelativePath),
            FileName.Create(file.Name), FileEntryType.File, 13, "text/plain", now.AddMinutes(1), "stable-key");
        var service = CreateService(new FakeCatalog([root, file]), new FakeSnapshot([observed]), new MutableClock(now.AddMinutes(1)));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.UpdatedCount);
        Assert.Equal(2, file.FileVersion);
        Assert.Equal(13, file.Size);
    }

    [Theory]
    [InlineData(StorageStatus.Unavailable)]
    [InlineData(StorageStatus.ReadOnly)]
    public async Task Apply_StorageBecomesUnavailable_DoesNotCreateMissingCandidateAndFailsRun(
        StorageStatus unavailableStatus)
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("item.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/item.txt"), "text/plain", 12, now);
        var catalog = new FakeCatalog([root, file]);
        var service = new IndexScanService(
            catalog,
            new FakeSnapshot([]),
            new SequenceGuard(StorageStatus.Available, unavailableStatus),
            new MutableClock(now),
            new IndexingOptions { BatchSize = 10, MissingConfirmationDelayMinutes = 5, StagingRetentionHours = 24 });

        await Assert.ThrowsAsync<IndexStorageUnavailableException>(() => service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None));

        Assert.Equal(FileEntryStatus.Active, file.Status);
        Assert.Equal(IndexScanStatus.Failed, Assert.Single(catalog.Runs).Status);
    }

    [Fact]
    public async Task Apply_UniqueFolderMove_RelocatesDescendantPathsWithoutChangingVersions()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("before"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/before"), now);
        folder.ApplySourceObservation(0, null, now, "folder-key", now, false);
        var child = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, folder.Id, FileName.Create("child.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/before/child.txt"), "text/plain", 2, now);
        child.ApplySourceObservation(2, "text/plain", now, "child-key", now, false);
        var observed = new[]
        {
            new ObservedStorageEntry(
                ownerId, RelativeStoragePath.Create($"users/{ownerId:N}/files/after"),
                RelativeStoragePath.Create(root.RelativePath), FileName.Create("after"), FileEntryType.Folder,
                0, null, now, "folder-key"),
            new ObservedStorageEntry(
                ownerId, RelativeStoragePath.Create($"users/{ownerId:N}/files/after/child.txt"),
                RelativeStoragePath.Create($"users/{ownerId:N}/files/after"), FileName.Create("child.txt"),
                FileEntryType.File, 2, "text/plain", now, "child-key"),
        };
        var service = CreateService(new FakeCatalog([root, folder, child]), new FakeSnapshot(observed), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.MovedCount);
        Assert.EndsWith("/after", folder.RelativePath, StringComparison.Ordinal);
        Assert.EndsWith("/after/child.txt", child.RelativePath, StringComparison.Ordinal);
        Assert.Equal(1, child.FileVersion);
    }

    [Fact]
    public async Task Metrics_UseOnlyLowCardinalityTags()
    {
        var capturedTags = new List<KeyValuePair<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "KuraStorage.Indexing")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => capturedTags.AddRange(tags.ToArray()));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => capturedTags.AddRange(tags.ToArray()));
        listener.Start();
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var root = FileEntry.CreateRoot(ownerId, now);
        var secretName = "private-name.txt";
        var observed = new ObservedStorageEntry(
            ownerId, RelativeStoragePath.Create($"users/{ownerId:N}/files/{secretName}"),
            RelativeStoragePath.Create(root.RelativePath), FileName.Create(secretName), FileEntryType.File,
            1, "text/plain", now, "private-source-key");
        var service = CreateService(new FakeCatalog([root]), new FakeSnapshot([observed]), new MutableClock(now));

        await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.DryRun),
            CancellationToken.None);

        Assert.NotEmpty(capturedTags);
        Assert.All(capturedTags, tag => Assert.Contains(tag.Key, new[] { "trigger", "mode", "status", "result" }));
        var values = string.Join('|', capturedTags.Select(tag => tag.Value?.ToString()));
        Assert.DoesNotContain(secretName, values, StringComparison.Ordinal);
        Assert.DoesNotContain(ownerId.ToString(), values, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-source-key", values, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_AmbiguousMoveIdentity_DoesNotAttachObservedPathToEitherEntry()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        FileEntry Existing(string name)
        {
            var entry = FileEntry.CreateFile(
                Guid.NewGuid(), ownerId, root.Id, FileName.Create(name),
                RelativeStoragePath.Create($"{root.RelativePath}/{name}"), "text/plain", 4, now);
            entry.ApplySourceObservation(4, "text/plain", now, "ambiguous-key", now, false);
            return entry;
        }

        var first = Existing("first.txt");
        var second = Existing("second.txt");
        var observed = new ObservedStorageEntry(
            ownerId, RelativeStoragePath.Create($"{root.RelativePath}/new.txt"),
            RelativeStoragePath.Create(root.RelativePath), FileName.Create("new.txt"), FileEntryType.File,
            4, "text/plain", now, "ambiguous-key");
        var catalog = new FakeCatalog([root, first, second]);
        var service = CreateService(catalog, new FakeSnapshot([observed]), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.IsolatedCount);
        Assert.Equal(0, summary.MovedCount);
        Assert.Equal("first.txt", first.Name);
        Assert.Equal("second.txt", second.Name);
        Assert.DoesNotContain(catalog.Entries, entry => entry.Name == "new.txt");
    }

    [Fact]
    public async Task Apply_IncompleteFileOperation_PreventsMissingTransition()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("busy.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/busy.txt"), "text/plain", 1, now);
        var catalog = new FakeCatalog([root, file]);
        catalog.IncompleteEntryIds.Add(file.Id);
        var service = CreateService(catalog, new FakeSnapshot([]), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(FileEntryStatus.Active, file.Status);
        Assert.Equal(0, summary.CandidateCount);
    }

    [Fact]
    public async Task Apply_FileReappearsAfterEnumeration_FinalInspectionPreventsMissingTransition()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("raced.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/raced.txt"), "text/plain", 1, now);
        var reappeared = new ObservedStorageEntry(
            ownerId, RelativeStoragePath.Create(file.RelativePath), RelativeStoragePath.Create(root.RelativePath),
            FileName.Create(file.Name), FileEntryType.File, 1, "text/plain", now, "key");
        var service = CreateService(
            new FakeCatalog([root, file]),
            new FakeSnapshot([], reappeared),
            new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(FileEntryStatus.Active, file.Status);
        Assert.Equal(0, summary.CandidateCount);
    }

    [Fact]
    public async Task Apply_UnknownOwnerNamespace_IsIsolatedWithoutPublishingEntry()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var observed = new ObservedStorageEntry(
            ownerId, RelativeStoragePath.Create($"users/{ownerId:N}/files/item.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files"), FileName.Create("item.txt"),
            FileEntryType.File, 1, "text/plain", now, "key");
        var catalog = new FakeCatalog([]);
        var service = CreateService(catalog, new FakeSnapshot([observed]), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.IsolatedCount);
        Assert.Empty(catalog.Entries);
    }

    [Fact]
    public async Task Apply_OrphanedObservedEntry_IsIsolated()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var observed = new ObservedStorageEntry(
            ownerId, RelativeStoragePath.Create($"{root.RelativePath}/missing-parent/item.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/missing-parent"), FileName.Create("item.txt"),
            FileEntryType.File, 1, "text/plain", now, "key");
        var catalog = new FakeCatalog([root]);
        var service = CreateService(catalog, new FakeSnapshot([observed]), new MutableClock(now));

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);

        Assert.Equal(1, summary.IsolatedCount);
        Assert.Single(catalog.Entries);
    }

    [Fact]
    public async Task DryRun_ThreeHundredThousandEntries_AreStagedInBoundedBatches()
    {
        const int entryCount = 300_000;
        const int batchSize = 500;
        var catalog = new FakeCatalog([]) { DiscardStagedEntries = true };
        var service = new IndexScanService(
            catalog,
            new GeneratedSnapshot(entryCount),
            new AvailableGuard(),
            new MutableClock(DateTimeOffset.UtcNow),
            new IndexingOptions
            {
                BatchSize = batchSize,
                MissingConfirmationDelayMinutes = 5,
                StagingRetentionHours = 24,
            });

        var summary = await service.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.DryRun),
            CancellationToken.None);

        Assert.Equal(entryCount, summary.EnumeratedCount);
        Assert.Equal(entryCount, catalog.TotalStagedEntries);
        Assert.InRange(catalog.MaximumStagedBatch, 1, batchSize);
    }

    private static IndexScanService CreateService(
        FakeCatalog catalog,
        FakeSnapshot snapshot,
        MutableClock clock) =>
        new(
            catalog,
            snapshot,
            new AvailableGuard(),
            clock,
            new IndexingOptions
            {
                BatchSize = 10,
                MissingConfirmationDelayMinutes = 5,
                StagingRetentionHours = 24,
            });

    private sealed class MutableClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class AvailableGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }

    private sealed class SequenceGuard(params StorageStatus[] statuses) : IStorageGuard
    {
        private int index;

        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken)
        {
            var selected = statuses[Math.Min(index, statuses.Length - 1)];
            index++;
            return Task.FromResult(selected);
        }
    }

    private sealed class FakeSnapshot(
        IReadOnlyList<ObservedStorageEntry> entries,
        ObservedStorageEntry? inspectOverride = null)
        : IManagedFileSystemSnapshotReader
    {
        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
            StorageSnapshotContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
            }

            await Task.CompletedTask;
        }

        public Task<ObservedStorageEntry?> InspectAsync(
            RelativeStoragePath path,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                inspectOverride?.RelativePath == path
                    ? inspectOverride
                    : entries.SingleOrDefault(entry => entry.RelativePath == path));
    }

    private sealed class GeneratedSnapshot(int count) : IManagedFileSystemSnapshotReader
    {
        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
            StorageSnapshotContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = $"item-{index:D6}.txt";
                yield return new ObservedStorageEntry(
                    ownerId,
                    RelativeStoragePath.Create($"users/{ownerId:N}/files/{name}"),
                    RelativeStoragePath.Create($"users/{ownerId:N}/files"),
                    FileName.Create(name),
                    FileEntryType.File,
                    1,
                    "text/plain",
                    DateTimeOffset.UnixEpoch,
                    null);
            }

            await Task.CompletedTask;
        }

        public Task<ObservedStorageEntry?> InspectAsync(
            RelativeStoragePath path,
            CancellationToken cancellationToken) => Task.FromResult<ObservedStorageEntry?>(null);
    }

    private sealed class FakeCatalog(IEnumerable<FileEntry> entries) : IIndexCatalogRepository
    {
        public List<FileEntry> Entries { get; } = [.. entries];
        public List<IndexScanRun> Runs { get; } = [];
        public HashSet<Guid> IncompleteEntryIds { get; } = [];
        public bool DiscardStagedEntries { get; init; }
        public int TotalStagedEntries { get; private set; }
        public int MaximumStagedBatch { get; private set; }

        public Task<IIndexScanLock?> TryAcquireScanLockAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IIndexScanLock?>(new FakeLock());

        public Task<IIndexScanWorkspace> CreateWorkspaceAsync(
            Guid scanId,
            IndexScanMode mode,
            CancellationToken cancellationToken) =>
            Task.FromResult<IIndexScanWorkspace>(new FakeWorkspace(this));

        public Task<FileEntry?> FindEntryByPathAsync(
            Guid ownerUserId,
            string relativePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry =>
                entry.OwnerUserId == ownerUserId && entry.RelativePath == relativePath &&
                entry.Status != FileEntryStatus.Trashed));

        public Task<FileEntry?> FindEntryByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.Id == id));

        public Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry =>
                entry.OwnerUserId == ownerUserId && entry.ParentId is null && entry.Status == FileEntryStatus.Active));

        public Task<bool> HasIncompleteOperationAsync(
            Guid ownerUserId,
            Guid entryId,
            string relativePath,
            CancellationToken cancellationToken) => Task.FromResult(IncompleteEntryIds.Contains(entryId));

        public Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(
            Guid ownerUserId,
            string relativePathPrefix,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(Entries.Where(entry =>
                entry.OwnerUserId == ownerUserId && entry.RelativePath.StartsWith(relativePathPrefix + "/")).ToList());

        public void Add(FileEntry entry) => Entries.Add(entry);
        public void Add(IndexScanRun run) => Runs.Add(run);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CleanupStagingAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class FakeWorkspace(FakeCatalog catalog) : IIndexScanWorkspace
        {
            private readonly List<ObservedStorageEntry> staged = [];

            public Task StageAsync(IReadOnlyList<ObservedStorageEntry> entries, CancellationToken cancellationToken)
            {
                catalog.TotalStagedEntries += entries.Count;
                catalog.MaximumStagedBatch = Math.Max(catalog.MaximumStagedBatch, entries.Count);
                if (!catalog.DiscardStagedEntries)
                {
                    staged.AddRange(entries);
                }

                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<StagedIndexEntry>> ListStagedAsync(
                string? afterRelativePath,
                int take,
                CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<StagedIndexEntry>>(staged
                    .Where(entry => afterRelativePath is null || string.CompareOrdinal(entry.RelativePath.Value, afterRelativePath) > 0)
                    .OrderBy(entry => entry.RelativePath.Value, StringComparer.Ordinal)
                    .Take(take)
                    .Select(ToStaged)
                    .ToList());

            public Task<IReadOnlyList<IndexedCatalogEntry>> ListUnobservedAsync(
                Guid? afterOwnerUserId,
                string? afterRelativePath,
                int take,
                CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<IndexedCatalogEntry>>(catalog.Entries
                    .Where(entry => entry.ParentId is not null && entry.Status != FileEntryStatus.Trashed)
                    .Where(entry => !staged.Any(item => item.OwnerUserId == entry.OwnerUserId && item.RelativePath.Value == entry.RelativePath))
                    .Where(entry => afterOwnerUserId is null || entry.OwnerUserId.CompareTo(afterOwnerUserId.Value) > 0 ||
                                    (entry.OwnerUserId == afterOwnerUserId && string.CompareOrdinal(entry.RelativePath, afterRelativePath) > 0))
                    .OrderBy(entry => entry.OwnerUserId)
                    .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
                    .Take(take)
                    .Select(ToIndexed)
                    .ToList());

            public Task<bool> ContainsAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken) =>
                Task.FromResult(staged.Any(entry => entry.OwnerUserId == ownerUserId && entry.RelativePath.Value == relativePath));

            public Task<IReadOnlyList<IndexedCatalogEntry>> FindMoveCandidatesAsync(
                StagedIndexEntry observed,
                CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<IndexedCatalogEntry>>(catalog.Entries
                    .Where(entry => entry.ParentId is not null && entry.Status == FileEntryStatus.Active)
                    .Where(entry => entry.OwnerUserId == observed.OwnerUserId &&
                                    entry.EntryType == observed.EntryType &&
                                    entry.SourceFileKey == observed.SourceFileKey &&
                                    entry.Size == observed.Size &&
                                    entry.SourceModifiedAt == observed.SourceModifiedAt &&
                                    entry.RelativePath != observed.RelativePath &&
                                    !staged.Any(item => item.RelativePath.Value == entry.RelativePath))
                    .Take(2)
                    .Select(ToIndexed)
                    .ToList());

            public Task ClearAsync(CancellationToken cancellationToken)
            {
                staged.Clear();
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            private static StagedIndexEntry ToStaged(ObservedStorageEntry entry) =>
                new(entry.OwnerUserId, entry.RelativePath.Value, entry.ParentRelativePath.Value, entry.Name.Value,
                    entry.EntryType, entry.Size, entry.MimeType, entry.SourceModifiedAt, entry.SourceFileKey,
                    entry.IsolationReason);

            private static IndexedCatalogEntry ToIndexed(FileEntry entry) =>
                new(entry.Id, entry.OwnerUserId, entry.RelativePath, entry.EntryType, entry.Status,
                    entry.SourceFileKey, entry.Size, entry.SourceModifiedAt, entry.MissingDetectedAt,
                    entry.MissingObservationId);
        }

        private sealed class FakeLock : IIndexScanLock
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
