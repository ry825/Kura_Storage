using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaCleanupRepository(KuraStorageDbContext database) : IMediaCleanupRepository
{
    private const long CleanupLockKey = 5_427_781_528_102_636_112;
    private const long ScheduledRunLockKey = 5_427_781_528_102_636_113;

    public async Task<MediaCacheSnapshot> GetCacheSnapshotAsync(CancellationToken cancellationToken)
    {
        var cache = await database.FileDerivatives
            .AsNoTracking()
            .Where(item => item.Status == DerivativeStatus.Ready &&
                (item.DerivativeType == DerivativeType.ImageLow ||
                 item.DerivativeType == DerivativeType.ImageMedium ||
                 item.DerivativeType == DerivativeType.VideoLow ||
                 item.DerivativeType == DerivativeType.VideoMedium))
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ImageLow = group.Where(item => item.DerivativeType == DerivativeType.ImageLow).Sum(item => (long?)item.Size) ?? 0,
                ImageMedium = group.Where(item => item.DerivativeType == DerivativeType.ImageMedium).Sum(item => (long?)item.Size) ?? 0,
                VideoLow = group.Where(item => item.DerivativeType == DerivativeType.VideoLow).Sum(item => (long?)item.Size) ?? 0,
                VideoMedium = group.Where(item => item.DerivativeType == DerivativeType.VideoMedium).Sum(item => (long?)item.Size) ?? 0,
            })
            .SingleOrDefaultAsync(cancellationToken);
        var jobs = await database.MediaJobs
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Queued = group.Count(job => job.Status == MediaJobStatus.Queued),
                Running = group.Count(job => job.Status == MediaJobStatus.Running),
                Failed = group.Count(job => job.Status == MediaJobStatus.Failed),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var runs = await database.MediaCleanupRuns
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(run => run.Status == MediaCleanupRunStatus.Pending),
                Running = group.Count(run => run.Status == MediaCleanupRunStatus.Running),
            })
            .SingleOrDefaultAsync(cancellationToken);
        return new MediaCacheSnapshot(
            cache?.ImageLow ?? 0,
            cache?.ImageMedium ?? 0,
            cache?.VideoLow ?? 0,
            cache?.VideoMedium ?? 0,
            jobs?.Queued ?? 0,
            jobs?.Running ?? 0,
            jobs?.Failed ?? 0,
            runs?.Pending ?? 0,
            runs?.Running ?? 0);
    }

    public Task<MediaCleanupRun?> FindLatestRunAsync(CancellationToken cancellationToken) =>
        database.MediaCleanupRuns
            .AsNoTracking()
            .OrderByDescending(run => run.RequestedAt)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<MediaCleanupRequestPersistenceResult> CreateOrGetManualRunAsync(
        Guid requestingAdminUserId,
        string idempotencyKeyHash,
        string requestFingerprintHash,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        var existing = await FindManualRunAsync(requestingAdminUserId, idempotencyKeyHash, cancellationToken);
        if (existing is not null)
        {
            return new MediaCleanupRequestPersistenceResult(existing, existing.RequestFingerprintHash != requestFingerprintHash);
        }

        var created = MediaCleanupRun.CreateManual(
            Guid.NewGuid(), requestingAdminUserId, idempotencyKeyHash, requestFingerprintHash, requestedAt);
        database.MediaCleanupRuns.Add(created);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new MediaCleanupRequestPersistenceResult(created, false);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            existing = await FindManualRunAsync(requestingAdminUserId, idempotencyKeyHash, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new MediaCleanupRequestPersistenceResult(existing, existing.RequestFingerprintHash != requestFingerprintHash);
        }
    }

    public async Task<MediaCleanupRun?> EnsureScheduledRunAsync(
        DateTimeOffset now,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({ScheduledRunLockKey});",
            cancellationToken);
        var active = await database.MediaCleanupRuns
            .AsNoTracking()
            .Where(run => run.Trigger == MediaCleanupTrigger.Scheduled &&
                (run.Status == MediaCleanupRunStatus.Pending || run.Status == MediaCleanupRunStatus.Running))
            .OrderBy(run => run.RequestedAt)
            .ThenBy(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return active;
        }

        var latestRequestedAt = await database.MediaCleanupRuns
            .AsNoTracking()
            .Where(run => run.Trigger == MediaCleanupTrigger.Scheduled)
            .MaxAsync(run => (DateTimeOffset?)run.RequestedAt, cancellationToken);
        if (latestRequestedAt is not null && latestRequestedAt > now.Subtract(interval))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var created = MediaCleanupRun.CreateScheduled(Guid.NewGuid(), now);
        database.MediaCleanupRuns.Add(created);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<MediaCleanupRun?> ClaimNextRunAsync(
        Guid workerToken,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        if (workerToken == Guid.Empty || leaseExpiresAt <= now)
        {
            throw new ArgumentException("A worker token and future lease are required.");
        }

        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH candidate AS (
                SELECT id
                FROM media_cleanup_runs
                WHERE status = 'PENDING'
                   OR (status = 'RUNNING' AND lease_expires_at <= @now)
                ORDER BY CASE WHEN trigger = 'MANUAL' THEN 0 ELSE 1 END, requested_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE media_cleanup_runs AS run
            SET status = 'RUNNING', worker_token = @worker_token, lease_expires_at = @lease_expires_at,
                started_at = COALESCE(started_at, @now), completed_at = NULL, failure_code = NULL
            FROM candidate
            WHERE run.id = candidate.id
            RETURNING run.id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("worker_token", workerToken);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("lease_expires_at", leaseExpiresAt);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var claimedId = scalar is Guid id ? id : (Guid?)null;
        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return claimedId is null
            ? null
            : await database.MediaCleanupRuns.AsNoTracking().SingleAsync(run => run.Id == claimedId, cancellationToken);
    }

    public async Task<bool> ReleaseRunAsync(
        Guid runId,
        Guid workerToken,
        CancellationToken cancellationToken)
    {
        var updated = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE media_cleanup_runs
            SET status = 'PENDING', worker_token = NULL, lease_expires_at = NULL
            WHERE id = {runId} AND status = 'RUNNING' AND worker_token = {workerToken};
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return updated == 1;
    }

    public async Task<bool> CompleteRunAsync(
        Guid runId,
        Guid workerToken,
        DateTimeOffset completedAt,
        MediaCleanupResult result,
        CancellationToken cancellationToken)
    {
        var status = result.FailureCount == 0 ? "COMPLETED" : "FAILED";
        var failureCode = result.FailureCount == 0 ? null : "PARTIAL_DELETE_FAILURE";
        var examined = checked(result.DeletedCount + result.FailureCount);
        var updated = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE media_cleanup_runs
            SET status = {status}, worker_token = NULL, lease_expires_at = NULL, completed_at = {completedAt},
                examined_count = {examined}, deleted_count = {result.DeletedCount}, released_bytes = {result.DeletedBytes},
                failure_count = {result.FailureCount}, remaining_cache_bytes = {result.RemainingCacheBytes},
                failure_code = {failureCode}
            WHERE id = {runId} AND status = 'RUNNING' AND worker_token = {workerToken};
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return updated == 1;
    }

    public async Task<bool> FailRunAsync(
        Guid runId,
        Guid workerToken,
        DateTimeOffset completedAt,
        MediaCleanupFailureCode failureCode,
        CancellationToken cancellationToken)
    {
        var code = failureCode switch
        {
            MediaCleanupFailureCode.StorageUnavailable => "STORAGE_UNAVAILABLE",
            MediaCleanupFailureCode.PartialDeleteFailure => "PARTIAL_DELETE_FAILURE",
            MediaCleanupFailureCode.CleanupFailed => "CLEANUP_FAILED",
            _ => throw new ArgumentOutOfRangeException(nameof(failureCode)),
        };
        var updated = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE media_cleanup_runs
            SET status = 'FAILED', worker_token = NULL, lease_expires_at = NULL, completed_at = {completedAt},
                failure_count = GREATEST(failure_count, 1), failure_code = {code}
            WHERE id = {runId} AND status = 'RUNNING' AND worker_token = {workerToken};
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return updated == 1;
    }

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

    private Task<MediaCleanupRun?> FindManualRunAsync(
        Guid requestingAdminUserId,
        string idempotencyKeyHash,
        CancellationToken cancellationToken) =>
        database.MediaCleanupRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.Trigger == MediaCleanupTrigger.Manual &&
                    run.RequestedByAdminUserId == requestingAdminUserId &&
                    run.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);

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
