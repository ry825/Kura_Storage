using KuraStorage.Domain.Maintenance;

namespace KuraStorage.Application.Maintenance;

public sealed record TrashPurgeCandidate(
    Guid RootId,
    Guid OwnerUserId,
    DateTimeOffset TrashedAt,
    long EstimatedBytes);

public sealed record TrashPurgeRunSummary(
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int ExaminedRootCount,
    int DeletedRootCount,
    long ReleasedBytes,
    int ErrorCount)
{
    public static TrashPurgeRunSummary From(TrashPurgeRun run) =>
        new(
            run.StartedAt,
            run.CompletedAt,
            run.Status == TrashPurgeRunStatus.CompletedWithErrors
                ? "COMPLETED_WITH_ERRORS"
                : run.Status.ToString().ToUpperInvariant(),
            run.ExaminedRootCount,
            run.DeletedRootCount,
            run.ReleasedBytes,
            run.ErrorCount);
}

public sealed record StorageCapacity(long TotalBytes, long AvailableBytes);

public sealed record AdminStorageStatus(
    string Storage,
    long? TotalBytes,
    long? AvailableBytes,
    long CapacityWarningThresholdBytes,
    bool? CapacityWarning,
    long TrashBytes,
    int ExpiredTrashRootCount,
    int RetentionDays,
    int RecoveryRequiredPurgeCount,
    TrashPurgeRunSummary? LastPurgeRun);

public sealed class TrashPurgeInfrastructureException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface ITrashPurgeRunner
{
    Task<TrashPurgeRunSummary> RunAsync(CancellationToken cancellationToken);
}
