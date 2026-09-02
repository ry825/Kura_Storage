using KuraStorage.Application.Files;
using KuraStorage.Domain.Backup;

namespace KuraStorage.Application.Transfers;

public sealed class UploadSessionOptions
{
    public const string SectionName = "UploadSession";
    public const int MinimumChunkBytes = 256 * 1024;

    public int PreferredChunkBytes { get; init; } = 4 * 1024 * 1024;

    public int MaximumChunkBytes { get; init; } = 8 * 1024 * 1024;

    public long MaximumFileBytes { get; init; } = 1024L * 1024 * 1024 * 1024;

    public int IdleExpirationHours { get; init; } = 24;

    public int AbsoluteExpirationHours { get; init; } = 168;

    public int CleanupIntervalMinutes { get; init; } = 15;

    public int CleanupBatchSize { get; init; } = 100;

    public int MaximumActiveSessionsPerUser { get; init; } = 10;

    public int MaximumActiveSessionsPerDevice { get; init; } = 5;

    public int MaximumConcurrentChunkWrites { get; init; } = 2;

    public int OverloadRetryAfterSeconds { get; init; } = 5;
}

public sealed record CreateUploadSessionCommand(
    Guid ActorUserId,
    Guid DeviceId,
    Guid DestinationFolderId,
    string FileName,
    long Size,
    string? ContentType,
    string? Sha256,
    string IdempotencyKey,
    string RequestId,
    BackupUploadRequest? Backup = null);

public sealed record BackupUploadRequest(
    string LocalDocumentKey,
    string RelativePath,
    DateTimeOffset ModifiedAt,
    BackupUploadDecision Decision,
    Guid? ExpectedRemoteFileId,
    long? ExpectedRemoteFileVersion);

public sealed record UploadChunkCommand(
    Guid ActorUserId,
    Guid DeviceId,
    Guid SessionId,
    long Offset,
    long Length,
    string Sha256,
    Stream Content,
    string RequestId);

public sealed record UploadSessionItem(
    Guid Id,
    string Status,
    long Size,
    long ReceivedBytes,
    long NextOffset,
    int PreferredChunkBytes,
    int MaximumChunkBytes,
    DateTimeOffset ExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    bool Resumable,
    FileItem? File);

public sealed record CreatedUploadSession(UploadSessionItem Session, bool Created);

public sealed record UploadChunkItem(
    long Offset,
    long Length,
    string Sha256,
    long ReceivedBytes,
    long NextOffset,
    DateTimeOffset ExpiresAt,
    bool Replayed);

public sealed record StoredChunk(long Length, string Sha256);

public sealed record TemporaryUploadState(bool Exists, long Length);

public sealed class UploadChunkSizeMismatchException : IOException;

public sealed class UploadTemporaryFileTooShortException : IOException;
