using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;

namespace KuraStorage.Application.Maintenance;

public sealed class AdminStorageService(
    IFileRepository repository,
    StorageCapacityService capacityService,
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

        var capacity = await capacityService.GetAsync(cancellationToken);
        var storageAvailable = capacity.Storage == "AVAILABLE";

        return new AdminStorageStatus(
            storageAvailable ? "AVAILABLE" : "UNAVAILABLE",
            capacity.TotalBytes,
            capacity.AvailableBytes,
            capacityWarningFreeBytes,
            capacity.AvailableBytes is null ? null : capacity.AvailableBytes <= capacityWarningFreeBytes,
            trashBytes,
            expiredCount,
            purgeOptions.RetentionDays,
            recoveryRequiredCount,
            latest is null ? null : TrashPurgeRunSummary.From(latest));
    }
}
