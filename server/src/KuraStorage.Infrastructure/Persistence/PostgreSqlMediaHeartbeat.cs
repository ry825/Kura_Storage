using KuraStorage.Application.Abstractions;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaHeartbeat(IOptions<DatabaseOptions> databaseOptions) : IMediaHeartbeat
{
    private readonly string connectionString = databaseOptions.Value.ConnectionString;

    public async Task<bool> PulseAsync(
        Guid jobId,
        Guid workerToken,
        Guid derivativeId,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        await PulseCoreAsync(
            jobId,
            workerToken,
            derivativeId,
            leaseOwnerToken,
            now,
            leaseDuration,
            null,
            cancellationToken);

    public async Task<bool> PulseProgressAsync(
        Guid jobId,
        Guid workerToken,
        Guid derivativeId,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        MediaGenerationProgress progress,
        CancellationToken cancellationToken) =>
        await PulseCoreAsync(
            jobId,
            workerToken,
            derivativeId,
            leaseOwnerToken,
            now,
            leaseDuration,
            progress,
            cancellationToken);

    private async Task<bool> PulseCoreAsync(
        Guid jobId,
        Guid workerToken,
        Guid derivativeId,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        MediaGenerationProgress? progress,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH renewed AS (
                UPDATE derivative_leases
                SET expires_at = @expires_at, updated_at = @now
                WHERE derivative_id = @derivative_id
                  AND lease_type = 'GENERATION'
                  AND owner_token = @lease_owner_token
                  AND expires_at > @now
                RETURNING derivative_id
            ), heartbeat AS (
                UPDATE media_jobs
                SET heartbeat_at = @now,
                    progress_percent = COALESCE(@progress, progress_percent),
                    processed_duration_ms = COALESCE(@processed, processed_duration_ms),
                    total_duration_ms = COALESCE(@total, total_duration_ms),
                    updated_at = @now
                WHERE id = @job_id AND status = 'RUNNING' AND worker_token = @worker_token
                  AND EXISTS (SELECT 1 FROM renewed)
                RETURNING derivative_id
            )
            UPDATE file_derivatives
            SET lease_until = @expires_at, revision = revision + 1, updated_at = @now
            WHERE id = @derivative_id
              AND EXISTS (SELECT 1 FROM heartbeat WHERE heartbeat.derivative_id = file_derivatives.id);
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("worker_token", workerToken);
        command.Parameters.AddWithValue("derivative_id", derivativeId);
        command.Parameters.AddWithValue("lease_owner_token", leaseOwnerToken);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("expires_at", now.Add(leaseDuration));
        command.Parameters.AddWithValue(
            "progress", NpgsqlDbType.Integer, (object?)progress?.Percent ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "processed", NpgsqlDbType.Bigint, (object?)progress?.ProcessedDurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "total", NpgsqlDbType.Bigint, (object?)progress?.TotalDurationMs ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
