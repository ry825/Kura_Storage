namespace KuraStorage.Domain.Files;

public sealed class FileEntry
{
    private FileEntry()
    {
    }

    private FileEntry(
        Guid id,
        Guid ownerUserId,
        Guid? parentId,
        FileEntryType entryType,
        FileName name,
        RelativeStoragePath relativePath,
        string? mimeType,
        long size,
        DateTimeOffset now)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        Id = id;
        OwnerUserId = ownerUserId;
        ParentId = parentId;
        EntryType = entryType;
        Name = name.Value;
        RelativePath = relativePath.Value;
        MimeType = mimeType;
        Size = size;
        Status = FileEntryStatus.Active;
        FileVersion = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid? ParentId { get; private set; }

    public FileEntryType EntryType { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string RelativePath { get; private set; } = string.Empty;

    public string? MimeType { get; private set; }

    public long Size { get; private set; }

    public FileEntryStatus Status { get; private set; }

    public Guid? OriginalParentId { get; private set; }

    public string? OriginalRelativePath { get; private set; }

    public DateTimeOffset? TrashedAt { get; private set; }

    public DateTimeOffset? SourceModifiedAt { get; private set; }

    public string? SourceFileKey { get; private set; }

    public DateTimeOffset? SourceObservedAt { get; private set; }

    public DateTimeOffset? MissingDetectedAt { get; private set; }

    public DateTimeOffset? MissingLastCheckedAt { get; private set; }

    public Guid? MissingObservationId { get; private set; }

    public long FileVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static FileEntry CreateRoot(Guid ownerUserId, DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            ownerUserId,
            null,
            FileEntryType.Folder,
            FileName.Create("Files"),
            RelativeStoragePath.Create($"users/{ownerUserId:N}/files"),
            null,
            0,
            now);

    public static FileEntry CreateFolder(
        Guid id,
        Guid ownerUserId,
        Guid parentId,
        FileName name,
        RelativeStoragePath relativePath,
        DateTimeOffset now) =>
        new(id, ownerUserId, parentId, FileEntryType.Folder, name, relativePath, null, 0, now);

    public static FileEntry CreateFile(
        Guid id,
        Guid ownerUserId,
        Guid parentId,
        FileName name,
        RelativeStoragePath relativePath,
        string? mimeType,
        long size,
        DateTimeOffset now) =>
        new(id, ownerUserId, parentId, FileEntryType.File, name, relativePath, mimeType, size, now);

    public void Trash(RelativeStoragePath trashPath, DateTimeOffset now)
    {
        if (Status != FileEntryStatus.Active)
        {
            throw new InvalidOperationException("Only active entries can be trashed.");
        }

        OriginalParentId = ParentId;
        OriginalRelativePath = RelativePath;
        ParentId = null;
        RelativePath = trashPath.Value;
        Status = FileEntryStatus.Trashed;
        TrashedAt = now;
        UpdatedAt = now;
    }

    public void TrashDescendant(RelativeStoragePath trashPath, DateTimeOffset now)
    {
        RelativePath = trashPath.Value;
        Status = FileEntryStatus.Trashed;
        ClearMissingMetadata();
        TrashedAt = now;
        UpdatedAt = now;
    }

    public void Restore(Guid parentId, RelativeStoragePath restoredPath, DateTimeOffset now)
    {
        if (Status != FileEntryStatus.Trashed)
        {
            throw new InvalidOperationException("Only trashed entries can be restored.");
        }

        ParentId = parentId;
        RelativePath = restoredPath.Value;
        Status = FileEntryStatus.Active;
        OriginalParentId = null;
        OriginalRelativePath = null;
        TrashedAt = null;
        UpdatedAt = now;
    }

    public void RestoreDescendant(RelativeStoragePath restoredPath, DateTimeOffset now)
    {
        RelativePath = restoredPath.Value;
        Status = FileEntryStatus.Active;
        TrashedAt = null;
        UpdatedAt = now;
    }

    public void Rename(FileName name, RelativeStoragePath targetPath, DateTimeOffset now)
    {
        EnsureRelocatable();
        Name = name.Value;
        RelativePath = targetPath.Value;
        UpdatedAt = now;
    }

    public void MoveTo(Guid parentId, RelativeStoragePath targetPath, DateTimeOffset now)
    {
        EnsureRelocatable();
        if (parentId == Guid.Empty)
        {
            throw new ArgumentException("The parent ID is required.", nameof(parentId));
        }

        ParentId = parentId;
        RelativePath = targetPath.Value;
        UpdatedAt = now;
    }

    public void RelocateDescendant(RelativeStoragePath targetPath, DateTimeOffset now)
    {
        if (Status == FileEntryStatus.Trashed || ParentId is null)
        {
            throw new InvalidFileOperationException("Only managed non-trashed descendants can be relocated.");
        }

        RelativePath = targetPath.Value;
        UpdatedAt = now;
    }

    public void ApplySourceObservation(
        long size,
        string? mimeType,
        DateTimeOffset sourceModifiedAt,
        string? sourceFileKey,
        DateTimeOffset observedAt,
        bool contentChanged)
    {
        if (ParentId is null || Status == FileEntryStatus.Trashed)
        {
            throw new InvalidFileOperationException("Only managed non-trashed entries can be observed.");
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (sourceFileKey is { Length: > 128 })
        {
            throw new ArgumentException("The source file key is too long.", nameof(sourceFileKey));
        }

        if (contentChanged && EntryType == FileEntryType.File)
        {
            FileVersion = checked(FileVersion + 1);
        }

        Size = size;
        MimeType = mimeType;
        SourceModifiedAt = sourceModifiedAt;
        SourceFileKey = sourceFileKey;
        SourceObservedAt = observedAt;
        Status = FileEntryStatus.Active;
        ClearMissingMetadata();
        UpdatedAt = observedAt;
    }

    public void MarkMissingCandidate(Guid observationId, DateTimeOffset checkedAt)
    {
        if (observationId == Guid.Empty)
        {
            throw new ArgumentException("An observation ID is required.", nameof(observationId));
        }

        if (Status != FileEntryStatus.Active || ParentId is null)
        {
            throw new InvalidFileOperationException("Only active non-root entries can become missing candidates.");
        }

        Status = FileEntryStatus.MissingCandidate;
        MissingDetectedAt = checkedAt;
        MissingLastCheckedAt = checkedAt;
        MissingObservationId = observationId;
        UpdatedAt = checkedAt;
    }

    public void ConfirmMissing(Guid observationId, DateTimeOffset checkedAt, TimeSpan confirmationDelay)
    {
        if (Status != FileEntryStatus.MissingCandidate ||
            MissingDetectedAt is null ||
            MissingObservationId is null)
        {
            throw new InvalidFileOperationException("Only a valid missing candidate can be confirmed missing.");
        }

        if (observationId == Guid.Empty || observationId == MissingObservationId)
        {
            throw new InvalidFileOperationException("Missing confirmation requires an independent observation.");
        }

        if (confirmationDelay <= TimeSpan.Zero || checkedAt < MissingDetectedAt.Value + confirmationDelay)
        {
            throw new InvalidFileOperationException("Missing confirmation was attempted before the confirmation delay elapsed.");
        }

        Status = FileEntryStatus.Missing;
        MissingLastCheckedAt = checkedAt;
        UpdatedAt = checkedAt;
    }

    public void RecordMissingCheck(DateTimeOffset checkedAt)
    {
        if (Status != FileEntryStatus.Missing)
        {
            throw new InvalidFileOperationException("Only missing entries can record a continuing absence.");
        }

        MissingLastCheckedAt = checkedAt;
        UpdatedAt = checkedAt;
    }

    public void RecordMissingCandidateCheck(DateTimeOffset checkedAt)
    {
        if (Status != FileEntryStatus.MissingCandidate)
        {
            throw new InvalidFileOperationException("Only missing candidates can record a continuing absence.");
        }

        MissingLastCheckedAt = checkedAt;
        UpdatedAt = checkedAt;
    }

    private void EnsureRelocatable()
    {
        if (Status != FileEntryStatus.Active)
        {
            throw new InvalidFileOperationException("Only active entries can be relocated.");
        }

        if (ParentId is null)
        {
            throw new InvalidFileOperationException("The storage root cannot be relocated.");
        }
    }

    private void ClearMissingMetadata()
    {
        MissingDetectedAt = null;
        MissingLastCheckedAt = null;
        MissingObservationId = null;
    }
}

public sealed class InvalidFileOperationException(string message) : InvalidOperationException(message);
