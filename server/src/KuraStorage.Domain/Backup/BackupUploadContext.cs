namespace KuraStorage.Domain.Backup;

public enum BackupUploadDecision
{
    New,
    Changed,
}

public sealed record BackupUploadContext
{
    public BackupUploadContext(
        BackupDocumentMetadata metadata,
        BackupUploadDecision decision,
        Guid? expectedRemoteFileId,
        long? expectedRemoteFileVersion)
    {
        if (!Enum.IsDefined(decision) ||
            (decision == BackupUploadDecision.New &&
             (expectedRemoteFileId is not null || expectedRemoteFileVersion is not null)) ||
            (decision == BackupUploadDecision.Changed &&
             (expectedRemoteFileId is null || expectedRemoteFileId == Guid.Empty ||
              expectedRemoteFileVersion is null or < 1)))
        {
            throw new ArgumentException("The backup upload decision is invalid.");
        }

        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Decision = decision;
        ExpectedRemoteFileId = expectedRemoteFileId;
        ExpectedRemoteFileVersion = expectedRemoteFileVersion;
    }

    public BackupDocumentMetadata Metadata { get; }
    public BackupUploadDecision Decision { get; }
    public Guid? ExpectedRemoteFileId { get; }
    public long? ExpectedRemoteFileVersion { get; }
    public string LocalDocumentKey => Metadata.LocalDocumentKey;
    public string RelativePath => Metadata.RelativePath;
    public DateTimeOffset SourceModifiedAt => Metadata.SourceModifiedAt;
    public string? SourceChecksum => Metadata.Checksum;
}
