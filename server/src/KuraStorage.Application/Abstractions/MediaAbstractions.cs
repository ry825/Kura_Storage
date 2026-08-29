using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Abstractions;

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

    Task<int?> GetQueuePositionAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IDerivativeStore
{
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
}

public sealed record DerivativeTemporaryFile(RelativeStoragePath Path, long Size, Guid JobId, int Attempt);

public sealed record PublishedDerivative(RelativeStoragePath Path, long Size);
