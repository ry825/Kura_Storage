using KuraStorage.Application.Files;
using KuraStorage.Application.Transfers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KuraStorage.Infrastructure.Storage;

public sealed class FileRecoveryHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<FileRecoveryHostedService> logger,
    IOptions<UploadSessionOptions> configuredUploadOptions) : BackgroundService
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan cleanupInterval = TimeSpan.FromMinutes(configuredUploadOptions.Value.CleanupIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var nextRecovery = DateTimeOffset.UtcNow.Add(RecoveryInterval);
        var nextCleanup = DateTimeOffset.UtcNow.Add(cleanupInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextRecovery)
            {
                await RecoverAsync(stoppingToken);
                nextRecovery = now.Add(RecoveryInterval);
            }

            if (now >= nextCleanup)
            {
                await CleanupAsync(stoppingToken);
                nextCleanup = now.Add(cleanupInterval);
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<UploadSessionRecoveryService>()
                .RecoverAsync(cancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<FileOperationRecoveryService>()
                .RecoverAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "File operation recovery failed.");
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<UploadSessionCleanupService>()
                .RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Upload session cleanup failed.");
        }
    }
}
