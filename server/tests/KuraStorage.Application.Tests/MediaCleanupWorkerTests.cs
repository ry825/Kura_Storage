using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Media;
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
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
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
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
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
        Assert.Equal(MediaCleanupFailureCode.CleanupFailed, runs.LastFailureCode);
    }

    [Fact]
    public async Task RunLoop_UsesFailureBackoffForPersistedPartialFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var cleanup = new RecordingCleanup
        {
            Result = new MediaCleanupResult(true, 1, 10, 1, 20, 0, 1),
        };
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var delay = new CancellingDelay(cancellation);
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions { FailureBackoffMinutes = 5 },
            new MediaCleanupMetrics(),
            delay,
            NullLogger<MediaCleanupWorker>.Instance);

        await worker.RunLoopAsync(cancellation.Token);

        Assert.Equal(TimeSpan.FromMinutes(5), delay.Requested);
        Assert.Equal(1, runs.CompleteCount);
    }

    [Fact]
    public async Task RunOnce_ReleasesClaimWhenGlobalCleanupLockIsBusy()
    {
        var cleanup = new RecordingCleanup
        {
            Result = new MediaCleanupResult(false, 0, 0, 0, 0, 0, 1),
        };
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, runs.ReleaseCount);
        Assert.Equal(0, runs.CompleteCount);
    }

    [Fact]
    public async Task RunOnce_RecordsStorageUnavailableWithSafeFailureCode()
    {
        var cleanup = new RecordingCleanup { Failure = new MediaCleanupStorageUnavailableException() };
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        await Assert.ThrowsAsync<MediaCleanupStorageUnavailableException>(
            () => worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(MediaCleanupFailureCode.StorageUnavailable, runs.LastFailureCode);
    }

    [Fact]
    public async Task RunOnce_PersistsPartialDeletionResultWithoutRetryingInsideRequest()
    {
        var cleanup = new RecordingCleanup
        {
            Result = new MediaCleanupResult(true, 2, 20, 1, 30, 0, 4),
        };
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        var result = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.FailureCount);
        Assert.Equal(result, runs.LastCompletionResult);
        Assert.Equal(1, runs.CompleteCount);
        Assert.Null(runs.LastFailureCode);
    }

    [Fact]
    public async Task RunOnce_ReturnsWithoutCleanupWhenNoRunIsClaimable()
    {
        var cleanup = new RecordingCleanup();
        var runs = new RunRepository { ClaimAvailable = false };
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        var result = await worker.RunOnceAsync(CancellationToken.None);

        Assert.False(result.AcquiredLock);
        Assert.Empty(cleanup.TerminalFlags);
    }

    [Fact]
    public async Task RunOnce_LeavesCancelledRunForLeaseRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cleanup = new RecordingCleanup { Failure = new OperationCanceledException(cancellation.Token) };
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => worker.RunOnceAsync(cancellation.Token));

        Assert.Null(runs.LastFailureCode);
        Assert.Equal(0, runs.CompleteCount);
    }

    [Fact]
    public async Task RunOnce_FailsSafelyWhenLeaseOwnershipIsLostBeforeCompletion()
    {
        var runs = new RunRepository { CompleteSucceeded = false };
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => new RecordingCleanup())
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            new SystemMediaCleanupDelay(),
            NullLogger<MediaCleanupWorker>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(MediaCleanupFailureCode.CleanupFailed, runs.LastFailureCode);
    }

    [Fact]
    public async Task SystemDelay_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SystemMediaCleanupDelay().DelayAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }

    [Fact]
    public async Task HostedWorker_StartsAndStopsThroughBackgroundServiceLifecycle()
    {
        var cleanup = new RecordingCleanup();
        var runs = new RunRepository();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaCleanupService>(_ => cleanup)
            .AddScoped<IMediaCleanupRepository>(_ => runs)
            .BuildServiceProvider();
        var delay = new SignalingDelay();
        var worker = new MediaCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new MutableClock(DateTimeOffset.UtcNow),
            new MediaCleanupOptions(),
            new MediaCleanupMetrics(),
            delay,
            NullLogger<MediaCleanupWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await delay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        var execution = worker.ExecuteTask;
        Assert.NotNull(execution);
        Assert.True(execution.IsCompletedSuccessfully);
    }

    private sealed class RecordingCleanup : IMediaCleanupService
    {
        public Exception? Failure { get; init; }
        public MediaCleanupResult Result { get; init; } = new(true, 1, 10, 0, 20, 0, 3);
        public List<bool> TerminalFlags { get; } = [];

        public Task<MediaCleanupResult> RunAsync(bool includeTerminalJobCleanup, CancellationToken cancellationToken)
        {
            TerminalFlags.Add(includeTerminalJobCleanup);
            return Failure is null
                ? Task.FromResult(Result with { DeletedTerminalJobCount = includeTerminalJobCleanup ? 2 : 0 })
                : Task.FromException<MediaCleanupResult>(Failure);
        }
    }

    private sealed class RunRepository : IMediaCleanupRepository
    {
        private MediaCleanupRun? pending;

        public int ReleaseCount { get; private set; }
        public int CompleteCount { get; private set; }
        public MediaCleanupFailureCode? LastFailureCode { get; private set; }
        public MediaCleanupResult? LastCompletionResult { get; private set; }
        public bool ClaimAvailable { get; init; } = true;
        public bool CompleteSucceeded { get; init; } = true;

        public Task<MediaCleanupRun?> EnsureScheduledRunAsync(DateTimeOffset now, TimeSpan interval, CancellationToken cancellationToken)
        {
            pending ??= MediaCleanupRun.CreateScheduled(Guid.NewGuid(), now);
            return Task.FromResult<MediaCleanupRun?>(pending);
        }

        public Task<MediaCleanupRun?> ClaimNextRunAsync(Guid workerToken, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken)
        {
            if (!ClaimAvailable)
            {
                return Task.FromResult<MediaCleanupRun?>(null);
            }

            var claimed = pending;
            pending = null;
            claimed?.Claim(workerToken, now, leaseExpiresAt);
            return Task.FromResult(claimed);
        }

        public Task<bool> ReleaseRunAsync(Guid runId, Guid workerToken, CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.FromResult(true);
        }

        public Task<bool> CompleteRunAsync(Guid runId, Guid workerToken, DateTimeOffset completedAt, MediaCleanupResult result, CancellationToken cancellationToken)
        {
            CompleteCount++;
            LastCompletionResult = result;
            return Task.FromResult(CompleteSucceeded);
        }

        public Task<bool> FailRunAsync(Guid runId, Guid workerToken, DateTimeOffset completedAt, MediaCleanupFailureCode failureCode, CancellationToken cancellationToken)
        {
            LastFailureCode = failureCode;
            return Task.FromResult(true);
        }

        public Task<IAsyncDisposable?> TryAcquireCleanupLockAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimDeletingAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimLruAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<long> GetReadyCacheSizeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteDeleteAsync(Guid derivativeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RestoreReadyAsync(Guid derivativeId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteTerminalJobsAsync(DateTimeOffset completedBefore, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class SignalingDelay : IMediaCleanupDelay
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
