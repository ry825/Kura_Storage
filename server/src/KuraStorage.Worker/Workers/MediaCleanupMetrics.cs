using System.Diagnostics.Metrics;
using KuraStorage.Application.Media;

namespace KuraStorage.Worker.Workers;

public sealed class MediaCleanupMetrics
{
    private readonly Meter meter = new("KuraStorage.Media.Cleanup.Worker");
    private long lastCandidateCount;
    private long lastDeletedCount;
    private long lastDeletedBytes;
    private long lastRemainingBytes;
    private long lastFailureCount;
    private long lastElapsedMilliseconds;
    private long lastRunUnixSeconds;

    public MediaCleanupMetrics()
    {
        meter.CreateObservableGauge("kurastorage.media.cleanup.candidate_count", () => lastCandidateCount);
        meter.CreateObservableGauge("kurastorage.media.cleanup.deleted_count", () => lastDeletedCount);
        meter.CreateObservableGauge("kurastorage.media.cleanup.deleted_bytes_last_run", () => lastDeletedBytes, "By");
        meter.CreateObservableGauge("kurastorage.media.cleanup.remaining_bytes", () => lastRemainingBytes, "By");
        meter.CreateObservableGauge("kurastorage.media.cleanup.failure_count", () => lastFailureCount);
        meter.CreateObservableGauge("kurastorage.media.cleanup.duration", () => lastElapsedMilliseconds, "ms");
        meter.CreateObservableGauge("kurastorage.media.cleanup.last_run", () => lastRunUnixSeconds, "s");
    }

    public void Record(MediaCleanupResult result, DateTimeOffset observedAt)
    {
        Interlocked.Exchange(ref lastCandidateCount, result.DeletedCount + result.FailureCount);
        Interlocked.Exchange(ref lastDeletedCount, result.DeletedCount);
        Interlocked.Exchange(ref lastDeletedBytes, result.DeletedBytes);
        Interlocked.Exchange(ref lastRemainingBytes, result.RemainingCacheBytes);
        Interlocked.Exchange(ref lastFailureCount, result.FailureCount);
        Interlocked.Exchange(ref lastElapsedMilliseconds, result.ElapsedMilliseconds);
        Interlocked.Exchange(ref lastRunUnixSeconds, observedAt.ToUnixTimeSeconds());
    }
}
