using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Domain.Maintenance;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class AdminStorageServiceTests
{
    [Fact]
    public async Task Get_AtWarningThreshold_ReturnsAggregatesWithoutStartingPurge()
    {
        var now = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        var run = new TrashPurgeRun(Guid.NewGuid(), now.AddHours(-1));
        run.Complete(now.AddMinutes(-59));
        var repository = new StatusRepository(run);
        var service = new AdminStorageService(
            repository,
            new CapacityStore(new StorageCapacity(1_000, 100)),
            new FixedGuard(StorageStatus.Available),
            new FixedClock(now),
            new TrashPurgeOptions { RetentionDays = 30 },
            100);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal("AVAILABLE", result.Storage);
        Assert.Equal(1_000, result.TotalBytes);
        Assert.Equal(100, result.AvailableBytes);
        Assert.True(result.CapacityWarning);
        Assert.Equal(345, result.TrashBytes);
        Assert.Equal(2, result.ExpiredTrashRootCount);
        Assert.Equal(1, result.RecoveryRequiredPurgeCount);
        Assert.Equal("COMPLETED", result.LastPurgeRun!.Status);
        Assert.Equal(now.AddDays(-30), repository.LastCutoff);
    }

    [Fact]
    public async Task Get_WhenStorageUnavailable_DoesNotExposeCapacityValues()
    {
        var service = new AdminStorageService(
            new StatusRepository(null),
            new CapacityStore(new StorageCapacity(-1, -1)),
            new FixedGuard(StorageStatus.Unavailable),
            new FixedClock(DateTimeOffset.UtcNow),
            new TrashPurgeOptions(),
            100);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal("UNAVAILABLE", result.Storage);
        Assert.Null(result.TotalBytes);
        Assert.Null(result.AvailableBytes);
        Assert.Null(result.CapacityWarning);
    }

    private sealed class StatusRepository(TrashPurgeRun? latest) : IFileRepository
    {
        public DateTimeOffset? LastCutoff { get; private set; }
        public Task<long> SumTrashedFileBytesAsync(CancellationToken cancellationToken) => Task.FromResult(345L);
        public Task<int> CountExpiredTrashRootsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
        { LastCutoff = cutoff; return Task.FromResult(2); }
        public Task<int> CountRecoveryRequiredPurgesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<TrashPurgeRun?> FindLatestPurgeRunAsync(CancellationToken cancellationToken) => Task.FromResult(latest);
        public Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KuraStorage.Domain.Files.FileEntry?> FindOwnedAsync(Guid ownerUserId, Guid entryId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReloadAsync(KuraStorage.Domain.Files.FileEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KuraStorage.Domain.Files.FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KuraStorage.Domain.Files.FileEntry?> FindActiveChildAsync(Guid ownerUserId, Guid parentId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KuraStorage.Domain.Files.FileEntry?> FindActiveFolderByPathAsync(Guid ownerUserId, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsRelocationBlockedAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasIncompleteOperationAsync(Guid ownerUserId, Guid entryId, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IFileMutationLock> AcquireMutationLocksAsync(IEnumerable<Guid> entryIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<KuraStorage.Domain.Files.FileEntry>> ListActiveChildrenAsync(Guid ownerUserId, Guid parentId, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountActiveChildrenAsync(Guid ownerUserId, Guid parentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<KuraStorage.Domain.Files.FileEntry>> ListTrashedAsync(Guid ownerUserId, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<KuraStorage.Domain.Files.FileEntry>> ListDescendantsAsync(Guid ownerUserId, string relativePathPrefix, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KuraStorage.Domain.Files.FileOperation?> FindOperationAsync(Guid ownerUserId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<KuraStorage.Domain.Files.FileOperation>> ListIncompleteOperationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Remove(KuraStorage.Domain.Files.FileEntry entry) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<KuraStorage.Domain.Files.FileEntry> entries) => throw new NotSupportedException();
        public void Add(KuraStorage.Domain.Files.FileEntry entry) => throw new NotSupportedException();
        public void Add(KuraStorage.Domain.Files.FileOperation operation) => throw new NotSupportedException();
        public void Add(KuraStorage.Domain.Audit.AuditLog auditLog) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapacityStore(StorageCapacity capacity) : IFileStore
    {
        public Task<StorageCapacity> GetCapacityAsync(CancellationToken cancellationToken) => Task.FromResult(capacity);
        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(KuraStorage.Domain.Files.RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredUpload> WriteUploadTempAsync(Guid ownerUserId, Guid operationId, Stream source, long expectedSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveAsync(KuraStorage.Domain.Files.RelativeStoragePath source, KuraStorage.Domain.Files.RelativeStoragePath target, bool sourceIsDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteIfExistsAsync(KuraStorage.Domain.Files.RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTreeIfExistsAsync(KuraStorage.Domain.Files.RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(KuraStorage.Domain.Files.RelativeStoragePath path, bool directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(KuraStorage.Domain.Files.RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedGuard(StorageStatus status) : IStorageGuard
    { public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) => Task.FromResult(status); }
    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    { public DateTimeOffset UtcNow => now; }
}
