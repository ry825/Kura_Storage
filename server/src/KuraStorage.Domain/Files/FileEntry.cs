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
}
