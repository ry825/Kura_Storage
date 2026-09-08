using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaMaintenanceRepository(KuraStorageDbContext database) : IMediaMaintenanceRepository
{
    private const long MaintenanceLockKey = 5_427_781_528_102_636_112;

    public async Task<IAsyncDisposable?> TryAcquireMaintenanceLockAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(database.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key);", connection);
            command.Parameters.AddWithValue("key", MaintenanceLockKey);
            if (!(bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                await connection.DisposeAsync();
                return null;
            }
            return new MaintenanceLock(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<MediaMaintenanceCandidate>> ClaimDeletingAsync(
        DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        await database.FileDerivatives.AsNoTracking()
            .Where(item => item.Status == Domain.Media.DerivativeStatus.Deleting && item.RelativePath != null && item.Size > 0 &&
                !database.DerivativeLeases.Any(lease => lease.DerivativeId == item.Id && lease.ExpiresAt > now))
            .OrderBy(item => item.UpdatedAt).ThenBy(item => item.Id).Take(batchSize)
            .Select(item => new MediaMaintenanceCandidate(item.Id, RelativeStoragePath.Create(item.RelativePath!), item.Size,
                item.ErrorCode == "MEDIA_CACHE_CLEANUP"))
            .ToListAsync(cancellationToken);

    public async Task CompleteDeleteAsync(Guid derivativeId, CancellationToken cancellationToken)
    {
        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM file_derivatives WHERE id = {derivativeId} AND status = 'DELETING';", cancellationToken);
        database.ChangeTracker.Clear();
        if (deleted != 1) throw new InvalidOperationException("The maintenance deletion state was lost.");
    }

    public async Task RestoreReadyAsync(Guid derivativeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var restored = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE file_derivatives SET status = 'READY', error_code = NULL, revision = revision + 1, updated_at = {now}
            WHERE id = {derivativeId} AND status = 'DELETING' AND relative_path IS NOT NULL AND size > 0;
            """, cancellationToken);
        database.ChangeTracker.Clear();
        if (restored != 1) throw new InvalidOperationException("The derivative could not be restored.");
    }

    public async Task<int> DeleteTerminalJobsAsync(DateTimeOffset completedBefore, int batchSize, CancellationToken cancellationToken)
    {
        var deleted = await database.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidates AS (
                SELECT job.id FROM media_jobs AS job
                WHERE job.status IN ('COMPLETED', 'FAILED', 'CANCELLED') AND job.completed_at <= {completedBefore}
                  AND NOT EXISTS (SELECT 1 FROM media_jobs AS active WHERE active.derivative_id = job.derivative_id AND active.status IN ('QUEUED', 'RUNNING'))
                ORDER BY job.completed_at, job.id FOR UPDATE SKIP LOCKED LIMIT {batchSize}
            ) DELETE FROM media_jobs AS job USING candidates WHERE job.id = candidates.id;
            """, cancellationToken);
        database.ChangeTracker.Clear();
        return deleted;
    }

    private sealed class MaintenanceLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", connection);
            command.Parameters.AddWithValue("key", MaintenanceLockKey);
            await command.ExecuteNonQueryAsync();
            await connection.DisposeAsync();
        }
    }
}
