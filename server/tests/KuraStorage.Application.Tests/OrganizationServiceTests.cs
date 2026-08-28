using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Organization;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class OrganizationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");

    [Fact]
    public async Task FavoriteMutations_UseActorEntryAndServerTimeWithIdempotentRemoval()
    {
        var repository = new FakeRepository { FavoriteOutcome = OrganizationRepositoryOutcome.NoChange };
        var actor = Guid.NewGuid();
        var entry = Guid.NewGuid();
        var service = new OrganizationService(repository, new FixedClock(Now));

        var add = await service.AddFavoriteAsync(actor, entry, CancellationToken.None);
        var remove = await service.RemoveFavoriteAsync(actor, entry, CancellationToken.None);

        Assert.True(add.IsSuccess);
        Assert.True(remove.IsSuccess);
        Assert.Equal((actor, entry, Now), repository.FavoriteInput);
        Assert.Equal((actor, entry), repository.RemovedFavorite);
    }

    [Fact]
    public async Task FavoriteList_ValidatesBoundedPaging()
    {
        var repository = new FakeRepository();
        var service = new OrganizationService(repository, new FixedClock(Now));

        var valid = await service.ListFavoritesAsync(Guid.NewGuid(), 2, 100, CancellationToken.None);
        var invalid = await service.ListFavoritesAsync(Guid.NewGuid(), 0, 101, CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.Equal((2, 100), repository.FavoritePageInput);
        Assert.Equal(OrganizationErrorCodes.InvalidFavoritesRequest, invalid.Failure!.Code);
    }

    [Fact]
    public async Task TagCreateAndRename_NormalizeAndMapRepositoryOutcomes()
    {
        var repository = new FakeRepository
        {
            CreateTagResult = new(OrganizationRepositoryOutcome.Created, new TagItem(Guid.NewGuid(), "Café")),
            RenameTagResult = new(OrganizationRepositoryOutcome.Conflict),
        };
        var actor = Guid.NewGuid();
        var service = new OrganizationService(repository, new FixedClock(Now));

        var created = await service.CreateTagAsync(actor, new CreateTagCommand(" Cafe\u0301 "), CancellationToken.None);
        var renamed = await service.RenameTagAsync(actor, new RenameTagCommand(Guid.NewGuid(), "Work"), CancellationToken.None);
        var invalid = await service.CreateTagAsync(actor, new CreateTagCommand("bad\nname"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(("Café", "CAFÉ", Now), repository.CreateTagInput);
        Assert.Equal(OrganizationErrorCodes.TagNameConflict, renamed.Failure!.Code);
        Assert.Equal(OrganizationErrorCodes.InvalidOrganizationRequest, invalid.Failure!.Code);
    }

    [Fact]
    public async Task TagDeleteAttachDetachAndState_MapExistenceHidingFailures()
    {
        var repository = new FakeRepository
        {
            DeleteTagOutcome = OrganizationRepositoryOutcome.NotFound,
            AttachOutcome = OrganizationRepositoryOutcome.EntryLimitExceeded,
            State = new EntryOrganizationState(true, []),
        };
        var actor = Guid.NewGuid();
        var entry = Guid.NewGuid();
        var tag = Guid.NewGuid();
        var service = new OrganizationService(repository, new FixedClock(Now));

        var deleted = await service.DeleteTagAsync(actor, tag, CancellationToken.None);
        var attached = await service.AttachTagAsync(actor, entry, tag, CancellationToken.None);
        var detached = await service.DetachTagAsync(actor, entry, tag, CancellationToken.None);
        var state = await service.GetEntryOrganizationAsync(actor, entry, CancellationToken.None);

        Assert.Equal(OrganizationErrorCodes.TagNotFound, deleted.Failure!.Code);
        Assert.Equal(OrganizationErrorCodes.EntryTagLimitExceeded, attached.Failure!.Code);
        Assert.True(detached.IsSuccess);
        Assert.True(state.Value!.IsFavorite);
    }

    [Fact]
    public async Task InvalidIds_DoNotReachRepositoryAndCleanupDetachRemainsIdempotent()
    {
        var repository = new FakeRepository();
        var service = new OrganizationService(repository, new FixedClock(Now));

        Assert.Equal(OrganizationErrorCodes.FileNotFound,
            (await service.AddFavoriteAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None)).Failure!.Code);
        Assert.True((await service.DetachTagAsync(Guid.Empty, Guid.Empty, Guid.Empty, CancellationToken.None)).IsSuccess);
        Assert.Equal(0, repository.CallCount);
    }

    private sealed class FakeRepository : IOrganizationRepository
    {
        public OrganizationRepositoryOutcome FavoriteOutcome { get; init; } = OrganizationRepositoryOutcome.Created;
        public OrganizationRepositoryResult<TagItem> CreateTagResult { get; init; } = new(OrganizationRepositoryOutcome.NotFound);
        public OrganizationRepositoryResult<TagItem> RenameTagResult { get; init; } = new(OrganizationRepositoryOutcome.NotFound);
        public OrganizationRepositoryOutcome DeleteTagOutcome { get; init; } = OrganizationRepositoryOutcome.NoChange;
        public OrganizationRepositoryOutcome AttachOutcome { get; init; } = OrganizationRepositoryOutcome.Created;
        public EntryOrganizationState? State { get; init; }
        public (Guid, Guid, DateTimeOffset) FavoriteInput { get; private set; }
        public (Guid, Guid) RemovedFavorite { get; private set; }
        public (int, int) FavoritePageInput { get; private set; }
        public (string, string, DateTimeOffset) CreateTagInput { get; private set; }
        public int CallCount { get; private set; }

        public Task<OrganizationRepositoryOutcome> TryAddFavoriteAuthorizedAsync(Guid userId, Guid entryId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CallCount++;
            FavoriteInput = (userId, entryId, now);
            return Task.FromResult(FavoriteOutcome);
        }

        public Task RemoveFavoriteAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
        {
            CallCount++;
            RemovedFavorite = (userId, entryId);
            return Task.CompletedTask;
        }

        public Task<FavoritePage> ListFavoritesAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            CallCount++;
            FavoritePageInput = (page, pageSize);
            return Task.FromResult(new FavoritePage([], page, pageSize, 0));
        }

        public Task<IReadOnlyList<TagItem>> ListTagsAsync(Guid userId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<TagItem>>([]);
        }

        public Task<OrganizationRepositoryResult<TagItem>> TryCreateTagAsync(Guid userId, string name, string nameKey, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CallCount++;
            CreateTagInput = (name, nameKey, now);
            return Task.FromResult(CreateTagResult);
        }

        public Task<OrganizationRepositoryResult<TagItem>> TryRenameTagAsync(Guid userId, Guid tagId, string name, string nameKey, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(RenameTagResult);
        }

        public Task<OrganizationRepositoryOutcome> DeleteTagAsync(Guid userId, Guid tagId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(DeleteTagOutcome);
        }

        public Task<EntryOrganizationState?> GetEntryOrganizationAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(State);
        }

        public Task<OrganizationRepositoryOutcome> TryAttachTagAuthorizedAsync(Guid userId, Guid entryId, Guid tagId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(AttachOutcome);
        }

        public Task DetachTagAsync(Guid userId, Guid entryId, Guid tagId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
