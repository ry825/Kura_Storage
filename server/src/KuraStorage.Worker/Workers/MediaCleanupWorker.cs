using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;

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

            var nextDelay = TimeSpan.FromMinutes(
                failed ? options.FailureBackoffMinutes : options.IntervalMinutes);
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
        var includeTerminalJobs = now >= nextTerminalJobCleanupAt;
        var result = await scope.ServiceProvider
            .GetRequiredService<IMediaCleanupService>()
            .RunAsync(includeTerminalJobs, cancellationToken);
        if (result.AcquiredLock && includeTerminalJobs)
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
