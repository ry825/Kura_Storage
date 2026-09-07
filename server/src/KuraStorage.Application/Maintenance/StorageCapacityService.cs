using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Maintenance;

public sealed record StorageCapacityStatus(
    string Storage,
    long? TotalBytes,
    long? UsedBytes,
    long? AvailableBytes);

public sealed class StorageCapacityService(
    IFileStore fileStore,
    IStorageGuard storageGuard)
{
    public async Task<StorageCapacityStatus> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
            {
                return Unavailable();
            }

            var capacity = await fileStore.GetCapacityAsync(cancellationToken);
            if (capacity.TotalBytes < 0 || capacity.AvailableBytes < 0 ||
                capacity.AvailableBytes > capacity.TotalBytes)
            {
                return Unavailable();
            }

            return new StorageCapacityStatus(
                "AVAILABLE",
                capacity.TotalBytes,
                checked(capacity.TotalBytes - capacity.AvailableBytes),
                capacity.AvailableBytes);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return Unavailable();
        }
    }

    private static StorageCapacityStatus Unavailable() => new("UNAVAILABLE", null, null, null);
}
