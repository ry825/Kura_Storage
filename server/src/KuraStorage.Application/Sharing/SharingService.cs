using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Sharing;

public sealed class SharingService(
    IShareRepository repository,
    IAuthorizationService authorizationService,
    ISystemClock clock)
{
    public async Task<SharingResult<IReadOnlyList<ShareCandidate>>> ListCandidatesAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return SharingResult<IReadOnlyList<ShareCandidate>>.Fail(
                SharingErrorCodes.ValidationFailed,
                SharingFailureKind.BadRequest);
        }

        return SharingResult<IReadOnlyList<ShareCandidate>>.Success(
            await repository.ListCandidatesAsync(actorUserId, cancellationToken));
    }

    public async Task<SharingResult<ShareItem>> CreateAsync(
        CreateShareCommand command,
        CancellationToken cancellationToken)
    {
        if (!ValidActor(command.ActorUserId, command.ActorDeviceId, command.RequestId) ||
            command.TargetEntryId == Guid.Empty ||
            command.Members.Count == 0 ||
            command.Members.Any(member => member.UserId == Guid.Empty || !Enum.IsDefined(member.Permission)) ||
            command.Members.Select(member => member.UserId).Distinct().Count() != command.Members.Count)
        {
            return Fail<ShareItem>(SharingErrorCodes.ValidationFailed, SharingFailureKind.BadRequest);
        }

        var target = await repository.FindEntryAsync(command.TargetEntryId, cancellationToken);
        if (!ValidShareTarget(target) || target!.OwnerUserId != command.ActorUserId)
        {
            return Fail<ShareItem>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound);
        }

        if (await repository.FindByTargetAsync(target.Id, cancellationToken) is not null)
        {
            return Fail<ShareItem>(SharingErrorCodes.ShareConflict, SharingFailureKind.Conflict);
        }

        foreach (var member in command.Members)
        {
            var failure = await ValidateMemberAsync(target, member.UserId, member.Permission, cancellationToken);
            if (failure is not null)
            {
                return Fail<ShareItem>(failure.Code, failure.Kind);
            }
        }

        var now = clock.UtcNow;
        var share = new Share(Guid.NewGuid(), target.Id, target.OwnerUserId, now);
        foreach (var member in command.Members)
        {
            share.AddMember(member.UserId, member.Permission, now);
        }

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        repository.Add(share);
        repository.Add(CreateAudit(command.ActorUserId, command.ActorDeviceId, "SHARE_CREATE", share.Id, command.RequestId, now));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SharePersistenceConflictException)
        {
            return Fail<ShareItem>(SharingErrorCodes.ShareConflict, SharingFailureKind.Conflict);
        }

        return await LoadItemAsync(share.Id, command.ActorUserId, cancellationToken);
    }

    public async Task<SharingResult<SharePage>> ListAsync(
        Guid actorUserId,
        ShareScope scope,
        FileEntryType? targetType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || !Enum.IsDefined(scope) ||
            targetType is not null && !Enum.IsDefined(targetType.Value) ||
            page < 1 || pageSize is < 1 or > 500)
        {
            return Fail<SharePage>(SharingErrorCodes.ValidationFailed, SharingFailureKind.BadRequest);
        }

        var skip = checked((page - 1) * pageSize);
        var views = await repository.ListViewsAsync(
            actorUserId, scope, targetType, skip, pageSize, cancellationToken);
        var count = await repository.CountViewsAsync(actorUserId, scope, targetType, cancellationToken);
        var permissions = await authorizationService.ResolveBatchAsync(
            actorUserId,
            views.Select(view => view.TargetEntryId).ToArray(),
            cancellationToken);
        return SharingResult<SharePage>.Success(
            new SharePage(
                views.Select(view => Map(view, actorUserId, permissions[view.TargetEntryId], false)).ToArray(),
                page,
                pageSize,
                count));
    }

    public async Task<SharingResult<ShareItem>> GetAsync(
        Guid actorUserId,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || shareId == Guid.Empty)
        {
            return Fail<ShareItem>(SharingErrorCodes.ValidationFailed, SharingFailureKind.BadRequest);
        }

        var share = await repository.FindByIdAsync(shareId, cancellationToken);
        if (share is null || !CanView(share, actorUserId))
        {
            return Fail<ShareItem>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound);
        }

        return await LoadItemAsync(shareId, actorUserId, cancellationToken);
    }

    public async Task<SharingResult<ShareItem>> SetMemberAsync(
        SetShareMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (!ValidActor(command.ActorUserId, command.ActorDeviceId, command.RequestId) ||
            command.ShareId == Guid.Empty ||
            command.MemberUserId == Guid.Empty ||
            !Enum.IsDefined(command.Permission))
        {
            return Fail<ShareItem>(SharingErrorCodes.ValidationFailed, SharingFailureKind.BadRequest);
        }

        var share = await repository.FindByIdAsync(command.ShareId, cancellationToken);
        if (share is null || !CanManage(share, command.ActorUserId))
        {
            return Fail<ShareItem>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound);
        }

        var target = await repository.FindEntryAsync(share.TargetEntryId, cancellationToken);
        if (!ValidShareTarget(target) || target!.OwnerUserId != share.OwnerUserId)
        {
            return Fail<ShareItem>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound);
        }

        var failure = await ValidateMemberAsync(target, command.MemberUserId, command.Permission, cancellationToken);
        if (failure is not null)
        {
            return Fail<ShareItem>(failure.Code, failure.Kind);
        }

        var now = clock.UtcNow;
        if (share.Members.Any(member => member.UserId == command.MemberUserId))
        {
            share.SetMemberPermission(command.MemberUserId, command.Permission, now);
        }
        else
        {
            share.AddMember(command.MemberUserId, command.Permission, now);
        }

        var saved = await SaveMutationAsync(
            share,
            command.ActorUserId,
            command.ActorDeviceId,
            "SHARE_MEMBER_SET",
            command.RequestId,
            now,
            cancellationToken);
        return saved
            ? await LoadItemAsync(share.Id, command.ActorUserId, cancellationToken)
            : Fail<ShareItem>(SharingErrorCodes.ShareConflict, SharingFailureKind.Conflict);
    }

    public async Task<SharingResult<bool>> RemoveMemberAsync(
        RemoveShareMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (!ValidActor(command.ActorUserId, command.ActorDeviceId, command.RequestId) ||
            command.ShareId == Guid.Empty || command.MemberUserId == Guid.Empty)
        {
            return Fail<bool>(SharingErrorCodes.ValidationFailed, SharingFailureKind.BadRequest);
        }

        var share = await repository.FindByIdAsync(command.ShareId, cancellationToken);
        if (share is null || !CanManage(share, command.ActorUserId))
        {
            return Fail<bool>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound);
        }

        if (!share.Members.Any(member => member.UserId == command.MemberUserId))
        {
            return Fail<bool>(SharingErrorCodes.ShareMemberNotFound, SharingFailureKind.NotFound);
        }

        var now = clock.UtcNow;
        var empty = share.RemoveMember(command.MemberUserId, now);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        if (empty)
        {
            repository.Remove(share);
        }

        repository.Add(CreateAudit(
            command.ActorUserId, command.ActorDeviceId, "SHARE_MEMBER_REMOVE", share.Id, command.RequestId, now));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SharingResult<bool>.Success(true);
        }
        catch (SharePersistenceConflictException)
        {
            return Fail<bool>(SharingErrorCodes.ShareConflict, SharingFailureKind.Conflict);
        }
    }

    public async Task<SharingResult<bool>> DeleteAsync(
        DeleteShareCommand command,
        CancellationToken cancellationToken)
    {
        if (!ValidActor(command.ActorUserId, command.ActorDeviceId, command.RequestId) || command.ShareId == Guid.Empty)
        {
            return Fail<bool>(SharingErrorCodes.ValidationFailed, SharingFailureKind.BadRequest);
        }

        var share = await repository.FindByIdAsync(command.ShareId, cancellationToken);
        if (share is null || !CanManage(share, command.ActorUserId))
        {
            return Fail<bool>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound);
        }

        var now = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        repository.Remove(share);
        repository.Add(CreateAudit(
            command.ActorUserId, command.ActorDeviceId, "SHARE_DELETE", share.Id, command.RequestId, now));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SharingResult<bool>.Success(true);
        }
        catch (SharePersistenceConflictException)
        {
            return Fail<bool>(SharingErrorCodes.ShareConflict, SharingFailureKind.Conflict);
        }
    }

    private async Task<SharingFailure?> ValidateMemberAsync(
        FileEntry target,
        Guid memberUserId,
        SharePermission permission,
        CancellationToken cancellationToken)
    {
        if (target.EntryType == FileEntryType.File && permission == SharePermission.Contributor)
        {
            return new SharingFailure(SharingErrorCodes.InvalidSharePermission, SharingFailureKind.BadRequest);
        }

        if (memberUserId == target.OwnerUserId)
        {
            return new SharingFailure(SharingErrorCodes.ShareOperationNotAllowed, SharingFailureKind.Conflict);
        }

        var user = await repository.FindUserAsync(memberUserId, cancellationToken);
        return user is null || user.Status != UserStatus.Active
            ? new SharingFailure(SharingErrorCodes.ShareOperationNotAllowed, SharingFailureKind.Conflict)
            : null;
    }

    private async Task<bool> SaveMutationAsync(
        Share share,
        Guid actorUserId,
        Guid actorDeviceId,
        string action,
        string requestId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        repository.Add(CreateAudit(actorUserId, actorDeviceId, action, share.Id, requestId, now));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (SharePersistenceConflictException)
        {
            return false;
        }
    }

    private async Task<SharingResult<ShareItem>> LoadItemAsync(
        Guid shareId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var view = await repository.GetViewAsync(shareId, cancellationToken);
        return view is null
            ? Fail<ShareItem>(SharingErrorCodes.ShareNotFound, SharingFailureKind.NotFound)
            : SharingResult<ShareItem>.Success(
                Map(
                    view,
                    actorUserId,
                    await authorizationService.ResolveAsync(actorUserId, view.TargetEntryId, cancellationToken)));
    }

    private static bool ValidShareTarget(FileEntry? target) =>
        target is { Status: FileEntryStatus.Active, ParentId: not null } &&
        target.EntryType is FileEntryType.File or FileEntryType.Folder;

    private static bool ValidActor(Guid actorUserId, Guid actorDeviceId, string requestId) =>
        actorUserId != Guid.Empty && actorDeviceId != Guid.Empty && !string.IsNullOrWhiteSpace(requestId);

    private static bool CanView(Share share, Guid actorUserId) =>
        share.OwnerUserId == actorUserId || share.Members.Any(member => member.UserId == actorUserId);

    private static bool CanManage(Share share, Guid actorUserId) =>
        share.OwnerUserId == actorUserId ||
        share.Members.Any(member =>
            member.UserId == actorUserId && member.Permission == SharePermission.Manager);

    private static AuditLog CreateAudit(
        Guid actorUserId,
        Guid actorDeviceId,
        string action,
        Guid shareId,
        string requestId,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(), actorUserId, actorDeviceId, null, action, "SHARE", shareId.ToString(),
            "SUCCESS", requestId, now, AuditActorType.UserDevice);

    private static ShareItem Map(
        ShareView view,
        Guid actorUserId,
        EffectivePermission effectivePermission,
        bool includeMembers = true)
    {
        var permission = view.OwnerUserId == actorUserId
            ? SharePermission.Manager.ToString().ToUpperInvariant()
            : effectivePermission.Permission switch
            {
                EffectivePermissionLevel.Viewer => "VIEWER",
                EffectivePermissionLevel.Contributor => "CONTRIBUTOR",
                EffectivePermissionLevel.Editor => "EDITOR",
                EffectivePermissionLevel.Manager => "MANAGER",
                _ => null,
            };
        return new ShareItem(
            view.Id,
            view.TargetEntryId,
            view.EntryType.ToString().ToUpperInvariant(),
            view.Name,
            new ShareOwner(view.OwnerUserId, view.OwnerDisplayName),
            permission,
            includeMembers
                ? view.Members
                    .OrderBy(member => member.DisplayName, StringComparer.Ordinal)
                    .ThenBy(member => member.UserId)
                    .Select(member => new ShareMemberItem(
                        member.UserId,
                        member.DisplayName,
                        member.Permission.ToString().ToUpperInvariant()))
                    .ToArray()
                : [],
            view.CreatedAt,
            view.UpdatedAt);
    }

    private static SharingResult<T> Fail<T>(string code, SharingFailureKind kind) =>
        SharingResult<T>.Fail(code, kind);
}
