using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Sharing;

public enum ShareScope
{
    Owned,
    Received,
}

public sealed record ShareCandidate(Guid UserId, string DisplayName);

public sealed record ShareOwner(Guid Id, string DisplayName);

public sealed record ShareMemberItem(Guid UserId, string DisplayName, string Permission);

public sealed record ShareItem(
    Guid Id,
    Guid TargetEntryId,
    string EntryType,
    string Name,
    ShareOwner Owner,
    string? Permission,
    IReadOnlyList<ShareMemberItem> Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SharePage(
    IReadOnlyList<ShareItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ShareMemberInput(Guid UserId, SharePermission Permission);

public sealed record CreateShareCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid TargetEntryId,
    IReadOnlyList<ShareMemberInput> Members,
    string RequestId);

public sealed record SetShareMemberCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid ShareId,
    Guid MemberUserId,
    SharePermission Permission,
    string RequestId);

public sealed record RemoveShareMemberCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid ShareId,
    Guid MemberUserId,
    string RequestId);

public sealed record DeleteShareCommand(
    Guid ActorUserId,
    Guid ActorDeviceId,
    Guid ShareId,
    string RequestId);

public sealed record ShareView(
    Guid Id,
    Guid TargetEntryId,
    FileEntryType EntryType,
    string Name,
    Guid OwnerUserId,
    string OwnerDisplayName,
    IReadOnlyList<ShareViewMember> Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ShareViewMember(Guid UserId, string DisplayName, SharePermission Permission);

public enum SharingFailureKind
{
    BadRequest,
    NotFound,
    Conflict,
}

public sealed record SharingFailure(string Code, SharingFailureKind Kind);

public sealed class SharingResult<T>
{
    private SharingResult(T? value, SharingFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public SharingFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static SharingResult<T> Success(T value) => new(value, null);

    public static SharingResult<T> Fail(string code, SharingFailureKind kind) =>
        new(default, new SharingFailure(code, kind));
}

public static class SharingErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidSharePermission = "INVALID_SHARE_PERMISSION";
    public const string ShareNotFound = "SHARE_NOT_FOUND";
    public const string ShareMemberNotFound = "SHARE_MEMBER_NOT_FOUND";
    public const string ShareConflict = "SHARE_CONFLICT";
    public const string ShareOperationNotAllowed = "SHARE_OPERATION_NOT_ALLOWED";
}
