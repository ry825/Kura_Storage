namespace KuraStorage.Domain.Maintenance;

public enum TrashPurgeRunStatus
{
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
}

public sealed class TrashPurgeRun
{
    private TrashPurgeRun()
    {
    }

    public TrashPurgeRun(Guid id, DateTimeOffset startedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A purge run ID is required.", nameof(id));
        }

        Id = id;
        StartedAt = startedAt;
        Status = TrashPurgeRunStatus.Running;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public TrashPurgeRunStatus Status { get; private set; }

    public int ExaminedRootCount { get; private set; }

    public int DeletedRootCount { get; private set; }

    public long ReleasedBytes { get; private set; }

    public int ErrorCount { get; private set; }

    public void RecordExamined()
    {
        EnsureRunning();
        ExaminedRootCount = checked(ExaminedRootCount + 1);
    }

    public void RecordDeleted(long releasedBytes)
    {
        EnsureRunning();
        if (releasedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(releasedBytes));
        }

        DeletedRootCount = checked(DeletedRootCount + 1);
        ReleasedBytes = checked(ReleasedBytes + releasedBytes);
    }

    public void RecordError()
    {
        EnsureRunning();
        ErrorCount = checked(ErrorCount + 1);
    }

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureRunning();
        CompletedAt = completedAt;
        Status = ErrorCount == 0
            ? TrashPurgeRunStatus.Completed
            : TrashPurgeRunStatus.CompletedWithErrors;
    }

    public void Fail(DateTimeOffset completedAt)
    {
        EnsureRunning();
        ErrorCount = checked(ErrorCount + 1);
        CompletedAt = completedAt;
        Status = TrashPurgeRunStatus.Failed;
    }

    private void EnsureRunning()
    {
        if (Status != TrashPurgeRunStatus.Running)
        {
            throw new InvalidOperationException("A completed purge run cannot be changed.");
        }
    }
}
