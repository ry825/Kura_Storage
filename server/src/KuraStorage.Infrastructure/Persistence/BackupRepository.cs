using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Backup;
using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class BackupRepository(KuraStorageDbContext dbContext) : IBackupRepository
{
    public Task<bool> IsDeviceActiveAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken) =>
        dbContext.Devices.AnyAsync(
            device => device.Id == deviceId && device.UserId == userId && device.Status == DeviceStatus.Active,
            cancellationToken);

    public Task<BackupDestination?> FindDestinationAsync(Guid folderId, CancellationToken cancellationToken) =>
        dbContext.FileEntries
            .Where(entry => entry.Id == folderId)
            .Select(entry => new BackupDestination(entry.Id, entry.OwnerUserId, entry.EntryType, entry.Status))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, BackupReceiptState>> ListReceiptStatesAsync(
        Guid userId,
        Guid deviceId,
        IReadOnlyCollection<string> localDocumentKeys,
        CancellationToken cancellationToken)
    {
        var keys = localDocumentKeys.ToArray();
        var rows = await dbContext.BackupReceipts
            .AsNoTracking()
            .Where(receipt => receipt.UserId == userId && receipt.DeviceId == deviceId &&
                              keys.Contains(receipt.LocalDocumentKey))
            .GroupJoin(
                dbContext.FileEntries.AsNoTracking(),
                receipt => receipt.RemoteFileId,
                entry => entry.Id,
                (receipt, entries) => new { receipt, entries })
            .SelectMany(
                row => row.entries.DefaultIfEmpty(),
                (row, entry) => new
                {
                    Receipt = row.receipt,
                    Status = entry == null ? (KuraStorage.Domain.Files.FileEntryStatus?)null : entry.Status,
                    Version = entry == null ? (long?)null : entry.FileVersion,
                })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Receipt.LocalDocumentKey,
            row => new BackupReceiptState(row.Receipt, row.Status, row.Version),
            StringComparer.Ordinal);
    }

    public Task<BackupReceipt?> FindReceiptAsync(
        Guid userId,
        Guid deviceId,
        string localDocumentKey,
        CancellationToken cancellationToken) =>
        dbContext.BackupReceipts.SingleOrDefaultAsync(
            receipt => receipt.UserId == userId && receipt.DeviceId == deviceId &&
                       receipt.LocalDocumentKey == localDocumentKey,
            cancellationToken);

    public void Add(BackupReceipt receipt) => dbContext.BackupReceipts.Add(receipt);

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
}
