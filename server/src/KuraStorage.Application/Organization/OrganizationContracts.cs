namespace KuraStorage.Application.Organization;

public sealed record FavoriteItem(
    Guid Id,
    string EntryType,
    string Name,
    string? MimeType,
    string? FileCategory,
    long Size,
    string Status,
    DateTimeOffset UpdatedAt,
    Files.FileOwnerItem Owner,
    string Permission,
    string PermissionSource,
    Guid? ShareTargetId,
    DateTimeOffset FavoritedAt);

public sealed record FavoritePage(
    IReadOnlyList<FavoriteItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record TagItem(Guid Id, string Name);

public sealed record EntryOrganizationState(bool IsFavorite, IReadOnlyList<TagItem> Tags);

public sealed record CreateTagCommand(string Name);

public sealed record RenameTagCommand(Guid TagId, string Name);

public enum OrganizationFailureKind
{
    InvalidRequest,
    NotFound,
    Conflict,
}

public sealed record OrganizationFailure(string Code, OrganizationFailureKind Kind);

public sealed class OrganizationResult<T>
{
    private OrganizationResult(T? value, OrganizationFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public OrganizationFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static OrganizationResult<T> Success(T value) => new(value, null);

    public static OrganizationResult<T> Fail(string code, OrganizationFailureKind kind) =>
        new(default, new OrganizationFailure(code, kind));
}

public static class OrganizationErrorCodes
{
    public const string InvalidOrganizationRequest = "INVALID_ORGANIZATION_REQUEST";
    public const string InvalidFavoritesRequest = "INVALID_FAVORITES_REQUEST";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string TagNotFound = "TAG_NOT_FOUND";
    public const string TagNameConflict = "TAG_NAME_CONFLICT";
    public const string TagLimitExceeded = "TAG_LIMIT_EXCEEDED";
    public const string EntryTagLimitExceeded = "ENTRY_TAG_LIMIT_EXCEEDED";
}

public enum OrganizationRepositoryOutcome
{
    Created,
    NoChange,
    NotFound,
    Conflict,
    UserLimitExceeded,
    EntryLimitExceeded,
}

public sealed record OrganizationRepositoryResult<T>(OrganizationRepositoryOutcome Outcome, T? Value = default);
