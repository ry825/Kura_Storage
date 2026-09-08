using System.Diagnostics;
using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Media;

public sealed class MediaMaintenanceOptions
{
    public int IntervalMinutes { get; init; } = 30;
    public int BatchSize { get; init; } = 100;
    public int TerminalJobRetentionDays { get; init; } = 7;
}

public sealed record MediaMaintenanceResult(
    bool AcquiredLock,
    int DeletedCount,
    long DeletedBytes,
    int FailureCount,
    int DeletedTerminalJobCount,
    long ElapsedMilliseconds);

public sealed class MediaMaintenanceService(
    IMediaMaintenanceRepository repository,
    IDerivativeStore store,
    IStorageGuard storageGuard,
    ISystemClock clock,
    MediaMaintenanceOptions options) : IMediaMaintenanceService
{
    public async Task<MediaMaintenanceResult> RunAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        if (await storageGuard.InspectAsync(StorageIntent.Delete, cancellationToken) != StorageStatus.Available)
        {
            return new MediaMaintenanceResult(false, 0, 0, 1, 0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        await using var maintenanceLock = await repository.TryAcquireMaintenanceLockAsync(cancellationToken);
        if (maintenanceLock is null)
        {
            return new MediaMaintenanceResult(false, 0, 0, 0, 0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        var deleted = 0;
        long bytes = 0;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var candidates = await repository.ClaimDeletingAsync(clock.UtcNow, options.BatchSize, cancellationToken);
            if (candidates.Count == 0) break;
            foreach (var candidate in candidates)
            {
                try
                {
                    await store.DeleteIfExistsAsync(candidate.Path, cancellationToken);
                    await repository.CompleteDeleteAsync(candidate.DerivativeId, cancellationToken);
                    deleted++;
                    bytes = checked(bytes + candidate.Size);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failures++;
                    if (candidate.RestoreReadyOnFailure)
                    {
                        await repository.RestoreReadyAsync(candidate.DerivativeId, clock.UtcNow, cancellationToken);
                    }
                }
            }
            if (candidates.Count < options.BatchSize || failures > 0) break;
        }

        var deletedJobs = 0;
        if (failures == 0)
        {
            var cutoff = clock.UtcNow.AddDays(-options.TerminalJobRetentionDays);
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await repository.DeleteTerminalJobsAsync(cutoff, options.BatchSize, cancellationToken);
                deletedJobs = checked(deletedJobs + count);
                if (count < options.BatchSize) break;
            }
        }

        return new MediaMaintenanceResult(true, deleted, bytes, failures, deletedJobs,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
