using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;

namespace KuraStorage.Worker.Workers;

public sealed class MediaGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ISystemClock clock,
    MediaRuntimeOptions options,
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
            await scope.ServiceProvider.GetRequiredService<IMediaJobQueue>()
                .RecoverStaleAsync(now, 100, cancellationToken);
            nextRecoveryAt = now.AddSeconds(30);
        }

        return await scope.ServiceProvider.GetRequiredService<MediaJobRunner>().RunNextAsync(cancellationToken);
    }
}
