using System.Diagnostics.Metrics;
using System.Threading.Channels;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Indexing;

namespace KuraStorage.Worker.Workers;

public sealed class IndexEventWorker(
    IServiceScopeFactory scopeFactory,
    IIndexChangeWatcher watcher,
    IStorageGuard storageGuard,
    IIndexRescanSignal rescanSignal,
    IndexingOptions options,
    IndexingWorkerMetrics metrics,
    ILogger<IndexEventWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("KuraStorage.Indexing");
    private static readonly Counter<long> QueueOverflows =
        Meter.CreateCounter<long>("kurastorage.index.event.queue_overflow");
    private static long queueLength;
    private static readonly ObservableGauge<long> QueueLength = Meter.CreateObservableGauge(
        "kurastorage.index.event.queue.length",
        () => Interlocked.Read(ref queueLength));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        var channel = Channel.CreateBounded<IndexChangeEvent>(new BoundedChannelOptions(options.EventQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var producer = ProduceAsync(channel.Writer, stoppingToken);
        try
        {
            await ConsumeAsync(channel.Reader, stoppingToken);
        }
        finally
        {
            channel.Writer.TryComplete();
            try
            {
                await producer;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal graceful shutdown.
            }
        }
    }

    private async Task ProduceAsync(ChannelWriter<IndexChangeEvent> writer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
                {
                    rescanSignal.SetReady(false);
                    rescanSignal.Request(IndexScanTrigger.Overflow);
                    await Task.Delay(TimeSpan.FromSeconds(options.RetryBackoffSeconds), cancellationToken);
                    continue;
                }

                await foreach (var change in watcher.WatchAsync(cancellationToken))
                {
                    if (change.Kind is IndexChangeKind.Overflow or IndexChangeKind.WatcherStopped)
                    {
                        rescanSignal.SetReady(false);
                        rescanSignal.Request(IndexScanTrigger.Overflow);
                        continue;
                    }

                    if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
                    {
                        rescanSignal.SetReady(false);
                        rescanSignal.Request(IndexScanTrigger.Overflow);
                        break;
                    }

                    if (!writer.TryWrite(change))
                    {
                        QueueOverflows.Add(1);
                        rescanSignal.SetReady(false);
                        rescanSignal.Request(IndexScanTrigger.Overflow);
                        continue;
                    }

                    Interlocked.Increment(ref queueLength);
                    metrics.RecordEvent(DateTimeOffset.UtcNow);
                }

                rescanSignal.SetReady(false);
                rescanSignal.Request(IndexScanTrigger.Overflow);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Index watcher stopped; a full rescan was requested.");
                rescanSignal.SetReady(false);
                rescanSignal.Request(IndexScanTrigger.Overflow);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.RetryBackoffSeconds), cancellationToken);
        }
    }

    private async Task ConsumeAsync(ChannelReader<IndexChangeEvent> reader, CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            await rescanSignal.WaitUntilReadyAsync(cancellationToken);
            if (!reader.TryRead(out var first))
            {
                continue;
            }

            Interlocked.Decrement(ref queueLength);
            await Task.Delay(TimeSpan.FromMilliseconds(options.EventDebounceMilliseconds), cancellationToken);
            var coalesced = new Dictionary<string, IndexChangeEvent>(StringComparer.Ordinal)
            {
                [Key(first)] = first,
            };
            while (reader.TryRead(out var next))
            {
                Interlocked.Decrement(ref queueLength);
                var key = Key(next);
                coalesced[key] = Coalesce(coalesced.GetValueOrDefault(key), next);
            }

            foreach (var change in coalesced.Values)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                try
                {
                    var result = await scope.ServiceProvider.GetRequiredService<IIndexEventService>()
                        .ReconcileAsync(change, cancellationToken);
                    if (result is IndexEventResult.RescanRequired or IndexEventResult.Deferred)
                    {
                        rescanSignal.SetReady(false);
                        rescanSignal.Request(IndexScanTrigger.Overflow);
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "An index event failed; a full rescan was requested.");
                    rescanSignal.SetReady(false);
                    rescanSignal.Request(IndexScanTrigger.Overflow);
                    break;
                }
            }
        }
    }

    private static string Key(IndexChangeEvent change) =>
        change.Kind == IndexChangeKind.Move
            ? $"{change.PreviousRelativePath}\n{change.RelativePath}"
            : change.RelativePath;

    private static IndexChangeEvent Coalesce(IndexChangeEvent? previous, IndexChangeEvent current)
    {
        if (previous is null || current.Kind == IndexChangeKind.Move)
        {
            return current;
        }

        return current with
        {
            ContentMayHaveChanged = previous.ContentMayHaveChanged || current.ContentMayHaveChanged,
        };
    }
}
