using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class UserActivityFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddUpload_UsesPersistedActorDeviceOwnerAndTargetSnapshots()
    {
        var fixture = CreateFixture();

        var activity = await fixture.Factory.AddUploadAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            3,
            default);

        Assert.NotNull(activity);
        Assert.Same(activity, Assert.Single(fixture.Repository.Added));
        Assert.Equal("Actor", activity.ActorDisplayName);
        Assert.Equal("Phone", activity.ActorDeviceName);
        Assert.Equal("Owner", activity.OwnerDisplayName);
        Assert.Equal("document.txt", activity.TargetName);
        Assert.Equal(3, activity.ResultingFileVersion);
        Assert.Equal(Now, activity.OccurredAt);
    }

    [Fact]
    public async Task AddMove_UsesPersistedFolderSnapshots()
    {
        var fixture = CreateFixture();
        var source = CreateFolder(fixture.Owner.Id, "Source");
        var destination = CreateFolder(fixture.Owner.Id, "Destination");

        var activity = await fixture.Factory.AddMoveAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            source,
            destination,
            default);

        Assert.Equal("Source", activity!.SourceParentName);
        Assert.Equal("Destination", activity.DestinationParentName);
    }

    [Fact]
    public async Task AddShare_UsesPersistedRecipientSnapshot()
    {
        var fixture = CreateFixture();
        var recipient = CreateUser("Recipient");
        fixture.Repository.Users.Add(recipient);

        var activity = await fixture.Factory.AddShareAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            recipient.Id,
            SharePermission.Manager,
            ActivityShareAction.Created,
            default);

        Assert.Equal("Recipient", activity!.RecipientDisplayName);
        Assert.Equal(SharePermission.Manager, activity.SharePermission);
    }

    [Fact]
    public async Task ExistingMatchingOperation_IsIdempotentWithoutSecondRecord()
    {
        var fixture = CreateFixture();
        var first = await fixture.Factory.AddEditAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            2,
            ActivityEditKind.TextSave,
            default);
        fixture.Repository.Existing = first;

        var repeated = await fixture.Factory.AddEditAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            2,
            ActivityEditKind.TextSave,
            default);

        Assert.Same(first, repeated);
        Assert.Single(fixture.Repository.Added);
    }

    [Fact]
    public async Task ExistingUploadMoveShareAndDeleteOperations_AreIdempotentWithoutSecondRecords()
    {
        var upload = CreateFixture();
        upload.Repository.Existing = await upload.Factory.AddUploadAsync(
            upload.OperationId,
            upload.Actor.Id,
            upload.Device.Id,
            upload.Target,
            2,
            default);
        Assert.Same(upload.Repository.Existing, await upload.Factory.AddUploadAsync(
            upload.OperationId,
            upload.Actor.Id,
            upload.Device.Id,
            upload.Target,
            2,
            default));
        Assert.Single(upload.Repository.Added);

        var move = CreateFixture();
        var source = CreateFolder(move.Owner.Id, "Source");
        var destination = CreateFolder(move.Owner.Id, "Destination");
        move.Repository.Existing = await move.Factory.AddMoveAsync(
            move.OperationId,
            move.Actor.Id,
            move.Device.Id,
            move.Target,
            source,
            destination,
            default);
        Assert.Same(move.Repository.Existing, await move.Factory.AddMoveAsync(
            move.OperationId,
            move.Actor.Id,
            move.Device.Id,
            move.Target,
            source,
            destination,
            default));
        Assert.Single(move.Repository.Added);

        var share = CreateFixture();
        var recipient = CreateUser("Recipient");
        share.Repository.Users.Add(recipient);
        share.Repository.Existing = await share.Factory.AddShareAsync(
            share.OperationId,
            share.Actor.Id,
            share.Device.Id,
            share.Target,
            recipient.Id,
            SharePermission.Editor,
            ActivityShareAction.Updated,
            default);
        Assert.Same(share.Repository.Existing, await share.Factory.AddShareAsync(
            share.OperationId,
            share.Actor.Id,
            share.Device.Id,
            share.Target,
            recipient.Id,
            SharePermission.Editor,
            ActivityShareAction.Updated,
            default));
        Assert.Single(share.Repository.Added);

        var delete = CreateFixture();
        delete.Repository.Existing = await delete.Factory.AddDeleteAsync(
            delete.OperationId,
            delete.Actor.Id,
            delete.Device.Id,
            delete.Target,
            ActivityDeleteKind.Trashed,
            default);
        Assert.Same(delete.Repository.Existing, await delete.Factory.AddDeleteAsync(
            delete.OperationId,
            delete.Actor.Id,
            delete.Device.Id,
            delete.Target,
            ActivityDeleteKind.Trashed,
            default));
        Assert.Single(delete.Repository.Added);

        var systemDelete = CreateFixture();
        systemDelete.Repository.Existing = await systemDelete.Factory.AddSystemDeleteAsync(
            systemDelete.OperationId,
            systemDelete.Target,
            ActivityDeleteKind.Purged,
            default);
        Assert.Same(systemDelete.Repository.Existing, await systemDelete.Factory.AddSystemDeleteAsync(
            systemDelete.OperationId,
            systemDelete.Target,
            ActivityDeleteKind.Purged,
            default));
        Assert.Single(systemDelete.Repository.Added);
    }

    [Fact]
    public async Task ExistingMismatchedOperation_FailsClosed()
    {
        var fixture = CreateFixture();
        fixture.Repository.Existing = UserActivity.CreateDelete(
            new UserActivityContext(
                Guid.NewGuid(),
                fixture.OperationId,
                new ActivityActorSnapshot(fixture.Actor.Id, fixture.Actor.DisplayName, fixture.Device.DeviceName),
                new ActivityTargetSnapshot(
                    Guid.NewGuid(), FileEntryType.File, "other.txt", fixture.Owner.Id, fixture.Owner.DisplayName, Guid.NewGuid()),
                Now),
            ActivityDeleteKind.Trashed);

        await Assert.ThrowsAsync<ActivityIdempotencyConflictException>(() => fixture.Factory.AddUploadAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            1,
            default));
    }

    [Fact]
    public async Task MissingOrMismatchedSecuritySnapshot_FailsBeforeAdd()
    {
        var fixture = CreateFixture();
        fixture.Repository.Devices.Clear();

        await Assert.ThrowsAsync<ActivitySnapshotUnavailableException>(() => fixture.Factory.AddDeleteAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            ActivityDeleteKind.Purged,
            default));
        Assert.Empty(fixture.Repository.Added);
    }

    [Fact]
    public async Task EmptyOperationIdAndNonFolderMoveSnapshots_FailBeforeAdd()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Factory.AddUploadAsync(
            Guid.Empty,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            1,
            default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Factory.AddMoveAsync(
            fixture.OperationId,
            fixture.Actor.Id,
            fixture.Device.Id,
            fixture.Target,
            fixture.Target,
            CreateFolder(fixture.Owner.Id, "Destination"),
            default));
        Assert.Empty(fixture.Repository.Added);
    }

    private static Fixture CreateFixture()
    {
        var actor = CreateUser("Actor");
        var owner = CreateUser("Owner");
        var device = new Device(Guid.NewGuid(), actor.Id, "Phone", Now);
        var target = FileEntry.CreateFile(
            Guid.NewGuid(),
            owner.Id,
            Guid.NewGuid(),
            FileName.Create("document.txt"),
            RelativeStoragePath.Create($"users/{owner.Id:N}/files/document.txt"),
            "text/plain",
            1,
            Now);
        var repository = new FakeActivityRepository();
        repository.Users.AddRange([actor, owner]);
        repository.Devices.Add(device);
        return new Fixture(
            new UserActivityFactory(repository, new FakeClock(Now)),
            repository,
            actor,
            owner,
            device,
            target,
            Guid.NewGuid());
    }

    private static User CreateUser(string displayName) =>
        new(Guid.NewGuid(), displayName.ToUpperInvariant(), displayName, "hash", UserRole.Member, Now);

    private static FileEntry CreateFolder(Guid ownerId, string name) =>
        FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerId,
            Guid.NewGuid(),
            FileName.Create(name),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/{name}"),
            Now);

    private sealed record Fixture(
        UserActivityFactory Factory,
        FakeActivityRepository Repository,
        User Actor,
        User Owner,
        Device Device,
        FileEntry Target,
        Guid OperationId);

    private sealed class FakeActivityRepository : IUserActivityRepository
    {
        public List<User> Users { get; } = [];

        public List<Device> Devices { get; } = [];

        public List<UserActivity> Added { get; } = [];

        public UserActivity? Existing { get; set; }

        public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<Device?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(Devices.SingleOrDefault(device => device.Id == deviceId));

        public Task<UserActivity?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(Existing?.OperationId == operationId ? Existing : null);

        public void Add(UserActivity activity) => Added.Add(activity);
    }

    private sealed class FakeClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
