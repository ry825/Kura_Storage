namespace KuraStorage.Domain.Backup;

public sealed class BackupReceipt
{
    private BackupReceipt()
    {
    }

    public BackupReceipt(
        Guid id,
        Guid userId,
        Guid deviceId,
        string localDocumentKey,
        Guid remoteFileId,
        string relativePath,
        long size,
        DateTimeOffset sourceModifiedAt,
        string? checksum,
        long remoteFileVersion,
        DateTimeOffset uploadedAt)
    {
        if (id == Guid.Empty || userId == Guid.Empty || deviceId == Guid.Empty || remoteFileId == Guid.Empty)
        {
            throw new ArgumentException("Receipt, user, device, and remote file IDs are required.");
        }

        Id = id;
        UserId = userId;
        DeviceId = deviceId;
        var metadata = new BackupDocumentMetadata(localDocumentKey, relativePath, size, sourceModifiedAt, checksum);
        LocalDocumentKey = metadata.LocalDocumentKey;
        RemoteFileId = remoteFileId;
        ApplyCompletion(metadata, remoteFileVersion, uploadedAt);
        CreatedAt = uploadedAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid DeviceId { get; private set; }

    public string LocalDocumentKey { get; private set; } = string.Empty;

    public Guid RemoteFileId { get; private set; }

    public string RelativePath { get; private set; } = string.Empty;

    public long Size { get; private set; }

    public DateTimeOffset SourceModifiedAt { get; private set; }

    public string? Checksum { get; private set; }

    public long RemoteFileVersion { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool Matches(BackupDocumentMetadata metadata) =>
        string.Equals(LocalDocumentKey, metadata.LocalDocumentKey, StringComparison.Ordinal) &&
        string.Equals(RelativePath, metadata.RelativePath, StringComparison.Ordinal) &&
        Size == metadata.Size && SourceModifiedAt == metadata.SourceModifiedAt &&
        (metadata.Checksum is null || string.Equals(Checksum, metadata.Checksum, StringComparison.Ordinal));

    public void UpdateCompletion(
        Guid remoteFileId,
        string relativePath,
        long size,
        DateTimeOffset sourceModifiedAt,
        string? checksum,
        long remoteFileVersion,
        DateTimeOffset uploadedAt)
    {
        if (remoteFileId == Guid.Empty || remoteFileId != RemoteFileId)
        {
            throw new ArgumentException("The confirmed remote file must match the receipt.", nameof(remoteFileId));
        }

        if (remoteFileVersion <= RemoteFileVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteFileVersion));
        }

        ApplyCompletion(
            new BackupDocumentMetadata(LocalDocumentKey, relativePath, size, sourceModifiedAt, checksum),
            remoteFileVersion,
            uploadedAt);
    }

    private void ApplyCompletion(
        BackupDocumentMetadata metadata,
        long remoteFileVersion,
        DateTimeOffset uploadedAt)
    {
        if (remoteFileVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteFileVersion));
        }

        EnsureUtc(uploadedAt, nameof(uploadedAt));
        RelativePath = metadata.RelativePath;
        Size = metadata.Size;
        SourceModifiedAt = metadata.SourceModifiedAt;
        Checksum = metadata.Checksum;
        RemoteFileVersion = remoteFileVersion;
        UploadedAt = uploadedAt;
        UpdatedAt = uploadedAt;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be UTC.", parameterName);
        }
    }
}
