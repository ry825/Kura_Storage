namespace KuraStorage.Domain.Files;

public sealed class FileOperation
{
    private FileOperation()
    {
    }

    public FileOperation(
        Guid id,
        Guid ownerUserId,
        FileOperationType operationType,
        Guid? fileEntryId,
        string? idempotencyKey,
        string? sourceRelativePath,
        string? targetRelativePath,
        long? expectedSize,
        string? expectedSha256,
        DateTimeOffset now)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        OperationType = operationType;
        FileEntryId = fileEntryId;
        IdempotencyKey = idempotencyKey;
        SourceRelativePath = sourceRelativePath;
        TargetRelativePath = targetRelativePath;
        ExpectedSize = expectedSize;
        ExpectedSha256 = expectedSha256;
        Status = FileOperationStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public FileOperationType OperationType { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public Guid? FileEntryId { get; private set; }

    public string? SourceRelativePath { get; private set; }

    public string? TargetRelativePath { get; private set; }

    public long? ExpectedSize { get; private set; }

    public string? ExpectedSha256 { get; private set; }

    public FileOperationStatus Status { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkFilesystemDone(DateTimeOffset now)
    {
        if (Status != FileOperationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending operations can be advanced.");
        }

        Status = FileOperationStatus.FilesystemDone;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status is not (FileOperationStatus.Pending or FileOperationStatus.FilesystemDone))
        {
            throw new InvalidOperationException("The operation cannot be completed.");
        }

        Status = FileOperationStatus.Completed;
        ErrorCode = null;
        UpdatedAt = now;
    }

    public void RequireRecovery(string errorCode, DateTimeOffset now)
    {
        Status = FileOperationStatus.RecoveryRequired;
        ErrorCode = errorCode;
        UpdatedAt = now;
    }

    public void Retry(DateTimeOffset now)
    {
        if (Status != FileOperationStatus.RecoveryRequired)
        {
            throw new InvalidOperationException("Only recovery-required operations can be retried.");
        }

        Status = FileOperationStatus.Pending;
        ErrorCode = null;
        UpdatedAt = now;
    }
}
