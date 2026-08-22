using System.Runtime.CompilerServices;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Indexing;
using KuraStorage.Worker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class IndexWorkerTests
{
    [Fact]
    public async Task RescanSignal_OverflowTakesPriorityAndReadyGateCanBeReset()
    {
        using var signal = new IndexRescanSignal();
        signal.Request(IndexScanTrigger.Scheduled);
        signal.Request(IndexScanTrigger.Overflow);

        var trigger = await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(IndexScanTrigger.Overflow, trigger);

        signal.SetReady(true);
        await signal.WaitUntilReadyAsync(CancellationToken.None);
        signal.SetReady(false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => signal.WaitUntilReadyAsync(cancellation.Token));
    }

    [Fact]
    public async Task FullRescanWorker_RunOnce_UsesApplicationScanService()
    {
        var scanner = new RecordingScanService();
        await using var services = new ServiceCollection()
            .AddScoped<IIndexScanService>(_ => scanner)
            .BuildServiceProvider();
        using var signal = new IndexRescanSignal();
        var worker = new FullRescanWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new IndexingOptions { Enabled = true },
            signal,
            new IndexingWorkerMetrics(),
            NullLogger<FullRescanWorker>.Instance);

        var succeeded = await worker.RunOnceAsync(IndexScanTrigger.Startup, CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal(IndexScanTrigger.Startup, scanner.LastRequest?.Trigger);
        Assert.Equal(IndexScanMode.Apply, scanner.LastRequest?.Mode);
    }

    [Fact]
    public async Task IndexEventWorker_CoalescesBurstBeforeApplicationProcessing()
    {
        var eventService = new RecordingEventService();
        await using var services = new ServiceCollection()
            .AddScoped<IIndexEventService>(_ => eventService)
            .BuildServiceProvider();
        using var signal = new IndexRescanSignal();
        signal.SetReady(true);
        var path = $"users/{Guid.NewGuid():N}/files/burst.txt";
        var watcher = new BurstWatcher(
            new IndexChangeEvent(IndexChangeKind.Reconcile, path),
            new IndexChangeEvent(IndexChangeKind.Reconcile, path, ContentMayHaveChanged: true));
        var worker = new IndexEventWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            watcher,
            new AvailableStorageGuard(),
            signal,
            new IndexingOptions
            {
                Enabled = true,
                EventQueueCapacity = 128,
                EventDebounceMilliseconds = 50,
                RetryBackoffSeconds = 1,
            },
            new IndexingWorkerMetrics(),
            NullLogger<IndexEventWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var received = await eventService.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(received.ContentMayHaveChanged);
        Assert.Equal(1, eventService.CallCount);
    }

    [Fact]
    public async Task IndexEventWorker_OverflowRequestsFullRescan()
    {
        await using var services = new ServiceCollection()
            .AddScoped<IIndexEventService, RecordingEventService>()
            .BuildServiceProvider();
        using var signal = new IndexRescanSignal();
        signal.SetReady(true);
        var worker = new IndexEventWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            new BurstWatcher(new IndexChangeEvent(IndexChangeKind.Overflow, string.Empty)),
            new AvailableStorageGuard(),
            signal,
            new IndexingOptions
            {
                Enabled = true,
                EventQueueCapacity = 128,
                EventDebounceMilliseconds = 50,
                RetryBackoffSeconds = 1,
            },
            new IndexingWorkerMetrics(),
            NullLogger<IndexEventWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var trigger = await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(IndexScanTrigger.Overflow, trigger);
    }

    [Fact]
    public async Task IndexEventWorker_WhenStorageUnavailable_DoesNotStartWatcher()
    {
        await using var services = new ServiceCollection()
            .AddScoped<IIndexEventService, RecordingEventService>()
            .BuildServiceProvider();
        using var signal = new IndexRescanSignal();
        var watcher = new CountingWatcher();
        var worker = new IndexEventWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            watcher,
            new FixedStorageGuard(StorageStatus.Unavailable),
            signal,
            new IndexingOptions
            {
                Enabled = true,
                EventQueueCapacity = 128,
                EventDebounceMilliseconds = 50,
                RetryBackoffSeconds = 1,
            },
            new IndexingWorkerMetrics(),
            NullLogger<IndexEventWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var trigger = await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(IndexScanTrigger.Overflow, trigger);
        Assert.Equal(0, watcher.StartCount);
    }

    [Fact]
    public async Task IndexEventWorker_ThreeHundredThousandEventBurst_RemainsBoundedAndRequestsRescan()
    {
        await using var services = new ServiceCollection()
            .AddScoped<IIndexEventService, RecordingEventService>()
            .BuildServiceProvider();
        using var signal = new IndexRescanSignal();
        signal.SetReady(true);
        var watcher = new CountingWatcher(300_000);
        var worker = new IndexEventWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            watcher,
            new AvailableStorageGuard(),
            signal,
            new IndexingOptions
            {
                Enabled = true,
                EventQueueCapacity = 128,
                EventDebounceMilliseconds = 50,
                RetryBackoffSeconds = 1,
            },
            new IndexingWorkerMetrics(),
            NullLogger<IndexEventWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var trigger = await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await watcher.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(IndexScanTrigger.Overflow, trigger);
    }

    private sealed class RecordingScanService : IIndexScanService
    {
        public IndexScanRequest? LastRequest { get; private set; }

        public Task<IndexScanSummary> RunAsync(IndexScanRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new IndexScanSummary(
                Guid.NewGuid(), IndexScanStatus.Completed, 0, 0, 0, 0, 0, 0, 0, 0, 0, null));
        }
    }

    private sealed class RecordingEventService : IIndexEventService
    {
        public TaskCompletionSource<IndexChangeEvent> Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public Task<IndexEventResult> ReconcileAsync(IndexChangeEvent change, CancellationToken cancellationToken)
        {
            CallCount++;
            Received.TrySetResult(change);
            return Task.FromResult(IndexEventResult.Applied);
        }
    }

    private sealed class BurstWatcher(params IndexChangeEvent[] changes) : IIndexChangeWatcher
    {
        public async IAsyncEnumerable<IndexChangeEvent> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var change in changes)
            {
                yield return change;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class AvailableStorageGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }

    private sealed class FixedStorageGuard(StorageStatus status) : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class CountingWatcher(int eventCount = 0) : IIndexChangeWatcher
    {
        public int StartCount { get; private set; }
        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<IndexChangeEvent> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StartCount++;
            var path = $"users/{Guid.NewGuid():N}/files/burst.txt";
            for (var index = 0; index < eventCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new IndexChangeEvent(IndexChangeKind.Reconcile, path);
            }

            Completed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
