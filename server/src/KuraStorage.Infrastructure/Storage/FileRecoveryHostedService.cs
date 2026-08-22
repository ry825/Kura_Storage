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
    IOptions<UploadSessionOptions> configuredUploadOptions) : BackgroundService, IHostedLifecycleService
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan cleanupInterval = TimeSpan.FromMinutes(configuredUploadOptions.Value.CleanupIntervalMinutes);

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await RecoverAsync(cancellationToken);
        await CleanupAsync(cancellationToken);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
