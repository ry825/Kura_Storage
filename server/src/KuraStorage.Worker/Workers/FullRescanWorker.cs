using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Indexing;

namespace KuraStorage.Worker.Workers;

public sealed class FullRescanWorker(
    IServiceScopeFactory scopeFactory,
    IndexingOptions options,
    IIndexRescanSignal rescanSignal,
    IndexingWorkerMetrics metrics,
    ILogger<FullRescanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (options.RunOnStartup)
        {
            await RunWithRetryBoundaryAsync(IndexScanTrigger.Startup, stoppingToken);
        }
        else
        {
            rescanSignal.SetReady(true);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var trigger = await rescanSignal.WaitAsync(
                    TimeSpan.FromMinutes(options.FullRescanIntervalMinutes),
                    stoppingToken) ?? IndexScanTrigger.Scheduled;
                await RunWithRetryBoundaryAsync(trigger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<bool> RunOnceAsync(IndexScanTrigger trigger, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var summary = await scope.ServiceProvider.GetRequiredService<IIndexScanService>().RunAsync(
            new IndexScanRequest(trigger, IndexScanMode.Apply),
            cancellationToken);
        var succeeded = summary.Status is IndexScanStatus.Completed or IndexScanStatus.CompletedWithWarnings;
        if (succeeded)
        {
            var catalog = scope.ServiceProvider.GetService<IIndexCatalogRepository>();
            var counts = catalog is null
                ? (CandidateCount: 0, MissingCount: 0)
                : await catalog.CountMissingStatesAsync(cancellationToken);
            metrics.RecordSuccessfulScan(DateTimeOffset.UtcNow, counts.CandidateCount, counts.MissingCount);
        }

        return succeeded;
    }

    private async Task RunWithRetryBoundaryAsync(IndexScanTrigger trigger, CancellationToken cancellationToken)
    {
        rescanSignal.SetReady(false);
        try
        {
            if (await RunOnceAsync(trigger, cancellationToken))
            {
                rescanSignal.SetReady(true);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IndexScanAlreadyRunningException)
        {
            logger.LogInformation("Index scan was deferred because another scan owns the global lock.");
        }
        catch (IndexStorageUnavailableException)
        {
            logger.LogWarning("Index scan was deferred because storage is unavailable.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Index scan failed with a retryable worker error.");
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.RetryBackoffSeconds), cancellationToken);
            rescanSignal.Request(trigger == IndexScanTrigger.Overflow ? trigger : IndexScanTrigger.Scheduled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal graceful shutdown.
        }
    }
}
