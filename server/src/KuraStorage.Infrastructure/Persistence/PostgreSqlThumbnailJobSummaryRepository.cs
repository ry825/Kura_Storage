using System.Data;
using KuraStorage.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlThumbnailJobSummaryRepository(KuraStorageDbContext database)
    : IThumbnailJobSummaryRepository
{
    internal const int MaximumHierarchyDepth = 64;

    internal const string SummarySql =
        """
        WITH RECURSIVE active_actor AS (
            SELECT users.id
            FROM users
            WHERE users.id = @actor_user_id
              AND upper(users.status) = 'ACTIVE'
        ),
        thumbnail_jobs AS (
            SELECT
                derivative.id AS summary_item_id,
                derivative.source_file_id AS entry_id,
                latest_job.status
            FROM file_derivatives AS derivative
            CROSS JOIN LATERAL (
                SELECT job.status
                FROM media_jobs AS job
                WHERE job.derivative_id = derivative.id
                  AND job.job_type = derivative.derivative_type
                ORDER BY
                    CASE WHEN job.status IN ('QUEUED', 'RUNNING') THEN 0 ELSE 1 END,
                    job.created_at DESC,
                    job.id DESC
                LIMIT 1
            ) AS latest_job
            WHERE derivative.derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL')
              AND latest_job.status IN ('QUEUED', 'RUNNING', 'FAILED')
        ),
        hierarchy AS (
            SELECT
                job.summary_item_id,
                job.entry_id,
                job.status AS job_status,
                entry.id AS ancestor_id,
                entry.parent_id,
                entry.owner_user_id,
                entry.owner_user_id AS target_owner_user_id,
                entry.entry_type,
                entry.status AS entry_status,
                entry.relative_path,
                0 AS ancestor_depth,
                ARRAY[entry.id]::uuid[] AS visited,
                FALSE AS is_cycle
            FROM thumbnail_jobs AS job
            JOIN file_entries AS entry ON entry.id = job.entry_id
            WHERE entry.entry_type = 'FILE'
              AND entry.status = 'ACTIVE'

            UNION ALL

            SELECT
                hierarchy.summary_item_id,
                hierarchy.entry_id,
                hierarchy.job_status,
                parent.id,
                parent.parent_id,
                parent.owner_user_id,
                hierarchy.target_owner_user_id,
                parent.entry_type,
                parent.status,
                parent.relative_path,
                hierarchy.ancestor_depth + 1,
                hierarchy.visited || parent.id,
                parent.id = ANY(hierarchy.visited)
            FROM hierarchy
            JOIN file_entries AS parent ON parent.id = hierarchy.parent_id
            WHERE hierarchy.ancestor_depth < @maximum_depth
              AND NOT hierarchy.is_cycle
        ),
        readable_jobs AS (
            SELECT target.summary_item_id, target.job_status
            FROM hierarchy AS target
            JOIN active_actor AS actor ON TRUE
            WHERE target.ancestor_depth = 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM hierarchy AS invalid
                  WHERE invalid.summary_item_id = target.summary_item_id
                    AND (
                        invalid.is_cycle
                        OR invalid.owner_user_id <> invalid.target_owner_user_id
                        OR invalid.entry_status <> 'ACTIVE'
                        OR (invalid.ancestor_depth > 0 AND invalid.entry_type <> 'FOLDER')
                        OR (invalid.parent_id IS NULL AND invalid.entry_type <> 'FOLDER')
                        OR (invalid.ancestor_depth = @maximum_depth AND invalid.parent_id IS NOT NULL)
                    )
              )
              AND EXISTS (
                  SELECT 1
                  FROM hierarchy AS root
                  WHERE root.summary_item_id = target.summary_item_id
                    AND root.parent_id IS NULL
                    AND NOT root.is_cycle
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM file_operations AS operation
                  JOIN file_entries AS operation_target
                    ON operation_target.id = operation.file_entry_id
                   AND operation_target.owner_user_id = operation.owner_user_id
                  WHERE operation.owner_user_id = target.owner_user_id
                    AND operation.status IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')
                    AND (
                        operation_target.id = target.entry_id
                        OR starts_with(target.relative_path, operation_target.relative_path || '/')
                    )
              )
              AND (
                  actor.id = target.owner_user_id
                  OR EXISTS (
                      SELECT 1
                      FROM hierarchy AS shared_ancestor
                      JOIN shares AS share ON share.target_entry_id = shared_ancestor.ancestor_id
                      JOIN share_members AS member
                        ON member.share_id = share.id
                       AND member.user_id = actor.id
                      WHERE shared_ancestor.summary_item_id = target.summary_item_id
                        AND (
                            shared_ancestor.ancestor_depth = 0
                            OR (shared_ancestor.entry_type = 'FOLDER' AND shared_ancestor.entry_status = 'ACTIVE')
                        )
                  )
              )
        )
        SELECT
            count(*) FILTER (WHERE job_status = 'QUEUED') AS queued_count,
            count(*) FILTER (WHERE job_status = 'RUNNING') AS running_count,
            count(*) FILTER (WHERE job_status = 'FAILED') AS failed_count
        FROM readable_jobs;
        """;

    public async Task<ThumbnailJobSummarySnapshot> GetAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor user ID is required.", nameof(actorUserId));
        }

        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(SummarySql, connection);
            command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, actorUserId);
            command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The thumbnail job summary query returned no row.");
            }

            return new ThumbnailJobSummarySnapshot(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
