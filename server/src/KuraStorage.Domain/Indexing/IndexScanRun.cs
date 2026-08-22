namespace KuraStorage.Domain.Indexing;

public enum IndexScanTrigger
{
    Startup,
    Scheduled,
    Overflow,
    Admin,
}

public enum IndexScanMode
{
    DryRun,
    Apply,
}

public enum IndexScanStatus
{
    Running,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled,
}

public sealed class IndexScanRun
{
    private IndexScanRun()
    {
    }

    public IndexScanRun(Guid id, IndexScanTrigger trigger, IndexScanMode mode, DateTimeOffset startedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A scan run ID is required.", nameof(id));
        }

        Id = id;
        Trigger = trigger;
        Mode = mode;
        Status = IndexScanStatus.Running;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public IndexScanTrigger Trigger { get; private set; }
    public IndexScanMode Mode { get; private set; }
    public IndexScanStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int EnumeratedCount { get; private set; }
    public int AddedCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int MovedCount { get; private set; }
    public int CandidateCount { get; private set; }
    public int MissingCount { get; private set; }
    public int RevivedCount { get; private set; }
    public int IsolatedCount { get; private set; }
    public int ErrorCount { get; private set; }
    public string? ErrorCode { get; private set; }

    public void RecordEnumerated() => EnumeratedCount = Increment(EnumeratedCount);
    public void RecordAdded() => AddedCount = Increment(AddedCount);
    public void RecordUpdated() => UpdatedCount = Increment(UpdatedCount);
    public void RecordMoved() => MovedCount = Increment(MovedCount);
    public void RecordCandidate() => CandidateCount = Increment(CandidateCount);
    public void RecordMissing() => MissingCount = Increment(MissingCount);
    public void RecordRevived() => RevivedCount = Increment(RevivedCount);
    public void RecordIsolated() => IsolatedCount = Increment(IsolatedCount);
    public void RecordError() => ErrorCount = Increment(ErrorCount);

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureRunning();
        CompletedAt = completedAt;
        Status = ErrorCount == 0 ? IndexScanStatus.Completed : IndexScanStatus.CompletedWithWarnings;
    }

    public void Fail(string errorCode, DateTimeOffset completedAt)
    {
        EnsureRunning();
        ValidateErrorCode(errorCode);
        ErrorCount = checked(ErrorCount + 1);
        ErrorCode = errorCode;
        CompletedAt = completedAt;
        Status = IndexScanStatus.Failed;
    }

    public void Cancel(DateTimeOffset completedAt)
    {
        EnsureRunning();
        CompletedAt = completedAt;
        Status = IndexScanStatus.Cancelled;
    }

    private int Increment(int value)
    {
        EnsureRunning();
        return checked(value + 1);
    }

    private void EnsureRunning()
    {
        if (Status != IndexScanStatus.Running)
        {
            throw new InvalidOperationException("A completed index scan cannot be changed.");
        }
    }

    private static void ValidateErrorCode(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 64 ||
            errorCode.Any(character => character is not (>= 'A' and <= 'Z') and not '_' and not (>= '0' and <= '9')))
        {
            throw new ArgumentException("A low-cardinality uppercase error code is required.", nameof(errorCode));
        }
    }
}
