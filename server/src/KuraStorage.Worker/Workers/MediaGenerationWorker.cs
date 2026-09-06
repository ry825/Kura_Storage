using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;

namespace KuraStorage.Worker.Workers;

public sealed class MediaGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ISystemClock clock,
    MediaRuntimeOptions options,
    MediaWorkerMetrics metrics,
    ILogger<MediaGenerationWorker> logger) : BackgroundService
{
    private DateTimeOffset nextRecoveryAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lanes = new List<Task>(options.MaximumConcurrentThumbnailJobs + 1);
        for (var slot = 0; slot < options.MaximumConcurrentThumbnailJobs; slot++)
        {
            lanes.Add(RunLaneAsync(
                MediaJobClaimScope.Thumbnail,
                options.MaximumConcurrentThumbnailJobs,
                runsMaintenance: slot == 0,
                stoppingToken));
        }

        lanes.Add(RunLaneAsync(MediaJobClaimScope.NonThumbnail, 1, runsMaintenance: false, stoppingToken));
        await Task.WhenAll(lanes);
    }

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        await RunMaintenanceAsync(cancellationToken);
        var runs = new List<Task<bool>>(options.MaximumConcurrentThumbnailJobs + 1);
        for (var slot = 0; slot < options.MaximumConcurrentThumbnailJobs; slot++)
        {
            runs.Add(RunJobAsync(
                MediaJobClaimScope.Thumbnail,
                options.MaximumConcurrentThumbnailJobs,
                cancellationToken));
        }

        runs.Add(RunJobAsync(MediaJobClaimScope.NonThumbnail, 1, cancellationToken));
        return (await Task.WhenAll(runs)).Any(processed => processed);
    }

    private async Task RunLaneAsync(
        MediaJobClaimScope claimScope,
        int maximumConcurrency,
        bool runsMaintenance,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (runsMaintenance)
                {
                    await RunMaintenanceAsync(stoppingToken);
                }

                var processed = await RunJobAsync(claimScope, maximumConcurrency, stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(options.JobPollMilliseconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Media generation {ClaimScope} lane iteration failed.", claimScope);
                await Task.Delay(TimeSpan.FromMilliseconds(options.JobPollMilliseconds), stoppingToken);
            }
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var now = clock.UtcNow;
        if (now >= nextRecoveryAt)
        {
            var queue = scope.ServiceProvider.GetRequiredService<IMediaJobQueue>();
            var temporaryCandidates = await queue.FindStaleTemporaryCandidatesAsync(now, 100, cancellationToken);
            var recovered = await queue.RecoverStaleAsync(now, 100, cancellationToken);
            MediaGenerationMetrics.RecordStaleRecoveries(recovered);
            var derivativeStore = scope.ServiceProvider.GetRequiredService<IDerivativeStore>();
            foreach (var candidate in temporaryCandidates)
            {
                try
                {
                    await derivativeStore.DeleteTemporaryAsync(
                        candidate.JobId, candidate.Attempt, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        exception,
                        "Stale media temporary output cleanup failed for job {JobId} attempt {Attempt}.",
                        candidate.JobId,
                        candidate.Attempt);
                }
            }

            metrics.RecordSnapshot(await queue.GetOperationalSnapshotAsync(now, cancellationToken), now);
            nextRecoveryAt = now.AddMinutes(1);
        }

        else
        {
            metrics.RecordIteration(now);
        }
    }

    private async Task<bool> RunJobAsync(
        MediaJobClaimScope claimScope,
        int maximumConcurrency,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IMediaJobRunner>()
            .RunNextAsync(claimScope, maximumConcurrency, cancellationToken);
    }
}
