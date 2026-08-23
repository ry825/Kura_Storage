using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class AuthorizationServiceTests
{
    [Fact]
    public async Task ResolveBatch_SelectsOwnerStrongestAndDeterministicSourcesInOneCall()
    {
        var ownerEntry = Guid.NewGuid();
        var strongestEntry = Guid.NewGuid();
        var directTieEntry = Guid.NewGuid();
        var inheritedTieEntry = Guid.NewGuid();
        var unsharedEntry = Guid.NewGuid();
        var directTarget = Guid.NewGuid();
        var nearTarget = Guid.NewGuid();
        var repository = new FakeRepository(
            Candidate(ownerEntry, EffectivePermissionLevel.Owner, PermissionSource.Owner),
            Candidate(strongestEntry, EffectivePermissionLevel.Viewer, PermissionSource.Direct),
            Candidate(strongestEntry, EffectivePermissionLevel.Editor, PermissionSource.Inherited, depth: 2),
            Candidate(directTieEntry, EffectivePermissionLevel.Editor, PermissionSource.Inherited, depth: 1),
            Candidate(directTieEntry, EffectivePermissionLevel.Editor, PermissionSource.Direct, directTarget),
            Candidate(inheritedTieEntry, EffectivePermissionLevel.Manager, PermissionSource.Inherited, depth: 4),
            Candidate(inheritedTieEntry, EffectivePermissionLevel.Manager, PermissionSource.Inherited, nearTarget, depth: 1));
        var service = new AuthorizationService(repository);

        var result = await service.ResolveBatchAsync(
            Guid.NewGuid(),
            [ownerEntry, strongestEntry, directTieEntry, inheritedTieEntry, unsharedEntry],
            CancellationToken.None);

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(ownerEntry, result[ownerEntry].EntryId);
        Assert.Equal(EffectivePermissionLevel.Owner, result[ownerEntry].Permission);
        Assert.Null(result[ownerEntry].ShareId);
        Assert.Equal(EffectivePermissionLevel.Editor, result[strongestEntry].Permission);
        Assert.Equal(PermissionSource.Inherited, result[strongestEntry].Source);
        Assert.Equal(PermissionSource.Direct, result[directTieEntry].Source);
        Assert.Equal(directTarget, result[directTieEntry].ShareTargetId);
        Assert.Equal(nearTarget, result[inheritedTieEntry].ShareTargetId);
        Assert.Equal(EffectivePermissionLevel.None, result[unsharedEntry].Permission);
        Assert.Null(result[unsharedEntry].Source);
    }

    [Theory]
    [InlineData(EffectivePermissionLevel.Viewer, ShareOperation.View, true)]
    [InlineData(EffectivePermissionLevel.Viewer, ShareOperation.Contribute, false)]
    [InlineData(EffectivePermissionLevel.Contributor, ShareOperation.Edit, false)]
    [InlineData(EffectivePermissionLevel.Editor, ShareOperation.Edit, true)]
    [InlineData(EffectivePermissionLevel.Manager, ShareOperation.Manage, true)]
    [InlineData(EffectivePermissionLevel.Owner, ShareOperation.Manage, true)]
    [InlineData(EffectivePermissionLevel.None, ShareOperation.View, false)]
    public async Task AllowsAsync_UsesTheSharedOperationMatrix(
        EffectivePermissionLevel permission,
        ShareOperation operation,
        bool expected)
    {
        var entryId = Guid.NewGuid();
        var repository = permission == EffectivePermissionLevel.None
            ? new FakeRepository()
            : new FakeRepository(Candidate(
                entryId,
                permission,
                permission == EffectivePermissionLevel.Owner ? PermissionSource.Owner : PermissionSource.Direct));

        var allowed = await new AuthorizationService(repository).AllowsAsync(
            Guid.NewGuid(), entryId, operation, CancellationToken.None);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotCachePermissionAcrossRequestsOrGrantRoleBasedAccess()
    {
        var entryId = Guid.NewGuid();
        var repository = new FakeRepository(
            Candidate(entryId, EffectivePermissionLevel.Manager, PermissionSource.Direct));
        var service = new AuthorizationService(repository);

        Assert.Equal(
            EffectivePermissionLevel.Manager,
            (await service.ResolveAsync(Guid.NewGuid(), entryId, CancellationToken.None)).Permission);
        repository.Candidates = [];
        Assert.Equal(
            EffectivePermissionLevel.None,
            (await service.ResolveAsync(Guid.NewGuid(), entryId, CancellationToken.None)).Permission);
        Assert.Equal(2, repository.CallCount);
    }

    [Fact]
    public async Task ResolveBatch_RejectsInvalidOrDuplicateIdentifiers()
    {
        var service = new AuthorizationService(new FakeRepository());
        var entryId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResolveBatchAsync(Guid.Empty, [entryId], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResolveBatchAsync(Guid.NewGuid(), [Guid.Empty], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResolveBatchAsync(Guid.NewGuid(), [entryId, entryId], CancellationToken.None));
    }

    [Fact]
    public async Task ResolveBatch_EmptyInputReturnsWithoutRepositoryQuery()
    {
        var repository = new FakeRepository();

        var result = await new AuthorizationService(repository).ResolveBatchAsync(
            Guid.NewGuid(), [], CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task ResolveBatch_UnknownPermissionSourceFailsClosed()
    {
        var entryId = Guid.NewGuid();
        var repository = new FakeRepository(Candidate(
            entryId,
            EffectivePermissionLevel.Viewer,
            (PermissionSource)999));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new AuthorizationService(repository).ResolveAsync(
                Guid.NewGuid(), entryId, CancellationToken.None));
    }

    private static PermissionCandidate Candidate(
        Guid entryId,
        EffectivePermissionLevel permission,
        PermissionSource source,
        Guid? shareTargetId = null,
        int depth = 0) =>
        new(
            entryId,
            permission,
            source,
            shareTargetId,
            source == PermissionSource.Owner ? null : Guid.NewGuid(),
            depth);

    private sealed class FakeRepository(params PermissionCandidate[] candidates) : IAuthorizationRepository
    {
        public IReadOnlyList<PermissionCandidate> Candidates { get; set; } = candidates;

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<PermissionCandidate>> ListCandidatesAsync(
            Guid actorUserId,
            IReadOnlyCollection<Guid> entryIds,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<PermissionCandidate>>(
                Candidates.Where(candidate => entryIds.Contains(candidate.EntryId)).ToArray());
        }
    }
}
