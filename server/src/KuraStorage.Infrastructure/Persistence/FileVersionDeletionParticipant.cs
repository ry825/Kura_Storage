using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class FileVersionDeletionParticipant(KuraStorageDbContext database) :
    IPermanentDeleteParticipant,
    IFileIndexDeletionParticipant
{
    public Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RelativeStoragePath>>(
            Paths(target.OwnerUserId, target.DescendantIds.Append(target.RootId)));

    public Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        FileIndexDeletionTarget target,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RelativeStoragePath>>(
            Paths(target.OwnerUserId, target.EntryIds));

    public Task DeleteManagementDataAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken) =>
        DeleteManagementDataAsync(target.DescendantIds.Append(target.RootId).ToArray(), cancellationToken);

    public Task DeleteManagementDataAsync(
        FileIndexDeletionTarget target,
        CancellationToken cancellationToken) =>
        DeleteManagementDataAsync(target.EntryIds, cancellationToken);

    private async Task DeleteManagementDataAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return;
        }

        var records = await database.FileVersionRecords
            .Where(record => entryIds.Contains(record.FileEntryId))
            .ToListAsync(cancellationToken);
        database.FileVersionRecords.RemoveRange(records);
    }

    private static RelativeStoragePath[] Paths(Guid ownerUserId, IEnumerable<Guid> entryIds) =>
        entryIds
            .Where(entryId => entryId != Guid.Empty)
            .Distinct()
            .Order()
            .Select(entryId => RelativeStoragePath.Create($"versions/{ownerUserId:N}/{entryId:N}"))
            .ToArray();
}
