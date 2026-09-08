using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;

namespace KuraStorage.Worker.Workers;

public sealed class MediaMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    MediaMaintenanceOptions options,
    ILogger<MediaMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.IntervalMinutes));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IMediaMaintenanceService>().RunAsync(stoppingToken);
                if (result.FailureCount > 0)
                {
                    logger.LogWarning("Media maintenance finished with {FailureCount} failures.", result.FailureCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Media maintenance iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
