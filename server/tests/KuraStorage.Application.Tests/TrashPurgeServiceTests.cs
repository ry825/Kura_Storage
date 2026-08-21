using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class TrashPurgeServiceTests
{
    [Fact]
    public async Task PurgeAsync_TrashedFolder_DeletesTreeCatalogAndWritesMinimalAudit()
    {
        var now = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        var owner = Guid.NewGuid();
        var device = Guid.NewGuid();
        var root = FileEntry.CreateFolder(
            Guid.NewGuid(), owner, Guid.NewGuid(), FileName.Create("private-name"),
            RelativeStoragePath.Create($"users/{owner:N}/files/private-name"), now);
        var child = FileEntry.CreateFile(
            Guid.NewGuid(), owner, root.Id, FileName.Create("secret.txt"),
            RelativeStoragePath.Create($"users/{owner:N}/files/private-name/secret.txt"),
            "text/plain", 12, now);
        var trashPath = RelativeStoragePath.Create($"users/{owner:N}/trash/{root.Id:N}/private-name");
        root.Trash(trashPath, now);
        child.TrashDescendant(trashPath.Append(FileName.Create("secret.txt")), now);
        var repository = new FakeRepository(root, child);
        var store = new FakeStore($"users/{owner:N}/trash/{root.Id:N}");
        var participant = new RecordingParticipant();
        var service = Create(repository, store, participant, now);

        var result = await service.PurgeAsync(
            new PurgeFileCommand(owner, device, root.Id, Guid.NewGuid().ToString(), "request-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.Entries);
        Assert.DoesNotContain($"users/{owner:N}/trash/{root.Id:N}", store.Paths);
        Assert.Equal(["list", "management"], participant.Calls);
        var operation = Assert.Single(repository.Operations);
        Assert.Equal(FileOperationType.Purge, operation.OperationType);
        Assert.Equal(FileOperationStatus.Completed, operation.Status);
        Assert.Equal(12, operation.ExpectedSize);
        var audit = Assert.Single(repository.Audits);
        Assert.Equal("FILE_PURGE_MANUAL", audit.Action);
        Assert.Equal(AuditActorType.UserDevice, audit.ActorType);
        Assert.DoesNotContain("private-name", string.Join('|', audit.Action, audit.TargetId, audit.ResultCode, audit.RequestId));
    }

    [Fact]
    public async Task PurgeAsync_CompletedRetry_IsIdempotent()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var device = Guid.NewGuid();
        var entry = TrashedFile(owner, now);
        var repository = new FakeRepository(entry);
        var store = new FakeStore(Container(entry));
        var service = Create(repository, store, new RecordingParticipant(), now);
        var command = new PurgeFileCommand(owner, device, entry.Id, Guid.NewGuid().ToString(), "request");

        Assert.True((await service.PurgeAsync(command, CancellationToken.None)).IsSuccess);
        Assert.True((await service.PurgeAsync(command, CancellationToken.None)).IsSuccess);

        Assert.Single(repository.Operations);
        Assert.Single(repository.Audits);
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task PurgeAsync_KeyReusedForAnotherTarget_ReturnsConflict()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var first = TrashedFile(owner, now);
        var second = TrashedFile(owner, now);
        var repository = new FakeRepository(first, second);
        var store = new FakeStore(Container(first), Container(second));
        var service = Create(repository, store, new RecordingParticipant(), now);
        var key = Guid.NewGuid().ToString();
        Assert.True((await service.PurgeAsync(
            new PurgeFileCommand(owner, Guid.NewGuid(), first.Id, key, "first"), CancellationToken.None)).IsSuccess);

        var result = await service.PurgeAsync(
            new PurgeFileCommand(owner, Guid.NewGuid(), second.Id, key, "second"), CancellationToken.None);

        Assert.Equal(FileErrorCodes.IdempotencyConflict, result.Failure?.Code);
        Assert.Contains(repository.Entries, item => item.Id == second.Id);
    }

    [Fact]
    public async Task PurgeAsync_ActiveOrForeignEntry_ReturnsNonDisclosingNotFound()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var active = FileEntry.CreateFile(
            Guid.NewGuid(), owner, Guid.NewGuid(), FileName.Create("active.txt"),
            RelativeStoragePath.Create($"users/{owner:N}/files/active.txt"), null, 1, now);
        var repository = new FakeRepository(active);
        var service = Create(repository, new FakeStore(), new RecordingParticipant(), now);

        var activeResult = await service.PurgeAsync(
            new PurgeFileCommand(owner, Guid.NewGuid(), active.Id, Guid.NewGuid().ToString(), "active"),
            CancellationToken.None);
        var foreignResult = await service.PurgeAsync(
            new PurgeFileCommand(Guid.NewGuid(), Guid.NewGuid(), active.Id, Guid.NewGuid().ToString(), "foreign"),
            CancellationToken.None);

        Assert.Equal(FileErrorCodes.FileNotFound, activeResult.Failure?.Code);
        Assert.Equal(FileErrorCodes.FileNotFound, foreignResult.Failure?.Code);
    }

    [Fact]
    public async Task RecoverAsync_PendingPurge_DeletesAgainAndCompletesWithOriginalActor()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var device = Guid.NewGuid();
        var entry = TrashedFile(owner, now);
        var operation = new FileOperation(
            Guid.NewGuid(), owner, FileOperationType.Purge, entry.Id, Guid.NewGuid().ToString(),
            Container(entry), null, entry.Size, null, now, device, "original-request", "USER");
        var repository = new FakeRepository(entry);
        repository.Operations.Add(operation);
        var store = new FakeStore(Container(entry));

        await Create(repository, store, new RecordingParticipant(), now)
            .RecoverAsync(operation, CancellationToken.None);

        Assert.Equal(FileOperationStatus.Completed, operation.Status);
        Assert.Empty(repository.Entries);
        var audit = Assert.Single(repository.Audits);
        Assert.Equal("FILE_PURGE_MANUAL", audit.Action);
        Assert.Equal(device, audit.ActorDeviceId);
        Assert.Equal("original-request", audit.RequestId);
    }

    [Fact]
    public async Task RecoverAsync_UnsafeTree_QuarantinesWithoutDeletingCatalog()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var entry = TrashedFile(owner, now);
        var operation = new FileOperation(
            Guid.NewGuid(), owner, FileOperationType.Purge, entry.Id, Guid.NewGuid().ToString(),
            Container(entry), null, entry.Size, null, now);
        var repository = new FakeRepository(entry);
        repository.Operations.Add(operation);
        var store = new FakeStore(Container(entry))
        {
            DeleteException = new UnsafeStorageTreeException("unsafe"),
        };

        await Create(repository, store, new RecordingParticipant(), now)
            .RecoverAsync(operation, CancellationToken.None);

        Assert.Equal(FileOperationStatus.RecoveryRequired, operation.Status);
        Assert.Single(repository.Entries);
        Assert.Equal(FileErrorCodes.RecoveryRequired, Assert.Single(repository.Audits).ResultCode);
    }

    [Fact]
    public async Task RecoverAsync_RetryableIoFailure_LeavesPendingForNextRun()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var entry = TrashedFile(owner, now);
        var operation = new FileOperation(
            Guid.NewGuid(), owner, FileOperationType.Purge, entry.Id, Guid.NewGuid().ToString(),
            Container(entry), null, entry.Size, null, now);
        var repository = new FakeRepository(entry);
        repository.Operations.Add(operation);
        var service = Create(
            repository,
            new FakeStore(Container(entry)) { DeleteException = new IOException("retry") },
            new RecordingParticipant(),
            now);

        await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync(operation, CancellationToken.None));

        Assert.Equal(FileOperationStatus.Pending, operation.Status);
        Assert.Single(repository.Entries);
        Assert.Empty(repository.Audits);
    }

    private static TrashPurgeService Create(
        FakeRepository repository,
        FakeStore store,
        IPermanentDeleteParticipant participant,
        DateTimeOffset now) =>
        new(repository, store, new AvailableDeleteGuard(), [participant], new FixedClock(now));

    private static FileEntry TrashedFile(Guid owner, DateTimeOffset now)
    {
        var entry = FileEntry.CreateFile(
            Guid.NewGuid(), owner, Guid.NewGuid(), FileName.Create("item.txt"),
            RelativeStoragePath.Create($"users/{owner:N}/files/item.txt"), null, 5, now);
        entry.Trash(RelativeStoragePath.Create($"users/{owner:N}/trash/{entry.Id:N}/item.txt"), now);
        return entry;
    }

    private static string Container(FileEntry entry) =>
        entry.RelativePath[..entry.RelativePath.LastIndexOf('/')];

    private sealed class FakeRepository(params FileEntry[] entries) : IFileRepository
    {
        public List<FileEntry> Entries { get; } = [.. entries];
        public List<FileOperation> Operations { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IFileTransaction>(new NoOpTransaction());
        public Task<FileEntry?> FindOwnedAsync(Guid ownerUserId, Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(item => item.OwnerUserId == ownerUserId && item.Id == entryId));
        public Task<bool> ReloadAsync(FileEntry entry, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult<FileEntry?>(null);
        public Task<FileEntry?> FindActiveChildAsync(Guid ownerUserId, Guid parentId, string name, CancellationToken cancellationToken) => Task.FromResult<FileEntry?>(null);
        public Task<FileEntry?> FindActiveFolderByPathAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken) => Task.FromResult<FileEntry?>(null);
        public Task<bool> IsRelocationBlockedAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasIncompleteOperationAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Operations.Any(item => item.FileEntryId == entryId && item.Status != FileOperationStatus.Completed));
        public Task<IFileMutationLock> AcquireMutationLocksAsync(IEnumerable<Guid> entryIds, CancellationToken cancellationToken) => Task.FromResult<IFileMutationLock>(new NoOpLock());
        public Task<IReadOnlyList<FileEntry>> ListActiveChildrenAsync(Guid ownerUserId, Guid parentId, int skip, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileEntry>>([]);
        public Task<int> CountActiveChildrenAsync(Guid ownerUserId, Guid parentId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyList<FileEntry>> ListTrashedAsync(Guid ownerUserId, int skip, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileEntry>>([]);
        public Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(Guid ownerUserId, string relativePathPrefix, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(Entries.Where(item => item.OwnerUserId == ownerUserId && item.RelativePath.StartsWith(relativePathPrefix + '/', StringComparison.Ordinal)).ToArray());
        public Task<FileOperation?> FindOperationAsync(Guid ownerUserId, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(Operations.SingleOrDefault(item => item.OwnerUserId == ownerUserId && item.IdempotencyKey == idempotencyKey));
        public Task<IReadOnlyList<FileOperation>> ListIncompleteOperationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileOperation>>([]);
        public void Add(FileEntry entry) => Entries.Add(entry);
        public void Add(FileOperation operation) => Operations.Add(operation);
        public void Add(AuditLog auditLog) => Audits.Add(auditLog);
        public void Remove(FileEntry entry) => Entries.Remove(entry);
        public void RemoveRange(IEnumerable<FileEntry> removed) { foreach (var entry in removed.ToArray()) Entries.Remove(entry); }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeStore(params string[] paths) : IFileStore
    {
        public HashSet<string> Paths { get; } = [.. paths];
        public int DeleteCount { get; private set; }
        public Exception? DeleteException { get; init; }
        public Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            if (DeleteException is not null)
            {
                throw DeleteException;
            }
            DeleteCount++;
            Paths.RemoveWhere(item => item == path.Value || item.StartsWith(path.Value + '/', StringComparison.Ordinal));
            return Task.CompletedTask;
        }
        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<StoredUpload> WriteUploadTempAsync(Guid ownerUserId, Guid operationId, Stream source, long expectedSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveAsync(RelativeStoragePath source, RelativeStoragePath target, bool sourceIsDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> ExistsAsync(RelativeStoragePath path, bool directory, CancellationToken cancellationToken) =>
            Task.FromResult(directory && Paths.Contains(path.Value));
        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingParticipant : IPermanentDeleteParticipant
    {
        public List<string> Calls { get; } = [];
        public Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(PermanentDeleteTarget target, CancellationToken cancellationToken)
        { Calls.Add("list"); return Task.FromResult<IReadOnlyList<RelativeStoragePath>>([]); }
        public Task DeleteManagementDataAsync(PermanentDeleteTarget target, CancellationToken cancellationToken)
        { Calls.Add("management"); return Task.CompletedTask; }
    }

    private sealed class AvailableDeleteGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken)
        {
            Assert.Equal(StorageIntent.Delete, intent);
            return Task.FromResult(StorageStatus.Available);
        }
    }
    private sealed class FixedClock(DateTimeOffset now) : ISystemClock { public DateTimeOffset UtcNow => now; }
    private sealed class NoOpTransaction : IFileTransaction
    { public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class NoOpLock : IFileMutationLock { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
