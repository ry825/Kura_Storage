using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Media;

namespace KuraStorage.Worker.Workers;

public sealed class MediaCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ISystemClock clock,
    MediaCleanupOptions options,
    MediaCleanupMetrics metrics,
    IMediaCleanupDelay delay,
    ILogger<MediaCleanupWorker> logger) : BackgroundService
{
    private DateTimeOffset nextTerminalJobCleanupAt = DateTimeOffset.MinValue;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    public async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = false;
            try
            {
                var result = await RunOnceAsync(stoppingToken);
                failed = result.FailureCount > 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failed = true;
                logger.LogError(exception, "Media cleanup iteration failed.");
            }

            var nextDelay = failed
                ? TimeSpan.FromMinutes(options.FailureBackoffMinutes)
                : TimeSpan.FromSeconds(options.ManualRunPollSeconds);
            try
            {
                await delay.DelayAsync(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<MediaCleanupResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var now = clock.UtcNow;
        var repository = scope.ServiceProvider.GetRequiredService<IMediaCleanupRepository>();
        await repository.EnsureScheduledRunAsync(
            now,
            TimeSpan.FromMinutes(options.IntervalMinutes),
            cancellationToken);
        var workerToken = Guid.NewGuid();
        var run = await repository.ClaimNextRunAsync(
            workerToken,
            now,
            now.AddMinutes(options.RunLeaseMinutes),
            cancellationToken);
        if (run is null)
        {
            return new MediaCleanupResult(false, 0, 0, 0, 0, 0, 0);
        }

        var includeTerminalJobs = now >= nextTerminalJobCleanupAt;
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<IMediaCleanupService>()
                .RunAsync(includeTerminalJobs, cancellationToken);
            if (!result.AcquiredLock)
            {
                await repository.ReleaseRunAsync(run.Id, workerToken, cancellationToken);
                return result;
            }

            if (!await repository.CompleteRunAsync(run.Id, workerToken, clock.UtcNow, result, cancellationToken))
            {
                throw new InvalidOperationException("The media cleanup run lease was lost before completion.");
            }

            if (includeTerminalJobs)
            {
                nextTerminalJobCleanupAt = now.AddDays(1);
            }

            metrics.Record(result, now);
            logger.LogInformation(
                "Media cleanup completed: acquired={AcquiredLock}, deleted={DeletedCount}, bytes={DeletedBytes}, " +
                "remaining={RemainingBytes}, terminalJobs={TerminalJobs}, failures={Failures}, elapsedMs={ElapsedMilliseconds}.",
                result.AcquiredLock,
                result.DeletedCount,
                result.DeletedBytes,
                result.RemainingCacheBytes,
                result.DeletedTerminalJobCount,
                result.FailureCount,
                result.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var code = exception is MediaCleanupStorageUnavailableException
                ? MediaCleanupFailureCode.StorageUnavailable
                : MediaCleanupFailureCode.CleanupFailed;
            await repository.FailRunAsync(run.Id, workerToken, clock.UtcNow, code, CancellationToken.None);
            throw;
        }
    }
}

public interface IMediaCleanupDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemMediaCleanupDelay : IMediaCleanupDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
