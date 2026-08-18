using KuraStorage.Domain.Files;
using KuraStorage.Domain.Audit;

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

    Task ReloadAsync(FileEntry entry, CancellationToken cancellationToken);

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

    void Add(FileEntry entry);

    void Add(FileOperation operation);

    void Add(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IFileStore
{
    Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken);

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

    Task<bool> ExistsAsync(RelativeStoragePath path, bool directory, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken);
}

public sealed record StoredUpload(RelativeStoragePath Path, long Size, string Sha256);

public sealed class FilePersistenceConflictException : Exception
{
    public FilePersistenceConflictException(Exception innerException)
        : base("A file catalog uniqueness constraint was violated.", innerException)
    {
    }
}

public interface IUserStorageProvisioner
{
    Task ProvisionAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
