using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;

namespace KuraStorage.Worker.Workers;

public sealed class TrashPurgeWorker(
    IServiceScopeFactory scopeFactory,
    TrashPurgeOptions options,
    ITrashPurgeDelay purgeDelay,
    ILogger<TrashPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunLoopAsync(stoppingToken);
    }

    public async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = false;
            try
            {
                var result = await RunOnceAsync(stoppingToken);
                failed = result.Status == "FAILED";
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failed = true;
                logger.LogError(exception, "Trash purge run failed.");
            }

            var nextDelay = failed
                ? TimeSpan.FromMinutes(options.RetryDelayMinutes)
                : TimeSpan.FromHours(options.IntervalHours);
            try
            {
                await purgeDelay.DelayAsync(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<TrashPurgeRunSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ITrashPurgeRunner>()
            .RunAsync(cancellationToken);
    }
}

public interface ITrashPurgeDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemTrashPurgeDelay : ITrashPurgeDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
