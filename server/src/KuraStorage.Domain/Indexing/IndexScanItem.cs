using KuraStorage.Domain.Files;

namespace KuraStorage.Domain.Indexing;

public sealed class IndexScanItem
{
    private IndexScanItem()
    {
    }

    public IndexScanItem(
        Guid scanId,
        RelativeStoragePath relativePath,
        Guid ownerUserId,
        RelativeStoragePath parentRelativePath,
        FileName name,
        FileEntryType entryType,
        long size,
        string? mimeType,
        DateTimeOffset sourceModifiedAt,
        string? sourceFileKey,
        string? isolationReason = null)
    {
        if (scanId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Scan and owner IDs are required.");
        }

        if (size < 0 || (entryType == FileEntryType.Folder && size != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (sourceFileKey is { Length: > 128 })
        {
            throw new ArgumentException("The source file key is too long.", nameof(sourceFileKey));
        }

        ScanId = scanId;
        RelativePath = relativePath.Value;
        OwnerUserId = ownerUserId;
        ParentRelativePath = parentRelativePath.Value;
        Name = name.Value;
        EntryType = entryType;
        Size = size;
        MimeType = mimeType;
        SourceModifiedAt = sourceModifiedAt;
        SourceFileKey = sourceFileKey;
        IsolationReason = isolationReason;
    }

    public Guid ScanId { get; private set; }
    public string RelativePath { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public string ParentRelativePath { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public FileEntryType EntryType { get; private set; }
    public long Size { get; private set; }
    public string? MimeType { get; private set; }
    public DateTimeOffset SourceModifiedAt { get; private set; }
    public string? SourceFileKey { get; private set; }
    public string? IsolationReason { get; private set; }
}
