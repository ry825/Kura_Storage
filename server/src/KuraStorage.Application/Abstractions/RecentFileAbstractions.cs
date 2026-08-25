using KuraStorage.Application.Recent;

namespace KuraStorage.Application.Abstractions;

public interface IRecentFileRepository
{
    Task<bool> TryUpsertAuthorizedAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken);

    Task<RecentFilePage> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
