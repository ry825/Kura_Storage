using System.Diagnostics;
using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Media;

public sealed class MediaCleanupOptions
{
    public int IntervalMinutes { get; init; } = 30;

    public int FailureBackoffMinutes { get; init; } = 5;

    public int BatchSize { get; init; } = 100;

    public long CacheHighWatermarkBytes { get; init; } = 10_737_418_240;

    public long CacheLowWatermarkBytes { get; init; } = 6_442_450_944;

    public int TerminalJobRetentionDays { get; init; } = 7;
}

public sealed record MediaCleanupResult(
    bool AcquiredLock,
    int DeletedCount,
    long DeletedBytes,
    int FailureCount,
    long RemainingCacheBytes,
    int DeletedTerminalJobCount,
    long ElapsedMilliseconds);

public sealed class MediaCleanupService(
    IMediaCleanupRepository repository,
    IDerivativeStore store,
    IStorageGuard storageGuard,
    ISystemClock clock,
    MediaCleanupOptions options) : IMediaCleanupService
{
    private static readonly Meter Meter = new("KuraStorage.Media.Cleanup");
    private static readonly Counter<long> CandidateCounter = Meter.CreateCounter<long>("kurastorage.media.cleanup.candidates");
    private static readonly Counter<long> DeleteCounter = Meter.CreateCounter<long>("kurastorage.media.cleanup.deleted");
    private static readonly Counter<long> DeletedBytesCounter = Meter.CreateCounter<long>("kurastorage.media.cleanup.deleted_bytes", "By");
    private static readonly Counter<long> FailureCounter = Meter.CreateCounter<long>("kurastorage.media.cleanup.failures");
    private static readonly Counter<long> TerminalJobCounter = Meter.CreateCounter<long>("kurastorage.media.cleanup.terminal_jobs");

    public async Task<MediaCleanupResult> RunAsync(
        bool includeTerminalJobCleanup,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        if (await storageGuard.InspectAsync(StorageIntent.Delete, cancellationToken) != StorageStatus.Available)
        {
            throw new IOException("Derivative storage is unavailable for cleanup.");
        }

        await using var cleanupLock = await repository.TryAcquireCleanupLockAsync(cancellationToken);
        if (cleanupLock is null)
        {
            return new MediaCleanupResult(false, 0, 0, 0, 0, 0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        var deletedCount = 0;
        long deletedBytes = 0;
        var failureCount = 0;
        var failed = await DeleteBatchesAsync(
            (now, token) => repository.ClaimDeletingAsync(now, options.BatchSize, token),
            "deleting_recovery",
            cancellationToken,
            () => deletedCount++,
            bytes => deletedBytes = checked(deletedBytes + bytes),
            () => failureCount++);
        failed |= await DeleteBatchesAsync(
            (now, token) => repository.ClaimExpiredAsync(now, options.BatchSize, token),
            "expired",
            cancellationToken,
            () => deletedCount++,
            bytes => deletedBytes = checked(deletedBytes + bytes),
            () => failureCount++);

        var remaining = await repository.GetReadyCacheSizeAsync(cancellationToken);
        var watermarkCleanupRequired = remaining > options.CacheHighWatermarkBytes;
        while (!failed && watermarkCleanupRequired && remaining > options.CacheLowWatermarkBytes &&
            !cancellationToken.IsCancellationRequested)
        {
            // Claim one LRU entry at a time. Claiming a full batch changes every
            // selected row to DELETING before the service can observe that an
            // early large entry already brought the cache below the low watermark.
            var candidates = await repository.ClaimLruAsync(clock.UtcNow, 1, cancellationToken);
            if (candidates.Count == 0)
            {
                break;
            }

            CandidateCounter.Add(candidates.Count, KeyValuePair.Create<string, object?>("reason", "watermark"));
            failed = await DeleteCandidatesAsync(
                candidates,
                cancellationToken,
                () => deletedCount++,
                bytes => deletedBytes = checked(deletedBytes + bytes),
                () => failureCount++);
            remaining = await repository.GetReadyCacheSizeAsync(cancellationToken);
            if (remaining <= options.CacheLowWatermarkBytes)
            {
                break;
            }
        }

        var deletedJobs = 0;
        if (includeTerminalJobCleanup && !failed)
        {
            var cutoff = clock.UtcNow.AddDays(-options.TerminalJobRetentionDays);
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await repository.DeleteTerminalJobsAsync(cutoff, options.BatchSize, cancellationToken);
                deletedJobs = checked(deletedJobs + count);
                TerminalJobCounter.Add(count);
                if (count < options.BatchSize)
                {
                    break;
                }
            }
        }

        return new MediaCleanupResult(
            true,
            deletedCount,
            deletedBytes,
            failureCount,
            remaining,
            deletedJobs,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private async Task<bool> DeleteBatchesAsync(
        Func<DateTimeOffset, CancellationToken, Task<IReadOnlyList<MediaCleanupCandidate>>> claim,
        string reason,
        CancellationToken cancellationToken,
        Action deleted,
        Action<long> deletedBytes,
        Action failed)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var candidates = await claim(clock.UtcNow, cancellationToken);
            if (candidates.Count == 0)
            {
                return false;
            }

            CandidateCounter.Add(candidates.Count, KeyValuePair.Create<string, object?>("reason", reason));
            var hadFailure = await DeleteCandidatesAsync(candidates, cancellationToken, deleted, deletedBytes, failed);
            if (hadFailure || candidates.Count < options.BatchSize)
            {
                return hadFailure;
            }
        }

        return false;
    }

    private async Task<bool> DeleteCandidatesAsync(
        IReadOnlyList<MediaCleanupCandidate> candidates,
        CancellationToken cancellationToken,
        Action deleted,
        Action<long> deletedBytes,
        Action failed)
    {
        var hadFailure = false;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await store.DeleteIfExistsAsync(candidate.Path, cancellationToken);
                await repository.CompleteDeleteAsync(candidate.DerivativeId, cancellationToken);
                deleted();
                deletedBytes(candidate.Size);
                DeleteCounter.Add(1);
                DeletedBytesCounter.Add(candidate.Size);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                hadFailure = true;
                failed();
                FailureCounter.Add(1, KeyValuePair.Create<string, object?>("stage", "physical_delete"));
                if (candidate.RestoreReadyOnFailure)
                {
                    await repository.RestoreReadyAsync(candidate.DerivativeId, clock.UtcNow, cancellationToken);
                }
            }
        }

        return hadFailure;
    }
}
