using System.Diagnostics.Metrics;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Media;

public static class MediaGenerationMetrics
{
    private static readonly Meter Meter = new("KuraStorage.Media");
    private static readonly UpDownCounter<long> RunningJobs =
        Meter.CreateUpDownCounter<long>("kurastorage.media.jobs.running");
    private static readonly Counter<long> JobResults =
        Meter.CreateCounter<long>("kurastorage.media.job.results");
    private static readonly Histogram<double> GenerationDuration =
        Meter.CreateHistogram<double>("kurastorage.media.generation.duration", "s");
    private static readonly Histogram<long> OutputBytes =
        Meter.CreateHistogram<long>("kurastorage.media.output.bytes", "By");
    private static readonly Counter<long> StaleRecoveries =
        Meter.CreateCounter<long>("kurastorage.media.stale.recovered");

    public static void JobStarted() => RunningJobs.Add(1);

    public static void JobFinished(
        string result,
        string reason,
        DerivativeType type,
        TimeSpan elapsed,
        long? outputBytes = null)
    {
        var variant = Variant(type);
        RunningJobs.Add(-1);
        JobResults.Add(1,
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("variant", variant));
        GenerationDuration.Record(elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("variant", variant));
        if (outputBytes is > 0)
        {
            OutputBytes.Record(outputBytes.Value,
                new KeyValuePair<string, object?>("variant", variant));
        }
    }

    public static void RecordStaleRecoveries(int count)
    {
        if (count > 0)
        {
            StaleRecoveries.Add(count);
        }
    }

    private static string Variant(DerivativeType type) => type switch
    {
        DerivativeType.Thumbnail => "thumbnail",
        DerivativeType.PdfThumbnail => "pdf-thumbnail",
        DerivativeType.ImageLow => "image-low",
        DerivativeType.ImageMedium => "image-medium",
        DerivativeType.VideoLow => "video-low",
        DerivativeType.VideoMedium => "video-medium",
        _ => "unknown",
    };
}
