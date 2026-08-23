using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Indexing;
using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class FileServiceTests
{
    [Fact]
    public async Task ListAsync_SharedPage_ResolvesOneHundredChildPermissionsInOneBatch()
    {
        var now = DateTimeOffset.Parse("2026-08-23T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("Shared"),
            RelativeStoragePath.Create($"{root.RelativePath}/Shared"), now);
        var children = Enumerable.Range(0, 100).Select(index => FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, folder.Id, FileName.Create($"item-{index:D3}.txt"),
            RelativeStoragePath.Create($"{folder.RelativePath}/item-{index:D3}.txt"),
            "text/plain", index, now)).ToArray();
        var repository = new FakeFileRepository([root, folder, .. children]);
        var authorization = new RecordingAuthorizationService(folder.Id);
        var service = new FileService(
            repository,
            new FakeFileStore(),
            new AvailableStorageGuard(),
            new EmptySnapshotReader(),
            new NoOpProvisioner(),
            new FixedClock(now),
            null,
            authorization);

        var result = await service.ListAsync(actorId, folder.Id, 1, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value!.Items.Count);
        Assert.Equal(1, authorization.BatchCalls);
        Assert.All(result.Value.Items, item => Assert.Equal("INHERITED", item.PermissionSource));
    }

    [Fact]
    public async Task ListAsync_IncludesMissingStateAndTimestampsWithoutFilesystemScan()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var owner = Guid.NewGuid();
        var root = FileEntry.CreateRoot(owner, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), owner, root.Id, FileName.Create("gone.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/gone.txt"), null, 1, now);
        file.MarkMissingCandidate(Guid.NewGuid(), now.AddMinutes(1));
        var reader = new RecordingSnapshotReader();
        var service = new FileService(
            new FakeFileRepository(root, file), new FakeFileStore(), new AvailableStorageGuard(), reader,
            new NoOpProvisioner(), new FixedClock(now.AddMinutes(2)));

        var result = await service.ListAsync(owner, root.Id, 1, 100, CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("MISSING_CANDIDATE", item.Status);
        Assert.Equal(now.AddMinutes(1), item.MissingDetectedAt);
        Assert.Equal(0, reader.InspectCount);
    }

    [Fact]
    public async Task DownloadAsync_ActiveFileAbsent_MarksCandidateAndReturnsFileMissing()
    {
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var owner = Guid.NewGuid();
        var root = FileEntry.CreateRoot(owner, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), owner, root.Id, FileName.Create("gone.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/gone.txt"), null, 1, now);
        var service = new FileService(
            new FakeFileRepository(root, file), new FakeFileStore(), new AvailableStorageGuard(),
            new RecordingSnapshotReader(), new NoOpProvisioner(), new FixedClock(now.AddMinutes(1)));

        var result = await service.DownloadAsync(owner, file.Id, CancellationToken.None);

        Assert.Equal(FileErrorCodes.FileMissing, result.Failure!.Code);
        Assert.Equal(FileEntryStatus.MissingCandidate, file.Status);
    }

    [Fact]
    public async Task ListTrashAsync_UsesConfiguredServerRetentionDeadline()
    {
        var now = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        var owner = Guid.NewGuid();
        var entry = FileEntry.CreateFile(
            Guid.NewGuid(), owner, Guid.NewGuid(), FileName.Create("item.txt"),
            RelativeStoragePath.Create($"users/{owner:N}/files/item.txt"), null, 1, now);
        entry.Trash(RelativeStoragePath.Create($"users/{owner:N}/trash/{entry.Id:N}/item.txt"), now);
        var repository = new FakeFileRepository(entry);
        var service = new FileService(
            repository,
            new FakeFileStore(),
            new AvailableStorageGuard(),
            new EmptySnapshotReader(),
            new NoOpProvisioner(),
            new FixedClock(now),
            new TrashPurgeOptions { RetentionDays = 45 });

        var result = await service.ListTrashAsync(owner, 1, 100, CancellationToken.None);

        Assert.Equal(now.AddDays(45), Assert.Single(result.Value!.Items).PurgeEligibleAt);
    }

    [Fact]
    public async Task RenameAsync_ValidFile_MovesStorageAndPreservesVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var parent = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            Guid.NewGuid(),
            FileName.Create("Parent"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/Parent"),
            now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(),
            ownerId,
            parent.Id,
            FileName.Create("before.txt"),
            RelativeStoragePath.Create($"{parent.RelativePath}/before.txt"),
            "text/plain",
            12,
            now);
        var repository = new FakeFileRepository(parent, file);
        var store = new FakeFileStore(parent.RelativePath, file.RelativePath);
        var service = CreateService(repository, store, now.AddMinutes(1));

        var result = await service.RenameAsync(
            new RenameFileCommand(ownerId, deviceId, file.Id, "after.txt", "request-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("after.txt", result.Value!.Name);
        Assert.Equal(1, result.Value.FileVersion);
        Assert.Contains($"{parent.RelativePath}/after.txt", store.Paths);
        Assert.DoesNotContain($"{parent.RelativePath}/before.txt", store.Paths);
        Assert.Contains(
            repository.Audits,
            audit =>
                audit.Action == "FILE_RENAME" &&
                audit.ActorUserId == ownerId &&
                audit.ActorDeviceId == deviceId &&
                audit.ResultCode == "SUCCESS" &&
                audit.RequestId == "request-1");
    }

    [Fact]
    public async Task RenameAsync_IndexRaceAfterFilesystemMove_ReturnsRecoveryRequiredInsteadOfThrowing()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var parent = FileEntry.CreateFolder(
            Guid.NewGuid(), ownerId, Guid.NewGuid(), FileName.Create("Parent"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/Parent"), now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, parent.Id, FileName.Create("before.txt"),
            RelativeStoragePath.Create($"{parent.RelativePath}/before.txt"), "text/plain", 12, now);
        var repository = new FakeFileRepository(parent, file) { FailOnSaveCall = 3 };
        var store = new FakeFileStore(parent.RelativePath, file.RelativePath);
        var service = CreateService(repository, store, now.AddMinutes(1));

        var result = await service.RenameAsync(
            new RenameFileCommand(ownerId, Guid.NewGuid(), file.Id, "after.txt", "request-race"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileErrorCodes.RecoveryRequired, result.Failure!.Code);
        Assert.Equal(1, store.MoveCount);
        Assert.Contains($"{parent.RelativePath}/after.txt", store.Paths);
    }

    [Fact]
    public async Task TrashAsync_IndexRaceAfterFilesystemMove_ReturnsRecoveryRequiredInsteadOfThrowing()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("item.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/item.txt"), "text/plain", 12, now);
        var repository = new FakeFileRepository(root, file) { FailOnSaveCall = 3 };
        var store = new FakeFileStore(root.RelativePath, file.RelativePath);
        var service = CreateService(repository, store, now.AddMinutes(1));

        var result = await service.TrashAsync(ownerId, file.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileErrorCodes.RecoveryRequired, result.Failure!.Code);
        Assert.Equal(1, store.MoveCount);
    }

    [Fact]
    public async Task RestoreAsync_IndexRaceAfterFilesystemMove_ReturnsRecoveryRequiredInsteadOfThrowing()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, root.Id, FileName.Create("item.txt"),
            RelativeStoragePath.Create($"{root.RelativePath}/item.txt"), "text/plain", 12, now);
        var originalPath = file.RelativePath;
        var trashPath = $"users/{ownerId:N}/trash/{file.Id:N}/item.txt";
        file.Trash(RelativeStoragePath.Create(trashPath), now.AddMinutes(1));
        var repository = new FakeFileRepository(root, file) { FailOnSaveCall = 3 };
        var store = new FakeFileStore(root.RelativePath, trashPath);
        var service = CreateService(repository, store, now.AddMinutes(2));

        var result = await service.RestoreAsync(ownerId, file.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileErrorCodes.RecoveryRequired, result.Failure!.Code);
        Assert.Equal(1, store.MoveCount);
        Assert.Contains(originalPath, store.Paths);
    }

    [Fact]
    public async Task MoveAsync_FolderIntoDescendant_RejectsBeforeStorageMutation()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var parent = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            Guid.NewGuid(),
            FileName.Create("Parent"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/Parent"),
            now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            parent.Id,
            FileName.Create("Folder"),
            RelativeStoragePath.Create($"{parent.RelativePath}/Folder"),
            now);
        var child = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            folder.Id,
            FileName.Create("Child"),
            RelativeStoragePath.Create($"{folder.RelativePath}/Child"),
            now);
        var repository = new FakeFileRepository(parent, folder, child);
        var store = new FakeFileStore(parent.RelativePath, folder.RelativePath, child.RelativePath);
        var service = CreateService(repository, store, now.AddMinutes(1));

        var result = await service.MoveAsync(
            new MoveFileCommand(ownerId, Guid.NewGuid(), folder.Id, child.Id, "request-2"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FileErrorCodes.FileMoveCycle, result.Failure!.Code);
        Assert.Equal(0, store.MoveCount);
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public async Task MoveAsync_DepthBoundary_Allows64AndRejects65BeforeStorageMutation(
        int targetDepth,
        bool expectedSuccess)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var rootPath = $"users/{ownerId:N}/files";
        var root = FileEntry.CreateRoot(ownerId, now);
        var source = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            root.Id,
            FileName.Create("Source"),
            RelativeStoragePath.Create($"{rootPath}/Source"),
            now);
        var targetPath = rootPath + "/" + string.Join('/', Enumerable.Repeat("d", targetDepth));
        var target = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            root.Id,
            FileName.Create("Target"),
            RelativeStoragePath.Create(targetPath),
            now);
        var repository = new FakeFileRepository(root, source, target);
        var store = new FakeFileStore(rootPath, source.RelativePath, target.RelativePath);
        var service = CreateService(repository, store, now.AddMinutes(1));

        var result = await service.MoveAsync(
            new MoveFileCommand(ownerId, Guid.NewGuid(), source.Id, target.Id, "request-depth"),
            CancellationToken.None);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(expectedSuccess ? 1 : 0, store.MoveCount);
        if (!expectedSuccess)
        {
            Assert.Equal(FileErrorCodes.ValidationFailed, result.Failure!.Code);
        }
    }

    private static FileService CreateService(
        IFileRepository repository,
        IFileStore store,
        DateTimeOffset now) =>
        new(
            repository,
            store,
            new AvailableStorageGuard(),
            new EmptySnapshotReader(),
            new NoOpProvisioner(),
            new FixedClock(now));

    private sealed class FakeFileRepository(params FileEntry[] entries) : IFileRepository
    {
        private readonly List<FileEntry> entries = [.. entries];

        private int saveCallCount;

        public int? FailOnSaveCall { get; init; }

        public List<AuditLog> Audits { get; } = [];

        public Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IFileTransaction>(new NoOpTransaction());

        public Task<FileEntry?> FindOwnedAsync(
            Guid ownerUserId,
            Guid entryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(entries.SingleOrDefault(entry => entry.OwnerUserId == ownerUserId && entry.Id == entryId));

        public Task<FileEntry?> FindByIdAsync(Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(entries.SingleOrDefault(entry => entry.Id == entryId));

        public Task<FileOwnerItem?> FindOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<FileOwnerItem?>(new FileOwnerItem(ownerUserId, "Owner"));

        public Task<bool> ReloadAsync(FileEntry entry, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(entries.SingleOrDefault(entry => entry.OwnerUserId == ownerUserId && entry.ParentId is null));

        public Task<FileEntry?> FindActiveChildAsync(
            Guid ownerUserId,
            Guid parentId,
            string name,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                entries.SingleOrDefault(
                    entry =>
                        entry.OwnerUserId == ownerUserId &&
                        entry.ParentId == parentId &&
                        entry.Name == name &&
                        entry.Status == FileEntryStatus.Active));

        public Task<FileEntry?> FindActiveFolderByPathAsync(
            Guid ownerUserId,
            string relativePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                entries.SingleOrDefault(
                    entry =>
                        entry.OwnerUserId == ownerUserId &&
                        entry.RelativePath == relativePath &&
                        entry.EntryType == FileEntryType.Folder &&
                        entry.Status == FileEntryStatus.Active));

        public Task<bool> IsRelocationBlockedAsync(
            Guid ownerUserId,
            Guid entryId,
            string relativePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasIncompleteOperationAsync(
            Guid ownerUserId,
            Guid entryId,
            string relativePath,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IFileMutationLock> AcquireMutationLocksAsync(
            IEnumerable<Guid> entryIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IFileMutationLock>(new NoOpMutationLock());

        public Task<IReadOnlyList<FileEntry>> ListActiveChildrenAsync(
            Guid ownerUserId,
            Guid parentId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(
                entries.Where(entry => entry.OwnerUserId == ownerUserId && entry.ParentId == parentId)
                    .Skip(skip)
                    .Take(take)
                    .ToArray());

        public Task<int> CountActiveChildrenAsync(
            Guid ownerUserId,
            Guid parentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(entries.Count(entry => entry.OwnerUserId == ownerUserId && entry.ParentId == parentId));

        public Task<IReadOnlyList<FileEntry>> ListTrashedAsync(
            Guid ownerUserId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(
                entries.Where(entry =>
                        entry.OwnerUserId == ownerUserId &&
                        entry.Status == FileEntryStatus.Trashed &&
                        entry.ParentId is null)
                    .Skip(skip)
                    .Take(take)
                    .ToArray());

        public Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(
                entries.Count(entry =>
                    entry.OwnerUserId == ownerUserId &&
                    entry.Status == FileEntryStatus.Trashed &&
                    entry.ParentId is null));

        public Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(
            Guid ownerUserId,
            string relativePathPrefix,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>(
                entries.Where(
                        entry =>
                            entry.OwnerUserId == ownerUserId &&
                            entry.RelativePath.StartsWith(relativePathPrefix + "/", StringComparison.Ordinal))
                    .ToArray());

        public Task<FileOperation?> FindOperationAsync(
            Guid ownerUserId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<FileOperation?>(null);

        public Task<IReadOnlyList<FileOperation>> ListIncompleteOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileOperation>>([]);

        public void Add(FileEntry entry) => entries.Add(entry);

        public void Add(FileOperation operation)
        {
        }

        public void Add(AuditLog auditLog) => Audits.Add(auditLog);

        public void Remove(FileEntry entry) => entries.Remove(entry);

        public void RemoveRange(IEnumerable<FileEntry> removedEntries)
        {
            foreach (var entry in removedEntries.ToArray())
            {
                entries.Remove(entry);
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            saveCallCount++;
            if (saveCallCount == FailOnSaveCall)
            {
                throw new FilePersistenceConflictException(new InvalidOperationException("Simulated index race."));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileStore(params string[] initialPaths) : IFileStore
    {
        public HashSet<string> Paths { get; } = [.. initialPaths];

        public int MoveCount { get; private set; }

        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            Paths.Add(path.Value);
            return Task.CompletedTask;
        }

        public Task<StoredUpload> WriteUploadTempAsync(
            Guid ownerUserId,
            Guid operationId,
            Stream source,
            long expectedSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MoveAsync(
            RelativeStoragePath source,
            RelativeStoragePath target,
            bool sourceIsDirectory,
            CancellationToken cancellationToken)
        {
            if (!Paths.Remove(source.Value) || !Paths.Add(target.Value))
            {
                throw new IOException();
            }

            MoveCount++;
            return Task.CompletedTask;
        }

        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            Paths.Remove(path.Value);
            return Task.CompletedTask;
        }

        public Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            Paths.RemoveWhere(value => value == path.Value || value.StartsWith(path.Value + "/", StringComparison.Ordinal));
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(
            RelativeStoragePath path,
            bool directory,
            CancellationToken cancellationToken) =>
            Task.FromResult(Paths.Contains(path.Value));

        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AvailableStorageGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }

    private sealed class EmptySnapshotReader : IManagedFileSystemSnapshotReader
    {
        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
            StorageSnapshotContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ObservedStorageEntry?> InspectAsync(
            RelativeStoragePath path,
            CancellationToken cancellationToken) => Task.FromResult<ObservedStorageEntry?>(null);
    }

    private sealed class RecordingSnapshotReader : IManagedFileSystemSnapshotReader
    {
        public int InspectCount { get; private set; }

        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
            StorageSnapshotContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ObservedStorageEntry?> InspectAsync(
            RelativeStoragePath path,
            CancellationToken cancellationToken)
        {
            InspectCount++;
            return Task.FromResult<ObservedStorageEntry?>(null);
        }
    }

    private sealed class NoOpProvisioner : IUserStorageProvisioner
    {
        public Task ProvisionAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class RecordingAuthorizationService(Guid shareTargetId) : IAuthorizationService
    {
        public int BatchCalls { get; private set; }

        public Task<EffectivePermission> ResolveAsync(
            Guid actorUserId,
            Guid entryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Permission(entryId));

        public Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(
            Guid actorUserId,
            IReadOnlyCollection<Guid> entryIds,
            CancellationToken cancellationToken)
        {
            BatchCalls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, EffectivePermission>>(
                entryIds.ToDictionary(entryId => entryId, Permission));
        }

        public Task<bool> AllowsAsync(
            Guid actorUserId,
            Guid entryId,
            ShareOperation operation,
            CancellationToken cancellationToken) => Task.FromResult(true);

        private EffectivePermission Permission(Guid entryId) =>
            new(
                entryId,
                EffectivePermissionLevel.Viewer,
                PermissionSource.Inherited,
                shareTargetId,
                Guid.NewGuid());
    }

    private sealed class NoOpTransaction : IFileTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpMutationLock : IFileMutationLock
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
