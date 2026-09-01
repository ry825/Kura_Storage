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

    public void Add(FileVersionRecord record) => database.FileVersionRecords.Add(record);
}
