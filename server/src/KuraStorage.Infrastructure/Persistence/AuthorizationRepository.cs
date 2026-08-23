using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Sharing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class AuthorizationRepository(KuraStorageDbContext dbContext) : IAuthorizationRepository
{
    private const int MaximumHierarchyDepth = 64;

    private const string CandidateSql =
        """
        WITH RECURSIVE requested(entry_id) AS (
            SELECT unnest(@entry_ids::uuid[])
        ),
        active_actor AS (
            SELECT users.id
            FROM users
            WHERE users.id = @actor_user_id
              AND upper(users.status) = 'ACTIVE'
        ),
        hierarchy AS (
            SELECT
                requested.entry_id,
                entry.id AS ancestor_id,
                entry.parent_id,
                entry.owner_user_id,
                entry.owner_user_id AS target_owner_user_id,
                entry.entry_type,
                entry.status,
                entry.relative_path,
                0 AS ancestor_depth,
                ARRAY[entry.id]::uuid[] AS visited,
                FALSE AS is_cycle
            FROM requested
            JOIN file_entries AS entry ON entry.id = requested.entry_id

            UNION ALL

            SELECT
                hierarchy.entry_id,
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
        valid_hierarchies AS (
            SELECT hierarchy.entry_id
            FROM hierarchy
            GROUP BY hierarchy.entry_id
            HAVING bool_or(hierarchy.parent_id IS NULL AND NOT hierarchy.is_cycle)
               AND NOT bool_or(hierarchy.is_cycle)
               AND bool_and(hierarchy.owner_user_id = hierarchy.target_owner_user_id)
               AND bool_and(
                   hierarchy.ancestor_depth = 0
                   OR hierarchy.entry_type = 'FOLDER')
               AND bool_and(
                   hierarchy.parent_id IS NOT NULL
                   OR hierarchy.entry_type = 'FOLDER')
               AND NOT bool_or(
                   hierarchy.ancestor_depth = @maximum_depth
                   AND hierarchy.parent_id IS NOT NULL)
        ),
        accessible_targets AS (
            SELECT target.entry_id, target.owner_user_id
            FROM hierarchy AS target
            JOIN valid_hierarchies AS valid ON valid.entry_id = target.entry_id
            WHERE target.ancestor_depth = 0
              AND target.status <> 'TRASHED'
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
                        OR starts_with(
                            target.relative_path,
                            operation_target.relative_path || '/')
                    )
              )
        )
        SELECT
            target.entry_id,
            'OWNER'::text AS permission,
            'OWNER'::text AS source,
            NULL::uuid AS share_target_id,
            NULL::uuid AS share_id,
            0 AS ancestor_depth
        FROM accessible_targets AS target
        JOIN active_actor AS actor ON actor.id = target.owner_user_id

        UNION ALL

        SELECT
            hierarchy.entry_id,
            member.permission,
            CASE WHEN hierarchy.ancestor_depth = 0 THEN 'DIRECT' ELSE 'INHERITED' END AS source,
            share.target_entry_id AS share_target_id,
            share.id AS share_id,
            hierarchy.ancestor_depth
        FROM hierarchy
        JOIN accessible_targets AS target ON target.entry_id = hierarchy.entry_id
        JOIN shares AS share ON share.target_entry_id = hierarchy.ancestor_id
        JOIN share_members AS member
          ON member.share_id = share.id
         AND member.user_id = @actor_user_id
        JOIN active_actor AS actor ON actor.id = member.user_id
        WHERE hierarchy.ancestor_depth = 0
           OR (hierarchy.entry_type = 'FOLDER' AND hierarchy.status = 'ACTIVE')
        """;

    public async Task<IReadOnlyList<PermissionCandidate>> ListCandidatesAsync(
        Guid actorUserId,
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return [];
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(CandidateSql, connection);
            command.Parameters.AddWithValue(
                "entry_ids",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                entryIds.ToArray());
            command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, actorUserId);
            command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
            var candidates = new List<PermissionCandidate>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new PermissionCandidate(
                    reader.GetGuid(0),
                    ParsePermission(reader.GetString(1)),
                    ParseSource(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetInt32(5)));
            }

            return candidates;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static EffectivePermissionLevel ParsePermission(string value) => value switch
    {
        "VIEWER" => EffectivePermissionLevel.Viewer,
        "CONTRIBUTOR" => EffectivePermissionLevel.Contributor,
        "EDITOR" => EffectivePermissionLevel.Editor,
        "MANAGER" => EffectivePermissionLevel.Manager,
        "OWNER" => EffectivePermissionLevel.Owner,
        _ => throw new InvalidOperationException("The authorization query returned an unknown permission."),
    };

    private static PermissionSource ParseSource(string value) => value switch
    {
        "OWNER" => PermissionSource.Owner,
        "DIRECT" => PermissionSource.Direct,
        "INHERITED" => PermissionSource.Inherited,
        _ => throw new InvalidOperationException("The authorization query returned an unknown source."),
    };
}
