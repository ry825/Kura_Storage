using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class UserActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void CreateUpload_PreservesServerSnapshotsAndTypedDetail()
    {
        var context = CreateContext();

        var activity = UserActivity.CreateUpload(context, 7);

        Assert.Equal(UserActivityType.Upload, activity.ActivityType);
        Assert.Equal(UserActivityDetailKind.Upload, activity.DetailKind);
        Assert.Equal(context.OperationId, activity.OperationId);
        Assert.Equal(context.Actor.UserId, activity.ActorUserId);
        Assert.Equal("Actor", activity.ActorDisplayName);
        Assert.Equal("Phone", activity.ActorDeviceName);
        Assert.Equal(context.Target.EntryId, activity.TargetEntryId);
        Assert.Equal(ActivityTargetType.File, activity.TargetType);
        Assert.Equal("notes.txt", activity.TargetName);
        Assert.Equal(context.Target.OwnerUserId, activity.OwnerUserId);
        Assert.Equal("Owner", activity.OwnerDisplayName);
        Assert.Equal(7, activity.ResultingFileVersion);
        Assert.Equal(Now, activity.OccurredAt);
        Assert.Null(activity.EditKind);
        Assert.Null(activity.ShareAction);
        Assert.Null(activity.DeleteKind);
    }

    [Fact]
    public void CreateMove_RequiresAndPreservesSourceAndDestinationSnapshots()
    {
        var activity = UserActivity.CreateMove(
            CreateContext(),
            new ActivityFolderSnapshot(Guid.NewGuid(), "Source"),
            new ActivityFolderSnapshot(Guid.NewGuid(), "Destination"));

        Assert.Equal(UserActivityType.Move, activity.ActivityType);
        Assert.Equal(UserActivityDetailKind.Move, activity.DetailKind);
        Assert.Equal("Source", activity.SourceParentName);
        Assert.Equal("Destination", activity.DestinationParentName);
        Assert.Null(activity.ResultingFileVersion);
    }

    [Fact]
    public void CreateEdit_PreservesOnlyEditDetail()
    {
        var activity = UserActivity.CreateEdit(CreateContext(), 9, ActivityEditKind.VersionRestore);

        Assert.Equal(UserActivityType.Edit, activity.ActivityType);
        Assert.Equal(9, activity.ResultingFileVersion);
        Assert.Equal(ActivityEditKind.VersionRestore, activity.EditKind);
        Assert.Null(activity.SourceParentId);
        Assert.Null(activity.SharePermission);
        Assert.Null(activity.DeleteKind);
    }

    [Fact]
    public void CreateShare_PreservesRecipientPermissionAndAction()
    {
        var recipientId = Guid.NewGuid();
        var activity = UserActivity.CreateShare(
            CreateContext(),
            new ActivityRecipientSnapshot(recipientId, "Recipient"),
            SharePermission.Editor,
            ActivityShareAction.Updated);

        Assert.Equal(UserActivityType.Share, activity.ActivityType);
        Assert.Equal(recipientId, activity.RecipientUserId);
        Assert.Equal("Recipient", activity.RecipientDisplayName);
        Assert.Equal(SharePermission.Editor, activity.SharePermission);
        Assert.Equal(ActivityShareAction.Updated, activity.ShareAction);
        Assert.Null(activity.ResultingFileVersion);
    }

    [Theory]
    [InlineData(ActivityDeleteKind.Trashed)]
    [InlineData(ActivityDeleteKind.Purged)]
    public void CreateDelete_PreservesOnlyDeleteDetail(ActivityDeleteKind kind)
    {
        var activity = UserActivity.CreateDelete(CreateContext(), kind);

        Assert.Equal(UserActivityType.Delete, activity.ActivityType);
        Assert.Equal(kind, activity.DeleteKind);
        Assert.Null(activity.ResultingFileVersion);
        Assert.Null(activity.ShareAction);
    }

    [Fact]
    public void Create_RejectsMissingIdentifiersAndNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(id: Guid.Empty), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(operationId: Guid.Empty), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(actor: new ActivityActorSnapshot(Guid.Empty, "Actor", null)), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(target: CreateTarget(entryId: Guid.Empty)), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(target: CreateTarget(ownerUserId: Guid.Empty)), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(occurredAt: Now.ToOffset(TimeSpan.FromHours(10))), 1));
    }

    [Fact]
    public void CreateDelete_AllowsSystemActorWithoutUserOrDevice()
    {
        var activity = UserActivity.CreateDelete(
            CreateContext(actor: new ActivityActorSnapshot(null, "System", null)),
            ActivityDeleteKind.Purged);

        Assert.Null(activity.ActorUserId);
        Assert.Equal("System", activity.ActorDisplayName);
        Assert.Null(activity.ActorDeviceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("line\nbreak")]
    [InlineData("e\u0301")]
    public void Create_RejectsInvalidSnapshotText(string value)
    {
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(actor: new ActivityActorSnapshot(Guid.NewGuid(), value, null)), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(target: CreateTarget(name: value)), 1));
    }

    [Fact]
    public void Create_RejectsOverlongSnapshotText()
    {
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(actor: new ActivityActorSnapshot(Guid.NewGuid(), new string('a', 129), null)), 1));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateUpload(
            CreateContext(target: CreateTarget(name: new string('a', 256))), 1));
    }

    [Fact]
    public void Create_RejectsInvalidTypedDetails()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UserActivity.CreateUpload(CreateContext(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UserActivity.CreateEdit(
            CreateContext(), 1, (ActivityEditKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => UserActivity.CreateShare(
            CreateContext(),
            new ActivityRecipientSnapshot(Guid.NewGuid(), "Recipient"),
            (SharePermission)999,
            ActivityShareAction.Created));
        Assert.Throws<ArgumentOutOfRangeException>(() => UserActivity.CreateDelete(
            CreateContext(), (ActivityDeleteKind)999));
        Assert.Throws<ArgumentException>(() => UserActivity.CreateMove(
            CreateContext(),
            new ActivityFolderSnapshot(Guid.Empty, "Source"),
            new ActivityFolderSnapshot(Guid.NewGuid(), "Destination")));
    }

    [Fact]
    public void PersistenceConstructor_CreatesEmptyEntityForMaterialization()
    {
        var activity = Assert.IsType<UserActivity>(
            Activator.CreateInstance(typeof(UserActivity), nonPublic: true));

        Assert.Equal(Guid.Empty, activity.Id);
        Assert.Equal(string.Empty, activity.ActorDisplayName);
        Assert.Equal(string.Empty, activity.TargetName);
    }

    private static UserActivityContext CreateContext(
        Guid? id = null,
        Guid? operationId = null,
        ActivityActorSnapshot? actor = null,
        ActivityTargetSnapshot? target = null,
        DateTimeOffset? occurredAt = null) =>
        new(
            id ?? Guid.NewGuid(),
            operationId ?? Guid.NewGuid(),
            actor ?? new ActivityActorSnapshot(Guid.NewGuid(), "Actor", "Phone"),
            target ?? CreateTarget(),
            occurredAt ?? Now);

    private static ActivityTargetSnapshot CreateTarget(
        Guid? entryId = null,
        string name = "notes.txt",
        Guid? ownerUserId = null) =>
        new(
            entryId ?? Guid.NewGuid(),
            FileEntryType.File,
            name,
            ownerUserId ?? Guid.NewGuid(),
            "Owner",
            Guid.NewGuid());
}
