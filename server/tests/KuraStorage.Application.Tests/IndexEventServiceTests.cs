using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class IndexEventServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reconcile_NewFile_AddsCatalogEntry()
    {
        var fixture = new Fixture();
        var path = fixture.Path("new.txt");
        fixture.Snapshot.Entries[path] = fixture.Observed("new.txt", size: 4);

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, path, ContentMayHaveChanged: true),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Applied, result);
        var entry = Assert.Single(fixture.Repository.Entries, entry => entry.Name == "new.txt");
        Assert.Equal(FileEntryStatus.Active, entry.Status);
        Assert.Equal(4, entry.Size);
    }

    [Fact]
    public async Task Reconcile_CloseWrite_UpdatesContentVersionOncePerCoalescedEvent()
    {
        var fixture = new Fixture();
        var entry = fixture.AddFile("existing.txt", size: 2);
        fixture.Snapshot.Entries[entry.RelativePath] = fixture.Observed("existing.txt", size: 3);

        await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, entry.RelativePath, ContentMayHaveChanged: true),
            CancellationToken.None);

        Assert.Equal(2, entry.FileVersion);
        Assert.Equal(3, entry.Size);
    }

    [Fact]
    public async Task Reconcile_DeleteEvent_OnlyCreatesMissingCandidate()
    {
        var fixture = new Fixture();
        var entry = fixture.AddFile("gone.txt", size: 2);

        var first = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, entry.RelativePath),
            CancellationToken.None);
        var duplicate = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, entry.RelativePath),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Applied, first);
        Assert.Equal(IndexEventResult.Deferred, duplicate);
        Assert.Equal(FileEntryStatus.MissingCandidate, entry.Status);
    }

    [Fact]
    public async Task Reconcile_PairedMove_PreservesIdentityAndDoesNotIncrementVersion()
    {
        var fixture = new Fixture();
        var entry = fixture.AddFile("before.txt", size: 2);
        var oldId = entry.Id;
        var target = fixture.Path("after.txt");
        fixture.Snapshot.Entries[target] = fixture.Observed("after.txt", size: 2);

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Move, target, entry.RelativePath),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Applied, result);
        Assert.Equal(oldId, entry.Id);
        Assert.Equal(target, entry.RelativePath);
        Assert.Equal(1, entry.FileVersion);
    }

    [Fact]
    public async Task Reconcile_WhenStorageUnavailable_DefersWithoutMissingTransition()
    {
        var fixture = new Fixture { Storage = { Status = StorageStatus.Unavailable } };
        var entry = fixture.AddFile("safe.txt", size: 2);

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, entry.RelativePath),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Deferred, result);
        Assert.Equal(FileEntryStatus.Active, entry.Status);
    }

    [Fact]
    public async Task Reconcile_Overflow_RequestsFullRescanWithoutCatalogAccess()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Overflow, string.Empty),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.RescanRequired, result);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Reconcile_OutOfOrderDeleteAfterRecreate_UsesCurrentFileAndKeepsSameIdentity()
    {
        var fixture = new Fixture();
        var entry = fixture.AddFile("recreated.txt", size: 2);
        entry.MarkMissingCandidate(Guid.NewGuid(), Now);
        fixture.Snapshot.Entries[entry.RelativePath] = fixture.Observed("recreated.txt", size: 5);

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, entry.RelativePath),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Applied, result);
        Assert.Equal(FileEntryStatus.Active, entry.Status);
        Assert.Equal(5, entry.Size);
        Assert.Single(fixture.Repository.Entries, candidate => candidate.Name == "recreated.txt");
    }

    [Fact]
    public async Task Reconcile_IncompleteInternalOperation_DefersWithoutChangingEntry()
    {
        var fixture = new Fixture();
        var entry = fixture.AddFile("internal.txt", size: 2);
        fixture.Repository.HasIncompleteOperation = true;

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Reconcile, entry.RelativePath),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Deferred, result);
        Assert.Equal(FileEntryStatus.Active, entry.Status);
    }

    [Fact]
    public async Task Reconcile_MoveBetweenFolders_UpdatesParentAndPreservesIdentity()
    {
        var fixture = new Fixture();
        var entry = fixture.AddFile("source.txt", size: 2);
        var folder = fixture.AddFolder("target");
        var targetPath = $"{folder.RelativePath}/source.txt";
        fixture.Snapshot.Entries[targetPath] = new ObservedStorageEntry(
            entry.OwnerUserId,
            RelativeStoragePath.Create(targetPath),
            RelativeStoragePath.Create(folder.RelativePath),
            FileName.Create("source.txt"),
            FileEntryType.File,
            2,
            "text/plain",
            Now.AddMinutes(1),
            "00000001:00000001:0000000000000001");

        var result = await fixture.Service.ReconcileAsync(
            new IndexChangeEvent(IndexChangeKind.Move, targetPath, entry.RelativePath),
            CancellationToken.None);

        Assert.Equal(IndexEventResult.Applied, result);
        Assert.Equal(folder.Id, entry.ParentId);
        Assert.Equal(targetPath, entry.RelativePath);
    }

    private sealed class Fixture
    {
        private readonly Guid ownerId = Guid.NewGuid();

        public Fixture()
        {
            Root = FileEntry.CreateRoot(ownerId, Now);
            Repository.Entries.Add(Root);
            Service = new IndexEventService(Repository, Snapshot, Storage, new FixedClock(Now));
        }

        public FakeRepository Repository { get; } = new();
        public FakeSnapshot Snapshot { get; } = new();
        public FakeStorageGuard Storage { get; } = new();
        public FileEntry Root { get; }
        public IndexEventService Service { get; }

        public string Path(string name) => $"users/{ownerId:N}/files/{name}";

        public FileEntry AddFile(string name, long size)
        {
            var entry = FileEntry.CreateFile(
                Guid.NewGuid(), ownerId, Root.Id, FileName.Create(name),
                RelativeStoragePath.Create(Path(name)), "text/plain", size, Now);
            Repository.Entries.Add(entry);
            return entry;
        }

        public FileEntry AddFolder(string name)
        {
            var entry = FileEntry.CreateFolder(
                Guid.NewGuid(), ownerId, Root.Id, FileName.Create(name),
                RelativeStoragePath.Create(Path(name)), Now);
            Repository.Entries.Add(entry);
            return entry;
        }

        public ObservedStorageEntry Observed(string name, long size) =>
            new(
                ownerId,
                RelativeStoragePath.Create(Path(name)),
                RelativeStoragePath.Create($"users/{ownerId:N}/files"),
                FileName.Create(name),
                FileEntryType.File,
                size,
                "text/plain",
                Now.AddMinutes(1),
                "00000001:00000001:0000000000000001");
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeStorageGuard : IStorageGuard
    {
        public StorageStatus Status { get; set; } = StorageStatus.Available;
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(Status);
    }

    private sealed class FakeSnapshot : IManagedFileSystemSnapshotReader
    {
        public Dictionary<string, ObservedStorageEntry> Entries { get; } = new(StringComparer.Ordinal);

        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
            StorageSnapshotContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in Entries.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
                await Task.Yield();
            }
        }

        public Task<ObservedStorageEntry?> InspectAsync(
            RelativeStoragePath path,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entries.GetValueOrDefault(path.Value));
    }

    private sealed class FakeRepository : IIndexCatalogRepository
    {
        public List<FileEntry> Entries { get; } = [];
        public int SaveCount { get; private set; }
        public bool HasIncompleteOperation { get; set; }

        public Task<FileEntry?> FindEntryByPathAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry =>
                entry.OwnerUserId == ownerUserId && entry.RelativePath == relativePath));

        public Task<FileEntry?> FindEntryByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.Id == id));

        public Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.OwnerUserId == ownerUserId && entry.ParentId is null));

        public Task<bool> HasIncompleteOperationAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(HasIncompleteOperation);

        public Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(Guid ownerUserId, string relativePathPrefix, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(Entries.Where(entry =>
                entry.OwnerUserId == ownerUserId && entry.RelativePath.StartsWith(relativePathPrefix + "/", StringComparison.Ordinal)).ToArray());
        public Task<(int CandidateCount, int MissingCount)> CountMissingStatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult((
                Entries.Count(entry => entry.Status == FileEntryStatus.MissingCandidate),
                Entries.Count(entry => entry.Status == FileEntryStatus.Missing)));

        public void Add(FileEntry entry) => Entries.Add(entry);
        public void Add(IndexScanRun run) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) { SaveCount++; return Task.CompletedTask; }
        public Task CleanupStagingAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IIndexScanLock?> TryAcquireScanLockAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IIndexScanWorkspace> CreateWorkspaceAsync(Guid scanId, IndexScanMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
