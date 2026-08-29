namespace KuraStorage.Domain.Media;

public sealed class MediaJob
{
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(7);

    private MediaJob()
    {
    }

    public MediaJob(
        Guid id,
        Guid derivativeId,
        DerivativeType jobType,
        Guid requestedByUserId,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || derivativeId == Guid.Empty || requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Job, derivative, and requesting user IDs are required.");
        }

        if (!Enum.IsDefined(jobType))
        {
            throw new ArgumentOutOfRangeException(nameof(jobType));
        }

        Id = id;
        DerivativeId = derivativeId;
        JobType = jobType;
        RequestedByUserId = requestedByUserId;
        Status = MediaJobStatus.Queued;
        AvailableAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid DerivativeId { get; private set; }

    public DerivativeType JobType { get; private set; }

    public MediaJobStatus Status { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset AvailableAt { get; private set; }

    public Guid? WorkerToken { get; private set; }

    public DateTimeOffset? HeartbeatAt { get; private set; }

    public int? ProgressPercent { get; private set; }

    public long? ProcessedDurationMs { get; private set; }

    public long? TotalDurationMs { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Start(Guid workerToken, DateTimeOffset now)
    {
        if (Status != MediaJobStatus.Queued || AvailableAt > now || AttemptCount >= MaximumAttempts || workerToken == Guid.Empty)
        {
            throw new InvalidOperationException("The media job is not available for this worker.");
        }

        Status = MediaJobStatus.Running;
        AttemptCount = checked(AttemptCount + 1);
        WorkerToken = workerToken;
        HeartbeatAt = now;
        StartedAt ??= now;
        CompletedAt = null;
        ErrorCode = null;
        UpdatedAt = now;
    }

    public void RecordHeartbeat(
        Guid workerToken,
        DateTimeOffset now,
        int? progressPercent,
        long? processedDurationMs,
        long? totalDurationMs)
    {
        EnsureWorker(workerToken);
        ValidateProgress(progressPercent, processedDurationMs, totalDurationMs);
        HeartbeatAt = now;
        ProgressPercent = progressPercent;
        ProcessedDurationMs = processedDurationMs;
        TotalDurationMs = totalDurationMs;
        UpdatedAt = now;
    }

    public void Complete(Guid workerToken, DateTimeOffset now)
    {
        EnsureWorker(workerToken);
        Status = MediaJobStatus.Completed;
        CompletedAt = now;
        WorkerToken = null;
        HeartbeatAt = null;
        ErrorCode = null;
        UpdatedAt = now;
    }

    public void Fail(Guid workerToken, string errorCode, bool retryable, DateTimeOffset now)
    {
        EnsureWorker(workerToken);
        ErrorCode = RequireErrorCode(errorCode);
        WorkerToken = null;
        HeartbeatAt = null;
        ProgressPercent = null;
        ProcessedDurationMs = null;
        TotalDurationMs = null;

        if (retryable && AttemptCount < MaximumAttempts)
        {
            Status = MediaJobStatus.Queued;
            AvailableAt = now.Add(RetryDelayAfter(AttemptCount));
        }
        else
        {
            Status = MediaJobStatus.Failed;
            CompletedAt = now;
        }

        UpdatedAt = now;
    }

    public void Cancel(string errorCode, DateTimeOffset now)
    {
        if (Status is not (MediaJobStatus.Queued or MediaJobStatus.Running))
        {
            throw new InvalidOperationException("Only an active media job can be cancelled.");
        }

        Status = MediaJobStatus.Cancelled;
        ErrorCode = RequireErrorCode(errorCode);
        WorkerToken = null;
        HeartbeatAt = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public bool IsStaleAt(DateTimeOffset now) =>
        Status == MediaJobStatus.Running &&
        HeartbeatAt is not null &&
        HeartbeatAt.Value.Add(StaleAfter) <= now;

    public bool IsHistoryExpiredAt(DateTimeOffset now) =>
        Status is MediaJobStatus.Completed or MediaJobStatus.Failed or MediaJobStatus.Cancelled &&
        CompletedAt is not null &&
        CompletedAt.Value.Add(HistoryRetention) <= now;

    public static TimeSpan RetryDelayAfter(int attemptCount) =>
        attemptCount switch
        {
            1 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(2),
            _ => throw new ArgumentOutOfRangeException(nameof(attemptCount)),
        };

    private void EnsureWorker(Guid workerToken)
    {
        if (Status != MediaJobStatus.Running || workerToken == Guid.Empty || WorkerToken != workerToken)
        {
            throw new InvalidOperationException("The media job is not owned by this worker.");
        }
    }

    private static void ValidateProgress(int? progress, long? processed, long? total)
    {
        if (progress is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }

        if (processed < 0 || total < 0 || processed is not null && total is not null && processed > total)
        {
            throw new ArgumentOutOfRangeException(nameof(processed));
        }
    }

    private static string RequireErrorCode(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 64)
        {
            throw new ArgumentException("A bounded error code is required.", nameof(errorCode));
        }

        return errorCode;
    }
}
