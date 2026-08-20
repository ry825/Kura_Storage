using System.Data;
using System.Buffers.Binary;
using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class FileRepository(KuraStorageDbContext dbContext) : IFileRepository
{
    public async Task<IFileTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new FileTransaction(
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));

    public async Task<FileEntry?> FindOwnedAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries.SingleOrDefaultAsync(
            entry => entry.OwnerUserId == ownerUserId && entry.Id == entryId,
            cancellationToken);

    public async Task ReloadAsync(FileEntry entry, CancellationToken cancellationToken) =>
        await dbContext.Entry(entry).ReloadAsync(cancellationToken);

    public async Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.FileEntries.SingleOrDefaultAsync(
            entry => entry.OwnerUserId == ownerUserId && entry.ParentId == null && entry.Status == FileEntryStatus.Active,
            cancellationToken);

    public async Task<FileEntry?> FindActiveChildAsync(
        Guid ownerUserId,
        Guid parentId,
        string name,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries.SingleOrDefaultAsync(
            entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.ParentId == parentId &&
                entry.Status == FileEntryStatus.Active &&
                entry.Name == name,
            cancellationToken);

    public async Task<FileEntry?> FindActiveFolderByPathAsync(
        Guid ownerUserId,
        string relativePath,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries.SingleOrDefaultAsync(
            entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.RelativePath == relativePath &&
                entry.Status == FileEntryStatus.Active &&
                entry.EntryType == FileEntryType.Folder,
            cancellationToken);

    public async Task<bool> IsRelocationBlockedAsync(
        Guid ownerUserId,
        Guid entryId,
        string relativePath,
        CancellationToken cancellationToken) =>
        await IncompleteRelocationTargets(ownerUserId)
            .AnyAsync(
                target =>
                    target.Id == entryId ||
                    relativePath.StartsWith(target.RelativePath + "/"),
                cancellationToken);

    public async Task<bool> HasIncompleteOperationAsync(
        Guid ownerUserId,
        Guid entryId,
        string relativePath,
        CancellationToken cancellationToken) =>
        await IncompleteMutationTargets(ownerUserId)
            .AnyAsync(
                target => target.Id == entryId ||
                    target.RelativePath.StartsWith(relativePath + "/") ||
                    relativePath.StartsWith(target.RelativePath + "/"),
                cancellationToken);

    public async Task<IFileMutationLock> AcquireMutationLocksAsync(
        IEnumerable<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        var keys = entryIds
            .Where(id => id != Guid.Empty)
            .Select(ToAdvisoryLockKey)
            .Distinct()
            .Order()
            .ToArray();
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var acquired = new List<long>(keys.Length);
        try
        {
            foreach (var key in keys)
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteNonQueryAsync(cancellationToken);
                acquired.Add(key);
            }

            return new FileMutationLock(connection, acquired, closeConnection);
        }
        catch
        {
            await ReleaseLocksAsync(connection, acquired);
            if (closeConnection)
            {
                await connection.CloseAsync();
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<FileEntry>> ListActiveChildrenAsync(
        Guid ownerUserId,
        Guid parentId,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries
            .AsNoTracking()
            .Where(entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.ParentId == parentId &&
                entry.Status == FileEntryStatus.Active &&
                !IncompleteRelocationTargets(ownerUserId).Any(
                    target =>
                        target.Id == entry.Id ||
                        entry.RelativePath.StartsWith(target.RelativePath + "/")))
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountActiveChildrenAsync(
        Guid ownerUserId,
        Guid parentId,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries.CountAsync(
            entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.ParentId == parentId &&
                entry.Status == FileEntryStatus.Active &&
                !IncompleteRelocationTargets(ownerUserId).Any(
                    target =>
                        target.Id == entry.Id ||
                        entry.RelativePath.StartsWith(target.RelativePath + "/")),
            cancellationToken);

    public async Task<IReadOnlyList<FileEntry>> ListTrashedAsync(
        Guid ownerUserId,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries
            .AsNoTracking()
            .Where(entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.Status == FileEntryStatus.Trashed &&
                entry.ParentId == null &&
                !dbContext.FileOperations.Any(operation =>
                    operation.OwnerUserId == ownerUserId &&
                    operation.FileEntryId == entry.Id &&
                    operation.OperationType == FileOperationType.Purge &&
                    operation.Status != FileOperationStatus.Completed))
            .OrderByDescending(entry => entry.TrashedAt)
            .ThenBy(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountTrashedAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.FileEntries.CountAsync(
            entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.Status == FileEntryStatus.Trashed &&
                entry.ParentId == null &&
                !dbContext.FileOperations.Any(operation =>
                    operation.OwnerUserId == ownerUserId &&
                    operation.FileEntryId == entry.Id &&
                    operation.OperationType == FileOperationType.Purge &&
                    operation.Status != FileOperationStatus.Completed),
            cancellationToken);

    public async Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(
        Guid ownerUserId,
        string relativePathPrefix,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries
            .Where(entry =>
                entry.OwnerUserId == ownerUserId &&
                entry.RelativePath.StartsWith(relativePathPrefix + "/"))
            .OrderBy(entry => entry.RelativePath)
            .ToListAsync(cancellationToken);

    public async Task<FileOperation?> FindOperationAsync(
        Guid ownerUserId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await dbContext.FileOperations.SingleOrDefaultAsync(
            operation => operation.OwnerUserId == ownerUserId && operation.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<IReadOnlyList<FileOperation>> ListIncompleteOperationsAsync(
        CancellationToken cancellationToken) =>
        await dbContext.FileOperations
            .Where(operation =>
                operation.Status == FileOperationStatus.Pending ||
                operation.Status == FileOperationStatus.FilesystemDone)
            .OrderBy(operation => operation.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(FileEntry entry) => dbContext.FileEntries.Add(entry);

    public void Add(FileOperation operation) => dbContext.FileOperations.Add(operation);

    public void Add(AuditLog auditLog) => dbContext.AuditLogs.Add(auditLog);

    public void Remove(FileEntry entry) => dbContext.FileEntries.Remove(entry);

    public void RemoveRange(IEnumerable<FileEntry> entries) => dbContext.FileEntries.RemoveRange(entries);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new FilePersistenceConflictException(exception);
        }
    }

    private IQueryable<FileEntry> IncompleteRelocationTargets(Guid ownerUserId) =>
        from operation in dbContext.FileOperations
        join entry in dbContext.FileEntries on operation.FileEntryId equals entry.Id
        where
            operation.OwnerUserId == ownerUserId &&
            (operation.OperationType == FileOperationType.Rename ||
             operation.OperationType == FileOperationType.Move ||
             operation.OperationType == FileOperationType.Purge) &&
            operation.Status != FileOperationStatus.Completed
        select entry;

    private IQueryable<FileEntry> IncompleteMutationTargets(Guid ownerUserId) =>
        from operation in dbContext.FileOperations
        join entry in dbContext.FileEntries on operation.FileEntryId equals entry.Id
        where
            operation.OwnerUserId == ownerUserId &&
            operation.Status != FileOperationStatus.Completed &&
            operation.OperationType != FileOperationType.Upload &&
            operation.OperationType != FileOperationType.CreateFolder
        select entry;

    private static long ToAdvisoryLockKey(Guid id)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(id.ToByteArray(), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private static async Task ReleaseLocksAsync(NpgsqlConnection connection, IEnumerable<long> keys)
    {
        foreach (var key in keys.Reverse())
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class FileTransaction(IDbContextTransaction transaction) : IFileTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken) =>
            await transaction.CommitAsync(cancellationToken);

        public async ValueTask DisposeAsync() => await transaction.DisposeAsync();
    }

    private sealed class FileMutationLock(
        NpgsqlConnection connection,
        IReadOnlyList<long> keys,
        bool closeConnection) : IFileMutationLock
    {
        public async ValueTask DisposeAsync()
        {
            await ReleaseLocksAsync(connection, keys);
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
