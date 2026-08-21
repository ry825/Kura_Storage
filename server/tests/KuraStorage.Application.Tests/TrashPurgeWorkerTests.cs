using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Worker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class TrashPurgeWorkerTests
{
    [Theory]
    [InlineData("COMPLETED", 24, 0)]
    [InlineData("FAILED", 0, 15)]
    public async Task RunLoop_RunsImmediatelyThenUsesConfiguredDelay(
        string status,
        int expectedHours,
        int expectedMinutes)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new RecordingRunner(status);
        var services = new ServiceCollection()
            .AddScoped<ITrashPurgeRunner>(_ => runner)
            .BuildServiceProvider();
        var delay = new CancellingDelay(cancellation);
        var worker = new TrashPurgeWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new TrashPurgeOptions { IntervalHours = 24, RetryDelayMinutes = 15 },
            delay,
            NullLogger<TrashPurgeWorker>.Instance);

        await worker.RunLoopAsync(cancellation.Token);

        Assert.Equal(1, runner.CallCount);
        Assert.Equal(TimeSpan.FromHours(expectedHours) + TimeSpan.FromMinutes(expectedMinutes), delay.Requested);
    }

    [Fact]
    public async Task RunLoop_WhenAlreadyCancelled_DoesNotStartRun()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new RecordingRunner("COMPLETED");
        var services = new ServiceCollection()
            .AddScoped<ITrashPurgeRunner>(_ => runner)
            .BuildServiceProvider();
        var worker = new TrashPurgeWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new TrashPurgeOptions(),
            new CancellingDelay(cancellation),
            NullLogger<TrashPurgeWorker>.Instance);

        await worker.RunLoopAsync(cancellation.Token);

        Assert.Equal(0, runner.CallCount);
    }

    private sealed class RecordingRunner(string status) : ITrashPurgeRunner
    {
        public int CallCount { get; private set; }

        public Task<TrashPurgeRunSummary> RunAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                new TrashPurgeRunSummary(
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    status,
                    0,
                    0,
                    0,
                    status == "FAILED" ? 1 : 0));
        }
    }

    private sealed class CancellingDelay(CancellationTokenSource cancellation) : ITrashPurgeDelay
    {
        public TimeSpan Requested { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Requested = delay;
            cancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
        }
    }
}
