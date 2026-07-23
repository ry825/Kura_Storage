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

public sealed record DownloadFile(FileItem Item, Stream Content);

public enum FileFailureKind
{
    BadRequest,
    NotFound,
    Conflict,
    Unprocessable,
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
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string UploadSizeMismatch = "UPLOAD_SIZE_MISMATCH";
    public const string UploadChecksumMismatch = "UPLOAD_CHECKSUM_MISMATCH";
    public const string StorageUnavailable = "STORAGE_UNAVAILABLE";
    public const string StorageCapacityInsufficient = "STORAGE_CAPACITY_INSUFFICIENT";
    public const string RecoveryRequired = "RECOVERY_REQUIRED";
}
