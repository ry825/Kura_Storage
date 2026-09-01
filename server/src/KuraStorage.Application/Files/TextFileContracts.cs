using System.Text;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed record TextDocument(
    string Content,
    string Encoding,
    long FileVersion,
    long Size,
    string Sha256);

public sealed record SaveTextFileCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    string? Content,
    long ExpectedVersion,
    Guid OperationId,
    string RequestId);

public sealed record RestoreTextVersionCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    long Version,
    long ExpectedVersion,
    Guid OperationId,
    string RequestId);

public sealed record TextMutationResult(
    long FileVersion,
    long Size,
    string Sha256,
    string ChangeKind,
    DateTimeOffset CreatedAt);

public sealed record FileVersionItem(
    long Version,
    long Size,
    string Sha256,
    string ChangeKind,
    string ActorDisplayName,
    DateTimeOffset CreatedAt);

public sealed record FileVersionPage(
    IReadOnlyList<FileVersionItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public enum TextFileFailureKind
{
    BadRequest,
    NotFound,
    Conflict,
    Unprocessable,
    PayloadTooLarge,
    UnsupportedMediaType,
    StorageUnavailable,
    CapacityInsufficient,
}

public sealed record TextFileFailure(string Code, TextFileFailureKind Kind);

public sealed class TextFileResult<T>
{
    private TextFileResult(T? value, TextFileFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public TextFileFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static TextFileResult<T> Success(T value) => new(value, null);

    public static TextFileResult<T> Fail(string code, TextFileFailureKind kind) =>
        new(default, new TextFileFailure(code, kind));
}

public static class TextFileErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string UnsupportedTextType = "UNSUPPORTED_TEXT_TYPE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string TextEncodingInvalid = "TEXT_ENCODING_INVALID";
    public const string TextSizeLimitExceeded = "TEXT_SIZE_LIMIT_EXCEEDED";
    public const string FileVersionConflict = "FILE_VERSION_CONFLICT";
    public const string FileVersionNotFound = "FILE_VERSION_NOT_FOUND";
    public const string FileVersionCorrupt = "FILE_VERSION_CORRUPT";
    public const string FileStateConflict = "FILE_STATE_CONFLICT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string StorageUnavailable = "STORAGE_UNAVAILABLE";
    public const string StorageCapacityInsufficient = "STORAGE_CAPACITY_INSUFFICIENT";
    public const string RecoveryRequired = "RECOVERY_REQUIRED";
}

public enum TextEncodingFailure
{
    None,
    MissingContent,
    InvalidEncoding,
    SizeLimitExceeded,
}

public static class TextFileRules
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/csv",
        "application/json",
        "application/xml",
        "application/yaml",
    };

    public static bool IsSupportedMimeType(string? mimeType) =>
        mimeType is not null && SupportedMimeTypes.Contains(mimeType);

    public static bool TryEncode(string? content, out byte[] encoded) =>
        TryEncode(content, out encoded, out _);

    public static bool TryEncode(
        string? content,
        out byte[] encoded,
        out TextEncodingFailure failure)
    {
        encoded = [];
        failure = TextEncodingFailure.None;
        if (content is null)
        {
            failure = TextEncodingFailure.MissingContent;
            return false;
        }

        var normalized = content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;
        try
        {
            var byteCount = StrictUtf8.GetByteCount(normalized);
            if (byteCount > FileVersionRecord.MaximumContentBytes)
            {
                failure = TextEncodingFailure.SizeLimitExceeded;
                return false;
            }

            encoded = StrictUtf8.GetBytes(normalized);
            return true;
        }
        catch (EncoderFallbackException)
        {
            failure = TextEncodingFailure.InvalidEncoding;
            return false;
        }
    }

    public static bool ValidPage(int page, int pageSize) =>
        page >= 1 && pageSize is >= 1 and <= 100 && (long)(page - 1) * pageSize <= int.MaxValue;

    public static bool ValidVersion(long version) => version >= 1;

    public static bool ValidMutation(long expectedVersion, Guid operationId) =>
        ValidVersion(expectedVersion) && operationId != Guid.Empty;

    public static string ToContractChangeKind(FileVersionChangeKind value) => value switch
    {
        FileVersionChangeKind.Upload => "UPLOAD",
        FileVersionChangeKind.TextEdit => "TEXT_EDIT",
        FileVersionChangeKind.ExternalChange => "EXTERNAL_CHANGE",
        FileVersionChangeKind.Restore => "RESTORE",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
