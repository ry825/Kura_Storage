using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class IndexCatalogRepository(KuraStorageDbContext dbContext) : IIndexCatalogRepository
{
    private const long GlobalScanLockKey = 0x4b555241494e4458;

    public async Task<IIndexScanLock?> TryAcquireScanLockAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", GlobalScanLockKey);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new IndexScanLock(connection);
    }

    public async Task<IIndexScanWorkspace> CreateWorkspaceAsync(
        Guid scanId,
        IndexScanMode mode,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        if (mode == IndexScanMode.DryRun)
        {
            await using var command = new NpgsqlCommand(
                "CREATE TEMP TABLE index_scan_items_dry_run (LIKE index_scan_items INCLUDING DEFAULTS) ON COMMIT PRESERVE ROWS",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new IndexScanWorkspace(connection, scanId, mode == IndexScanMode.DryRun);
    }

    public async Task<FileEntry?> FindEntryByPathAsync(
        Guid ownerUserId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var local = dbContext.FileEntries.Local.SingleOrDefault(
            entry => entry.OwnerUserId == ownerUserId &&
                     entry.RelativePath == relativePath &&
                     entry.Status != FileEntryStatus.Trashed);
        return local ?? await dbContext.FileEntries.SingleOrDefaultAsync(
            entry => entry.OwnerUserId == ownerUserId &&
                     entry.RelativePath == relativePath &&
                     entry.Status != FileEntryStatus.Trashed,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FileEntry>> FindEntriesByPathsAsync(
        IReadOnlyList<IndexPathKey> paths,
        CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var requested = paths.ToHashSet();
        var pathValues = paths.Select(path => path.RelativePath).Distinct(StringComparer.Ordinal).ToArray();
        var result = dbContext.FileEntries.Local
            .Where(entry => entry.Status != FileEntryStatus.Trashed &&
                            requested.Contains(new IndexPathKey(entry.OwnerUserId, entry.RelativePath)))
            .ToDictionary(entry => entry.Id);
        var persisted = await dbContext.FileEntries
            .Where(entry => entry.Status != FileEntryStatus.Trashed && pathValues.Contains(entry.RelativePath))
            .ToListAsync(cancellationToken);
        foreach (var entry in persisted)
        {
            if (requested.Contains(new IndexPathKey(entry.OwnerUserId, entry.RelativePath)))
            {
                result[entry.Id] = entry;
            }
        }

        return result.Values.ToArray();
    }

    public async Task<FileEntry?> FindEntryByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.FileEntries.Local.SingleOrDefault(entry => entry.Id == id) ??
        await dbContext.FileEntries.SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    public async Task<FileEntry?> FindRootAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.FileEntries.Local.SingleOrDefault(
            entry => entry.OwnerUserId == ownerUserId && entry.ParentId == null && entry.Status == FileEntryStatus.Active) ??
        await dbContext.FileEntries.SingleOrDefaultAsync(
            entry => entry.OwnerUserId == ownerUserId && entry.ParentId == null && entry.Status == FileEntryStatus.Active,
            cancellationToken);

    public Task<bool> HasIncompleteOperationAsync(
        Guid ownerUserId,
        Guid entryId,
        string relativePath,
        CancellationToken cancellationToken) =>
        dbContext.FileOperations.AnyAsync(
            operation => operation.OwnerUserId == ownerUserId &&
                         operation.Status != FileOperationStatus.Completed &&
                         (operation.FileEntryId == entryId ||
                          operation.SourceRelativePath == relativePath ||
                          operation.TargetRelativePath == relativePath ||
                          (operation.SourceRelativePath != null && relativePath.StartsWith(operation.SourceRelativePath + "/")) ||
                          (operation.TargetRelativePath != null && relativePath.StartsWith(operation.TargetRelativePath + "/"))),
            cancellationToken);

    public async Task<IReadOnlySet<Guid>> FindEntriesWithIncompleteOperationsAsync(
        IReadOnlyList<IndexOperationKey> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var owners = entries.Select(entry => entry.OwnerUserId).Distinct().ToArray();
        var operations = await dbContext.FileOperations
            .AsNoTracking()
            .Where(operation => owners.Contains(operation.OwnerUserId) &&
                                operation.Status != FileOperationStatus.Completed)
            .ToListAsync(cancellationToken);
        return entries
            .Where(entry => operations.Any(operation =>
                operation.OwnerUserId == entry.OwnerUserId &&
                (operation.FileEntryId == entry.EntryId ||
                 operation.SourceRelativePath == entry.RelativePath ||
                 operation.TargetRelativePath == entry.RelativePath ||
                 (operation.SourceRelativePath != null &&
                  entry.RelativePath.StartsWith(operation.SourceRelativePath + "/", StringComparison.Ordinal)) ||
                 (operation.TargetRelativePath != null &&
                  entry.RelativePath.StartsWith(operation.TargetRelativePath + "/", StringComparison.Ordinal)))))
            .Select(entry => entry.EntryId)
            .ToHashSet();
    }

    public async Task<IReadOnlyList<FileEntry>> ListDescendantsAsync(
        Guid ownerUserId,
        string relativePathPrefix,
        CancellationToken cancellationToken) =>
        await dbContext.FileEntries
            .Where(entry => entry.OwnerUserId == ownerUserId &&
                            entry.RelativePath.StartsWith(relativePathPrefix + "/") &&
                            entry.Status != FileEntryStatus.Trashed)
            .OrderBy(entry => entry.RelativePath)
            .ToListAsync(cancellationToken);

    public async Task<(int CandidateCount, int MissingCount)> CountMissingStatesAsync(
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.FileEntries
            .Where(entry => entry.Status == FileEntryStatus.MissingCandidate ||
                            entry.Status == FileEntryStatus.Missing)
            .GroupBy(entry => entry.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return (
            counts.SingleOrDefault(item => item.Status == FileEntryStatus.MissingCandidate)?.Count ?? 0,
            counts.SingleOrDefault(item => item.Status == FileEntryStatus.Missing)?.Count ?? 0);
    }

    public void Add(FileEntry entry) => dbContext.FileEntries.Add(entry);
    public void Add(IndexScanRun run) => dbContext.IndexScanRuns.Add(run);
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            foreach (var entry in exception.Entries)
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
                else
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }

            throw new IndexCatalogConcurrencyException();
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
            })
        {
            // Event and full-scan reconciliation may discover the same new path concurrently.
            // Keep the database winner and discard this stale batch; the next event/scan converges it.
            foreach (var entry in dbContext.ChangeTracker.Entries<FileEntry>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }

            throw new IndexCatalogConcurrencyException();
        }
    }

    public async Task CleanupStagingAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await dbContext.IndexScanRuns
            .Where(run => run.StartedAt < cutoff && run.Status == IndexScanStatus.Running)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(run => run.Status, IndexScanStatus.Failed)
                    .SetProperty(run => run.CompletedAt, cutoff)
                    .SetProperty(run => run.ErrorCode, "STALE_RUN")
                    .SetProperty(run => run.ErrorCount, run => run.ErrorCount + 1),
                cancellationToken);
        await dbContext.IndexScanItems
            .Where(item => dbContext.IndexScanRuns.Any(
                run => run.Id == item.ScanId && run.StartedAt < cutoff && run.Status != IndexScanStatus.Running))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RecoverInterruptedRunsAsync(
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken)
    {
        await dbContext.IndexScanRuns
            .Where(run => run.Status == IndexScanStatus.Running)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(run => run.Status, IndexScanStatus.Failed)
                    .SetProperty(run => run.CompletedAt, recoveredAt)
                    .SetProperty(run => run.ErrorCode, "WORKER_INTERRUPTED")
                    .SetProperty(run => run.ErrorCount, run => run.ErrorCount + 1),
                cancellationToken);
    }

    private sealed class IndexScanLock(NpgsqlConnection connection) : IIndexScanLock
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", GlobalScanLockKey);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    private sealed class IndexScanWorkspace(NpgsqlConnection connection, Guid scanId, bool dryRun)
        : IIndexScanWorkspace
    {
        private string TableName => dryRun ? "index_scan_items_dry_run" : "index_scan_items";

        public async Task StageAsync(
            IReadOnlyList<ObservedStorageEntry> entries,
            CancellationToken cancellationToken)
        {
            if (entries.Count == 0)
            {
                return;
            }

            await using var importer = await connection.BeginBinaryImportAsync(
                $"COPY {TableName} (scan_id, relative_path, owner_user_id, parent_relative_path, name, entry_type, size, mime_type, source_modified_at, source_file_key, isolation_reason) FROM STDIN (FORMAT BINARY)",
                cancellationToken);
            foreach (var entry in entries)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(scanId, cancellationToken);
                await importer.WriteAsync(entry.RelativePath.Value, cancellationToken);
                await importer.WriteAsync(entry.OwnerUserId, cancellationToken);
                await importer.WriteAsync(entry.ParentRelativePath.Value, cancellationToken);
                await importer.WriteAsync(entry.Name.Value, cancellationToken);
                await importer.WriteAsync(entry.EntryType.ToString().ToUpperInvariant(), cancellationToken);
                await importer.WriteAsync(entry.Size, cancellationToken);
                if (entry.MimeType is null)
                {
                    await importer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await importer.WriteAsync(entry.MimeType, cancellationToken);
                }
                await importer.WriteAsync(entry.SourceModifiedAt, cancellationToken);
                if (entry.SourceFileKey is null)
                {
                    await importer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await importer.WriteAsync(entry.SourceFileKey, cancellationToken);
                }

                if (entry.IsolationReason is null)
                {
                    await importer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await importer.WriteAsync(entry.IsolationReason, cancellationToken);
                }
            }

            await importer.CompleteAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<StagedIndexEntry>> ListStagedAsync(
            string? afterRelativePath,
            int take,
            CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT owner_user_id, relative_path, parent_relative_path, name, entry_type, size, mime_type, source_modified_at, source_file_key, isolation_reason FROM {TableName} WHERE scan_id = @scan AND (@after IS NULL OR relative_path > @after) ORDER BY relative_path LIMIT @take",
                connection);
            command.Parameters.AddWithValue("scan", scanId);
            command.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.Text)
            {
                Value = (object?)afterRelativePath ?? DBNull.Value,
            });
            command.Parameters.AddWithValue("take", take);
            var result = new List<StagedIndexEntry>(take);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new StagedIndexEntry(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    Enum.Parse<FileEntryType>(reader.GetString(4), true), reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }

            return result;
        }

        public async Task<IReadOnlyList<IndexedCatalogEntry>> ListUnobservedAsync(
            Guid? afterOwnerUserId,
            string? afterRelativePath,
            int take,
            CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT f.id, f.owner_user_id, f.relative_path, f.entry_type, f.status, f.source_file_key, f.size, f.source_modified_at, f.missing_detected_at, f.missing_observation_id FROM file_entries f WHERE f.parent_id IS NOT NULL AND f.status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING') AND NOT EXISTS (SELECT 1 FROM {TableName} s WHERE s.scan_id = @scan AND s.owner_user_id = f.owner_user_id AND s.relative_path = f.relative_path) AND (@owner IS NULL OR (f.owner_user_id, f.relative_path) > (@owner, @path)) ORDER BY f.owner_user_id, f.relative_path LIMIT @take",
                connection);
            command.Parameters.AddWithValue("scan", scanId);
            command.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Uuid)
            {
                Value = (object?)afterOwnerUserId ?? DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Text)
            {
                Value = (object?)afterRelativePath ?? string.Empty,
            });
            command.Parameters.AddWithValue("take", take);
            var result = new List<IndexedCatalogEntry>(take);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new IndexedCatalogEntry(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    Enum.Parse<FileEntryType>(reader.GetString(3), true),
                    ParseFileEntryStatus(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetGuid(9)));
            }

            return result;
        }

        public async Task<bool> ContainsAsync(
            Guid ownerUserId,
            string relativePath,
            CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT EXISTS (SELECT 1 FROM {TableName} WHERE scan_id = @scan AND owner_user_id = @owner AND relative_path = @path)",
                connection);
            command.Parameters.AddWithValue("scan", scanId);
            command.Parameters.AddWithValue("owner", ownerUserId);
            command.Parameters.AddWithValue("path", relativePath);
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }

        public async Task<IReadOnlyList<IndexedCatalogEntry>> FindMoveCandidatesAsync(
            StagedIndexEntry observed,
            CancellationToken cancellationToken)
        {
            if (observed.SourceFileKey is null)
            {
                return [];
            }

            await using var command = new NpgsqlCommand(
                $"SELECT f.id, f.owner_user_id, f.relative_path, f.entry_type, f.status, f.source_file_key, f.size, f.source_modified_at, f.missing_detected_at, f.missing_observation_id FROM file_entries f WHERE f.owner_user_id = @owner AND f.parent_id IS NOT NULL AND f.status = 'ACTIVE' AND f.entry_type = @type AND f.source_file_key = @key AND f.size = @size AND f.source_modified_at = @modified AND f.relative_path <> @path AND NOT EXISTS (SELECT 1 FROM {TableName} s WHERE s.scan_id = @scan AND s.owner_user_id = f.owner_user_id AND s.relative_path = f.relative_path) ORDER BY f.id LIMIT 2",
                connection);
            command.Parameters.AddWithValue("owner", observed.OwnerUserId);
            command.Parameters.AddWithValue("type", observed.EntryType.ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("key", observed.SourceFileKey);
            command.Parameters.AddWithValue("size", observed.Size);
            command.Parameters.AddWithValue("modified", observed.SourceModifiedAt);
            command.Parameters.AddWithValue("path", observed.RelativePath);
            command.Parameters.AddWithValue("scan", scanId);
            var result = new List<IndexedCatalogEntry>(2);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new IndexedCatalogEntry(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    Enum.Parse<FileEntryType>(reader.GetString(3), true),
                    ParseFileEntryStatus(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetGuid(9)));
            }

            return result;
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<IndexedCatalogEntry>>> FindMoveCandidatesBatchAsync(
            IReadOnlyList<StagedIndexEntry> observed,
            CancellationToken cancellationToken)
        {
            var result = observed.ToDictionary(
                entry => entry.RelativePath,
                _ => (IReadOnlyList<IndexedCatalogEntry>)Array.Empty<IndexedCatalogEntry>(),
                StringComparer.Ordinal);
            var paths = observed
                .Where(entry => entry.SourceFileKey is not null && entry.IsolationReason is null)
                .Select(entry => entry.RelativePath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0)
            {
                return result;
            }

            await using var command = new NpgsqlCommand(
                $"SELECT s.relative_path, f.id, f.owner_user_id, f.relative_path, f.entry_type, f.status, f.source_file_key, f.size, f.source_modified_at, f.missing_detected_at, f.missing_observation_id FROM {TableName} s JOIN file_entries f ON f.owner_user_id = s.owner_user_id AND f.entry_type = s.entry_type AND f.source_file_key = s.source_file_key AND f.size = s.size AND f.source_modified_at = s.source_modified_at WHERE s.scan_id = @scan AND s.relative_path = ANY(@paths) AND s.source_file_key IS NOT NULL AND s.isolation_reason IS NULL AND f.parent_id IS NOT NULL AND f.status = 'ACTIVE' AND f.relative_path <> s.relative_path AND NOT EXISTS (SELECT 1 FROM {TableName} old WHERE old.scan_id = @scan AND old.owner_user_id = f.owner_user_id AND old.relative_path = f.relative_path) ORDER BY s.relative_path, f.id",
                connection);
            command.Parameters.AddWithValue("scan", scanId);
            command.Parameters.Add(new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = paths,
            });
            var candidates = new Dictionary<string, List<IndexedCatalogEntry>>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var observedPath = reader.GetString(0);
                if (!candidates.TryGetValue(observedPath, out var entries))
                {
                    entries = [];
                    candidates[observedPath] = entries;
                }

                if (entries.Count < 2)
                {
                    entries.Add(new IndexedCatalogEntry(
                        reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                        Enum.Parse<FileEntryType>(reader.GetString(4), true),
                        ParseFileEntryStatus(reader.GetString(5)),
                        reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt64(7),
                        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                        reader.IsDBNull(10) ? null : reader.GetGuid(10)));
                }
            }

            foreach (var candidate in candidates)
            {
                result[candidate.Key] = candidate.Value;
            }

            return result;
        }

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                $"DELETE FROM {TableName} WHERE scan_id = @scan",
                connection);
            command.Parameters.AddWithValue("scan", scanId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();

        private static FileEntryStatus ParseFileEntryStatus(string value) =>
            value == "MISSING_CANDIDATE"
                ? FileEntryStatus.MissingCandidate
                : Enum.Parse<FileEntryStatus>(value, true);
    }
}
