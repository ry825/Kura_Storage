using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Worker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class MediaGenerationWorkerTests
{
    [Fact]
    public async Task RunOnce_RecoversAtStartupAndOneMinuteIntervalsAndDeletesOnlyCandidates()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));
        var queue = new RecordingQueue();
        var store = new RecordingStore();
        var runner = new RecordingRunner();
        await using var services = new ServiceCollection()
            .AddScoped<IMediaJobQueue>(_ => queue)
            .AddScoped<IDerivativeStore>(_ => store)
            .AddScoped<IMediaJobRunner>(_ => runner)
            .BuildServiceProvider();
        var worker = new MediaGenerationWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new MediaRuntimeOptions(),
            new MediaWorkerMetrics(),
            NullLogger<MediaGenerationWorker>.Instance);

        Assert.False(await worker.RunOnceAsync(CancellationToken.None));
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(2, queue.RecoveryCount);
        Assert.Equal(2, queue.SnapshotCount);
        Assert.Equal(3, runner.CallCount);
        Assert.Equal(
            [new MediaTemporaryCandidate(queue.Candidate.JobId, queue.Candidate.Attempt),
             new MediaTemporaryCandidate(queue.Candidate.JobId, queue.Candidate.Attempt)],
            store.Deleted);
    }

    private sealed class MutableClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class RecordingRunner : IMediaJobRunner
    {
        public int CallCount { get; private set; }

        public Task<bool> RunNextAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(false);
        }
    }

    private sealed class RecordingQueue : IMediaJobQueue
    {
        public MediaTemporaryCandidate Candidate { get; } = new(Guid.NewGuid(), 2);
        public int RecoveryCount { get; private set; }
        public int SnapshotCount { get; private set; }

        public Task<IReadOnlyList<MediaTemporaryCandidate>> FindStaleTemporaryCandidatesAsync(
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaTemporaryCandidate>>([Candidate]);

        public Task<int> RecoverStaleAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
        {
            RecoveryCount++;
            return Task.FromResult(1);
        }

        public Task<MediaQueueSnapshot> GetOperationalSnapshotAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            SnapshotCount++;
            return Task.FromResult(new MediaQueueSnapshot(2, 0, 10));
        }

        public Task<MediaJob?> TryAcquireNextAsync(Guid workerToken, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> TryRecordHeartbeatAsync(Guid jobId, Guid workerToken, DateTimeOffset now, int? progressPercent,
            long? processedDurationMs, long? totalDurationMs, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> TryCompleteAsync(Guid jobId, Guid workerToken, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> TryFailAsync(Guid jobId, Guid workerToken, string errorCode, bool retryable,
            DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> TryRetryFailedAsync(Guid failedJobId, Guid newJobId, Guid requestedByUserId,
            DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int?> GetQueuePositionAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingStore : IDerivativeStore
    {
        public List<MediaTemporaryCandidate> Deleted { get; } = [];

        public Task DeleteTemporaryAsync(Guid jobId, int attempt, CancellationToken cancellationToken)
        {
            Deleted.Add(new MediaTemporaryCandidate(jobId, attempt));
            return Task.CompletedTask;
        }

        public Task<DerivativeTemporaryFile> WriteTemporaryAsync(Guid jobId, int attempt, Stream source,
            long expectedSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PublishedDerivative> PublishAsync(DerivativeTemporaryFile temporary, Guid ownerUserId,
            Guid sourceFileId, long sourceVersion, int profileVersion, DerivativeType derivativeType,
            string extension, long verifiedSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
