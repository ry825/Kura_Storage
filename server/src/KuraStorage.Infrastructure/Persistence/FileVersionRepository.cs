using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class FileVersionRepository(KuraStorageDbContext database) : IFileVersionRepository
{
    public Task<FileVersionRecord?> FindAsync(
        Guid fileEntryId,
        long version,
        CancellationToken cancellationToken) =>
        database.FileVersionRecords.SingleOrDefaultAsync(
            record => record.FileEntryId == fileEntryId && record.Version == version,
            cancellationToken);

    public async Task<IReadOnlyList<FileVersionHistoryRow>> ListAsync(
        Guid fileEntryId,
        long maximumVersion,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await (
            from record in database.FileVersionRecords.AsNoTracking()
            join user in database.Users.AsNoTracking()
                on record.ActorUserId equals (Guid?)user.Id into actorUsers
            from actor in actorUsers.DefaultIfEmpty()
            where record.FileEntryId == fileEntryId && record.Version <= maximumVersion
            orderby record.Version descending
            select new FileVersionHistoryRow(
                record.Version,
                record.Size,
                record.Sha256,
                record.ChangeKind,
                actor == null ? null : actor.DisplayName,
                record.CreatedAt)
        )
        .Skip(skip)
        .Take(take)
        .ToListAsync(cancellationToken);

    public Task<int> CountAsync(
        Guid fileEntryId,
        long maximumVersion,
        CancellationToken cancellationToken) =>
        database.FileVersionRecords.CountAsync(
            record => record.FileEntryId == fileEntryId && record.Version <= maximumVersion,
            cancellationToken);

    public void Add(FileVersionRecord record) => database.FileVersionRecords.Add(record);
}
