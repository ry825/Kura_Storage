namespace KuraStorage.Domain.Media;

public sealed class FileDerivative
{
    private FileDerivative()
    {
    }

    public FileDerivative(
        Guid id,
        Guid sourceFileId,
        long sourceVersion,
        DerivativeType derivativeType,
        int profileVersion,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || sourceFileId == Guid.Empty)
        {
            throw new ArgumentException("Derivative and source IDs are required.");
        }

        if (sourceVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        }

        if (profileVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(profileVersion));
        }

        if (!Enum.IsDefined(derivativeType))
        {
            throw new ArgumentOutOfRangeException(nameof(derivativeType));
        }

        Id = id;
        SourceFileId = sourceFileId;
        SourceVersion = sourceVersion;
        DerivativeType = derivativeType;
        ProfileVersion = profileVersion;
        Status = DerivativeStatus.Pending;
        Revision = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid SourceFileId { get; private set; }

    public long SourceVersion { get; private set; }

    public DerivativeType DerivativeType { get; private set; }

    public int ProfileVersion { get; private set; }

    public string? RelativePath { get; private set; }

    public long Size { get; private set; }

    public DerivativeStatus Status { get; private set; }

    public DateTimeOffset? LastAccessedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? LeaseUntil { get; private set; }

    public string? ErrorCode { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DerivativeLogicalKey LogicalKey =>
        new(SourceFileId, SourceVersion, DerivativeType, ProfileVersion);

    public bool IsThumbnail =>
        DerivativeType is DerivativeType.Thumbnail or DerivativeType.PdfThumbnail;

    public void Start(DateTimeOffset now)
    {
        EnsureStatus(DerivativeStatus.Pending);
        Status = DerivativeStatus.Running;
        ErrorCode = null;
        Touch(now);
    }

    public void MarkReady(
        string relativePath,
        long verifiedSize,
        DateTimeOffset now,
        DateTimeOffset? expiresAt)
    {
        EnsureStatus(DerivativeStatus.Running);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A formal derivative path is required.", nameof(relativePath));
        }

        if (verifiedSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(verifiedSize));
        }

        if (IsThumbnail && expiresAt is not null)
        {
            throw new InvalidOperationException("Thumbnails cannot expire.");
        }

        if (!IsThumbnail && (expiresAt is null || expiresAt <= now))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        RelativePath = relativePath;
        Size = verifiedSize;
        Status = DerivativeStatus.Ready;
        LastAccessedAt = IsThumbnail ? null : now;
        ExpiresAt = expiresAt;
        ErrorCode = null;
        Touch(now);
    }

    public void Requeue(DateTimeOffset now, string errorCode)
    {
        EnsureStatus(DerivativeStatus.Running);
        ErrorCode = RequireErrorCode(errorCode);
        Status = DerivativeStatus.Pending;
        ClearPublishedData();
        Touch(now);
    }

    public void Fail(string errorCode, DateTimeOffset now)
    {
        EnsureStatus(DerivativeStatus.Running);
        ErrorCode = RequireErrorCode(errorCode);
        Status = DerivativeStatus.Failed;
        ClearPublishedData();
        Touch(now);
    }

    public void Retry(DateTimeOffset now)
    {
        EnsureStatus(DerivativeStatus.Failed);
        Status = DerivativeStatus.Pending;
        ErrorCode = null;
        ClearPublishedData();
        Touch(now);
    }

    public void BlockSourceMissing(DateTimeOffset now)
    {
        if (Status == DerivativeStatus.Deleting)
        {
            throw new InvalidOperationException("A deleting derivative cannot be blocked.");
        }

        Status = DerivativeStatus.BlockedSourceMissing;
        ErrorCode = "MEDIA_SOURCE_MISSING";
        Touch(now);
    }

    public void BeginDeleting(DateTimeOffset now)
    {
        if (Status is not (DerivativeStatus.Ready or DerivativeStatus.Failed or DerivativeStatus.BlockedSourceMissing))
        {
            throw new InvalidOperationException("The derivative cannot be deleted in its current state.");
        }

        Status = DerivativeStatus.Deleting;
        Touch(now);
    }

    public void RecordAccess(DateTimeOffset now, DateTimeOffset expiresAt)
    {
        EnsureStatus(DerivativeStatus.Ready);
        if (IsThumbnail)
        {
            throw new InvalidOperationException("Thumbnail access does not update cache expiry.");
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        LastAccessedAt = now;
        ExpiresAt = expiresAt;
        Touch(now);
    }

    public void ProjectLeaseUntil(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        if (LeaseUntil is null || expiresAt > LeaseUntil)
        {
            LeaseUntil = expiresAt;
        }

        Touch(now);
    }

    public void ClearLeaseProjection(DateTimeOffset now)
    {
        LeaseUntil = null;
        Touch(now);
    }

    private void ClearPublishedData()
    {
        RelativePath = null;
        Size = 0;
        LastAccessedAt = null;
        ExpiresAt = null;
    }

    private void EnsureStatus(DerivativeStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The derivative must be {expected}.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        Revision = checked(Revision + 1);
        UpdatedAt = now;
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
