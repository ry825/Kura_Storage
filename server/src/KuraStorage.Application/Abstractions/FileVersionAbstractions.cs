using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Abstractions;

public interface IFileVersionRepository
{
    Task<FileVersionRecord?> FindAsync(Guid fileEntryId, long version, CancellationToken cancellationToken);

    void Add(FileVersionRecord record);
}

public interface IFileVersionStore
{
    Task<PublishedFileVersion?> TryPublishAsync(
        Guid ownerUserId,
        Guid fileEntryId,
        long version,
        Guid operationId,
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken);
}

public sealed record PublishedFileVersion(
    RelativeStoragePath TemporaryPath,
    RelativeStoragePath Path,
    long Size,
    string Sha256);
