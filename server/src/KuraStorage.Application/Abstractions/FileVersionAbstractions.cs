using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Abstractions;

public interface IFileVersionRepository
{
    Task<FileVersionRecord?> FindAsync(Guid fileEntryId, long version, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileVersionHistoryRow>> ListAsync(
        Guid fileEntryId,
        long maximumVersion,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<int> CountAsync(
        Guid fileEntryId,
        long maximumVersion,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    void Add(FileVersionRecord record);
}

public sealed record FileVersionHistoryRow(
    long Version,
    long Size,
    string Sha256,
    FileVersionChangeKind ChangeKind,
    string? ActorDisplayName,
    DateTimeOffset CreatedAt);

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

    Task<Stream> OpenReadAsync(
        RelativeStoragePath contentPath,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

public sealed record PublishedFileVersion(
    RelativeStoragePath TemporaryPath,
    RelativeStoragePath Path,
    long Size,
    string Sha256);
