using KuraStorage.Application.Files;

namespace KuraStorage.Application.Search;

public enum FileCategory
{
    Image,
    Video,
    Audio,
    Document,
    Archive,
    Other,
}

public enum SearchMatchMode
{
    None,
    Prefix,
    Contains,
}

public sealed record SearchQuery(
    string? Text = null,
    string? EntryType = null,
    string? FileCategory = null,
    string? Status = null,
    DateTimeOffset? UpdatedFrom = null,
    DateTimeOffset? UpdatedTo = null,
    long? MinSize = null,
    long? MaxSize = null,
    Guid? OwnerUserId = null,
    Guid? ShareTargetId = null,
    int Page = 1,
    int PageSize = 50,
    IReadOnlyList<Guid>? TagIds = null);

public sealed record SearchFilter(
    string? NormalizedText,
    string? EscapedPattern,
    SearchMatchMode MatchMode,
    string? EntryType,
    FileCategory? FileCategory,
    string? Status,
    DateTimeOffset? UpdatedFrom,
    DateTimeOffset? UpdatedTo,
    long? MinSize,
    long? MaxSize,
    Guid? OwnerUserId,
    Guid? ShareTargetId,
    int Page,
    int PageSize,
    IReadOnlyList<Guid> TagIds);

public sealed record SearchResultItem(
    Guid Id,
    string EntryType,
    string Name,
    string? MimeType,
    string? FileCategory,
    long Size,
    string Status,
    DateTimeOffset UpdatedAt,
    FileOwnerItem Owner,
    string Permission,
    string PermissionSource,
    Guid? ShareTargetId);

public sealed record SearchPage(
    IReadOnlyList<SearchResultItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public enum SearchFailureKind
{
    InvalidQuery,
    InvalidFilter,
}

public sealed record SearchFailure(string Code, SearchFailureKind Kind);

public sealed class SearchResult<T>
{
    private SearchResult(T? value, SearchFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public SearchFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static SearchResult<T> Success(T value) => new(value, null);

    public static SearchResult<T> Fail(string code, SearchFailureKind kind) =>
        new(default, new SearchFailure(code, kind));
}

public static class SearchErrorCodes
{
    public const string InvalidQuery = "INVALID_SEARCH_QUERY";
    public const string InvalidFilter = "INVALID_SEARCH_FILTER";
}
