using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Activity;

public sealed class UserActivityFactory(
    IUserActivityRepository repository,
    ISystemClock clock)
{
    public async Task<UserActivity?> AddUploadAsync(
        Guid operationId,
        Guid actorUserId,
        Guid actorDeviceId,
        FileEntry target,
        long resultingFileVersion,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(
            operationId,
            target.Id,
            UserActivityType.Upload,
            activity => activity.ResultingFileVersion == resultingFileVersion,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var activity = UserActivity.CreateUpload(
            await CreateContextAsync(operationId, actorUserId, actorDeviceId, target, cancellationToken),
            resultingFileVersion);
        repository.Add(activity);
        return activity;
    }

    public async Task<UserActivity?> AddMoveAsync(
        Guid operationId,
        Guid actorUserId,
        Guid actorDeviceId,
        FileEntry target,
        FileEntry sourceParent,
        FileEntry destinationParent,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(
            operationId,
            target.Id,
            UserActivityType.Move,
            activity =>
                activity.SourceParentId == sourceParent.Id &&
                activity.DestinationParentId == destinationParent.Id,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        EnsureFolder(sourceParent);
        EnsureFolder(destinationParent);
        var activity = UserActivity.CreateMove(
            await CreateContextAsync(operationId, actorUserId, actorDeviceId, target, cancellationToken),
            new ActivityFolderSnapshot(sourceParent.Id, sourceParent.Name),
            new ActivityFolderSnapshot(destinationParent.Id, destinationParent.Name));
        repository.Add(activity);
        return activity;
    }

    public async Task<UserActivity?> AddEditAsync(
        Guid operationId,
        Guid actorUserId,
        Guid actorDeviceId,
        FileEntry target,
        long resultingFileVersion,
        ActivityEditKind editKind,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(
            operationId,
            target.Id,
            UserActivityType.Edit,
            activity =>
                activity.ResultingFileVersion == resultingFileVersion &&
                activity.EditKind == editKind,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var activity = UserActivity.CreateEdit(
            await CreateContextAsync(operationId, actorUserId, actorDeviceId, target, cancellationToken),
            resultingFileVersion,
            editKind);
        repository.Add(activity);
        return activity;
    }

    public async Task<UserActivity?> AddShareAsync(
        Guid operationId,
        Guid actorUserId,
        Guid actorDeviceId,
        FileEntry target,
        Guid recipientUserId,
        SharePermission permission,
        ActivityShareAction action,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(
            operationId,
            target.Id,
            UserActivityType.Share,
            activity =>
                activity.RecipientUserId == recipientUserId &&
                activity.SharePermission == permission &&
                activity.ShareAction == action,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var recipient = await repository.FindUserAsync(recipientUserId, cancellationToken)
            ?? throw new ActivitySnapshotUnavailableException();
        var activity = UserActivity.CreateShare(
            await CreateContextAsync(operationId, actorUserId, actorDeviceId, target, cancellationToken),
            new ActivityRecipientSnapshot(recipient.Id, recipient.DisplayName),
            permission,
            action);
        repository.Add(activity);
        return activity;
    }

    public async Task<UserActivity?> AddDeleteAsync(
        Guid operationId,
        Guid actorUserId,
        Guid actorDeviceId,
        FileEntry target,
        ActivityDeleteKind deleteKind,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(
            operationId,
            target.Id,
            UserActivityType.Delete,
            activity => activity.DeleteKind == deleteKind,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var activity = UserActivity.CreateDelete(
            await CreateContextAsync(operationId, actorUserId, actorDeviceId, target, cancellationToken),
            deleteKind);
        repository.Add(activity);
        return activity;
    }

    public async Task<UserActivity?> AddSystemDeleteAsync(
        Guid operationId,
        FileEntry target,
        ActivityDeleteKind deleteKind,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(
            operationId,
            target.Id,
            UserActivityType.Delete,
            activity => activity.DeleteKind == deleteKind,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var owner = await repository.FindUserAsync(target.OwnerUserId, cancellationToken)
            ?? throw new ActivitySnapshotUnavailableException();
        var activity = UserActivity.CreateDelete(
            new UserActivityContext(
                Guid.NewGuid(),
                operationId,
                new ActivityActorSnapshot(null, "System", null),
                CreateTargetSnapshot(target, owner),
                clock.UtcNow),
            deleteKind);
        repository.Add(activity);
        return activity;
    }

    private async Task<UserActivityContext> CreateContextAsync(
        Guid operationId,
        Guid actorUserId,
        Guid actorDeviceId,
        FileEntry target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var actor = await repository.FindUserAsync(actorUserId, cancellationToken);
        var device = await repository.FindDeviceAsync(actorDeviceId, cancellationToken);
        var owner = target.OwnerUserId == actorUserId
            ? actor
            : await repository.FindUserAsync(target.OwnerUserId, cancellationToken);
        if (actor is null || owner is null || device is null || device.UserId != actorUserId)
        {
            throw new ActivitySnapshotUnavailableException();
        }

        return new UserActivityContext(
            Guid.NewGuid(),
            operationId,
            new ActivityActorSnapshot(actor.Id, actor.DisplayName, device.DeviceName),
            CreateTargetSnapshot(target, owner),
            clock.UtcNow);
    }

    private static ActivityTargetSnapshot CreateTargetSnapshot(FileEntry target, User owner) =>
        new(
            target.Id,
            target.EntryType,
            target.Name,
            owner.Id,
            owner.DisplayName,
            target.ParentId);

    private async Task<UserActivity?> FindExistingAsync(
        Guid operationId,
        Guid targetEntryId,
        UserActivityType expectedType,
        Func<UserActivity, bool> matchesDetail,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(operationId));
        }

        var existing = await repository.FindByOperationIdAsync(operationId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.ActivityType != expectedType ||
            (existing.TargetEntryId is not null && existing.TargetEntryId != targetEntryId) ||
            !matchesDetail(existing))
        {
            throw new ActivityIdempotencyConflictException();
        }

        return existing;
    }

    private static void EnsureFolder(FileEntry entry)
    {
        if (entry.EntryType != FileEntryType.Folder)
        {
            throw new ArgumentException("An activity folder snapshot must reference a folder.", nameof(entry));
        }
    }
}

public sealed class ActivitySnapshotUnavailableException : Exception
{
    public ActivitySnapshotUnavailableException()
        : base("A required activity snapshot is unavailable.")
    {
    }
}

public sealed class ActivityIdempotencyConflictException : Exception
{
    public ActivityIdempotencyConflictException()
        : base("The activity operation ID conflicts with an existing record.")
    {
    }
}
