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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await RunOnceAsync(stoppingToken);
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
                logger.LogError(exception, "Media generation iteration failed.");
                await Task.Delay(TimeSpan.FromMilliseconds(options.JobPollMilliseconds), stoppingToken);
            }
        }
    }

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
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

        return await scope.ServiceProvider.GetRequiredService<IMediaJobRunner>().RunNextAsync(cancellationToken);
    }
}
