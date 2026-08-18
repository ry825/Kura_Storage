using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class FileServiceTests
{
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
            new NoOpProvisioner(),
            new FixedClock(now));

    private sealed class FakeFileRepository(params FileEntry[] entries) : IFileRepository
    {
        private readonly List<FileEntry> entries = [.. entries];

        public List<AuditLog> Audits { get; } = [];

        public Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IFileTransaction>(new NoOpTransaction());

        public Task<FileEntry?> FindOwnedAsync(
            Guid ownerUserId,
            Guid entryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(entries.SingleOrDefault(entry => entry.OwnerUserId == ownerUserId && entry.Id == entryId));

        public Task ReloadAsync(FileEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

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
            Task.FromResult<IReadOnlyList<FileEntry>>([]);

        public Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

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

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
        public Task<StorageStatus> InspectAsync(bool requireWrite, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
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
