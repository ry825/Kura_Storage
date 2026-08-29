using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using System.Diagnostics.Metrics;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class MediaJobRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunNext_WhenQueueIsEmpty_ReturnsFalse()
    {
        var fixture = new RunnerFixture { HasJob = false };

        Assert.False(await fixture.Runner.RunNextAsync(CancellationToken.None));
        Assert.Null(fixture.Failure);
    }

    [Fact]
    public async Task RunNext_WhenGenerationLeaseCannotBeAcquired_FailsJobPermanently()
    {
        var fixture = new RunnerFixture { HasContext = false };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));
        Assert.Equal((MediaErrorCodes.SourceNotActive, false), fixture.Failure);
    }

    [Theory]
    [InlineData(FailurePoint.Generation, MediaErrorCodes.ToolUnavailable, true, 0)]
    [InlineData(FailurePoint.SourceRead, FileErrorCodes.StorageUnavailable, true, 0)]
    [InlineData(FailurePoint.Unexpected, MediaErrorCodes.GenerationFailed, false, 0)]
    [InlineData(FailurePoint.Completion, MediaErrorCodes.CompletionUnknown, true, 0)]
    public async Task RunNext_WhenProcessingFails_ReleasesLeaseAndClassifiesFailure(
        FailurePoint failurePoint,
        string expectedCode,
        bool expectedRetryable,
        int expectedDeletes)
    {
        var fixture = new RunnerFixture { InjectedFailure = failurePoint };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.Equal((expectedCode, expectedRetryable), fixture.Failure);
        Assert.Equal(1, fixture.ReleaseCount);
        Assert.Equal(expectedDeletes, fixture.DeleteCount);
    }

    [Fact]
    public async Task RunNext_WhenCompletionLosesCurrentSource_DeletesPublishedFileAndFailsClosed()
    {
        var fixture = new RunnerFixture { CompleteResult = false };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.Equal((MediaErrorCodes.SourceNotActive, false), fixture.Failure);
        Assert.Equal(1, fixture.ReleaseCount);
        Assert.Equal(1, fixture.DeleteCount);
    }

    [Fact]
    public async Task RunNext_WhenValidatedPublishedFileExists_CompletesWithoutRegeneration()
    {
        var fixture = new RunnerFixture { ExistingPublished = true };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.Equal(0, fixture.GenerationCount);
        Assert.Null(fixture.Failure);
        Assert.Equal(0, fixture.DeleteCount);
    }

    [Fact]
    public async Task RunNext_EmitsLowCardinalityOperationalMetrics()
    {
        var measurements = new List<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "KuraStorage.Media")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.ToArray())));
        listener.Start();

        Assert.True(await new RunnerFixture().Runner.RunNextAsync(CancellationToken.None));

        Assert.Contains(measurements, value => value.Name == "kurastorage.media.job.results" &&
            value.Tags.Any(tag => tag.Key == "result" && Equals(tag.Value, "succeeded")));
        Assert.Contains(measurements, value => value.Name == "kurastorage.media.generation.duration");
        Assert.Contains(measurements, value => value.Name == "kurastorage.media.output.bytes");
        Assert.All(measurements.SelectMany(value => value.Tags), tag =>
            Assert.Contains(tag.Key, new[] { "result", "reason", "variant" }));
    }

    [Fact]
    public async Task RunNext_WhenCancelled_RequeuesAndPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new RunnerFixture
        {
            InjectedFailure = FailurePoint.Cancellation,
            Cancellation = cancellation,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Runner.RunNextAsync(cancellation.Token));

        Assert.Equal(("MEDIA_WORKER_STOPPED", true), fixture.Failure);
        Assert.Equal(1, fixture.ReleaseCount);
    }

    [Fact]
    public async Task RunNext_WhileGenerating_HeartbeatsUntilOwnershipIsRejected()
    {
        var fixture = new RunnerFixture
        {
            GenerationDelay = TimeSpan.FromMilliseconds(1200),
            HeartbeatResult = false,
        };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.True(fixture.HeartbeatCount >= 1);
        Assert.Null(fixture.Failure);
    }

    [Fact]
    public async Task RunNext_RecordsProgressAndStopsWhenProgressLeaseOwnershipIsLost()
    {
        var fixture = new RunnerFixture { EmitProgress = true, HeartbeatResult = false };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.Equal(new MediaGenerationProgress(50, 5000, 10000), fixture.LastProgress);
        Assert.Null(fixture.Failure);
        Assert.Equal(0, fixture.DeleteCount);
    }

    [Fact]
    public async Task RunNext_CoalescesProgressUpdatesWithinFiveSeconds()
    {
        var fixture = new RunnerFixture { ProgressUpdates = 2 };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.Equal(1, fixture.ProgressPulseCount);
        Assert.Equal(new MediaGenerationProgress(50, 5000, 10000), fixture.LastProgress);
    }

    [Fact]
    public async Task RunNext_WhenPeriodicHeartbeatFaults_RequeuesWithoutStoppingWorkerLoop()
    {
        var fixture = new RunnerFixture
        {
            GenerationDelay = TimeSpan.FromMilliseconds(1200),
            HeartbeatThrows = true,
        };

        Assert.True(await fixture.Runner.RunNextAsync(CancellationToken.None));

        Assert.True(fixture.HeartbeatCount >= 1);
        Assert.Equal((MediaErrorCodes.WorkerUnavailable, true), fixture.Failure);
        Assert.Equal(1, fixture.ReleaseCount);
    }

    public enum FailurePoint
    {
        None,
        SourceRead,
        Generation,
        Unexpected,
        Completion,
        Cancellation,
    }

    private sealed class RunnerFixture :
        IMediaJobQueue,
        IMediaRepository,
        IMediaGenerator,
        IMediaHeartbeat,
        IFileStore,
        IDerivativeStore,
        ISystemClock
    {
        private readonly Guid derivativeId = Guid.NewGuid();
        private readonly Guid jobId = Guid.NewGuid();
        private readonly Guid ownerId = Guid.NewGuid();
        private readonly Guid sourceId = Guid.NewGuid();

        public RunnerFixture()
        {
            Runner = new MediaJobRunner(
                this, this, this, this, this, this, this,
                new MediaRuntimeOptions { JobHeartbeatSeconds = 1 });
        }

        public MediaJobRunner Runner { get; }

        public bool HasJob { get; init; } = true;

        public bool HasContext { get; init; } = true;

        public bool CompleteResult { get; init; } = true;

        public FailurePoint InjectedFailure { get; init; }

        public CancellationTokenSource? Cancellation { get; init; }

        public TimeSpan GenerationDelay { get; init; }

        public bool HeartbeatResult { get; init; } = true;

        public bool HeartbeatThrows { get; init; }

        public bool ExistingPublished { get; init; }

        public bool EmitProgress { get; init; }

        public int ProgressUpdates { get; init; }

        public MediaGenerationProgress? LastProgress { get; private set; }

        public int GenerationCount { get; private set; }

        public int HeartbeatCount { get; private set; }

        public int ProgressPulseCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int DeleteCount { get; private set; }

        public (string Code, bool Retryable)? Failure { get; private set; }

        public DateTimeOffset UtcNow => Now;

        public Task<MediaJob?> TryAcquireNextAsync(
            Guid workerToken,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<MediaJob?>(HasJob
                ? new MediaJob(jobId, derivativeId, DerivativeType.ImageLow, ownerId, now)
                : null);

        public Task<MediaGenerationContext?> TryAcquireGenerationAsync(
            Guid requestedJobId,
            Guid workerToken,
            Guid leaseOwnerToken,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<MediaGenerationContext?>(HasContext
                ? new MediaGenerationContext(
                    requestedJobId,
                    derivativeId,
                    ownerId,
                    sourceId,
                    1,
                    RelativeStoragePath.Create("users/source.jpg"),
                    3,
                    "image/jpeg",
                    DerivativeType.ImageLow,
                    1,
                    1,
                    leaseOwnerToken)
                : null);

        public async Task<GeneratedMedia> GenerateAsync(
            MediaGenerationContext context,
            Stream source,
            CancellationToken cancellationToken,
            Func<MediaGenerationProgress, CancellationToken, ValueTask>? progress = null)
        {
            GenerationCount++;
            if (GenerationDelay > TimeSpan.Zero)
            {
                await Task.Delay(GenerationDelay, cancellationToken);
            }

            switch (InjectedFailure)
            {
                case FailurePoint.Generation:
                    throw new MediaGenerationException(MediaErrorCodes.ToolUnavailable, retryable: true);
                case FailurePoint.Unexpected:
                    throw new InvalidOperationException("Injected unexpected failure.");
                case FailurePoint.Cancellation:
                    Cancellation!.Cancel();
                    throw new OperationCanceledException(Cancellation.Token);
            }

            var progressUpdates = EmitProgress ? 1 : ProgressUpdates;
            for (var index = 0; index < progressUpdates; index++)
            {
                await progress!(new MediaGenerationProgress(50, 5000, 10000), cancellationToken);
            }

            return new GeneratedMedia(new MemoryStream([1, 2, 3], writable: false), 3, "webp");
        }

        public Task<PublishedDerivative?> FindPublishedAsync(
            MediaGenerationContext context,
            string extension,
            CancellationToken cancellationToken) =>
            Task.FromResult<PublishedDerivative?>(ExistingPublished
                ? new PublishedDerivative(RelativeStoragePath.Create("derivatives/output.webp"), 3)
                : null);

        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            InjectedFailure == FailurePoint.SourceRead
                ? Task.FromException<Stream>(new IOException("Injected source read failure."))
                : Task.FromResult<Stream>(new MemoryStream([1, 2, 3], writable: false));

        public Task<DerivativeTemporaryFile> WriteTemporaryAsync(
            Guid requestedJobId,
            int attempt,
            Stream source,
            long expectedSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DerivativeTemporaryFile(
                RelativeStoragePath.Create("derivative-temp/output.webp"), expectedSize, requestedJobId, attempt));

        public Task<PublishedDerivative> PublishAsync(
            DerivativeTemporaryFile temporary,
            Guid ownerUserId,
            Guid sourceFileId,
            long sourceVersion,
            int profileVersion,
            DerivativeType derivativeType,
            string extension,
            long verifiedSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PublishedDerivative(RelativeStoragePath.Create("derivatives/output.webp"), verifiedSize));

        public Task<bool> CompleteGenerationAsync(
            Guid requestedJobId,
            Guid workerToken,
            Guid leaseOwnerToken,
            PublishedDerivative published,
            DateTimeOffset now,
            DateTimeOffset? expiresAt,
            CancellationToken cancellationToken) =>
            InjectedFailure == FailurePoint.Completion
                ? Task.FromException<bool>(new InvalidOperationException("Injected completion failure."))
                : Task.FromResult(CompleteResult);

        public Task<bool> PulseAsync(
            Guid requestedJobId,
            Guid workerToken,
            Guid requestedDerivativeId,
            Guid leaseOwnerToken,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            HeartbeatCount++;
            if (HeartbeatThrows)
            {
                return Task.FromException<bool>(new IOException("Injected heartbeat failure."));
            }

            return Task.FromResult(HeartbeatResult);
        }

        public Task<bool> PulseProgressAsync(
            Guid requestedJobId,
            Guid workerToken,
            Guid requestedDerivativeId,
            Guid leaseOwnerToken,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            MediaGenerationProgress progress,
            CancellationToken cancellationToken)
        {
            LastProgress = progress;
            ProgressPulseCount++;
            return Task.FromResult(HeartbeatResult);
        }

        public Task<bool> ReleaseLeaseAsync(
            Guid requestedDerivativeId,
            DerivativeLeaseType leaseType,
            Guid ownerToken,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(
            Guid requestedJobId,
            Guid workerToken,
            string errorCode,
            bool retryable,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Failure = (errorCode, retryable);
            return Task.FromResult(true);
        }

        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }

        public Task<MediaRequestSnapshot> GetOrCreateRequestAsync(
            FileEntry source,
            DerivativeType derivativeType,
            int profileVersion,
            Guid requestedByUserId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MediaRequestSnapshot?> FindByJobAsync(Guid requestedJobId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DerivativeLeaseHandle?> TryAcquireDeliveryAsync(
            Guid requestedDerivativeId,
            Guid ownerToken,
            DateTimeOffset now,
            TimeSpan duration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RenewLeaseAsync(
            Guid requestedDerivativeId,
            DerivativeLeaseType leaseType,
            Guid ownerToken,
            DateTimeOffset now,
            TimeSpan duration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RecordDeliveryAccessAsync(
            Guid requestedDerivativeId,
            DateTimeOffset now,
            TimeSpan cacheTtl,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryRecordHeartbeatAsync(
            Guid requestedJobId,
            Guid workerToken,
            DateTimeOffset now,
            int? progressPercent,
            long? processedDurationMs,
            long? totalDurationMs,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryCompleteAsync(
            Guid requestedJobId,
            Guid workerToken,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> TryRetryFailedAsync(
            Guid failedJobId,
            Guid newJobId,
            Guid requestedByUserId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RecoverStaleAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int?> GetQueuePositionAsync(
            Guid requestedJobId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredUpload> WriteUploadTempAsync(
            Guid ownerUserId,
            Guid operationId,
            Stream source,
            long expectedSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveAsync(
            RelativeStoragePath source,
            RelativeStoragePath target,
            bool sourceIsDirectory,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            RelativeStoragePath path,
            bool directory,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
