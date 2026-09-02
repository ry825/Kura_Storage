using KuraStorage.Domain.Activity;

namespace KuraStorage.Application.Activity;

public sealed record ActivityListRequest(string? Type = null, string? Cursor = null, int PageSize = 50);

public sealed record ActivityCursor(DateTimeOffset OccurredAt, Guid Id);

public sealed record ActivityQueryFilter(UserActivityType? Type, ActivityCursor? Cursor, int Limit);

public sealed record ActivityRecord(
    Guid Id,
    UserActivityType Type,
    DateTimeOffset OccurredAt,
    string ActorDisplayName,
    string? ActorDeviceName,
    Guid? TargetEntryId,
    ActivityTargetType TargetType,
    string TargetName,
    string OwnerDisplayName,
    string? SourceParentName,
    string? DestinationParentName,
    long? ResultingFileVersion,
    ActivityEditKind? EditKind,
    string? RecipientDisplayName,
    string? SharePermission,
    ActivityShareAction? ShareAction,
    ActivityDeleteKind? DeleteKind);

public sealed record ActivityItem(
    string Type,
    DateTimeOffset OccurredAt,
    string ActorDisplayName,
    string? ActorDeviceName,
    Guid? TargetEntryId,
    string TargetType,
    string TargetName,
    string OwnerDisplayName,
    string? SourceParentName,
    string? DestinationParentName,
    long? ResultingFileVersion,
    string? EditKind,
    string? RecipientDisplayName,
    string? SharePermission,
    string? ShareAction,
    string? DeleteKind);

public sealed record ActivityPage(IReadOnlyList<ActivityItem> Items, string? NextCursor);

public sealed record AdminActivitySearchRequest(
    string? ActorUser = null,
    string? OwnerUser = null,
    string? Type = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? FileId = null,
    int Limit = 100,
    string? Cursor = null);

public sealed record AdminActivitySearchFilter(
    string? ActorUser,
    string? OwnerUser,
    UserActivityType? Type,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? FileId,
    int Limit,
    ActivityCursor? Cursor);

public sealed record AdminActivityPage(IReadOnlyList<ActivityItem> Items, string? NextCursor);

public enum ActivityQueryFailureKind
{
    InvalidRequest,
}

public sealed record ActivityQueryFailure(string Code, ActivityQueryFailureKind Kind);

public sealed class ActivityQueryResult<T>
{
    private ActivityQueryResult(T? value, ActivityQueryFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public ActivityQueryFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static ActivityQueryResult<T> Success(T value) => new(value, null);

    public static ActivityQueryResult<T> Fail(string code) =>
        new(default, new ActivityQueryFailure(code, ActivityQueryFailureKind.InvalidRequest));
}

public static class ActivityQueryErrorCodes
{
    public const string InvalidRequest = "INVALID_ACTIVITY_REQUEST";
}
