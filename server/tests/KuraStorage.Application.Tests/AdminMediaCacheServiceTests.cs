using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Media;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class AdminMediaCacheServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsBoundedCacheAndOperationalAggregates()
    {
        var repository = new FakeRepository
        {
            Snapshot = new MediaCacheSnapshot(10, 20, 30, 40, 2, 1, 3, 4, 1),
        };
        var service = new AdminMediaCacheService(
            repository,
            new MediaCleanupOptions
            {
                CacheHighWatermarkBytes = 100,
                CacheLowWatermarkBytes = 60,
            },
            new FixedClock());

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(100, result.CacheBytes);
        Assert.Equal(10, result.ImageLowBytes);
        Assert.Equal(20, result.ImageMediumBytes);
        Assert.Equal(30, result.VideoLowBytes);
        Assert.Equal(40, result.VideoMediumBytes);
        Assert.Equal(100, result.HighWatermarkBytes);
        Assert.Equal(60, result.LowWatermarkBytes);
        Assert.Equal(2, result.QueuedJobCount);
        Assert.Equal(1, result.RunningJobCount);
        Assert.Equal(3, result.FailedJobCount);
        Assert.Equal(4, result.PendingRunCount);
        Assert.Equal(1, result.RunningRunCount);
        Assert.Null(result.LastCleanupRun);
    }

    [Fact]
    public async Task RequestManualAsync_ValidatesAndHashesTheKeyBeforePersistence()
    {
        var repository = new FakeRepository();
        var service = new AdminMediaCacheService(repository, new MediaCleanupOptions(), new FixedClock());
        var adminId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var invalid = await service.RequestManualAsync(adminId, "not-a-uuid", CancellationToken.None);
        var accepted = await service.RequestManualAsync(adminId, key.ToString("B"), CancellationToken.None);

        Assert.Equal(MediaCleanupRequestFailure.Validation, invalid.Failure);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(MediaCleanupRunStatus.Pending.ToString().ToUpperInvariant(), accepted.Run?.Status);
        Assert.Equal(adminId, repository.RequestingAdminUserId);
        Assert.NotNull(repository.IdempotencyKeyHash);
        Assert.NotNull(repository.RequestFingerprintHash);
        Assert.Equal(64, repository.IdempotencyKeyHash.Length);
        Assert.Equal(64, repository.RequestFingerprintHash.Length);
        Assert.DoesNotContain(key.ToString("D"), repository.IdempotencyKeyHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestManualAsync_MapsFingerprintConflictWithoutReturningTheRun()
    {
        var repository = new FakeRepository { Conflict = true };
        var service = new AdminMediaCacheService(repository, new MediaCleanupOptions(), new FixedClock());

        var result = await service.RequestManualAsync(Guid.NewGuid(), Guid.NewGuid().ToString("D"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Run);
        Assert.Equal(MediaCleanupRequestFailure.IdempotencyConflict, result.Failure);
    }

    [Theory]
    [InlineData(MediaCleanupFailureCode.StorageUnavailable, "STORAGE_UNAVAILABLE")]
    [InlineData(MediaCleanupFailureCode.PartialDeleteFailure, "PARTIAL_DELETE_FAILURE")]
    [InlineData(MediaCleanupFailureCode.CleanupFailed, "CLEANUP_FAILED")]
    public void RunSummary_MapsEverySafeFailureCodeAndResultField(
        MediaCleanupFailureCode failureCode,
        string expectedCode)
    {
        var token = Guid.NewGuid();
        var run = MediaCleanupRun.CreateScheduled(Guid.NewGuid(), FixedClock.Now);
        run.Claim(token, FixedClock.Now.AddSeconds(1), FixedClock.Now.AddMinutes(1));
        run.Fail(token, FixedClock.Now.AddSeconds(2), failureCode);

        var summary = MediaCleanupRunSummary.From(run);

        Assert.Equal(run.Id, summary.Id);
        Assert.Equal("SCHEDULED", summary.Trigger);
        Assert.Equal("FAILED", summary.Status);
        Assert.Equal(FixedClock.Now, summary.RequestedAt);
        Assert.Equal(FixedClock.Now.AddSeconds(1), summary.StartedAt);
        Assert.Equal(FixedClock.Now.AddSeconds(2), summary.CompletedAt);
        Assert.Equal(0, summary.ExaminedCount);
        Assert.Equal(0, summary.DeletedCount);
        Assert.Equal(0, summary.ReleasedBytes);
        Assert.Equal(1, summary.FailureCount);
        Assert.Null(summary.RemainingCacheBytes);
        Assert.Equal(expectedCode, summary.FailureCode);
    }

    private sealed class FakeRepository : IMediaCleanupRepository
    {
        public MediaCacheSnapshot Snapshot { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

        public bool Conflict { get; init; }

        public Guid RequestingAdminUserId { get; private set; }

        public string IdempotencyKeyHash { get; private set; } = string.Empty;

        public string RequestFingerprintHash { get; private set; } = string.Empty;

        public Task<MediaCacheSnapshot> GetCacheSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<MediaCleanupRun?> FindLatestRunAsync(CancellationToken cancellationToken) =>
            Task.FromResult<MediaCleanupRun?>(null);

        public Task<MediaCleanupRequestPersistenceResult> CreateOrGetManualRunAsync(
            Guid requestingAdminUserId,
            string idempotencyKeyHash,
            string requestFingerprintHash,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken)
        {
            RequestingAdminUserId = requestingAdminUserId;
            IdempotencyKeyHash = idempotencyKeyHash;
            RequestFingerprintHash = requestFingerprintHash;
            var run = MediaCleanupRun.CreateManual(
                Guid.NewGuid(), requestingAdminUserId, idempotencyKeyHash, requestFingerprintHash, requestedAt);
            return Task.FromResult(new MediaCleanupRequestPersistenceResult(run, Conflict));
        }

        public Task<IAsyncDisposable?> TryAcquireCleanupLockAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimDeletingAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimLruAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long> GetReadyCacheSizeAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CompleteDeleteAsync(Guid derivativeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreReadyAsync(Guid derivativeId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> DeleteTerminalJobsAsync(DateTimeOffset completedBefore, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock : ISystemClock
    {
        public static DateTimeOffset Now { get; } = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Now;
    }
}
