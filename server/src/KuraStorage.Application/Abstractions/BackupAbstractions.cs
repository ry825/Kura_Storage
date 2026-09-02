using KuraStorage.Application.Backup;
using KuraStorage.Domain.Backup;

namespace KuraStorage.Application.Abstractions;

public interface IBackupRepository
{
    Task<bool> IsDeviceActiveAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken);

    Task<BackupDestination?> FindDestinationAsync(Guid folderId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, BackupReceiptState>> ListReceiptStatesAsync(
        Guid userId,
        Guid deviceId,
        IReadOnlyCollection<string> localDocumentKeys,
        CancellationToken cancellationToken);

    Task<BackupReceipt?> FindReceiptAsync(
        Guid userId,
        Guid deviceId,
        string localDocumentKey,
        CancellationToken cancellationToken);

    void Add(BackupReceipt receipt);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
