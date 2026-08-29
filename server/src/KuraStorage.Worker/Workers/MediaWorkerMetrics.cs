using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;

namespace KuraStorage.Worker.Workers;

public sealed class MediaWorkerMetrics
{
    private readonly Meter meter = new("KuraStorage.Media.Worker");
    private long queuedCount;
    private long runningCount;
    private long oldestWaitSeconds;
    private long lastIterationUnixSeconds;

    public MediaWorkerMetrics()
    {
        meter.CreateObservableGauge("kurastorage.media.queue.depth", () => queuedCount);
        meter.CreateObservableGauge("kurastorage.media.queue.oldest_wait", () => oldestWaitSeconds, "s");
        meter.CreateObservableGauge("kurastorage.media.worker.running", () => runningCount);
        meter.CreateObservableGauge("kurastorage.media.worker.last_iteration", () => lastIterationUnixSeconds, "s");
    }

    public void RecordSnapshot(MediaQueueSnapshot snapshot, DateTimeOffset observedAt)
    {
        Interlocked.Exchange(ref queuedCount, snapshot.QueuedCount);
        Interlocked.Exchange(ref runningCount, snapshot.RunningCount);
        Interlocked.Exchange(ref oldestWaitSeconds, snapshot.OldestWaitSeconds);
        Interlocked.Exchange(ref lastIterationUnixSeconds, observedAt.ToUnixTimeSeconds());
    }

    public void RecordIteration(DateTimeOffset observedAt) =>
        Interlocked.Exchange(ref lastIterationUnixSeconds, observedAt.ToUnixTimeSeconds());
}
