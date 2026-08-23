using KuraStorage.Domain.Files;
using KuraStorage.Domain.Audit;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Domain.Maintenance;

namespace KuraStorage.Application.Abstractions;

public interface IFileTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IFileMutationLock : IAsyncDisposable;

public interface IFileRepository
{
    Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<FileEntry?> FindOwnedAsync(Guid ownerUserId, Guid entryId, CancellationToken cancellationToken);

    Task<FileEntry?> FindByIdAsync(Guid entryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<FileOwnerItem?> FindOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<FileOwnerItem?>(null);

    Task<bool> ReloadAsync(FileEntry entry, CancellationToken cancellationToken);

    Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task<FileEntry?> FindActiveChildAsync(
        Guid ownerUserId,
        Guid parentId,
        string name,
        CancellationToken cancellationToken);

    Task<FileEntry?> FindActiveFolderByPathAsync(
        Guid ownerUserId,
        string relativePath,
        CancellationToken cancellationToken);

    Task<bool> IsRelocationBlockedAsync(
        Guid ownerUserId,
        Guid entryId,
        string relativePath,
        CancellationToken cancellationToken);

    Task<bool> HasIncompleteOperationAsync(
        Guid ownerUserId,
        Guid entryId,
        string relativePath,
        CancellationToken cancellationToken);

    Task<IFileMutationLock> AcquireMutationLocksAsync(
        IEnumerable<Guid> entryIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileEntry>> ListActiveChildrenAsync(
        Guid ownerUserId,
        Guid parentId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<int> CountActiveChildrenAsync(Guid ownerUserId, Guid parentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileEntry>> ListTrashedAsync(
        Guid ownerUserId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(
        Guid ownerUserId,
        string relativePathPrefix,
        CancellationToken cancellationToken);

    Task<FileOperation?> FindOperationAsync(
        Guid ownerUserId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileOperation>> ListIncompleteOperationsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TrashPurgeCandidate>> ListPurgeCandidatesAsync(
        DateTimeOffset cutoff,
        DateTimeOffset? afterTrashedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<IReadOnlyList<TrashPurgeRun>> ListRunningPurgeRunsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<TrashPurgeRun?> FindLatestPurgeRunAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<long> SumTrashedFileBytesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<int> CountExpiredTrashRootsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<int> CountRecoveryRequiredPurgesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    void Remove(FileEntry entry);

    void RemoveRange(IEnumerable<FileEntry> entries);

    void Add(FileEntry entry);

    void Add(FileOperation operation);

    void Add(AuditLog auditLog);

    void Add(TrashPurgeRun run) => throw new NotSupportedException();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IFileStore
{
    Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken);

    Task<StorageCapacity> GetCapacityAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken);

    Task<StoredUpload> WriteUploadTempAsync(
        Guid ownerUserId,
        Guid operationId,
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken);

    Task MoveAsync(
        RelativeStoragePath source,
        RelativeStoragePath target,
        bool sourceIsDirectory,
        CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken);

    Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(RelativeStoragePath path, bool directory, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken);
}

public interface IPermanentDeleteParticipant
{
    Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken);

    Task DeleteManagementDataAsync(PermanentDeleteTarget target, CancellationToken cancellationToken);
}

public interface IFileIndexDeletionParticipant
{
    Task DeleteManagementDataAsync(FileIndexDeletionTarget target, CancellationToken cancellationToken);
}

public sealed record StoredUpload(RelativeStoragePath Path, long Size, string Sha256);

public sealed class FilePersistenceConflictException : Exception
{
    public FilePersistenceConflictException(Exception innerException)
        : base("A file catalog persistence conflict occurred.", innerException)
    {
    }
}

public interface IUserStorageProvisioner
{
    Task ProvisionAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
