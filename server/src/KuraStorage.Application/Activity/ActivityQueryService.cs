using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Activity;

namespace KuraStorage.Application.Activity;

public sealed class ActivityQueryService(IUserActivityQueryRepository repository)
{
    public const int MaximumPageSize = 100;

    public async Task<ActivityQueryResult<ActivityPage>> ListAsync(
        Guid actorUserId,
        ActivityListRequest request,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor user ID is required.", nameof(actorUserId));
        }

        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            return ActivityQueryResult<ActivityPage>.Fail(validation.Failure!.Code);
        }

        var filter = validation.Value!;
        var records = await repository.ListAsync(actorUserId, filter with { Limit = filter.Limit + 1 }, cancellationToken);
        return ActivityQueryResult<ActivityPage>.Success(CreatePage(records, filter.Limit));
    }

    public static ActivityQueryResult<ActivityQueryFilter> Validate(ActivityListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PageSize is < 1 or > MaximumPageSize ||
            !TryType(request.Type, out var type) ||
            !TryCursor(request.Cursor, out var cursor))
        {
            return ActivityQueryResult<ActivityQueryFilter>.Fail(ActivityQueryErrorCodes.InvalidRequest);
        }

        return ActivityQueryResult<ActivityQueryFilter>.Success(new ActivityQueryFilter(type, cursor, request.PageSize));
    }

    internal static ActivityPage CreatePage(IReadOnlyList<ActivityRecord> records, int limit)
    {
        var selected = records.Take(limit).ToArray();
        var nextCursor = records.Count > limit && selected.Length > 0
            ? ActivityCursorCodec.Encode(new ActivityCursor(selected[^1].OccurredAt, selected[^1].Id))
            : null;
        return new ActivityPage(selected.Select(ToItem).ToArray(), nextCursor);
    }

    internal static bool TryType(string? value, out UserActivityType? type)
    {
        type = null;
        if (value is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<UserActivityType>(value.Trim(), true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        type = parsed;
        return true;
    }

    internal static bool TryCursor(string? value, out ActivityCursor? cursor)
    {
        cursor = null;
        return value is null || ActivityCursorCodec.TryDecode(value, out cursor);
    }

    internal static ActivityItem ToItem(ActivityRecord item) => new(
        item.Type.ToString().ToUpperInvariant(),
        item.OccurredAt,
        item.ActorDisplayName,
        item.ActorDeviceName,
        item.TargetEntryId,
        item.TargetType.ToString().ToUpperInvariant(),
        item.TargetName,
        item.OwnerDisplayName,
        item.SourceParentName,
        item.DestinationParentName,
        item.ResultingFileVersion,
        EnumText(item.EditKind),
        item.RecipientDisplayName,
        item.SharePermission,
        EnumText(item.ShareAction),
        EnumText(item.DeleteKind));

    private static string? EnumText<T>(T? value) where T : struct, Enum =>
        value?.ToString().ToUpperInvariant() switch
        {
            "TEXTSAVE" => "TEXT_SAVE",
            "VERSIONRESTORE" => "VERSION_RESTORE",
            var text => text,
        };
}

public sealed class AdminActivityService(
    IUserActivityAdminQueryRepository repository,
    ISystemClock clock)
{
    public async Task<ActivityQueryResult<AdminActivityPage>> SearchAsync(
        AdminActivitySearchRequest request,
        string actorOsUser,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.IsSuccess || string.IsNullOrWhiteSpace(actorOsUser) ||
            actorOsUser.Length > 128 || actorOsUser.Any(char.IsControl))
        {
            return ActivityQueryResult<AdminActivityPage>.Fail(ActivityQueryErrorCodes.InvalidRequest);
        }

        var filter = validation.Value!;
        var records = await repository.SearchAsync(
            filter,
            actorOsUser,
            clock.UtcNow.ToUniversalTime(),
            cancellationToken);
        if (records is null)
        {
            return ActivityQueryResult<AdminActivityPage>.Fail(ActivityQueryErrorCodes.InvalidRequest);
        }

        var selected = records.Take(filter.Limit).ToArray();
        var next = records.Count > filter.Limit && selected.Length > 0
            ? ActivityCursorCodec.Encode(new ActivityCursor(selected[^1].OccurredAt, selected[^1].Id))
            : null;
        return ActivityQueryResult<AdminActivityPage>.Success(
            new AdminActivityPage(selected.Select(ActivityQueryService.ToItem).ToArray(), next));
    }

    public static ActivityQueryResult<AdminActivitySearchFilter> Validate(AdminActivitySearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var from = request.From?.ToUniversalTime();
        var to = request.To?.ToUniversalTime();
        if (request.Limit is < 1 or > 1000 ||
            request.FileId == Guid.Empty ||
            from > to ||
            to - from > TimeSpan.FromDays(365) ||
            !ValidSelector(request.ActorUser) ||
            !ValidSelector(request.OwnerUser) ||
            !ActivityQueryService.TryType(request.Type, out var type) ||
            !ActivityQueryService.TryCursor(request.Cursor, out var cursor))
        {
            return ActivityQueryResult<AdminActivitySearchFilter>.Fail(ActivityQueryErrorCodes.InvalidRequest);
        }

        return ActivityQueryResult<AdminActivitySearchFilter>.Success(
            new AdminActivitySearchFilter(request.ActorUser, request.OwnerUser, type, from, to, request.FileId, request.Limit, cursor));
    }

    private static bool ValidSelector(string? value) =>
        value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl));
}
