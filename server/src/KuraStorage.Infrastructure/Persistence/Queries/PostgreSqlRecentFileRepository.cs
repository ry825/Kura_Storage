using System.Data;
using System.Buffers.Binary;
using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Recent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence.Queries;

public sealed class PostgreSqlRecentFileRepository(KuraStorageDbContext dbContext) : IRecentFileRepository
{
    private const int MaximumHierarchyDepth = 64;
    private const int CommandTimeoutSeconds = 10;

    private const string RecentCte =
        """
        WITH RECURSIVE active_actor AS (
            SELECT id
            FROM users
            WHERE id = @actor_user_id
              AND upper(status) = 'ACTIVE'
        ),
        recent_entries AS NOT MATERIALIZED (
            SELECT
                recent.file_id AS id,
                recent.opened_at,
                entry.owner_user_id,
                entry.entry_type,
                entry.name,
                entry.relative_path,
                entry.mime_type,
                CASE
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'image/%' THEN 'IMAGE'
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'video/%' THEN 'VIDEO'
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'audio/%' THEN 'AUDIO'
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'text/%'
                      OR lower(coalesce(entry.mime_type, '')) IN (
                        'application/pdf', 'application/msword', 'application/rtf',
                        'application/vnd.ms-excel', 'application/vnd.ms-powerpoint',
                        'application/vnd.oasis.opendocument.presentation',
                        'application/vnd.oasis.opendocument.spreadsheet',
                        'application/vnd.oasis.opendocument.text',
                        'application/vnd.openxmlformats-officedocument.presentationml.presentation',
                        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                        'application/vnd.openxmlformats-officedocument.wordprocessingml.document') THEN 'DOCUMENT'
                    WHEN lower(coalesce(entry.mime_type, '')) IN (
                        'application/gzip', 'application/vnd.rar', 'application/x-7z-compressed',
                        'application/x-bzip2', 'application/x-tar', 'application/zip') THEN 'ARCHIVE'
                    ELSE 'OTHER'
                END AS file_category,
                entry.size,
                entry.status,
                entry.updated_at,
                owner.display_name AS owner_display_name
            FROM recent_files AS recent
            JOIN active_actor AS actor ON actor.id = recent.user_id
            JOIN file_entries AS entry ON entry.id = recent.file_id
            JOIN users AS owner ON owner.id = entry.owner_user_id
            WHERE entry.entry_type = 'FILE'
              AND entry.status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')
              AND NOT EXISTS (
                  SELECT 1
                  FROM file_operations AS operation
                  JOIN file_entries AS operation_target
                    ON operation_target.id = operation.file_entry_id
                   AND operation_target.owner_user_id = operation.owner_user_id
                  WHERE operation.owner_user_id = entry.owner_user_id
                    AND operation.status IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')
                    AND (
                        operation_target.id = entry.id
                        OR starts_with(entry.relative_path, operation_target.relative_path || '/')))
        ),
        owned_candidates AS (
            SELECT
                entry.id AS entry_id,
                entry.opened_at,
                entry.name,
                entry.mime_type,
                entry.file_category,
                entry.size,
                entry.status,
                entry.updated_at,
                entry.owner_user_id AS owner_id,
                entry.owner_display_name,
                'OWNER'::text AS permission,
                'OWNER'::text AS permission_source,
                NULL::uuid AS share_target_id,
                NULL::uuid AS share_id,
                0 AS ancestor_depth
            FROM recent_entries AS entry
            JOIN active_actor AS actor ON actor.id = entry.owner_user_id
        ),
        shared_tree AS (
            SELECT
                entry.id AS entry_id,
                entry.owner_user_id,
                entry.entry_type,
                entry.status,
                member.permission,
                share.target_entry_id AS share_target_id,
                share.id AS share_id,
                0 AS ancestor_depth,
                ARRAY[entry.id]::uuid[] AS visited,
                FALSE AS is_cycle
            FROM active_actor AS actor
            JOIN share_members AS member
              ON member.user_id = actor.id
             AND member.permission IN ('VIEWER', 'CONTRIBUTOR', 'EDITOR', 'MANAGER')
            JOIN shares AS share ON share.id = member.share_id
            JOIN file_entries AS entry
              ON entry.id = share.target_entry_id
             AND entry.owner_user_id = share.owner_user_id

            UNION ALL

            SELECT
                child.id,
                child.owner_user_id,
                child.entry_type,
                child.status,
                tree.permission,
                tree.share_target_id,
                tree.share_id,
                tree.ancestor_depth + 1,
                tree.visited || child.id,
                child.id = ANY(tree.visited)
            FROM shared_tree AS tree
            JOIN file_entries AS child
              ON child.parent_id = tree.entry_id
             AND child.owner_user_id = tree.owner_user_id
            WHERE tree.entry_type = 'FOLDER'
              AND tree.status = 'ACTIVE'
              AND tree.ancestor_depth < @maximum_depth
              AND NOT tree.is_cycle
        ),
        permission_candidates AS (
            SELECT
                entry_id, opened_at, name, mime_type, file_category, size, status, updated_at,
                owner_id, owner_display_name, permission, permission_source, share_target_id,
                share_id, ancestor_depth
            FROM owned_candidates

            UNION ALL

            SELECT
                recent.id,
                recent.opened_at,
                recent.name,
                recent.mime_type,
                recent.file_category,
                recent.size,
                recent.status,
                recent.updated_at,
                recent.owner_user_id,
                recent.owner_display_name,
                tree.permission,
                CASE WHEN tree.ancestor_depth = 0 THEN 'DIRECT' ELSE 'INHERITED' END,
                tree.share_target_id,
                tree.share_id,
                tree.ancestor_depth
            FROM shared_tree AS tree
            JOIN recent_entries AS recent ON recent.id = tree.entry_id
            WHERE NOT tree.is_cycle
        ),
        ranked_permissions AS (
            SELECT
                candidate.entry_id,
                candidate.opened_at,
                candidate.name,
                candidate.mime_type,
                candidate.file_category,
                candidate.size,
                candidate.status,
                candidate.updated_at,
                candidate.owner_id,
                candidate.owner_display_name,
                candidate.permission,
                candidate.permission_source,
                candidate.share_target_id,
                candidate.share_id,
                candidate.ancestor_depth,
                row_number() OVER (
                    PARTITION BY candidate.entry_id
                    ORDER BY
                        CASE candidate.permission
                            WHEN 'OWNER' THEN 5 WHEN 'MANAGER' THEN 4 WHEN 'EDITOR' THEN 3
                            WHEN 'CONTRIBUTOR' THEN 2 WHEN 'VIEWER' THEN 1 ELSE 0
                        END DESC,
                        CASE candidate.permission_source
                            WHEN 'OWNER' THEN 0 WHEN 'DIRECT' THEN 1 WHEN 'INHERITED' THEN 2 ELSE 3
                        END,
                        candidate.ancestor_depth,
                        candidate.share_id NULLS FIRST) AS permission_rank
            FROM permission_candidates AS candidate
        ),
        accessible AS MATERIALIZED (
            SELECT
                entry_id, opened_at, name, mime_type, file_category, size, status, updated_at,
                owner_id, owner_display_name, permission, permission_source, share_target_id
            FROM ranked_permissions
            WHERE permission_rank = 1
        )
        """;

    private const string PageSql = RecentCte +
        """
        SELECT
            entry_id, name, mime_type, file_category, size, status, updated_at,
            owner_id, owner_display_name, permission, permission_source, share_target_id,
            opened_at, count(*) OVER() AS total_count
        FROM accessible
        ORDER BY opened_at DESC, entry_id
        OFFSET @offset_rows
        LIMIT @page_size;
        """;

    private const string CountSql = RecentCte + "SELECT count(*) FROM accessible;";

    public async Task<bool> TryUpsertAuthorizedAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var initialScope = await ListMutationScopeAsync(fileId, cancellationToken);
        var lockKeys = initialScope.Append(fileId).Select(ToAdvisoryLockKey).Distinct().Order().ToArray();
        foreach (var lockKey in lockKeys)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                cancellationToken);
        }

        var lockedScope = await ListMutationScopeAsync(fileId, cancellationToken);
        if (!initialScope.SequenceEqual(lockedScope))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH RECURSIVE active_actor AS (
                SELECT id FROM users
                WHERE id = {userId} AND upper(status) = 'ACTIVE'
            ),
            eligible_file AS (
                SELECT entry.id, entry.owner_user_id, entry.relative_path
                FROM file_entries AS entry
                WHERE entry.id = {fileId}
                  AND entry.entry_type = 'FILE'
                  AND entry.status = 'ACTIVE'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM file_operations AS operation
                      JOIN file_entries AS operation_target
                        ON operation_target.id = operation.file_entry_id
                       AND operation_target.owner_user_id = operation.owner_user_id
                      WHERE operation.owner_user_id = entry.owner_user_id
                        AND operation.status IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')
                        AND (
                            operation_target.id = entry.id
                            OR starts_with(entry.relative_path, operation_target.relative_path || '/')))
            ),
            ancestors AS (
                SELECT
                    file.id AS entry_id,
                    entry.parent_id,
                    entry.entry_type,
                    entry.status,
                    0 AS ancestor_depth,
                    ARRAY[entry.id]::uuid[] AS visited,
                    TRUE AS path_active,
                    FALSE AS is_cycle
                FROM eligible_file AS file
                JOIN file_entries AS entry ON entry.id = file.id

                UNION ALL

                SELECT
                    parent.id,
                    parent.parent_id,
                    parent.entry_type,
                    parent.status,
                    child.ancestor_depth + 1,
                    child.visited || parent.id,
                    child.path_active AND parent.entry_type = 'FOLDER' AND parent.status = 'ACTIVE',
                    parent.id = ANY(child.visited)
                FROM ancestors AS child
                JOIN file_entries AS parent ON parent.id = child.parent_id
                WHERE child.ancestor_depth < {MaximumHierarchyDepth}
                  AND NOT child.is_cycle
            ),
            authorized AS (
                SELECT file.id
                FROM eligible_file AS file
                WHERE EXISTS (SELECT 1 FROM active_actor AS actor WHERE actor.id = file.owner_user_id)
                   OR EXISTS (
                       SELECT 1
                       FROM active_actor AS actor
                       JOIN share_members AS member
                         ON member.user_id = actor.id
                        AND member.permission IN ('VIEWER', 'CONTRIBUTOR', 'EDITOR', 'MANAGER')
                       JOIN shares AS share ON share.id = member.share_id
                       JOIN ancestors AS ancestor
                         ON ancestor.entry_id = share.target_entry_id
                        AND ancestor.path_active
                        AND NOT ancestor.is_cycle
                       WHERE share.owner_user_id = file.owner_user_id)
            )
            INSERT INTO recent_files (user_id, file_id, opened_at)
            SELECT {userId}, authorized.id, {openedAt}
            FROM authorized
            ON CONFLICT (user_id, file_id)
            DO UPDATE SET opened_at = GREATEST(recent_files.opened_at, EXCLUDED.opened_at)
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected == 1;
    }

    private async Task<IReadOnlyList<Guid>> ListMutationScopeAsync(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            """
            WITH RECURSIVE ancestors AS (
                SELECT id, parent_id, 0 AS depth, ARRAY[id]::uuid[] AS visited, FALSE AS is_cycle
                FROM file_entries
                WHERE id = @file_id

                UNION ALL

                SELECT parent.id, parent.parent_id, child.depth + 1,
                       child.visited || parent.id, parent.id = ANY(child.visited)
                FROM ancestors AS child
                JOIN file_entries AS parent ON parent.id = child.parent_id
                WHERE child.depth < @maximum_depth AND NOT child.is_cycle
            )
            SELECT id FROM ancestors WHERE NOT is_cycle ORDER BY id;
            """,
            connection,
            dbContext.Database.CurrentTransaction!.GetDbTransaction() as NpgsqlTransaction)
        {
            CommandTimeout = CommandTimeoutSeconds,
        };
        command.Parameters.AddWithValue("file_id", NpgsqlDbType.Uuid, fileId);
        command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public async Task<RecentFilePage> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var result = await PageAsync(connection, userId, page, pageSize, cancellationToken);
            var totalCount = result.TotalCount;
            if (result.Items.Count == 0 && page > 1)
            {
                totalCount = await CountAsync(connection, userId, cancellationToken);
            }

            return new RecentFilePage(result.Items, page, pageSize, totalCount);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<(IReadOnlyList<RecentFileItem> Items, int TotalCount)> PageAsync(
        NpgsqlConnection connection,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(PageSql, connection, userId);
        command.Parameters.AddWithValue("offset_rows", NpgsqlDbType.Integer, checked((page - 1) * pageSize));
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
        var items = new List<RecentFileItem>();
        var totalCount = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = checked((int)reader.GetInt64(13));
            items.Add(
                new RecentFileItem(
                    reader.GetGuid(0),
                    "FILE",
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    new FileOwnerItem(reader.GetGuid(7), reader.GetString(8)),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetGuid(11),
                    reader.GetFieldValue<DateTimeOffset>(12)));
        }

        return (items, totalCount);
    }

    private static async Task<int> CountAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(CountSql, connection, userId);
        return checked((int)(long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L));
    }

    private static NpgsqlCommand CreateCommand(string sql, NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
        return command;
    }

    private static long ToAdvisoryLockKey(Guid id)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(id.ToByteArray(), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
