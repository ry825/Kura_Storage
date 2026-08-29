using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Abstractions;

public interface IMediaJobRunner
{
    Task<bool> RunNextAsync(CancellationToken cancellationToken);
}

public interface IMediaCleanupRepository
{
    Task<IAsyncDisposable?> TryAcquireCleanupLockAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaCleanupCandidate>> ClaimExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaCleanupCandidate>> ClaimDeletingAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaCleanupCandidate>> ClaimLruAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);

    Task<long> GetReadyCacheSizeAsync(CancellationToken cancellationToken);

    Task CompleteDeleteAsync(Guid derivativeId, CancellationToken cancellationToken);

    Task RestoreReadyAsync(Guid derivativeId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> DeleteTerminalJobsAsync(
        DateTimeOffset completedBefore,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record MediaCleanupCandidate(
    Guid DerivativeId,
    RelativeStoragePath Path,
    long Size,
    bool RestoreReadyOnFailure = true);

public interface IMediaCleanupService
{
    Task<Media.MediaCleanupResult> RunAsync(
        bool includeTerminalJobCleanup,
        CancellationToken cancellationToken);
}

public interface IMediaJobQueue
{
    Task<MediaJob?> TryAcquireNextAsync(Guid workerToken, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryRecordHeartbeatAsync(
        Guid jobId,
        Guid workerToken,
        DateTimeOffset now,
        int? progressPercent,
        long? processedDurationMs,
        long? totalDurationMs,
        CancellationToken cancellationToken);

    Task<bool> TryCompleteAsync(Guid jobId, Guid workerToken, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryFailAsync(
        Guid jobId,
        Guid workerToken,
        string errorCode,
        bool retryable,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<Guid?> TryRetryFailedAsync(
        Guid failedJobId,
        Guid newJobId,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> RecoverStaleAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaTemporaryCandidate>> FindStaleTemporaryCandidatesAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MediaTemporaryCandidate>>([]);

    Task<MediaQueueSnapshot> GetOperationalSnapshotAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MediaQueueSnapshot(0, 0, 0));

    Task<int?> GetQueuePositionAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IDerivativeStore
{
    Task<PublishedDerivative?> FindPublishedAsync(
        MediaGenerationContext context,
        string extension,
        CancellationToken cancellationToken) => Task.FromResult<PublishedDerivative?>(null);

    Task<DerivativeTemporaryFile> WriteTemporaryAsync(
        Guid jobId,
        int attempt,
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken);

    Task<PublishedDerivative> PublishAsync(
        DerivativeTemporaryFile temporary,
        Guid ownerUserId,
        Guid sourceFileId,
        long sourceVersion,
        int profileVersion,
        DerivativeType derivativeType,
        string extension,
        long verifiedSize,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken);

    Task DeleteTemporaryAsync(
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record DerivativeTemporaryFile(RelativeStoragePath Path, long Size, Guid JobId, int Attempt);

public sealed record MediaQueueSnapshot(int QueuedCount, int RunningCount, long OldestWaitSeconds);

public sealed record MediaTemporaryCandidate(Guid JobId, int Attempt);

public sealed record PublishedDerivative(RelativeStoragePath Path, long Size);

public interface IMediaProcessRunner
{
    Task<MediaProcessResult> RunAsync(MediaProcessRequest request, CancellationToken cancellationToken);
}

public sealed record MediaProcessRequest(
    string BinaryPath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string>? Environment = null,
    Func<string, CancellationToken, ValueTask>? StandardOutputLineHandler = null);

public sealed record MediaProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class MediaProcessTimeoutException() : TimeoutException("The media process exceeded its time limit.");

public sealed class MediaProcessOutputLimitException()
    : IOException("The media process output exceeded the diagnostic limit.");

public interface IMediaRepository
{
    Task<MediaRequestSnapshot> GetOrCreateRequestAsync(
        FileEntry source,
        DerivativeType derivativeType,
        int profileVersion,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<MediaRequestSnapshot?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<MediaGenerationContext?> TryAcquireGenerationAsync(
        Guid jobId,
        Guid workerToken,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteGenerationAsync(
        Guid jobId,
        Guid workerToken,
        Guid leaseOwnerToken,
        PublishedDerivative published,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);

    Task<DerivativeLeaseHandle?> TryAcquireDeliveryAsync(
        Guid derivativeId,
        Guid ownerToken,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid derivativeId,
        DerivativeLeaseType leaseType,
        Guid ownerToken,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task<bool> ReleaseLeaseAsync(
        Guid derivativeId,
        DerivativeLeaseType leaseType,
        Guid ownerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> RecordDeliveryAccessAsync(
        Guid derivativeId,
        DateTimeOffset now,
        TimeSpan cacheTtl,
        CancellationToken cancellationToken);
}

public sealed record MediaRequestSnapshot(FileEntry Source, FileDerivative Derivative, MediaJob? Job);

public sealed record MediaGenerationContext(
    Guid JobId,
    Guid DerivativeId,
    Guid OwnerUserId,
    Guid SourceFileId,
    long SourceVersion,
    RelativeStoragePath SourcePath,
    long SourceSize,
    string? SourceMimeType,
    DerivativeType DerivativeType,
    int ProfileVersion,
    int Attempt,
    Guid LeaseOwnerToken);

public sealed record DerivativeLeaseHandle(Guid DerivativeId, Guid OwnerToken, DateTimeOffset ExpiresAt);

public interface IMediaGenerator
{
    Task<GeneratedMedia> GenerateAsync(
        MediaGenerationContext context,
        Stream source,
        CancellationToken cancellationToken,
        Func<MediaGenerationProgress, CancellationToken, ValueTask>? progress = null);
}

public sealed record MediaGenerationProgress(
    int? Percent,
    long? ProcessedDurationMs,
    long? TotalDurationMs);

public sealed class GeneratedMedia(
    Stream content,
    long size,
    string extension,
    Func<ValueTask>? cleanup = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public long Size { get; } = size;

    public string Extension { get; } = extension;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Content.DisposeAsync();
        }
        finally
        {
            if (cleanup is not null)
            {
                await cleanup();
            }
        }
    }
}

public interface IMediaWaiter
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IMediaHeartbeat
{
    Task<bool> PulseAsync(
        Guid jobId,
        Guid workerToken,
        Guid derivativeId,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> PulseProgressAsync(
        Guid jobId,
        Guid workerToken,
        Guid derivativeId,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        MediaGenerationProgress progress,
        CancellationToken cancellationToken) =>
        PulseAsync(
            jobId,
            workerToken,
            derivativeId,
            leaseOwnerToken,
            now,
            leaseDuration,
            cancellationToken);
}

public sealed class MediaGenerationException(string errorCode, bool retryable, Exception? innerException = null)
    : Exception("Media generation failed.", innerException)
{
    public string ErrorCode { get; } = errorCode;

    public bool Retryable { get; } = retryable;
}

public sealed class MediaGenerationOwnershipLostException()
    : Exception("Media generation ownership was lost.");
