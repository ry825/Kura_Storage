using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Abstractions;

public interface IShareTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IShareRepository
{
    Task<IShareTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ShareCandidate>> ListCandidatesAsync(
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<FileEntry?> FindEntryAsync(Guid entryId, CancellationToken cancellationToken);

    Task<Share?> FindByIdAsync(Guid shareId, CancellationToken cancellationToken);

    Task<Share?> FindByTargetAsync(Guid targetEntryId, CancellationToken cancellationToken);

    Task<Share?> ReloadAsync(Share share, CancellationToken cancellationToken) => Task.FromResult<Share?>(share);

    Task<ShareView?> GetViewAsync(Guid shareId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShareView>> ListViewsAsync(
        Guid actorUserId,
        ShareScope scope,
        FileEntryType? targetType,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<int> CountViewsAsync(
        Guid actorUserId,
        ShareScope scope,
        FileEntryType? targetType,
        CancellationToken cancellationToken);

    void Add(Share share);

    void Add(AuditLog auditLog);

    void Remove(Share share);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class SharePersistenceConflictException : Exception
{
    public SharePersistenceConflictException(Exception innerException)
        : base("A sharing persistence conflict occurred.", innerException)
    {
    }
}
