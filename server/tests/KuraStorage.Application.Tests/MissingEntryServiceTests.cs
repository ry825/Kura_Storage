using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class MissingEntryServiceTests
{
    [Fact]
    public async Task RecheckAsync_ReappearedFile_RevivesSameEntry()
    {
        var fixture = Fixture.Create();
        var observed = fixture.Observe(fixture.File, size: 9);
        fixture.Reader.Result = observed;

        var result = await fixture.Service.RecheckAsync(fixture.Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.File.Id, result.Value!.Id);
        Assert.Equal("ACTIVE", result.Value.Status);
        Assert.Equal(9, result.Value.Size);
        Assert.Null(result.Value.MissingDetectedAt);
        Assert.Contains(fixture.Repository.Audits, audit => audit.Action == "FILE_MISSING_RECHECK");
    }

    [Fact]
    public async Task RecheckAsync_AbsentCandidateBeforeDelay_OnlyUpdatesLastCheck()
    {
        var fixture = Fixture.Create(candidateAge: TimeSpan.FromMinutes(2));

        var result = await fixture.Service.RecheckAsync(fixture.Command, CancellationToken.None);

        Assert.Equal("MISSING_CANDIDATE", result.Value!.Status);
        Assert.Equal(fixture.Now, result.Value.MissingLastCheckedAt);
    }

    [Fact]
    public async Task RecheckAsync_AbsentCandidateAfterDelay_ConfirmsMissing()
    {
        var fixture = Fixture.Create(candidateAge: TimeSpan.FromMinutes(6));

        var result = await fixture.Service.RecheckAsync(fixture.Command, CancellationToken.None);

        Assert.Equal("MISSING", result.Value!.Status);
        Assert.Equal(fixture.Now, result.Value.MissingLastCheckedAt);
    }

    [Fact]
    public async Task RecheckAsync_StorageUnavailable_DoesNotInspectOrAdvance()
    {
        var fixture = Fixture.Create(storageStatus: StorageStatus.Unavailable);

        var result = await fixture.Service.RecheckAsync(fixture.Command, CancellationToken.None);

        Assert.Equal(FileErrorCodes.StorageUnavailable, result.Failure!.Code);
        Assert.Equal(0, fixture.Reader.InspectCount);
        Assert.Equal(FileEntryStatus.MissingCandidate, fixture.File.Status);
    }

    [Fact]
    public async Task DeleteIndexEntryAsync_MissingFolder_RemovesDeepestFirstWithoutFilesystemBoundary()
    {
        var fixture = Fixture.Create(missing: true, folder: true, withMissingChild: true);

        var result = await fixture.Service.DeleteIndexEntryAsync(fixture.Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            fixture.Repository.Entries,
            entry => entry.Id == fixture.File.Id || entry.ParentId == fixture.File.Id);
        Assert.Single(fixture.Participant.Targets);
        Assert.Equal(0, fixture.Reader.InspectCount);
        Assert.Contains(fixture.Repository.Audits, audit => audit.Action == "FILE_MISSING_INDEX_DELETE");
    }

    [Fact]
    public async Task DeleteIndexEntryAsync_PartiallyReappearedFolder_ReturnsConflictAndKeepsTree()
    {
        var fixture = Fixture.Create(missing: true, folder: true, withMissingChild: true);
        fixture.Child!.ApplySourceObservation(1, null, fixture.Now, null, fixture.Now, false);

        var result = await fixture.Service.DeleteIndexEntryAsync(fixture.Command, CancellationToken.None);

        Assert.Equal(FileErrorCodes.FileStateConflict, result.Failure!.Code);
        Assert.Contains(fixture.File, fixture.Repository.Entries);
        Assert.Empty(fixture.Participant.Targets);
    }

    [Fact]
    public async Task DeleteIndexEntryAsync_SecondRequestAndOtherOwner_AreIndistinguishableNotFound()
    {
        var fixture = Fixture.Create(missing: true);
        Assert.True((await fixture.Service.DeleteIndexEntryAsync(fixture.Command, CancellationToken.None)).IsSuccess);

        var repeated = await fixture.Service.DeleteIndexEntryAsync(fixture.Command, CancellationToken.None);
        var otherOwner = await fixture.Service.DeleteIndexEntryAsync(
            fixture.Command with { OwnerUserId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(FileErrorCodes.FileNotFound, repeated.Failure!.Code);
        Assert.Equal(FileErrorCodes.FileNotFound, otherOwner.Failure!.Code);
    }

    private sealed class Fixture
    {
        private Fixture(
            DateTimeOffset now,
            FileEntry root,
            FileEntry file,
            FileEntry? child,
            FakeRepository repository,
            FakeReader reader,
            RecordingParticipant participant,
            MissingEntryService service)
        {
            Now = now;
            Root = root;
            File = file;
            Child = child;
            Repository = repository;
            Reader = reader;
            Participant = participant;
            Service = service;
            Command = new MissingFileCommand(file.OwnerUserId, Guid.NewGuid(), file.Id, "request-missing");
        }

        public DateTimeOffset Now { get; }
        public FileEntry Root { get; }
        public FileEntry File { get; }
        public FileEntry? Child { get; }
        public FakeRepository Repository { get; }
        public FakeReader Reader { get; }
        public RecordingParticipant Participant { get; }
        public MissingEntryService Service { get; }
        public MissingFileCommand Command { get; }

        public static Fixture Create(
            TimeSpan? candidateAge = null,
            StorageStatus storageStatus = StorageStatus.Available,
            bool missing = false,
            bool folder = false,
            bool withMissingChild = false)
        {
            var now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
            var owner = Guid.NewGuid();
            var root = FileEntry.CreateRoot(owner, now.AddHours(-1));
            var path = RelativeStoragePath.Create($"{root.RelativePath}/{(folder ? "Gone" : "gone.txt")}");
            var file = folder
                ? FileEntry.CreateFolder(Guid.NewGuid(), owner, root.Id, FileName.Create("Gone"), path, now.AddHours(-1))
                : FileEntry.CreateFile(Guid.NewGuid(), owner, root.Id, FileName.Create("gone.txt"), path, null, 1, now.AddHours(-1));
            MakeMissing(file, now - (candidateAge ?? TimeSpan.FromMinutes(10)), missing || folder);
            FileEntry? child = null;
            if (withMissingChild)
            {
                child = FileEntry.CreateFile(
                    Guid.NewGuid(), owner, file.Id, FileName.Create("child.txt"),
                    RelativeStoragePath.Create($"{file.RelativePath}/child.txt"), null, 1, now.AddHours(-1));
                MakeMissing(child, now.AddMinutes(-10), true);
            }

            var repository = new FakeRepository([root, file, .. (child is null ? [] : new[] { child })]);
            var reader = new FakeReader();
            var participant = new RecordingParticipant();
            var service = new MissingEntryService(
                repository,
                reader,
                new FixedStorageGuard(storageStatus),
                [participant],
                new FixedClock(now),
                new IndexingOptions { MissingConfirmationDelayMinutes = 5 });
            return new Fixture(now, root, file, child, repository, reader, participant, service);
        }

        public ObservedStorageEntry Observe(FileEntry entry, long size) =>
            new(
                entry.OwnerUserId,
                RelativeStoragePath.Create(entry.RelativePath),
                RelativeStoragePath.Create(entry.RelativePath[..entry.RelativePath.LastIndexOf('/')]),
                FileName.Create(entry.Name),
                entry.EntryType,
                size,
                entry.MimeType,
                Now,
                "source-key");

        private static void MakeMissing(FileEntry entry, DateTimeOffset detectedAt, bool confirm)
        {
            var first = Guid.NewGuid();
            entry.MarkMissingCandidate(first, detectedAt);
            if (confirm)
            {
                entry.ConfirmMissing(Guid.NewGuid(), detectedAt.AddMinutes(5), TimeSpan.FromMinutes(5));
            }
        }
    }

    private sealed class FakeRepository(IEnumerable<FileEntry> entries) : IFileRepository
    {
        public List<FileEntry> Entries { get; } = [.. entries];
        public List<AuditLog> Audits { get; } = [];
        public Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IFileTransaction>(new NoOpTransaction());
        public Task<FileEntry?> FindOwnedAsync(Guid ownerUserId, Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.OwnerUserId == ownerUserId && entry.Id == entryId));
        public Task<bool> ReloadAsync(FileEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.Contains(entry));
        public Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntry?> FindActiveChildAsync(Guid ownerUserId, Guid parentId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntry?> FindActiveFolderByPathAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsRelocationBlockedAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasIncompleteOperationAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IFileMutationLock> AcquireMutationLocksAsync(IEnumerable<Guid> entryIds, CancellationToken cancellationToken) =>
            Task.FromResult<IFileMutationLock>(new NoOpMutationLock());
        public Task<IReadOnlyList<FileEntry>> ListActiveChildrenAsync(Guid ownerUserId, Guid parentId, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountActiveChildrenAsync(Guid ownerUserId, Guid parentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileEntry>> ListTrashedAsync(Guid ownerUserId, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(Guid ownerUserId, string relativePathPrefix, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(Entries.Where(entry => entry.OwnerUserId == ownerUserId && entry.RelativePath.StartsWith(relativePathPrefix + "/", StringComparison.Ordinal)).ToArray());
        public Task<FileOperation?> FindOperationAsync(Guid ownerUserId, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult<FileOperation?>(null);
        public Task<IReadOnlyList<FileOperation>> ListIncompleteOperationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileOperation>>([]);
        public void Remove(FileEntry entry) => Entries.Remove(entry);
        public void RemoveRange(IEnumerable<FileEntry> removed) { foreach (var entry in removed.ToArray()) Entries.Remove(entry); }
        public void Add(FileEntry entry) => Entries.Add(entry);
        public void Add(FileOperation operation) { }
        public void Add(AuditLog auditLog) => Audits.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeReader : IManagedFileSystemSnapshotReader
    {
        public ObservedStorageEntry? Result { get; set; }
        public int InspectCount { get; private set; }
        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(StorageSnapshotContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<ObservedStorageEntry?> InspectAsync(RelativeStoragePath path, CancellationToken cancellationToken) { InspectCount++; return Task.FromResult(Result); }
    }

    private sealed class RecordingParticipant : IFileIndexDeletionParticipant
    {
        public List<FileIndexDeletionTarget> Targets { get; } = [];
        public Task DeleteManagementDataAsync(FileIndexDeletionTarget target, CancellationToken cancellationToken) { Targets.Add(target); return Task.CompletedTask; }
    }

    private sealed class FixedStorageGuard(StorageStatus status) : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) => Task.FromResult(status);
    }
    private sealed class FixedClock(DateTimeOffset now) : ISystemClock { public DateTimeOffset UtcNow => now; }
    private sealed class NoOpTransaction : IFileTransaction { public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class NoOpMutationLock : IFileMutationLock { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
