using KuraStorage.Application.Files;

namespace KuraStorage.Application.Recent;

public sealed record RecentFileItem(
    Guid Id,
    string EntryType,
    string Name,
    string? MimeType,
    string FileCategory,
    long Size,
    string Status,
    DateTimeOffset UpdatedAt,
    FileOwnerItem Owner,
    string Permission,
    string PermissionSource,
    Guid? ShareTargetId,
    DateTimeOffset OpenedAt);

public sealed record RecentFilePage(
    IReadOnlyList<RecentFileItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public enum RecentFileFailureKind
{
    InvalidRequest,
    NotFound,
}

public sealed record RecentFileFailure(string Code, RecentFileFailureKind Kind);

public sealed class RecentFileResult<T>
{
    private RecentFileResult(T? value, RecentFileFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public RecentFileFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static RecentFileResult<T> Success(T value) => new(value, null);

    public static RecentFileResult<T> Fail(string code, RecentFileFailureKind kind) =>
        new(default, new RecentFileFailure(code, kind));
}

public static class RecentFileErrorCodes
{
    public const string InvalidRequest = "INVALID_RECENT_FILES_REQUEST";
    public const string FileNotFound = "FILE_NOT_FOUND";
}
