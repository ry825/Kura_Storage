using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class SharingServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-23T04:00:00Z");

    [Fact]
    public async Task CreateAsync_FolderWithActiveMembers_PersistsShareAndAuditAtomically()
    {
        var owner = User("Owner");
        var viewer = User("Viewer");
        var manager = User("Manager");
        var root = FileEntry.CreateRoot(owner.Id, Now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(), owner.Id, root.Id, FileName.Create("Family"),
            RelativeStoragePath.Create($"{root.RelativePath}/Family"), Now);
        var repository = new FakeShareRepository([owner, viewer, manager], [root, folder]);
        var service = CreateService(repository, Now);

        var result = await service.CreateAsync(
            new CreateShareCommand(
                owner.Id,
                Guid.NewGuid(),
                folder.Id,
                [
                    new ShareMemberInput(viewer.Id, SharePermission.Viewer),
                    new ShareMemberInput(manager.Id, SharePermission.Manager),
                ],
                "request-create"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(folder.Id, result.Value!.TargetEntryId);
        Assert.Equal(2, result.Value.Members.Count);
        Assert.True(repository.TransactionCommitted);
        Assert.Contains(repository.Audits, audit =>
            audit.Action == "SHARE_CREATE" &&
            audit.ActorUserId == owner.Id &&
            audit.ResultCode == "SUCCESS" &&
            audit.RequestId == "request-create");
    }

    [Fact]
    public async Task CreateAsync_FileContributor_IsRejectedBeforePersistence()
    {
        var owner = User("Owner");
        var recipient = User("Recipient");
        var root = FileEntry.CreateRoot(owner.Id, Now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), owner.Id, root.Id, FileName.Create("photo.jpg"),
            RelativeStoragePath.Create($"{root.RelativePath}/photo.jpg"), "image/jpeg", 10, Now);
        var repository = new FakeShareRepository([owner, recipient], [root, file]);
        var service = CreateService(repository, Now);

        var result = await service.CreateAsync(
            new CreateShareCommand(
                owner.Id,
                Guid.NewGuid(),
                file.Id,
                [new ShareMemberInput(recipient.Id, SharePermission.Contributor)],
                "request-invalid"),
            CancellationToken.None);

        Assert.Equal(SharingErrorCodes.InvalidSharePermission, result.Failure!.Code);
        Assert.Empty(repository.Shares);
    }

    [Fact]
    public async Task SetMemberAsync_ManagerCanAddMember_AndLastRemovalDeletesShare()
    {
        var owner = User("Owner");
        var manager = User("Manager");
        var recipient = User("Recipient");
        var root = FileEntry.CreateRoot(owner.Id, Now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(), owner.Id, root.Id, FileName.Create("Family"),
            RelativeStoragePath.Create($"{root.RelativePath}/Family"), Now);
        var share = new Share(Guid.NewGuid(), folder.Id, owner.Id, Now);
        share.AddMember(manager.Id, SharePermission.Manager, Now);
        var repository = new FakeShareRepository([owner, manager, recipient], [root, folder], [share]);
        var service = CreateService(repository, Now.AddMinutes(1));

        var added = await service.SetMemberAsync(
            new SetShareMemberCommand(
                manager.Id, Guid.NewGuid(), share.Id, recipient.Id, SharePermission.Editor, "request-add"),
            CancellationToken.None);
        var managerRemoved = await service.RemoveMemberAsync(
            new RemoveShareMemberCommand(
                owner.Id, Guid.NewGuid(), share.Id, manager.Id, "request-remove-manager"),
            CancellationToken.None);
        var recipientRemoved = await service.RemoveMemberAsync(
            new RemoveShareMemberCommand(
                owner.Id, Guid.NewGuid(), share.Id, recipient.Id, "request-remove-last"),
            CancellationToken.None);

        Assert.True(added.IsSuccess);
        Assert.True(managerRemoved.IsSuccess);
        Assert.True(recipientRemoved.IsSuccess);
        Assert.Empty(repository.Shares);
    }

    [Fact]
    public async Task GetAsync_UnrelatedUser_HidesShareExistence()
    {
        var owner = User("Owner");
        var recipient = User("Recipient");
        var unrelated = User("Unrelated");
        var root = FileEntry.CreateRoot(owner.Id, Now);
        var share = new Share(Guid.NewGuid(), root.Id, owner.Id, Now);
        share.AddMember(recipient.Id, SharePermission.Viewer, Now);
        var repository = new FakeShareRepository([owner, recipient, unrelated], [root], [share]);
        var service = CreateService(repository, Now);

        var result = await service.GetAsync(unrelated.Id, share.Id, CancellationToken.None);

        Assert.Equal(SharingErrorCodes.ShareNotFound, result.Failure!.Code);
        Assert.Equal(SharingFailureKind.NotFound, result.Failure.Kind);
    }

    [Fact]
    public async Task InvalidIdentifiersAndPaging_ReturnValidationFailures()
    {
        var repository = new FakeShareRepository([], []);
        var service = CreateService(repository, Now);

        var candidates = await service.ListCandidatesAsync(Guid.Empty, CancellationToken.None);
        var list = await service.ListAsync(Guid.Empty, ShareScope.Owned, null, 0, 0, CancellationToken.None);
        var get = await service.GetAsync(Guid.Empty, Guid.Empty, CancellationToken.None);
        var set = await service.SetMemberAsync(
            new SetShareMemberCommand(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, SharePermission.Viewer, ""),
            CancellationToken.None);
        var remove = await service.RemoveMemberAsync(
            new RemoveShareMemberCommand(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, ""),
            CancellationToken.None);
        var delete = await service.DeleteAsync(
            new DeleteShareCommand(Guid.Empty, Guid.Empty, Guid.Empty, ""),
            CancellationToken.None);

        Assert.All(
            new[] { candidates.Failure, list.Failure, get.Failure, set.Failure, remove.Failure, delete.Failure },
            failure => Assert.Equal(SharingErrorCodes.ValidationFailed, failure!.Code));
    }

    [Fact]
    public async Task ManagementAsync_UnrelatedUser_HidesShareExistence()
    {
        var owner = User("Owner");
        var recipient = User("Recipient");
        var unrelated = User("Unrelated");
        var root = FileEntry.CreateRoot(owner.Id, Now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(), owner.Id, root.Id, FileName.Create("Family"),
            RelativeStoragePath.Create($"{root.RelativePath}/Family"), Now);
        var share = new Share(Guid.NewGuid(), folder.Id, owner.Id, Now);
        share.AddMember(recipient.Id, SharePermission.Viewer, Now);
        var repository = new FakeShareRepository([owner, recipient, unrelated], [root, folder], [share]);
        var service = CreateService(repository, Now);

        var remove = await service.RemoveMemberAsync(
            new RemoveShareMemberCommand(
                unrelated.Id, Guid.NewGuid(), share.Id, recipient.Id, "request-remove"),
            CancellationToken.None);
        var delete = await service.DeleteAsync(
            new DeleteShareCommand(unrelated.Id, Guid.NewGuid(), share.Id, "request-delete"),
            CancellationToken.None);

        Assert.Equal(SharingErrorCodes.ShareNotFound, remove.Failure!.Code);
        Assert.Equal(SharingErrorCodes.ShareNotFound, delete.Failure!.Code);
        Assert.Single(repository.Shares);
    }

    [Fact]
    public async Task SetMemberAsync_PersistenceConflict_ReturnsConflict()
    {
        var owner = User("Owner");
        var recipient = User("Recipient");
        var root = FileEntry.CreateRoot(owner.Id, Now);
        var folder = FileEntry.CreateFolder(
            Guid.NewGuid(), owner.Id, root.Id, FileName.Create("Family"),
            RelativeStoragePath.Create($"{root.RelativePath}/Family"), Now);
        var share = new Share(Guid.NewGuid(), folder.Id, owner.Id, Now);
        share.AddMember(recipient.Id, SharePermission.Viewer, Now);
        var repository = new FakeShareRepository([owner, recipient], [root, folder], [share])
        {
            ThrowPersistenceConflict = true,
        };
        var service = CreateService(repository, Now);

        var result = await service.SetMemberAsync(
            new SetShareMemberCommand(
                owner.Id, Guid.NewGuid(), share.Id, recipient.Id, SharePermission.Editor, "request-conflict"),
            CancellationToken.None);

        Assert.Equal(SharingErrorCodes.ShareConflict, result.Failure!.Code);
        Assert.Equal(SharingFailureKind.Conflict, result.Failure.Kind);
    }

    private static User User(string displayName) =>
        new(Guid.NewGuid(), displayName.ToLowerInvariant(), displayName, "hash", UserRole.Member, Now);

    private static SharingService CreateService(FakeShareRepository repository, DateTimeOffset now) =>
        new(repository, new FakeAuthorizationService(repository), new SharingFixedClock(now));

    private sealed class SharingFixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeAuthorizationService(FakeShareRepository repository) : IAuthorizationService
    {
        public Task<EffectivePermission> ResolveAsync(
            Guid actorUserId,
            Guid entryId,
            CancellationToken cancellationToken)
        {
            var share = repository.Shares.SingleOrDefault(candidate => candidate.TargetEntryId == entryId);
            var permission = share?.OwnerUserId == actorUserId
                ? EffectivePermissionLevel.Owner
                : share?.Members.SingleOrDefault(member => member.UserId == actorUserId)?.Permission switch
                {
                    SharePermission.Viewer => EffectivePermissionLevel.Viewer,
                    SharePermission.Contributor => EffectivePermissionLevel.Contributor,
                    SharePermission.Editor => EffectivePermissionLevel.Editor,
                    SharePermission.Manager => EffectivePermissionLevel.Manager,
                    _ => EffectivePermissionLevel.None,
                };
            return Task.FromResult(new EffectivePermission(entryId, permission, null, null, null));
        }

        public async Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(
            Guid actorUserId,
            IReadOnlyCollection<Guid> entryIds,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<Guid, EffectivePermission>();
            foreach (var entryId in entryIds)
            {
                result[entryId] = await ResolveAsync(actorUserId, entryId, cancellationToken);
            }

            return result;
        }

        public async Task<bool> AllowsAsync(
            Guid actorUserId,
            Guid entryId,
            ShareOperation operation,
            CancellationToken cancellationToken) =>
            (await ResolveAsync(actorUserId, entryId, cancellationToken)).Allows(operation);
    }

    private sealed class FakeShareRepository : IShareRepository
    {
        private readonly List<User> users;
        private readonly List<FileEntry> entries;

        public FakeShareRepository(
            IEnumerable<User> users,
            IEnumerable<FileEntry> entries,
            IEnumerable<Share>? shares = null)
        {
            this.users = users.ToList();
            this.entries = entries.ToList();
            Shares = shares?.ToList() ?? [];
        }

        public List<Share> Shares { get; }

        public List<AuditLog> Audits { get; } = [];

        public bool TransactionCommitted { get; private set; }

        public bool ThrowPersistenceConflict { get; init; }

        public Task<IShareTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IShareTransaction>(new FakeTransaction(() => TransactionCommitted = true));

        public Task<IReadOnlyList<ShareCandidate>> ListCandidatesAsync(
            Guid actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ShareCandidate>>(
                users.Where(user => user.Id != actorUserId && user.Status == UserStatus.Active)
                    .OrderBy(user => user.DisplayName)
                    .ThenBy(user => user.Id)
                    .Select(user => new ShareCandidate(user.Id, user.DisplayName))
                    .ToArray());

        public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(users.SingleOrDefault(user => user.Id == userId));

        public Task<FileEntry?> FindEntryAsync(Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(entries.SingleOrDefault(entry => entry.Id == entryId));

        public Task<Share?> FindByIdAsync(Guid shareId, CancellationToken cancellationToken) =>
            Task.FromResult(Shares.SingleOrDefault(share => share.Id == shareId));

        public Task<Share?> FindByTargetAsync(Guid targetEntryId, CancellationToken cancellationToken) =>
            Task.FromResult(Shares.SingleOrDefault(share => share.TargetEntryId == targetEntryId));

        public Task<ShareView?> GetViewAsync(Guid shareId, CancellationToken cancellationToken)
        {
            var share = Shares.SingleOrDefault(candidate => candidate.Id == shareId);
            return Task.FromResult(share is null ? null : View(share));
        }

        public Task<IReadOnlyList<ShareView>> ListViewsAsync(
            Guid actorUserId,
            ShareScope scope,
            FileEntryType? targetType,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ShareView>>(
                Shares.Where(share =>
                        (scope == ShareScope.Owned
                            ? share.OwnerUserId == actorUserId
                            : share.Members.Any(member => member.UserId == actorUserId)) &&
                        (targetType is null || entries.Single(entry => entry.Id == share.TargetEntryId).EntryType == targetType))
                    .Skip(skip)
                    .Take(take)
                    .Select(View)
                    .ToArray());

        public Task<int> CountViewsAsync(
            Guid actorUserId,
            ShareScope scope,
            FileEntryType? targetType,
            CancellationToken cancellationToken) =>
            Task.FromResult(Shares.Count(share =>
                scope == ShareScope.Owned
                    ? share.OwnerUserId == actorUserId
                    : share.Members.Any(member => member.UserId == actorUserId)));

        public void Add(Share share) => Shares.Add(share);

        public void Add(AuditLog auditLog) => Audits.Add(auditLog);

        public void Remove(Share share) => Shares.Remove(share);

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            ThrowPersistenceConflict
                ? throw new SharePersistenceConflictException(new InvalidOperationException("Concurrent update."))
                : Task.CompletedTask;

        private ShareView View(Share share)
        {
            var entry = entries.Single(candidate => candidate.Id == share.TargetEntryId);
            var owner = users.Single(candidate => candidate.Id == share.OwnerUserId);
            return new ShareView(
                share.Id,
                entry.Id,
                entry.EntryType,
                entry.Name,
                owner.Id,
                owner.DisplayName,
                share.Members.Select(member =>
                {
                    var user = users.Single(candidate => candidate.Id == member.UserId);
                    return new ShareViewMember(user.Id, user.DisplayName, member.Permission);
                }).ToArray(),
                share.CreatedAt,
                share.UpdatedAt);
        }

        private sealed class FakeTransaction(Action commit) : IShareTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                commit();
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
