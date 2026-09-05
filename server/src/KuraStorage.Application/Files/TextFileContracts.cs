using System.Text;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed record TextDocument(
    string Content,
    string Encoding,
    long FileVersion,
    long Size,
    string Sha256,
    string DecodeStatus = "EXACT");

public sealed record SaveTextFileCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    string? Content,
    long ExpectedVersion,
    Guid OperationId,
    string RequestId,
    bool AcknowledgeLossySource = false);

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
    public const string LossySourceAcknowledgementRequired = "LOSSY_SOURCE_ACKNOWLEDGEMENT_REQUIRED";
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
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, false, true);

    public static bool TryEncode(string? content, out byte[] encoded) =>
        TryEncode(content, out encoded, out _);

    public static bool TryEncode(
        string? content,
        out byte[] encoded,
        out TextEncodingFailure failure)
    {
        encoded = [];
        if (!TryNormalize(content, out var normalized, out failure))
        {
            return false;
        }

        var byteCount = StrictUtf8.GetByteCount(normalized);
        if (byteCount > FileVersionRecord.MaximumContentBytes)
        {
            failure = TextEncodingFailure.SizeLimitExceeded;
            return false;
        }

        encoded = StrictUtf8.GetBytes(normalized);
        return true;
    }

    public static bool TryNormalize(
        string? content,
        out string normalized,
        out TextEncodingFailure failure)
    {
        normalized = string.Empty;
        failure = TextEncodingFailure.None;
        if (content is null)
        {
            failure = TextEncodingFailure.MissingContent;
            return false;
        }

        normalized = content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;
        try
        {
            _ = StrictUtf8.GetByteCount(normalized);
            return true;
        }
        catch (EncoderFallbackException)
        {
            failure = TextEncodingFailure.InvalidEncoding;
            return false;
        }
    }

    public static byte[] Encode(string content, string sourceEncoding)
    {
        var normalized = content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;
        if (sourceEncoding == "UTF-16LE" || sourceEncoding == "UTF-16BE")
        {
            var encoding = sourceEncoding == "UTF-16LE" ? StrictUtf16Le : StrictUtf16Be;
            var body = encoding.GetBytes(normalized);
            var preamble = sourceEncoding == "UTF-16LE" ? new byte[] { 0xff, 0xfe } : [0xfe, 0xff];
            return [.. preamble, .. body];
        }

        return StrictUtf8.GetBytes(normalized);
    }

    public static TextDecodeResult Decode(byte[] bytes)
    {
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            {
                return new TextDecodeResult(StrictUtf8.GetString(bytes, 3, bytes.Length - 3), "UTF-8", "EXACT");
            }
            if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
            {
                return new TextDecodeResult(StrictUtf16Le.GetString(bytes, 2, bytes.Length - 2), "UTF-16LE", "EXACT");
            }
            if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
            {
                return new TextDecodeResult(StrictUtf16Be.GetString(bytes, 2, bytes.Length - 2), "UTF-16BE", "EXACT");
            }

            return new TextDecodeResult(StrictUtf8.GetString(bytes), "UTF-8", "EXACT");
        }
        catch (DecoderFallbackException)
        {
            return new TextDecodeResult(Encoding.UTF8.GetString(bytes), "UTF-8", "LOSSY");
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

public sealed record TextDecodeResult(string Content, string Encoding, string DecodeStatus);
