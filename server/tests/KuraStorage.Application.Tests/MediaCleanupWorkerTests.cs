using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Worker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class MediaCleanupWorkerTests
{
    [Fact]
    public async Task RunOnce_RunsCacheAtStartupAndTerminalHistoryAtDailyIntervals()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));
        var cleanup = new RecordingCleanup();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(30);
        await worker.RunOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddDays(1);
        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal([true, false, true], cleanup.TerminalFlags);
    }

    [Fact]
    public async Task RunLoop_UsesFailureBackoffAndStopsWithoutStartingAnotherScope()
    {
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RecordingCleanup { Failure = new IOException("injected") };
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .BuildServiceProvider();
        var delay = new CancellingDelay(cancellation);
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions { IntervalMinutes = 30, FailureBackoffMinutes = 5 },
            new MediaCleanupMetrics(),
            delay,
            NullLogger<MediaCleanupWorker>.Instance);

        await worker.RunLoopAsync(cancellation.Token);

        Assert.Equal(TimeSpan.FromMinutes(5), delay.Requested);
        Assert.Single(cleanup.TerminalFlags);
    }

    private sealed class RecordingCleanup : IMediaCleanupService
    {
        public Exception? Failure { get; init; }
        public List<bool> TerminalFlags { get; } = [];

        public Task<MediaCleanupResult> RunAsync(bool includeTerminalJobCleanup, CancellationToken cancellationToken)
        {
            TerminalFlags.Add(includeTerminalJobCleanup);
            return Failure is null
                ? Task.FromResult(new MediaCleanupResult(true, 1, 10, 0, 20, includeTerminalJobCleanup ? 2 : 0, 3))
                : Task.FromException<MediaCleanupResult>(Failure);
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class CancellingDelay(CancellationTokenSource cancellation) : IMediaCleanupDelay
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
