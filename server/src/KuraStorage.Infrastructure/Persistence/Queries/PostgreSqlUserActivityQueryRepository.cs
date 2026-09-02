using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Activity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence.Queries;

public sealed class PostgreSqlUserActivityQueryRepository(KuraStorageDbContext dbContext)
    : IUserActivityQueryRepository
{
    private const int MaximumHierarchyDepth = 64;
    private const int CommandTimeoutSeconds = 10;

    internal const string Projection =
        """
        activity.id, activity.activity_type, activity.occurred_at,
        activity.actor_display_name, activity.actor_device_name,
        __target_entry_id__,
        activity.target_type, activity.target_name, activity.owner_display_name,
        activity.source_parent_name, activity.destination_parent_name,
        activity.resulting_file_version, activity.edit_kind,
        activity.recipient_display_name, activity.share_permission,
        activity.share_action, activity.delete_kind
        """;

    private const string UserQuerySql =
        """
        WITH RECURSIVE active_actor AS (
            SELECT id
            FROM users
            WHERE id = @actor_user_id AND upper(status) = 'ACTIVE'
        ),
        shared_tree AS (
            SELECT
                entry.id AS entry_id,
                entry.owner_user_id,
                entry.entry_type,
                entry.status,
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
        visible_entries AS (
            SELECT entry.id
            FROM active_actor AS actor
            JOIN file_entries AS entry ON entry.owner_user_id = actor.id

            UNION

            SELECT tree.entry_id
            FROM shared_tree AS tree
            WHERE NOT tree.is_cycle
              AND tree.status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')
              AND NOT EXISTS (
                  SELECT 1
                  FROM file_operations AS operation
                  JOIN file_entries AS operation_target
                    ON operation_target.id = operation.file_entry_id
                   AND operation_target.owner_user_id = operation.owner_user_id
                  JOIN file_entries AS candidate ON candidate.id = tree.entry_id
                  WHERE operation.owner_user_id = candidate.owner_user_id
                    AND operation.status IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')
                    AND (operation_target.id = candidate.id
                         OR starts_with(candidate.relative_path, operation_target.relative_path || '/')))
        )
        SELECT __projection__
        FROM active_actor AS actor
        JOIN user_activities AS activity ON TRUE
        WHERE (@activity_type IS NULL OR activity.activity_type = @activity_type)
          AND (@cursor_time IS NULL OR (activity.occurred_at, activity.id) < (@cursor_time, @cursor_id))
          AND (
              activity.actor_user_id = actor.id
              OR (activity.target_entry_id IS NULL AND activity.owner_user_id = actor.id)
              OR activity.target_entry_id IN (SELECT id FROM visible_entries))
        ORDER BY activity.occurred_at DESC, activity.id DESC
        LIMIT @limit;
        """;

    public async Task<IReadOnlyList<ActivityRecord>> ListAsync(
        Guid actorUserId,
        ActivityQueryFilter filter,
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
            var sql = UserQuerySql.Replace("__projection__", Projection, StringComparison.Ordinal)
                .Replace(
                    "__target_entry_id__",
                    "CASE WHEN activity.target_entry_id IN (SELECT id FROM visible_entries) THEN activity.target_entry_id ELSE NULL END",
                    StringComparison.Ordinal);
            await using var command = new NpgsqlCommand(
                sql,
                connection)
            {
                CommandTimeout = CommandTimeoutSeconds,
            };
            command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, actorUserId);
            command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
            AddCommonParameters(command, filter.Type, filter.Cursor, filter.Limit);
            return await ReadAsync(command, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddCommonParameters(
        NpgsqlCommand command,
        UserActivityType? type,
        ActivityCursor? cursor,
        int limit)
    {
        AddNullable(command, "activity_type", NpgsqlDbType.Text, type?.ToString().ToUpperInvariant());
        AddNullable(command, "cursor_time", NpgsqlDbType.TimestampTz, cursor?.OccurredAt);
        AddNullable(command, "cursor_id", NpgsqlDbType.Uuid, cursor?.Id);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
    }

    internal static async Task<IReadOnlyList<ActivityRecord>> ReadAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var items = new List<ActivityRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(
                new ActivityRecord(
                    reader.GetGuid(0),
                    Parse<UserActivityType>(reader.GetString(1)),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetString(3),
                    NullableString(reader, 4),
                    NullableGuid(reader, 5),
                    Parse<ActivityTargetType>(reader.GetString(6)),
                    reader.GetString(7),
                    reader.GetString(8),
                    NullableString(reader, 9),
                    NullableString(reader, 10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    ParseNullable<ActivityEditKind>(reader, 12),
                    NullableString(reader, 13),
                    NullableString(reader, 14),
                    ParseNullable<ActivityShareAction>(reader, 15),
                    ParseNullable<ActivityDeleteKind>(reader, 16)));
        }

        return items;
    }

    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Replace("_", string.Empty), true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException("An activity row contains an unsupported enum value.");

    private static T? ParseNullable<T>(NpgsqlDataReader reader, int ordinal) where T : struct, Enum =>
        reader.IsDBNull(ordinal) ? null : Parse<T>(reader.GetString(ordinal));

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    internal static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
}
