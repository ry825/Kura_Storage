namespace KuraStorage.Domain.Transfers;

public sealed class UploadSession
{
    private UploadSession()
    {
    }

    public UploadSession(
        Guid id,
        Guid actorUserId,
        Guid targetOwnerUserId,
        Guid deviceId,
        Guid destinationFolderId,
        Guid fileEntryId,
        string idempotencyKey,
        string fileName,
        string? contentType,
        long expectedSize,
        string? expectedSha256,
        string temporaryRelativePath,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        DateTimeOffset absoluteExpiresAt)
    {
        if (id == Guid.Empty || actorUserId == Guid.Empty || targetOwnerUserId == Guid.Empty || deviceId == Guid.Empty ||
            destinationFolderId == Guid.Empty || fileEntryId == Guid.Empty || expectedSize < 0 ||
            string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(fileName) ||
            string.IsNullOrWhiteSpace(temporaryRelativePath) || expiresAt <= now ||
            absoluteExpiresAt < expiresAt)
        {
            throw new ArgumentException("The upload session metadata is invalid.");
        }

        Id = id;
        ActorUserId = actorUserId;
        TargetOwnerUserId = targetOwnerUserId;
        DeviceId = deviceId;
        DestinationFolderId = destinationFolderId;
        FileEntryId = fileEntryId;
        IdempotencyKey = idempotencyKey;
        FileName = fileName;
        ContentType = contentType;
        ExpectedSize = expectedSize;
        ExpectedSha256 = expectedSha256;
        TemporaryRelativePath = temporaryRelativePath;
        Status = UploadSessionStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
        ExpiresAt = expiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
    }

    public Guid Id { get; private set; }

    public Guid ActorUserId { get; private set; }

    public Guid TargetOwnerUserId { get; private set; }

    public Guid DeviceId { get; private set; }

    public Guid? DestinationFolderId { get; private set; }

    public Guid FileEntryId { get; private set; }

    public Guid? FileOperationId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public string? ContentType { get; private set; }

    public long ExpectedSize { get; private set; }

    public string? ExpectedSha256 { get; private set; }

    public long ReceivedBytes { get; private set; }

    public long? LastChunkOffset { get; private set; }

    public long? LastChunkLength { get; private set; }

    public string? LastChunkSha256 { get; private set; }

    public string TemporaryRelativePath { get; private set; } = string.Empty;

    public UploadSessionStatus Status { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CleanedAt { get; private set; }

    public bool IsExpiredAt(DateTimeOffset now) =>
        Status == UploadSessionStatus.Active && ExpiresAt <= now;

    public bool SameMetadata(
        Guid deviceId,
        Guid destinationFolderId,
        string fileName,
        string? contentType,
        long expectedSize,
        string? expectedSha256) =>
        DeviceId == deviceId && DestinationFolderId == destinationFolderId &&
        string.Equals(FileName, fileName, StringComparison.Ordinal) &&
        string.Equals(ContentType, contentType, StringComparison.OrdinalIgnoreCase) &&
        ExpectedSize == expectedSize &&
        string.Equals(ExpectedSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);

    public bool IsLastChunk(long offset, long length, string sha256) =>
        LastChunkOffset == offset && LastChunkLength == length &&
        string.Equals(LastChunkSha256, sha256, StringComparison.OrdinalIgnoreCase);

    public void AcceptChunk(
        long offset,
        long length,
        string sha256,
        DateTimeOffset now,
        TimeSpan idleExpiration)
    {
        if (Status != UploadSessionStatus.Active || offset != ReceivedBytes || length <= 0 ||
            length > ExpectedSize - ReceivedBytes)
        {
            throw new InvalidOperationException("The chunk cannot be accepted in the current state.");
        }

        LastChunkOffset = offset;
        LastChunkLength = length;
        LastChunkSha256 = sha256;
        ReceivedBytes = checked(ReceivedBytes + length);
        ExpiresAt = Min(now.Add(idleExpiration), AbsoluteExpiresAt);
        UpdatedAt = now;
        ErrorCode = null;
    }

    public void BeginCompletion(Guid operationId, DateTimeOffset now)
    {
        if (Status != UploadSessionStatus.Active || ReceivedBytes != ExpectedSize || operationId == Guid.Empty)
        {
            throw new InvalidOperationException("The upload session is not ready for completion.");
        }

        Status = UploadSessionStatus.Completing;
        FileOperationId = operationId;
        UpdatedAt = now;
        ErrorCode = null;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != UploadSessionStatus.Completing)
        {
            throw new InvalidOperationException("Only a completing upload session can complete.");
        }

        Status = UploadSessionStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;
        ErrorCode = null;
    }

    public void ResetAfterChecksumFailure(DateTimeOffset now, TimeSpan idleExpiration)
    {
        if (Status != UploadSessionStatus.Active)
        {
            throw new InvalidOperationException("Only an active upload session can be reset.");
        }

        ReceivedBytes = 0;
        LastChunkOffset = null;
        LastChunkLength = null;
        LastChunkSha256 = null;
        ExpiresAt = Min(now.Add(idleExpiration), AbsoluteExpiresAt);
        UpdatedAt = now;
        ErrorCode = "UPLOAD_CHECKSUM_MISMATCH";
    }

    public void Cancel(string? errorCode, DateTimeOffset now)
    {
        if (Status == UploadSessionStatus.Cancelled)
        {
            return;
        }

        if (Status != UploadSessionStatus.Active)
        {
            throw new InvalidOperationException("Only an active upload session can be cancelled.");
        }

        Status = UploadSessionStatus.Cancelled;
        ErrorCode = errorCode;
        UpdatedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status == UploadSessionStatus.Expired)
        {
            return;
        }

        if (Status != UploadSessionStatus.Active || ExpiresAt > now)
        {
            throw new InvalidOperationException("The upload session cannot expire.");
        }

        Status = UploadSessionStatus.Expired;
        ErrorCode = "UPLOAD_SESSION_EXPIRED";
        UpdatedAt = now;
    }

    public void RequireRecovery(string errorCode, DateTimeOffset now)
    {
        if (Status is UploadSessionStatus.Completed or UploadSessionStatus.Cancelled or UploadSessionStatus.Expired)
        {
            throw new InvalidOperationException("A terminal upload session cannot require recovery.");
        }

        Status = UploadSessionStatus.RecoveryRequired;
        ErrorCode = errorCode;
        UpdatedAt = now;
    }

    public void MarkCleaned(DateTimeOffset now)
    {
        if (Status is not (UploadSessionStatus.Cancelled or UploadSessionStatus.Expired))
        {
            throw new InvalidOperationException("Only a terminal unpublished session can be cleaned.");
        }

        CleanedAt = now;
        UpdatedAt = now;
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}
