using System.Data;
using KuraStorage.Application.Abstractions;
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
                entry.Status == FileEntryStatus.Active)
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
                entry.Status == FileEntryStatus.Active,
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
                entry.ParentId == null)
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
                entry.ParentId == null,
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

    private sealed class FileTransaction(IDbContextTransaction transaction) : IFileTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken) =>
            await transaction.CommitAsync(cancellationToken);

        public async ValueTask DisposeAsync() => await transaction.DisposeAsync();
    }
}
