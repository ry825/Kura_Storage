using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed record FileItem(
    Guid Id,
    Guid? ParentId,
    string Name,
    string EntryType,
    string? MimeType,
    long Size,
    string Status,
    long FileVersion,
    DateTimeOffset? TrashedAt,
    DateTimeOffset? PurgeEligibleAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FilePage(
    Guid? ParentId,
    IReadOnlyList<FileItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record UploadFileCommand(
    Guid DestinationFolderId,
    string FileName,
    long Size,
    string? ContentType,
    string? Sha256,
    string IdempotencyKey,
    Stream Content);

public sealed record RenameFileCommand(
    Guid OwnerUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    string Name,
    string RequestId);

public sealed record MoveFileCommand(
    Guid OwnerUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    Guid TargetParentId,
    string RequestId);

public enum PurgeTrigger
{
    User,
    RetentionWorker,
}

public sealed record PurgeFileCommand(
    Guid OwnerUserId,
    Guid? ActorDeviceId,
    Guid FileEntryId,
    string IdempotencyKey,
    string RequestId,
    PurgeTrigger Trigger = PurgeTrigger.User);

public sealed record PermanentDeleteTarget(
    Guid RootId,
    Guid OwnerUserId,
    string EntryType,
    RelativeStoragePath TrashContainer,
    IReadOnlyList<Guid> DescendantIds,
    long TotalSize);

public sealed class TrashPurgeOptions
{
    public const string SectionName = "TrashPurge";
    public const int MinimumRetentionDays = 30;
    public int RetentionDays { get; init; } = 30;
    public int IntervalHours { get; init; } = 24;
    public int BatchSize { get; init; } = 100;
    public int RetryDelayMinutes { get; init; } = 15;
}

public sealed class UnsafeStorageTreeException(string message) : IOException(message);

public sealed record DownloadFile(FileItem Item, Stream Content);

public enum FileFailureKind
{
    BadRequest,
    NotFound,
    Conflict,
    Unprocessable,
    PayloadTooLarge,
    TooManyRequests,
    StorageUnavailable,
    CapacityInsufficient,
}

public sealed record FileFailure(string Code, FileFailureKind Kind);

public sealed class FileResult<T>
{
    private FileResult(T? value, FileFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public FileFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static FileResult<T> Success(T value) => new(value, null);

    public static FileResult<T> Fail(string code, FileFailureKind kind) => new(default, new FileFailure(code, kind));
}

public static class FileErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string FileNameConflict = "FILE_NAME_CONFLICT";
    public const string FileRestoreConflict = "FILE_RESTORE_CONFLICT";
    public const string FileMoveCycle = "FILE_MOVE_CYCLE";
    public const string FileOperationNotAllowed = "FILE_OPERATION_NOT_ALLOWED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string UploadSizeMismatch = "UPLOAD_SIZE_MISMATCH";
    public const string UploadChecksumMismatch = "UPLOAD_CHECKSUM_MISMATCH";
    public const string StorageUnavailable = "STORAGE_UNAVAILABLE";
    public const string StorageCapacityInsufficient = "STORAGE_CAPACITY_INSUFFICIENT";
    public const string RecoveryRequired = "RECOVERY_REQUIRED";
    public const string UploadSessionNotFound = "UPLOAD_SESSION_NOT_FOUND";
    public const string UploadOffsetMismatch = "UPLOAD_OFFSET_MISMATCH";
    public const string UploadIncomplete = "UPLOAD_INCOMPLETE";
    public const string UploadSessionExpired = "UPLOAD_SESSION_EXPIRED";
    public const string UploadSessionCancelled = "UPLOAD_SESSION_CANCELLED";
    public const string UploadSessionCompleted = "UPLOAD_SESSION_COMPLETED";
    public const string ChunkChecksumMismatch = "CHUNK_CHECKSUM_MISMATCH";
    public const string ChunkSizeLimitExceeded = "CHUNK_SIZE_LIMIT_EXCEEDED";
    public const string FileSizeLimitExceeded = "FILE_SIZE_LIMIT_EXCEEDED";
    public const string UploadLimitReached = "UPLOAD_LIMIT_REACHED";
}
