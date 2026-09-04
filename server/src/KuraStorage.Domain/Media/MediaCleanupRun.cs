namespace KuraStorage.Domain.Media;

public enum MediaCleanupTrigger
{
    Scheduled,
    Manual,
}

public enum MediaCleanupRunStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}

public enum MediaCleanupFailureCode
{
    StorageUnavailable,
    PartialDeleteFailure,
    CleanupFailed,
}

public sealed class MediaCleanupRun
{
    private MediaCleanupRun()
    {
    }

    private MediaCleanupRun(
        Guid id,
        MediaCleanupTrigger trigger,
        Guid? requestedByAdminUserId,
        string? idempotencyKeyHash,
        string? requestFingerprintHash,
        DateTimeOffset requestedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A cleanup run ID is required.", nameof(id));
        }

        if (trigger == MediaCleanupTrigger.Manual)
        {
            if (requestedByAdminUserId is null || requestedByAdminUserId == Guid.Empty)
            {
                throw new ArgumentException("A requesting administrator is required.", nameof(requestedByAdminUserId));
            }

            ValidateHash(idempotencyKeyHash, nameof(idempotencyKeyHash));
            ValidateHash(requestFingerprintHash, nameof(requestFingerprintHash));
        }
        else if (requestedByAdminUserId is not null || idempotencyKeyHash is not null || requestFingerprintHash is not null)
        {
            throw new ArgumentException("Scheduled cleanup cannot carry manual request identity.");
        }

        Id = id;
        Trigger = trigger;
        RequestedByAdminUserId = requestedByAdminUserId;
        IdempotencyKeyHash = idempotencyKeyHash;
        RequestFingerprintHash = requestFingerprintHash;
        RequestedAt = requestedAt;
        Status = MediaCleanupRunStatus.Pending;
    }

    public Guid Id { get; private set; }

    public MediaCleanupTrigger Trigger { get; private set; }

    public MediaCleanupRunStatus Status { get; private set; }

    public Guid? RequestedByAdminUserId { get; private set; }

    public string? IdempotencyKeyHash { get; private set; }

    public string? RequestFingerprintHash { get; private set; }

    public Guid? WorkerToken { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int ExaminedCount { get; private set; }

    public int DeletedCount { get; private set; }

    public long ReleasedBytes { get; private set; }

    public int FailureCount { get; private set; }

    public long? RemainingCacheBytes { get; private set; }

    public MediaCleanupFailureCode? FailureCode { get; private set; }

    public static MediaCleanupRun CreateManual(
        Guid id,
        Guid requestedByAdminUserId,
        string idempotencyKeyHash,
        string requestFingerprintHash,
        DateTimeOffset requestedAt) =>
        new(id, MediaCleanupTrigger.Manual, requestedByAdminUserId, idempotencyKeyHash, requestFingerprintHash, requestedAt);

    public static MediaCleanupRun CreateScheduled(Guid id, DateTimeOffset requestedAt) =>
        new(id, MediaCleanupTrigger.Scheduled, null, null, null, requestedAt);

    public void Claim(Guid workerToken, DateTimeOffset startedAt, DateTimeOffset leaseExpiresAt)
    {
        if (workerToken == Guid.Empty || leaseExpiresAt <= startedAt)
        {
            throw new ArgumentException("A worker token and future lease are required.");
        }

        if (Status != MediaCleanupRunStatus.Pending &&
            (Status != MediaCleanupRunStatus.Running || LeaseExpiresAt is null || LeaseExpiresAt > startedAt))
        {
            throw new InvalidOperationException("The cleanup run is not claimable.");
        }

        Status = MediaCleanupRunStatus.Running;
        WorkerToken = workerToken;
        LeaseExpiresAt = leaseExpiresAt;
        StartedAt ??= startedAt;
        CompletedAt = null;
        FailureCode = null;
    }

    public void Release(Guid workerToken)
    {
        EnsureWorker(workerToken);
        Status = MediaCleanupRunStatus.Pending;
        WorkerToken = null;
        LeaseExpiresAt = null;
    }

    public void Complete(
        Guid workerToken,
        DateTimeOffset completedAt,
        int examinedCount,
        int deletedCount,
        long releasedBytes,
        int failureCount,
        long remainingCacheBytes)
    {
        EnsureWorker(workerToken);
        if (examinedCount < 0 || deletedCount < 0 || deletedCount > examinedCount ||
            releasedBytes < 0 || failureCount < 0 || failureCount > examinedCount || remainingCacheBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(examinedCount));
        }

        ExaminedCount = examinedCount;
        DeletedCount = deletedCount;
        ReleasedBytes = releasedBytes;
        FailureCount = failureCount;
        RemainingCacheBytes = remainingCacheBytes;
        CompletedAt = completedAt;
        Status = failureCount == 0 ? MediaCleanupRunStatus.Completed : MediaCleanupRunStatus.Failed;
        FailureCode = failureCount == 0 ? null : MediaCleanupFailureCode.PartialDeleteFailure;
        WorkerToken = null;
        LeaseExpiresAt = null;
    }

    public void Fail(Guid workerToken, DateTimeOffset completedAt, MediaCleanupFailureCode failureCode)
    {
        EnsureWorker(workerToken);
        if (!Enum.IsDefined(failureCode))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        }

        Status = MediaCleanupRunStatus.Failed;
        FailureCount = Math.Max(1, FailureCount);
        FailureCode = failureCode;
        CompletedAt = completedAt;
        WorkerToken = null;
        LeaseExpiresAt = null;
    }

    private void EnsureWorker(Guid workerToken)
    {
        if (Status != MediaCleanupRunStatus.Running || workerToken == Guid.Empty || WorkerToken != workerToken)
        {
            throw new InvalidOperationException("The cleanup run is not owned by this worker.");
        }
    }

    private static void ValidateHash(string? value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 hexadecimal hash is required.", parameterName);
        }
    }
}
