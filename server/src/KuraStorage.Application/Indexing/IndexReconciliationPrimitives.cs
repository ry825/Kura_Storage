using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Indexing;

internal static class IndexReconciliationPrimitives
{
    internal static void ApplyPresent(
        FileEntry entry,
        long size,
        string? mimeType,
        DateTimeOffset sourceModifiedAt,
        string? sourceFileKey,
        DateTimeOffset observedAt,
        bool contentMayHaveChanged)
    {
        var contentChanged = entry.EntryType == FileEntryType.File &&
            (contentMayHaveChanged || entry.Size != size ||
             (entry.SourceModifiedAt is not null && entry.SourceModifiedAt != sourceModifiedAt));
        entry.ApplySourceObservation(
            size,
            mimeType,
            sourceModifiedAt,
            sourceFileKey,
            observedAt,
            contentChanged);
    }

    internal static async Task<IReadOnlyList<FileEntry>> RelocateAsync(
        FileEntry entry,
        FileEntry parent,
        string targetName,
        string targetPath,
        IIndexCatalogRepository catalog,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var oldPrefix = entry.RelativePath;
        if (entry.Name != targetName)
        {
            entry.Rename(FileName.Create(targetName), RelativeStoragePath.Create(targetPath), now);
        }

        if (entry.ParentId != parent.Id)
        {
            entry.MoveTo(parent.Id, RelativeStoragePath.Create(targetPath), now);
        }

        if (entry.EntryType != FileEntryType.Folder)
        {
            return [entry];
        }

        var relocated = new List<FileEntry> { entry };
        foreach (var descendant in await catalog.ListDescendantsAsync(
                     entry.OwnerUserId,
                     oldPrefix,
                     cancellationToken))
        {
            var suffix = descendant.RelativePath[oldPrefix.Length..];
            descendant.RelocateDescendant(RelativeStoragePath.Create(targetPath + suffix), now);
            relocated.Add(descendant);
        }

        return relocated;
    }
}
