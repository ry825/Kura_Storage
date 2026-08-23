using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Sharing;

public sealed class AuthorizationService(IAuthorizationRepository repository) : IAuthorizationService
{
    public async Task<EffectivePermission> ResolveAsync(
        Guid actorUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var permissions = await ResolveBatchAsync(actorUserId, [entryId], cancellationToken);
        return permissions[entryId];
    }

    public async Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(
        Guid actorUserId,
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor user ID is required.", nameof(actorUserId));
        }

        var requestedIds = entryIds.Where(entryId => entryId != Guid.Empty).Distinct().ToArray();
        if (requestedIds.Length != entryIds.Count)
        {
            throw new ArgumentException("Entry IDs must be non-empty and unique.", nameof(entryIds));
        }

        if (requestedIds.Length == 0)
        {
            return new Dictionary<Guid, EffectivePermission>();
        }

        var candidates = await repository.ListCandidatesAsync(actorUserId, requestedIds, cancellationToken);
        var candidatesByEntry = candidates
            .Where(candidate => requestedIds.Contains(candidate.EntryId))
            .GroupBy(candidate => candidate.EntryId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var resolved = new Dictionary<Guid, EffectivePermission>(requestedIds.Length);
        foreach (var entryId in requestedIds)
        {
            if (!candidatesByEntry.TryGetValue(entryId, out var entryCandidates) || entryCandidates.Length == 0)
            {
                resolved[entryId] = new EffectivePermission(
                    entryId,
                    EffectivePermissionLevel.None,
                    null,
                    null,
                    null);
                continue;
            }

            var winner = entryCandidates
                .OrderByDescending(candidate => candidate.Permission)
                .ThenBy(candidate => SourceOrder(candidate.Source))
                .ThenBy(candidate => candidate.AncestorDepth)
                .ThenBy(candidate => candidate.ShareTargetId)
                .ThenBy(candidate => candidate.ShareId)
                .First();
            resolved[entryId] = new EffectivePermission(
                entryId,
                winner.Permission,
                winner.Source,
                winner.Source == PermissionSource.Owner ? null : winner.ShareTargetId,
                winner.Source == PermissionSource.Owner ? null : winner.ShareId);
        }

        return resolved;
    }

    public async Task<bool> AllowsAsync(
        Guid actorUserId,
        Guid entryId,
        ShareOperation operation,
        CancellationToken cancellationToken) =>
        (await ResolveAsync(actorUserId, entryId, cancellationToken)).Allows(operation);

    private static int SourceOrder(PermissionSource source) => source switch
    {
        PermissionSource.Owner => 0,
        PermissionSource.Direct => 1,
        PermissionSource.Inherited => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };
}
