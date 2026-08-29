using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class MediaCleanupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_DeletesExpiredThenLruUntilLowWatermarkAndPrunesTerminalJobs()
    {
        var expired = Candidate(1, 40);
        var oldest = Candidate(2, 75);
        var repository = new FakeRepository
        {
            Expired = [[expired], []],
            Lru = [[oldest], []],
            CacheSizes = [120, 45],
            TerminalDeleteCounts = [2, 0],
        };
        var store = new FakeStore();
        var service = Create(repository, store);

        var result = await service.RunAsync(includeTerminalJobCleanup: true, CancellationToken.None);

        Assert.True(result.AcquiredLock);
        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(115, result.DeletedBytes);
        Assert.Equal(45, result.RemainingCacheBytes);
        Assert.Equal(2, result.DeletedTerminalJobCount);
        Assert.Equal([expired.DerivativeId, oldest.DerivativeId], repository.Completed);
        Assert.Empty(repository.Restored);
        Assert.Equal([expired.Path, oldest.Path], store.Deleted);
        Assert.Equal([1], repository.LruBatchSizes);
    }

    [Fact]
    public async Task RunAsync_RestoresReadyStateAfterPhysicalDeleteFailureAndStopsReclaimingBatch()
    {
        var failed = Candidate(3, 40);
        var repository = new FakeRepository
        {
            Expired = [[failed]],
            CacheSizes = [40],
        };
        var store = new FakeStore { Failure = new IOException("injected") };

        var result = await Create(repository, store).RunAsync(false, CancellationToken.None);

        Assert.Equal(1, result.FailureCount);
        Assert.Equal([failed.DerivativeId], repository.Restored);
        Assert.Empty(repository.Completed);
        Assert.Equal(1, repository.ExpiredCalls);
    }

    [Fact]
    public async Task RunAsync_ReturnsWithoutStorageWorkWhenAnotherCleanupOwnsGlobalLock()
    {
        var repository = new FakeRepository { LockAvailable = false };
        var store = new FakeStore();

        var result = await Create(repository, store).RunAsync(true, CancellationToken.None);

        Assert.False(result.AcquiredLock);
        Assert.Equal(0, repository.ExpiredCalls);
        Assert.Empty(store.Deleted);
    }

    private static MediaCleanupService Create(FakeRepository repository, FakeStore store) =>
        new(
            repository,
            store,
            new AvailableStorageGuard(),
            new FixedClock(),
            new MediaCleanupOptions
            {
                BatchSize = 2,
                CacheHighWatermarkBytes = 100,
                CacheLowWatermarkBytes = 50,
                TerminalJobRetentionDays = 7,
            });

    private static MediaCleanupCandidate Candidate(int suffix, long size) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"), RelativeStoragePath.Create($"derivatives/cache-{suffix}.webp"), size);

    private sealed class FakeRepository : IMediaCleanupRepository
    {
        private int sizeIndex;
        private int terminalIndex;
        private int lruCalls;

        public bool LockAvailable { get; init; } = true;
        public IReadOnlyList<IReadOnlyList<MediaCleanupCandidate>> Expired { get; init; } = [[]];
        public IReadOnlyList<IReadOnlyList<MediaCleanupCandidate>> Lru { get; init; } = [[]];
        public IReadOnlyList<long> CacheSizes { get; init; } = [0];
        public IReadOnlyList<int> TerminalDeleteCounts { get; init; } = [0];
        public int ExpiredCalls { get; private set; }
        public List<Guid> Completed { get; } = [];
        public List<Guid> Restored { get; } = [];
        public List<int> LruBatchSizes { get; } = [];

        public Task<IAsyncDisposable?> TryAcquireCleanupLockAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(LockAvailable ? new AsyncHandle() : null);

        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
        {
            var index = ExpiredCalls++;
            return Task.FromResult(index < Expired.Count ? Expired[index] : (IReadOnlyList<MediaCleanupCandidate>)[]);
        }

        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimDeletingAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaCleanupCandidate>>([]);

        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimLruAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
        {
            LruBatchSizes.Add(batchSize);
            var index = lruCalls++;
            return Task.FromResult(index < Lru.Count ? Lru[index] : (IReadOnlyList<MediaCleanupCandidate>)[]);
        }

        public Task<long> GetReadyCacheSizeAsync(CancellationToken cancellationToken)
        {
            var index = Math.Min(sizeIndex++, CacheSizes.Count - 1);
            return Task.FromResult(CacheSizes[index]);
        }

        public Task CompleteDeleteAsync(Guid derivativeId, CancellationToken cancellationToken)
        {
            Completed.Add(derivativeId);
            return Task.CompletedTask;
        }

        public Task RestoreReadyAsync(Guid derivativeId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Restored.Add(derivativeId);
            return Task.CompletedTask;
        }

        public Task<int> DeleteTerminalJobsAsync(DateTimeOffset completedBefore, int batchSize, CancellationToken cancellationToken)
        {
            var index = Math.Min(terminalIndex++, TerminalDeleteCounts.Count - 1);
            return Task.FromResult(TerminalDeleteCounts[index]);
        }
    }

    private sealed class FakeStore : IDerivativeStore
    {
        public Exception? Failure { get; init; }
        public List<RelativeStoragePath> Deleted { get; } = [];

        public Task<DerivativeTemporaryFile> WriteTemporaryAsync(Guid jobId, int attempt, Stream source, long expectedSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PublishedDerivative> PublishAsync(DerivativeTemporaryFile temporary, Guid ownerUserId, Guid sourceFileId, long sourceVersion, int profileVersion, DerivativeType derivativeType, string extension, long verifiedSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            Deleted.Add(path);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class AsyncHandle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AvailableStorageGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
