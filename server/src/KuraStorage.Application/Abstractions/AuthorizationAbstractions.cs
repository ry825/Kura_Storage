using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Abstractions;

/// <summary>
/// Loads all ownership and sharing candidates for a bounded set of file entries in one request.
/// </summary>
public interface IAuthorizationRepository
{
    Task<IReadOnlyList<PermissionCandidate>> ListCandidatesAsync(
        Guid actorUserId,
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves effective file permissions without retaining results across requests.
/// </summary>
public interface IAuthorizationService
{
    Task<EffectivePermission> ResolveAsync(
        Guid actorUserId,
        Guid entryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(
        Guid actorUserId,
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken);

    Task<bool> AllowsAsync(
        Guid actorUserId,
        Guid entryId,
        ShareOperation operation,
        CancellationToken cancellationToken);
}
