using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;

namespace KuraStorage.Application.Maintenance;

public sealed class AdminStorageService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    ISystemClock clock,
    TrashPurgeOptions purgeOptions,
    long capacityWarningFreeBytes)
{
    public async Task<AdminStorageStatus> GetAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddDays(-purgeOptions.RetentionDays);
        var trashBytes = await repository.SumTrashedFileBytesAsync(cancellationToken);
        var expiredCount = await repository.CountExpiredTrashRootsAsync(cutoff, cancellationToken);
        var recoveryRequiredCount = await repository.CountRecoveryRequiredPurgesAsync(cancellationToken);
        var latest = await repository.FindLatestPurgeRunAsync(cancellationToken);

        var storageAvailable =
            await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) == StorageStatus.Available;
        StorageCapacity? capacity = null;
        if (storageAvailable)
        {
            try
            {
                capacity = await fileStore.GetCapacityAsync(cancellationToken);
            }
            catch (IOException)
            {
                storageAvailable = false;
            }
            catch (UnauthorizedAccessException)
            {
                storageAvailable = false;
            }
        }

        return new AdminStorageStatus(
            storageAvailable ? "AVAILABLE" : "UNAVAILABLE",
            capacity?.TotalBytes,
            capacity?.AvailableBytes,
            capacityWarningFreeBytes,
            capacity is null ? null : capacity.AvailableBytes <= capacityWarningFreeBytes,
            trashBytes,
            expiredCount,
            purgeOptions.RetentionDays,
            recoveryRequiredCount,
            latest is null ? null : TrashPurgeRunSummary.From(latest));
    }
}
