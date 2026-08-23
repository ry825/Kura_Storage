using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class SharingDeletionParticipant(KuraStorageDbContext dbContext) :
    IPermanentDeleteParticipant,
    IFileIndexDeletionParticipant
{
    public Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RelativeStoragePath>>([]);

    public Task DeleteManagementDataAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken) =>
        DeleteSharesAsync(target.DescendantIds.Append(target.RootId).ToArray(), cancellationToken);

    public Task DeleteManagementDataAsync(
        FileIndexDeletionTarget target,
        CancellationToken cancellationToken) =>
        DeleteSharesAsync(target.EntryIds, cancellationToken);

    private async Task DeleteSharesAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return;
        }

        var shares = await dbContext.Shares
            .Where(share => entryIds.Contains(share.TargetEntryId))
            .ToListAsync(cancellationToken);
        dbContext.Shares.RemoveRange(shares);
    }
}
