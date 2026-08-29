using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence.Queries;

public sealed class PostgreSqlSearchRepository(KuraStorageDbContext dbContext) : ISearchRepository
{
    private const int MaximumHierarchyDepth = 64;
    private const int CommandTimeoutSeconds = 10;
    private const string TagSearchWorkMemorySql = "SET LOCAL work_mem = '16MB';";

    private const string SearchCte =
        """
        WITH RECURSIVE active_actor AS (
            SELECT id
            FROM users
            WHERE id = @actor_user_id
              AND upper(status) = 'ACTIVE'
        ),
        matching_tag_entries AS MATERIALIZED (
            SELECT relation.entry_id
            FROM entry_tags AS relation
            WHERE cardinality(@tag_ids) = 1
              AND relation.tag_id = @tag_ids[1]

            UNION ALL

            SELECT relation.entry_id
            FROM entry_tags AS relation
            WHERE cardinality(@tag_ids) > 1
              AND relation.tag_id = ANY(@tag_ids)
            GROUP BY relation.entry_id
            HAVING count(DISTINCT relation.tag_id) = cardinality(@tag_ids)
        ),
        entry_metadata AS NOT MATERIALIZED (
            SELECT
                entry.id,
                entry.owner_user_id,
                entry.entry_type,
                entry.name,
                entry.relative_path,
                entry.mime_type,
                CASE
                    WHEN entry.entry_type <> 'FILE' THEN NULL
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'image/%' THEN 'IMAGE'
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'video/%' THEN 'VIDEO'
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'audio/%' THEN 'AUDIO'
                    WHEN lower(coalesce(entry.mime_type, '')) LIKE 'text/%'
                      OR lower(coalesce(entry.mime_type, '')) IN (
                        'application/pdf',
                        'application/msword',
                        'application/rtf',
                        'application/vnd.ms-excel',
                        'application/vnd.ms-powerpoint',
                        'application/vnd.oasis.opendocument.presentation',
                        'application/vnd.oasis.opendocument.spreadsheet',
                        'application/vnd.oasis.opendocument.text',
                        'application/vnd.openxmlformats-officedocument.presentationml.presentation',
                        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                        'application/vnd.openxmlformats-officedocument.wordprocessingml.document') THEN 'DOCUMENT'
                    WHEN lower(coalesce(entry.mime_type, '')) IN (
                        'application/gzip',
                        'application/vnd.rar',
                        'application/x-7z-compressed',
                        'application/x-bzip2',
                        'application/x-tar',
                        'application/zip') THEN 'ARCHIVE'
                    ELSE 'OTHER'
                END AS file_category,
                entry.size,
                entry.status,
                entry.updated_at,
                owner.display_name AS owner_display_name
            FROM file_entries AS entry
            JOIN users AS owner ON owner.id = entry.owner_user_id
            WHERE entry.status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')
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
        eligible_entries AS __eligible_materialization__ (
            SELECT
                id,
                owner_user_id,
                entry_type,
                name,
                relative_path,
                mime_type,
                file_category,
                size,
                status,
                updated_at,
                owner_display_name
            FROM entry_metadata
            WHERE (
                    @match_mode = 'NONE'
                    OR (@match_mode = 'PREFIX' AND starts_with(lower(name), @normalized_text))
                    OR (@match_mode = 'CONTAINS' AND lower(name) LIKE @name_pattern ESCAPE '\'))
              AND (@entry_type IS NULL OR entry_type = @entry_type)
              AND (@file_category IS NULL OR file_category = @file_category)
              AND (@status IS NULL OR status = @status)
              AND (@updated_from IS NULL OR updated_at >= @updated_from)
              AND (@updated_to IS NULL OR updated_at <= @updated_to)
              AND (@min_size IS NULL OR (entry_type = 'FILE' AND size >= @min_size))
              AND (@max_size IS NULL OR (entry_type = 'FILE' AND size <= @max_size))
              AND (@owner_user_id IS NULL OR owner_user_id = @owner_user_id)
              AND (cardinality(@tag_ids) = 0 OR id IN (SELECT entry_id FROM matching_tag_entries))
        ),
        owned_candidates AS (
            SELECT
                entry.id AS entry_id,
                entry.entry_type,
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
            FROM eligible_entries AS entry
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
                entry_id,
                entry_type,
                name,
                mime_type,
                file_category,
                size,
                status,
                updated_at,
                owner_id,
                owner_display_name,
                permission,
                permission_source,
                share_target_id,
                share_id,
                ancestor_depth
            FROM owned_candidates

            UNION ALL

            SELECT
                eligible.id,
                eligible.entry_type,
                eligible.name,
                eligible.mime_type,
                eligible.file_category,
                eligible.size,
                eligible.status,
                eligible.updated_at,
                eligible.owner_user_id,
                eligible.owner_display_name,
                tree.permission,
                CASE WHEN tree.ancestor_depth = 0 THEN 'DIRECT' ELSE 'INHERITED' END,
                tree.share_target_id,
                tree.share_id,
                tree.ancestor_depth
            FROM shared_tree AS tree
            JOIN eligible_entries AS eligible ON eligible.id = tree.entry_id
            WHERE NOT tree.is_cycle
        ),
        ranked_permissions AS (
            SELECT
                candidate.entry_id,
                candidate.entry_type,
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
                            WHEN 'OWNER' THEN 5
                            WHEN 'MANAGER' THEN 4
                            WHEN 'EDITOR' THEN 3
                            WHEN 'CONTRIBUTOR' THEN 2
                            WHEN 'VIEWER' THEN 1
                            ELSE 0
                        END DESC,
                        CASE candidate.permission_source
                            WHEN 'OWNER' THEN 0
                            WHEN 'DIRECT' THEN 1
                            WHEN 'INHERITED' THEN 2
                            ELSE 3
                        END,
                        candidate.ancestor_depth,
                        candidate.share_id NULLS FIRST
                ) AS permission_rank
            FROM permission_candidates AS candidate
        ),
        accessible AS (
            SELECT
                permission.entry_id AS id,
                permission.entry_type,
                permission.name,
                permission.mime_type,
                permission.file_category,
                permission.size,
                permission.status,
                permission.updated_at,
                permission.owner_id,
                permission.owner_display_name,
                permission.permission,
                permission.permission_source,
                permission.share_target_id
            FROM ranked_permissions AS permission
            WHERE permission.permission_rank = 1
        ),
        filtered AS MATERIALIZED (
            SELECT
                id,
                entry_type,
                name,
                mime_type,
                file_category,
                size,
                status,
                updated_at,
                owner_id,
                owner_display_name,
                permission,
                permission_source,
                share_target_id
            FROM accessible
            WHERE (@share_target_id IS NULL OR share_target_id = @share_target_id)
        )
        """;

    private const string CountSql = SearchCte + "SELECT count(*) FROM filtered;";

    private const string PageSql = SearchCte +
        """
        SELECT
            id,
            entry_type,
            name,
            mime_type,
            file_category,
            size,
            status,
            updated_at,
            owner_id,
            owner_display_name,
            permission,
            permission_source,
            share_target_id,
            count(*) OVER() AS total_count
        FROM filtered
        ORDER BY
            CASE WHEN @match_mode <> 'NONE' AND lower(name) = @normalized_text THEN 0 ELSE 1 END,
            CASE WHEN @match_mode <> 'NONE' AND starts_with(lower(name), @normalized_text) THEN 0 ELSE 1 END,
            CASE WHEN @match_mode <> 'NONE' THEN similarity(lower(name), @normalized_text) ELSE 0 END DESC,
            updated_at DESC,
            id
        OFFSET @offset_rows
        LIMIT @page_size;
        """;

    public async Task<SearchPage?> SearchAsync(
        Guid actorUserId,
        SearchFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = filter.TagIds.Count > 0
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        try
        {
            if (filter.TagIds.Count > 0)
            {
                await dbContext.Database.ExecuteSqlRawAsync(TagSearchWorkMemorySql, cancellationToken);
                var ownedCount = await dbContext.Tags.AsNoTracking()
                    .CountAsync(tag => tag.UserId == actorUserId && filter.TagIds.Contains(tag.Id), cancellationToken);
                if (ownedCount != filter.TagIds.Count)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            var page = await PageAsync(connection, actorUserId, filter, cancellationToken);
            var totalCount = page.TotalCount;
            if (page.Items.Count == 0 && filter.Page > 1)
            {
                totalCount = await CountAsync(connection, actorUserId, filter, cancellationToken);
            }

            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new SearchPage(page.Items, filter.Page, filter.PageSize, totalCount);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> CountAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        SearchFilter filter,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(CountSql, connection, actorUserId, filter);
        var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        return checked((int)count);
    }

    private static async Task<(IReadOnlyList<SearchResultItem> Items, int TotalCount)> PageAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        SearchFilter filter,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(PageSql, connection, actorUserId, filter);
        command.Parameters.AddWithValue("offset_rows", NpgsqlDbType.Integer, checked((filter.Page - 1) * filter.PageSize));
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, filter.PageSize);
        var items = new List<SearchResultItem>();
        var totalCount = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = checked((int)reader.GetInt64(13));
            items.Add(
                new SearchResultItem(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    new FileOwnerItem(reader.GetGuid(8), reader.GetString(9)),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetGuid(12)));
        }

        return (items, totalCount);
    }

    private static NpgsqlCommand CreateCommand(
        string sql,
        NpgsqlConnection connection,
        Guid actorUserId,
        SearchFilter filter)
    {
        sql = sql.Replace(
            "__eligible_materialization__",
            filter.TagIds.Count > 0 ? "MATERIALIZED" : "NOT MATERIALIZED",
            StringComparison.Ordinal);
        var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = CommandTimeoutSeconds,
        };
        command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, actorUserId);
        command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
        command.Parameters.AddWithValue("match_mode", NpgsqlDbType.Text, filter.MatchMode.ToString().ToUpperInvariant());
        AddNullable(command, "name_pattern", NpgsqlDbType.Text, filter.EscapedPattern);
        AddNullable(command, "normalized_text", NpgsqlDbType.Text, filter.NormalizedText);
        AddNullable(command, "entry_type", NpgsqlDbType.Text, filter.EntryType);
        AddNullable(command, "file_category", NpgsqlDbType.Text, filter.FileCategory?.ToString().ToUpperInvariant());
        AddNullable(command, "status", NpgsqlDbType.Text, filter.Status);
        AddNullable(command, "updated_from", NpgsqlDbType.TimestampTz, filter.UpdatedFrom);
        AddNullable(command, "updated_to", NpgsqlDbType.TimestampTz, filter.UpdatedTo);
        AddNullable(command, "min_size", NpgsqlDbType.Bigint, filter.MinSize);
        AddNullable(command, "max_size", NpgsqlDbType.Bigint, filter.MaxSize);
        AddNullable(command, "owner_user_id", NpgsqlDbType.Uuid, filter.OwnerUserId);
        AddNullable(command, "share_target_id", NpgsqlDbType.Uuid, filter.ShareTargetId);
        command.Parameters.AddWithValue("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, filter.TagIds.ToArray());
        return command;
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
}
