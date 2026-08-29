using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaJobQueue(KuraStorageDbContext database) : IMediaJobQueue
{
    public async Task<MediaJob?> TryAcquireNextAsync(
        Guid workerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (workerToken == Guid.Empty)
        {
            throw new ArgumentException("A worker token is required.", nameof(workerToken));
        }

        const string sql =
            """
            WITH gate AS MATERIALIZED (
                SELECT pg_advisory_xact_lock(1263815501)
            ), candidate AS (
                SELECT job.id, job.derivative_id
                FROM media_jobs AS job
                INNER JOIN file_derivatives AS derivative ON derivative.id = job.derivative_id
                CROSS JOIN gate
                WHERE job.status = 'QUEUED'
                  AND job.available_at <= @now
                  AND job.attempt_count < 3
                  AND derivative.status = 'PENDING'
                  AND NOT EXISTS (SELECT 1 FROM media_jobs AS running WHERE running.status = 'RUNNING')
                ORDER BY job.created_at, job.id
                FOR UPDATE OF job, derivative SKIP LOCKED
                LIMIT 1
            ), acquired AS (
                UPDATE media_jobs AS job
                SET status = 'RUNNING',
                    worker_token = @worker_token,
                    attempt_count = job.attempt_count + 1,
                    started_at = COALESCE(job.started_at, @now),
                    heartbeat_at = @now,
                    completed_at = NULL,
                    error_code = NULL,
                    updated_at = @now
                FROM candidate
                WHERE job.id = candidate.id
                RETURNING job.id, job.derivative_id
            )
            UPDATE file_derivatives AS derivative
            SET status = 'RUNNING',
                error_code = NULL,
                revision = derivative.revision + 1,
                updated_at = @now
            FROM acquired
            WHERE derivative.id = acquired.derivative_id
            RETURNING acquired.id;
            """;
        var jobId = await ExecuteScalarGuidAsync(sql, cancellationToken,
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("worker_token", workerToken));
        if (jobId is null)
        {
            return null;
        }

        database.ChangeTracker.Clear();
        return await database.MediaJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken);
    }

    public async Task<bool> TryRecordHeartbeatAsync(
        Guid jobId,
        Guid workerToken,
        DateTimeOffset now,
        int? progressPercent,
        long? processedDurationMs,
        long? totalDurationMs,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty || workerToken == Guid.Empty || progressPercent is < 0 or > 100 ||
            processedDurationMs < 0 || totalDurationMs < 0 ||
            processedDurationMs is not null && totalDurationMs is not null && processedDurationMs > totalDurationMs)
        {
            return false;
        }

        const string sql =
            """
            UPDATE media_jobs
            SET heartbeat_at = @now,
                progress_percent = @progress,
                processed_duration_ms = @processed,
                total_duration_ms = @total,
                updated_at = @now
            WHERE id = @job_id
              AND status = 'RUNNING'
              AND worker_token = @worker_token;
            """;
        return await ExecuteNonQueryAsync(sql, cancellationToken,
            new NpgsqlParameter("now", now),
            NullableParameter("progress", progressPercent),
            NullableParameter("processed", processedDurationMs),
            NullableParameter("total", totalDurationMs),
            new NpgsqlParameter("job_id", jobId),
            new NpgsqlParameter("worker_token", workerToken)) == 1;
    }

    public async Task<bool> TryCompleteAsync(
        Guid jobId,
        Guid workerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE media_jobs
            SET status = 'COMPLETED',
                worker_token = NULL,
                heartbeat_at = NULL,
                completed_at = @now,
                error_code = NULL,
                updated_at = @now
            WHERE id = @job_id
              AND status = 'RUNNING'
              AND worker_token = @worker_token;
            """;
        return await ExecuteNonQueryAsync(sql, cancellationToken,
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("job_id", jobId),
            new NpgsqlParameter("worker_token", workerToken)) == 1;
    }

    public async Task<bool> TryFailAsync(
        Guid jobId,
        Guid workerToken,
        string errorCode,
        bool retryable,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 64)
        {
            throw new ArgumentException("A bounded error code is required.", nameof(errorCode));
        }

        const string sql =
            """
            WITH updated_job AS (
                UPDATE media_jobs AS job
                SET status = CASE WHEN @retryable AND job.attempt_count < 3 THEN 'QUEUED' ELSE 'FAILED' END,
                    available_at = CASE
                        WHEN @retryable AND job.attempt_count = 1 THEN @now + interval '30 seconds'
                        WHEN @retryable AND job.attempt_count = 2 THEN @now + interval '2 minutes'
                        ELSE job.available_at
                    END,
                    worker_token = NULL,
                    heartbeat_at = NULL,
                    progress_percent = NULL,
                    processed_duration_ms = NULL,
                    total_duration_ms = NULL,
                    completed_at = CASE WHEN @retryable AND job.attempt_count < 3 THEN NULL ELSE @now END,
                    error_code = @error_code,
                    updated_at = @now
                WHERE job.id = @job_id
                  AND job.status = 'RUNNING'
                  AND job.worker_token = @worker_token
                RETURNING job.derivative_id, job.status
            )
            UPDATE file_derivatives AS derivative
            SET status = CASE WHEN updated_job.status = 'QUEUED' THEN 'PENDING' ELSE 'FAILED' END,
                relative_path = NULL,
                size = 0,
                last_accessed_at = NULL,
                expires_at = NULL,
                error_code = @error_code,
                revision = derivative.revision + 1,
                updated_at = @now
            FROM updated_job
            WHERE derivative.id = updated_job.derivative_id
              AND derivative.status = 'RUNNING';
            """;
        return await ExecuteNonQueryAsync(sql, cancellationToken,
            new NpgsqlParameter("retryable", retryable),
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("error_code", errorCode),
            new NpgsqlParameter("job_id", jobId),
            new NpgsqlParameter("worker_token", workerToken)) == 1;
    }

    public async Task<int> RecoverStaleAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        const string sql =
            """
            WITH candidates AS (
                SELECT job.id
                FROM media_jobs AS job
                WHERE job.status = 'RUNNING'
                  AND job.heartbeat_at <= @stale_before
                  AND NOT EXISTS (
                      SELECT 1
                      FROM derivative_leases AS lease
                      WHERE lease.derivative_id = job.derivative_id
                        AND lease.lease_type = 'GENERATION'
                        AND lease.expires_at > @now)
                ORDER BY job.heartbeat_at, job.id
                FOR UPDATE SKIP LOCKED
                LIMIT @batch_size
            ), updated_jobs AS (
                UPDATE media_jobs AS job
                SET status = CASE WHEN job.attempt_count < 3 THEN 'QUEUED' ELSE 'FAILED' END,
                    available_at = CASE
                        WHEN job.attempt_count = 1 THEN @now + interval '30 seconds'
                        WHEN job.attempt_count = 2 THEN @now + interval '2 minutes'
                        ELSE job.available_at
                    END,
                    worker_token = NULL,
                    heartbeat_at = NULL,
                    progress_percent = NULL,
                    processed_duration_ms = NULL,
                    total_duration_ms = NULL,
                    completed_at = CASE WHEN job.attempt_count < 3 THEN NULL ELSE @now END,
                    error_code = 'MEDIA_WORKER_STALE',
                    updated_at = @now
                FROM candidates
                WHERE job.id = candidates.id
                RETURNING job.derivative_id, job.status
            ), released_leases AS (
                DELETE FROM derivative_leases AS lease
                USING updated_jobs
                WHERE lease.derivative_id = updated_jobs.derivative_id
                  AND lease.lease_type = 'GENERATION'
                  AND lease.expires_at <= @now
                RETURNING lease.derivative_id
            )
            UPDATE file_derivatives AS derivative
            SET status = CASE WHEN updated_jobs.status = 'QUEUED' THEN 'PENDING' ELSE 'FAILED' END,
                relative_path = NULL,
                size = 0,
                last_accessed_at = NULL,
                expires_at = NULL,
                lease_until = (
                    SELECT max(lease.expires_at)
                    FROM derivative_leases AS lease
                    WHERE lease.derivative_id = derivative.id
                      AND lease.expires_at > @now),
                error_code = 'MEDIA_WORKER_STALE',
                revision = derivative.revision + 1,
                updated_at = @now
            FROM updated_jobs
            WHERE derivative.id = updated_jobs.derivative_id
              AND derivative.status = 'RUNNING'
              AND (SELECT count(*) FROM released_leases) >= 0;
            """;
        return await ExecuteNonQueryAsync(sql, cancellationToken,
            new NpgsqlParameter("stale_before", now.Subtract(MediaJob.StaleAfter)),
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("batch_size", batchSize));
    }

    public async Task<Guid?> TryRetryFailedAsync(
        Guid failedJobId,
        Guid newJobId,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (failedJobId == Guid.Empty || newJobId == Guid.Empty || requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Failed job, new job, and requesting user IDs are required.");
        }

        const string sql =
            """
            WITH target AS (
                SELECT failed.derivative_id, failed.job_type
                FROM media_jobs AS failed
                INNER JOIN file_derivatives AS derivative ON derivative.id = failed.derivative_id
                WHERE failed.id = @failed_job_id
                  AND failed.status = 'FAILED'
                  AND derivative.status = 'FAILED'
                FOR UPDATE OF failed, derivative
            ), prepared AS (
                UPDATE file_derivatives AS derivative
                SET status = 'PENDING',
                    error_code = NULL,
                    revision = derivative.revision + 1,
                    updated_at = @now
                FROM target
                WHERE derivative.id = target.derivative_id
                RETURNING derivative.id
            ), inserted AS (
                INSERT INTO media_jobs
                    (id, derivative_id, job_type, status, requested_by_user_id, attempt_count,
                     available_at, created_at, updated_at)
                SELECT @new_job_id, target.derivative_id, target.job_type, 'QUEUED',
                       @requested_by_user_id, 0, @now, @now, @now
                FROM target
                INNER JOIN prepared ON prepared.id = target.derivative_id
                ON CONFLICT (derivative_id, status)
                    WHERE status IN ('QUEUED', 'RUNNING')
                    DO NOTHING
                RETURNING id
            )
            SELECT id FROM inserted LIMIT 1;
            """;
        var inserted = await ExecuteScalarGuidAsync(sql, cancellationToken,
            new NpgsqlParameter("failed_job_id", failedJobId),
            new NpgsqlParameter("new_job_id", newJobId),
            new NpgsqlParameter("requested_by_user_id", requestedByUserId),
            new NpgsqlParameter("now", now));
        if (inserted is not null)
        {
            return inserted;
        }

        const string existingSql =
            """
            SELECT active.id
            FROM media_jobs AS failed
            INNER JOIN media_jobs AS active ON active.derivative_id = failed.derivative_id
            WHERE failed.id = @failed_job_id
              AND active.status IN ('QUEUED', 'RUNNING')
            ORDER BY active.created_at, active.id
            LIMIT 1;
            """;
        return await ExecuteScalarGuidAsync(existingSql, cancellationToken,
            new NpgsqlParameter("failed_job_id", failedJobId));
    }

    public async Task<int?> GetQueuePositionAsync(
        Guid jobId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT position::integer
            FROM (
                SELECT id, row_number() OVER (ORDER BY created_at, id) AS position
                FROM media_jobs
                WHERE status = 'QUEUED' AND available_at <= @now
            ) AS queued
            WHERE id = @job_id;
            """;
        var value = await ExecuteScalarAsync(sql, cancellationToken,
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("job_id", jobId));
        return value is null or DBNull ? null : Convert.ToInt32(value, global::System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<Guid?> ExecuteScalarGuidAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        var value = await ExecuteScalarAsync(sql, cancellationToken, parameters);
        return value is Guid id ? id : null;
    }

    private async Task<object?> ExecuteScalarAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<int> ExecuteNonQueryAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter NullableParameter(string name, object? value) =>
        new(name, value ?? DBNull.Value);
}
