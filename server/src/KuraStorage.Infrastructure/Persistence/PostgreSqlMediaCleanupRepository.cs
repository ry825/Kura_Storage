using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaCleanupRepository(KuraStorageDbContext database) : IMediaCleanupRepository
{
    private const long CleanupLockKey = 5_427_781_528_102_636_112;

    public async Task<IAsyncDisposable?> TryAcquireCleanupLockAsync(CancellationToken cancellationToken)
    {
        var connectionString = database.Database.GetConnectionString() ??
            throw new InvalidOperationException("The media cleanup database connection is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key);", connection);
            command.Parameters.AddWithValue("key", CleanupLockKey);
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new CleanupLock(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken) =>
        ClaimAsync(now, batchSize, expiredOnly: true, cancellationToken);

    public async Task<IReadOnlyList<MediaCleanupCandidate>> ClaimDeletingAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        return await database.FileDerivatives
            .AsNoTracking()
            .Where(item => item.Status == Domain.Media.DerivativeStatus.Deleting &&
                item.RelativePath != null && item.Size > 0 &&
                !database.DerivativeLeases.Any(lease =>
                    lease.DerivativeId == item.Id && lease.ExpiresAt > now))
            .OrderBy(item => item.UpdatedAt)
            .ThenBy(item => item.Id)
            .Take(batchSize)
            .Select(item => new MediaCleanupCandidate(
                item.Id,
                RelativeStoragePath.Create(item.RelativePath!),
                item.Size,
                item.ErrorCode == "MEDIA_CACHE_CLEANUP"))
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<MediaCleanupCandidate>> ClaimLruAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken) =>
        ClaimAsync(now, batchSize, expiredOnly: false, cancellationToken);

    public async Task<long> GetReadyCacheSizeAsync(CancellationToken cancellationToken) =>
        await database.FileDerivatives
            .Where(item =>
                (item.DerivativeType == Domain.Media.DerivativeType.ImageLow ||
                 item.DerivativeType == Domain.Media.DerivativeType.ImageMedium ||
                 item.DerivativeType == Domain.Media.DerivativeType.VideoLow ||
                 item.DerivativeType == Domain.Media.DerivativeType.VideoMedium) &&
                item.Status == Domain.Media.DerivativeStatus.Ready)
            .SumAsync(item => (long?)item.Size, cancellationToken) ?? 0;

    public async Task CompleteDeleteAsync(Guid derivativeId, CancellationToken cancellationToken)
    {
        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM file_derivatives WHERE id = {derivativeId} AND status = 'DELETING';",
            cancellationToken);
        database.ChangeTracker.Clear();
        if (deleted != 1)
        {
            throw new InvalidOperationException("The claimed derivative cleanup state was lost.");
        }
    }

    public async Task RestoreReadyAsync(
        Guid derivativeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var restored = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE file_derivatives
            SET status = 'READY', error_code = NULL, revision = revision + 1, updated_at = {now}
            WHERE id = {derivativeId}
              AND status = 'DELETING'
              AND relative_path IS NOT NULL
              AND size > 0;
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        if (restored != 1)
        {
            throw new InvalidOperationException("The derivative could not be restored for cleanup retry.");
        }
    }

    public async Task<int> DeleteTerminalJobsAsync(
        DateTimeOffset completedBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH candidates AS (
                SELECT job.id
                FROM media_jobs AS job
                WHERE job.status IN ('COMPLETED', 'FAILED', 'CANCELLED')
                  AND job.completed_at <= {completedBefore}
                  AND NOT EXISTS (
                      SELECT 1
                      FROM media_jobs AS active
                      WHERE active.derivative_id = job.derivative_id
                        AND active.status IN ('QUEUED', 'RUNNING'))
                ORDER BY job.completed_at, job.id
                FOR UPDATE SKIP LOCKED
                LIMIT {batchSize}
            )
            DELETE FROM media_jobs AS job
            USING candidates
            WHERE job.id = candidates.id;
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return deleted;
    }

    private async Task<IReadOnlyList<MediaCleanupCandidate>> ClaimAsync(
        DateTimeOffset now,
        int batchSize,
        bool expiredOnly,
        CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var expiryClause = expiredOnly ? "AND derivative.expires_at <= @now" : string.Empty;
        await using var command = new NpgsqlCommand(
            $"""
            WITH candidates AS (
                SELECT derivative.id
                FROM file_derivatives AS derivative
                WHERE derivative.status = 'READY'
                  AND derivative.derivative_type IN ('IMAGE_LOW', 'IMAGE_MEDIUM', 'VIDEO_LOW', 'VIDEO_MEDIUM')
                  {expiryClause}
                  AND NOT EXISTS (
                      SELECT 1
                      FROM derivative_leases AS lease
                      WHERE lease.derivative_id = derivative.id
                        AND lease.expires_at > @now)
                ORDER BY derivative.last_accessed_at, derivative.created_at, derivative.id
                FOR UPDATE SKIP LOCKED
                LIMIT @batch_size
            ), claimed AS (
                UPDATE file_derivatives AS derivative
                SET status = 'DELETING', error_code = 'MEDIA_CACHE_CLEANUP',
                    revision = revision + 1, updated_at = @now
                FROM candidates
                WHERE derivative.id = candidates.id
                  AND derivative.status = 'READY'
                RETURNING derivative.id, derivative.relative_path, derivative.size,
                    derivative.last_accessed_at, derivative.created_at
            )
            SELECT id, relative_path, size
            FROM claimed
            ORDER BY last_accessed_at, created_at, id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("batch_size", batchSize);
        var candidates = new List<MediaCleanupCandidate>(batchSize);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new MediaCleanupCandidate(
                    reader.GetGuid(0),
                    RelativeStoragePath.Create(reader.GetString(1)),
                    reader.GetInt64(2)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return candidates;
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
    }

    private sealed class CleanupLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", connection);
                command.Parameters.AddWithValue("key", CleanupLockKey);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
