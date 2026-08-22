using System.Diagnostics.Metrics;

namespace KuraStorage.Worker.Workers;

public sealed class IndexingWorkerMetrics
{
    private readonly Meter meter = new("KuraStorage.Indexing.Worker");
    private long lastEventUnixSeconds;
    private long lastSuccessfulScanUnixSeconds;
    private long candidateCount;
    private long missingCount;

    public IndexingWorkerMetrics()
    {
        meter.CreateObservableGauge("kurastorage.index.event.last_seen", () => lastEventUnixSeconds, "s");
        meter.CreateObservableGauge("kurastorage.index.scan.last_success", () => lastSuccessfulScanUnixSeconds, "s");
        meter.CreateObservableGauge(
            "kurastorage.index.scan.lag",
            () => lastSuccessfulScanUnixSeconds == 0
                ? 0
                : Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() -
                              Interlocked.Read(ref lastSuccessfulScanUnixSeconds)),
            "s");
        meter.CreateObservableGauge("kurastorage.index.entries.missing_candidate", () => candidateCount);
        meter.CreateObservableGauge("kurastorage.index.entries.missing", () => missingCount);
    }

    public void RecordEvent(DateTimeOffset observedAt) =>
        Interlocked.Exchange(ref lastEventUnixSeconds, observedAt.ToUnixTimeSeconds());

    public void RecordSuccessfulScan(DateTimeOffset completedAt, int candidates, int missing)
    {
        Interlocked.Exchange(ref lastSuccessfulScanUnixSeconds, completedAt.ToUnixTimeSeconds());
        Interlocked.Exchange(ref candidateCount, candidates);
        Interlocked.Exchange(ref missingCount, missing);
    }
}
