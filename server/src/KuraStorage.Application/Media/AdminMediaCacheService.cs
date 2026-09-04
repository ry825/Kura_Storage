using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Media;

public sealed record MediaCacheSnapshot(
    long ImageLowBytes,
    long ImageMediumBytes,
    long VideoLowBytes,
    long VideoMediumBytes,
    int QueuedJobCount,
    int RunningJobCount,
    int FailedJobCount,
    int PendingRunCount,
    int RunningRunCount);

public sealed record MediaCleanupRunSummary(
    Guid Id,
    string Trigger,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ExaminedCount,
    int DeletedCount,
    long ReleasedBytes,
    int FailureCount,
    long? RemainingCacheBytes,
    string? FailureCode)
{
    public static MediaCleanupRunSummary From(MediaCleanupRun run) =>
        new(
            run.Id,
            run.Trigger.ToString().ToUpperInvariant(),
            run.Status.ToString().ToUpperInvariant(),
            run.RequestedAt,
            run.StartedAt,
            run.CompletedAt,
            run.ExaminedCount,
            run.DeletedCount,
            run.ReleasedBytes,
            run.FailureCount,
            run.RemainingCacheBytes,
            run.FailureCode is null ? null : ToUpperSnakeCase(run.FailureCode.Value));

    private static string ToUpperSnakeCase(MediaCleanupFailureCode code) => code switch
    {
        MediaCleanupFailureCode.StorageUnavailable => "STORAGE_UNAVAILABLE",
        MediaCleanupFailureCode.PartialDeleteFailure => "PARTIAL_DELETE_FAILURE",
        MediaCleanupFailureCode.CleanupFailed => "CLEANUP_FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}

public sealed record AdminMediaCacheStatus(
    long CacheBytes,
    long ImageLowBytes,
    long ImageMediumBytes,
    long VideoLowBytes,
    long VideoMediumBytes,
    long HighWatermarkBytes,
    long LowWatermarkBytes,
    int QueuedJobCount,
    int RunningJobCount,
    int FailedJobCount,
    int PendingRunCount,
    int RunningRunCount,
    MediaCleanupRunSummary? LastCleanupRun);

public enum MediaCleanupRequestFailure
{
    Validation,
    IdempotencyConflict,
}

public sealed record MediaCleanupRequestResult(
    MediaCleanupRunSummary? Run,
    MediaCleanupRequestFailure? Failure)
{
    public bool IsSuccess => Run is not null && Failure is null;
}

public sealed class AdminMediaCacheService(
    IMediaCleanupRepository repository,
    MediaCleanupOptions options,
    ISystemClock clock)
{
    private const string ManualRequestFingerprint = "media-cache-cleanup:manual:v1";

    public async Task<AdminMediaCacheStatus> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await repository.GetCacheSnapshotAsync(cancellationToken);
        var latest = await repository.FindLatestRunAsync(cancellationToken);
        return new AdminMediaCacheStatus(
            checked(snapshot.ImageLowBytes + snapshot.ImageMediumBytes + snapshot.VideoLowBytes + snapshot.VideoMediumBytes),
            snapshot.ImageLowBytes,
            snapshot.ImageMediumBytes,
            snapshot.VideoLowBytes,
            snapshot.VideoMediumBytes,
            options.CacheHighWatermarkBytes,
            options.CacheLowWatermarkBytes,
            snapshot.QueuedJobCount,
            snapshot.RunningJobCount,
            snapshot.FailedJobCount,
            snapshot.PendingRunCount,
            snapshot.RunningRunCount,
            latest is null ? null : MediaCleanupRunSummary.From(latest));
    }

    public async Task<MediaCleanupRequestResult> RequestManualAsync(
        Guid requestingAdminUserId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (requestingAdminUserId == Guid.Empty || !Guid.TryParse(idempotencyKey, out var parsedKey))
        {
            return new MediaCleanupRequestResult(null, MediaCleanupRequestFailure.Validation);
        }

        var keyHash = Hash(parsedKey.ToString("D"));
        var fingerprintHash = Hash(ManualRequestFingerprint);
        var persisted = await repository.CreateOrGetManualRunAsync(
            requestingAdminUserId,
            keyHash,
            fingerprintHash,
            clock.UtcNow,
            cancellationToken);
        return persisted.Conflict
            ? new MediaCleanupRequestResult(null, MediaCleanupRequestFailure.IdempotencyConflict)
            : new MediaCleanupRequestResult(MediaCleanupRunSummary.From(persisted.Run), null);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record MediaCleanupRequestPersistenceResult(MediaCleanupRun Run, bool Conflict);
