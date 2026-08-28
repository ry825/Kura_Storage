using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Organization;
using KuraStorage.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence.Queries;

public sealed class PostgreSqlOrganizationRepository(KuraStorageDbContext dbContext) : IOrganizationRepository
{
    private const int MaximumHierarchyDepth = 64;
    private const int MaximumTagsPerUser = 200;
    private const int MaximumTagsPerEntry = 20;
    private const int CommandTimeoutSeconds = 10;

    private const string FavoritesCte =
        """
        WITH RECURSIVE actor_favorites AS NOT MATERIALIZED (
            SELECT favorite.entry_id, favorite.favorited_at, entry.owner_user_id, entry.parent_id,
                   entry.entry_type, entry.name, entry.mime_type, entry.size, entry.status,
                   entry.updated_at, entry.relative_path, owner.display_name AS owner_display_name
            FROM favorite_entries AS favorite
            JOIN users AS actor ON actor.id = favorite.user_id AND upper(actor.status) = 'ACTIVE'
            JOIN file_entries AS entry ON entry.id = favorite.entry_id
            JOIN users AS owner ON owner.id = entry.owner_user_id
            WHERE favorite.user_id = @actor_user_id
              AND entry.status IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')
              AND NOT EXISTS (
                  SELECT 1 FROM file_operations AS operation
                  JOIN file_entries AS target ON target.id = operation.file_entry_id
                   AND target.owner_user_id = operation.owner_user_id
                  WHERE operation.owner_user_id = entry.owner_user_id
                    AND operation.status IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')
                    AND (target.id = entry.id OR starts_with(entry.relative_path, target.relative_path || '/')))
        ),
        ancestors AS (
            SELECT entry_id, entry_id AS ancestor_id, parent_id, entry_type, status,
                   0 AS depth, ARRAY[entry_id]::uuid[] AS visited, TRUE AS path_active, FALSE AS is_cycle
            FROM actor_favorites
            UNION ALL
            SELECT child.entry_id, parent.id, parent.parent_id, parent.entry_type, parent.status,
                   child.depth + 1, child.visited || parent.id,
                   child.path_active AND parent.entry_type = 'FOLDER' AND parent.status = 'ACTIVE',
                   parent.id = ANY(child.visited)
            FROM ancestors AS child
            JOIN file_entries AS parent ON parent.id = child.parent_id
            WHERE child.depth < @maximum_depth AND NOT child.is_cycle
        ),
        permission_candidates AS (
            SELECT favorite.entry_id, 'OWNER'::text AS permission, 'OWNER'::text AS permission_source,
                   NULL::uuid AS share_target_id, NULL::uuid AS share_id, 0 AS depth
            FROM actor_favorites AS favorite
            WHERE favorite.owner_user_id = @actor_user_id
            UNION ALL
            SELECT favorite.entry_id, member.permission,
                   CASE WHEN ancestor.depth = 0 THEN 'DIRECT' ELSE 'INHERITED' END,
                   share.target_entry_id, share.id, ancestor.depth
            FROM actor_favorites AS favorite
            JOIN ancestors AS ancestor ON ancestor.entry_id = favorite.entry_id
             AND ancestor.path_active AND NOT ancestor.is_cycle
            JOIN shares AS share ON share.target_entry_id = ancestor.ancestor_id
             AND share.owner_user_id = favorite.owner_user_id
            JOIN share_members AS member ON member.share_id = share.id
             AND member.user_id = @actor_user_id
             AND member.permission IN ('VIEWER', 'CONTRIBUTOR', 'EDITOR', 'MANAGER')
        ),
        ranked AS (
            SELECT candidate.*,
                   row_number() OVER (PARTITION BY candidate.entry_id ORDER BY
                     CASE candidate.permission WHEN 'OWNER' THEN 5 WHEN 'MANAGER' THEN 4
                       WHEN 'EDITOR' THEN 3 WHEN 'CONTRIBUTOR' THEN 2 WHEN 'VIEWER' THEN 1 ELSE 0 END DESC,
                     CASE candidate.permission_source WHEN 'OWNER' THEN 0 WHEN 'DIRECT' THEN 1 ELSE 2 END,
                     candidate.depth, candidate.share_id NULLS FIRST) AS permission_rank
            FROM permission_candidates AS candidate
        ),
        accessible AS MATERIALIZED (
            SELECT favorite.*, ranked.permission, ranked.permission_source, ranked.share_target_id,
                   CASE
                     WHEN favorite.entry_type = 'FOLDER' THEN NULL
                     WHEN lower(coalesce(favorite.mime_type, '')) LIKE 'image/%' THEN 'IMAGE'
                     WHEN lower(coalesce(favorite.mime_type, '')) LIKE 'video/%' THEN 'VIDEO'
                     WHEN lower(coalesce(favorite.mime_type, '')) LIKE 'audio/%' THEN 'AUDIO'
                     WHEN lower(coalesce(favorite.mime_type, '')) LIKE 'text/%'
                       OR lower(coalesce(favorite.mime_type, '')) IN ('application/pdf', 'application/msword',
                         'application/rtf', 'application/vnd.ms-excel', 'application/vnd.ms-powerpoint',
                         'application/vnd.oasis.opendocument.presentation', 'application/vnd.oasis.opendocument.spreadsheet',
                         'application/vnd.oasis.opendocument.text',
                         'application/vnd.openxmlformats-officedocument.presentationml.presentation',
                         'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                         'application/vnd.openxmlformats-officedocument.wordprocessingml.document') THEN 'DOCUMENT'
                     WHEN lower(coalesce(favorite.mime_type, '')) IN ('application/gzip', 'application/vnd.rar',
                       'application/x-7z-compressed', 'application/x-bzip2', 'application/x-tar', 'application/zip') THEN 'ARCHIVE'
                     ELSE 'OTHER' END AS file_category
            FROM actor_favorites AS favorite
            JOIN ranked ON ranked.entry_id = favorite.entry_id AND ranked.permission_rank = 1
        )
        """;

    private const string FavoritePageSql = FavoritesCte +
        """
        SELECT entry_id, entry_type, name, mime_type, file_category, size, status, updated_at,
               owner_user_id, owner_display_name, permission, permission_source, share_target_id,
               favorited_at, count(*) OVER() AS total_count
        FROM accessible
        ORDER BY favorited_at DESC, entry_id
        OFFSET @offset_rows LIMIT @page_size;
        """;

    private const string FavoriteCountSql = FavoritesCte + "SELECT count(*) FROM accessible;";

    public async Task<OrganizationRepositoryOutcome> TryAddFavoriteAuthorizedAsync(
        Guid userId,
        Guid entryId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var scope = await LockEntryScopeAsync(entryId, [], cancellationToken);
        if (scope is null || !await IsAuthorizedAsync(userId, entryId, activeOnly: true, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return OrganizationRepositoryOutcome.NotFound;
        }

        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO favorite_entries (user_id, entry_id, favorited_at)
            VALUES ({userId}, {entryId}, {now})
            ON CONFLICT (user_id, entry_id) DO NOTHING
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected == 1 ? OrganizationRepositoryOutcome.Created : OrganizationRepositoryOutcome.NoChange;
    }

    public Task RemoveFavoriteAsync(Guid userId, Guid entryId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM favorite_entries WHERE user_id = {userId} AND entry_id = {entryId}",
            cancellationToken);

    public async Task<FavoritePage> ListFavoritesAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = CreateFavoritesCommand(FavoritePageSql, connection, userId);
            command.Parameters.AddWithValue("offset_rows", NpgsqlDbType.Integer, checked((page - 1) * pageSize));
            command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
            var items = new List<FavoriteItem>();
            var totalCount = 0;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    totalCount = checked((int)reader.GetInt64(14));
                    items.Add(new FavoriteItem(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5),
                        reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7),
                        new FileOwnerItem(reader.GetGuid(8), reader.GetString(9)),
                        reader.GetString(10), reader.GetString(11),
                        reader.IsDBNull(12) ? null : reader.GetGuid(12),
                        reader.GetFieldValue<DateTimeOffset>(13)));
                }
            }

            if (items.Count == 0 && page > 1)
            {
                await using var count = CreateFavoritesCommand(FavoriteCountSql, connection, userId);
                totalCount = checked((int)(long)(await count.ExecuteScalarAsync(cancellationToken) ?? 0L));
            }

            return new FavoritePage(items, page, pageSize, totalCount);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    public async Task<IReadOnlyList<TagItem>> ListTagsAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Tags.AsNoTracking()
            .Where(tag => tag.UserId == userId)
            .OrderBy(tag => tag.NameKey).ThenBy(tag => tag.Id)
            .Select(tag => new TagItem(tag.Id, tag.Name))
            .ToListAsync(cancellationToken);

    public async Task<OrganizationRepositoryResult<TagItem>> TryCreateTagAsync(
        Guid userId,
        string name,
        string nameKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLocksAsync([ToOrganizationLockKey(userId)], cancellationToken);
        if (!await dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.Status == Domain.Identity.UserStatus.Active, cancellationToken))
        {
            return new(OrganizationRepositoryOutcome.NotFound);
        }

        var existing = await dbContext.Tags.AsNoTracking()
            .Where(tag => tag.UserId == userId && tag.NameKey == nameKey)
            .Select(tag => new TagItem(tag.Id, tag.Name))
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new(OrganizationRepositoryOutcome.Conflict);
        }

        if (await dbContext.Tags.CountAsync(tag => tag.UserId == userId, cancellationToken) >= MaximumTagsPerUser)
        {
            return new(OrganizationRepositoryOutcome.UserLimitExceeded);
        }

        var tag = Tag.Create(Guid.NewGuid(), userId, name, nameKey, now);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(OrganizationRepositoryOutcome.Created, new TagItem(tag.Id, tag.Name));
    }

    public async Task<OrganizationRepositoryResult<TagItem>> TryRenameTagAsync(
        Guid userId,
        Guid tagId,
        string name,
        string nameKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLocksAsync([ToOrganizationLockKey(userId)], cancellationToken);
        var tag = await dbContext.Tags.SingleOrDefaultAsync(item => item.Id == tagId && item.UserId == userId, cancellationToken);
        if (tag is null) return new(OrganizationRepositoryOutcome.NotFound);
        if (await dbContext.Tags.AsNoTracking().AnyAsync(
            item => item.UserId == userId && item.Id != tagId && item.NameKey == nameKey,
            cancellationToken))
        {
            return new(OrganizationRepositoryOutcome.Conflict);
        }

        var changed = tag.Rename(name, nameKey, now);
        if (changed) await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(changed ? OrganizationRepositoryOutcome.Created : OrganizationRepositoryOutcome.NoChange, new TagItem(tag.Id, tag.Name));
    }

    public async Task<OrganizationRepositoryOutcome> DeleteTagAsync(
        Guid userId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLocksAsync([ToOrganizationLockKey(userId)], cancellationToken);
        var affected = await dbContext.Tags.Where(tag => tag.Id == tagId && tag.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected == 1 ? OrganizationRepositoryOutcome.NoChange : OrganizationRepositoryOutcome.NotFound;
    }

    public async Task<EntryOrganizationState?> GetEntryOrganizationAsync(
        Guid userId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(userId, entryId, activeOnly: false, cancellationToken)) return null;
        var favorite = await dbContext.FavoriteEntries.AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.EntryId == entryId, cancellationToken);
        var tags = await (
            from relation in dbContext.EntryTags.AsNoTracking()
            join tag in dbContext.Tags.AsNoTracking() on relation.TagId equals tag.Id
            where relation.EntryId == entryId && tag.UserId == userId
            orderby tag.NameKey, tag.Id
            select new TagItem(tag.Id, tag.Name))
            .Take(MaximumTagsPerEntry)
            .ToListAsync(cancellationToken);
        return new EntryOrganizationState(favorite, tags);
    }

    public async Task<OrganizationRepositoryOutcome> TryAttachTagAuthorizedAsync(
        Guid userId,
        Guid entryId,
        Guid tagId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var scope = await ListEntryScopeAsync(entryId, cancellationToken);
        var lockKeys = scope.Select(ToEntryLockKey).Append(ToOrganizationLockKey(userId)).Distinct().Order().ToArray();
        await AcquireLocksAsync(lockKeys, cancellationToken);
        if (!scope.SequenceEqual(await ListEntryScopeAsync(entryId, cancellationToken)) ||
            !await IsAuthorizedAsync(userId, entryId, activeOnly: true, cancellationToken) ||
            !await dbContext.Tags.AsNoTracking().AnyAsync(tag => tag.Id == tagId && tag.UserId == userId, cancellationToken))
        {
            return OrganizationRepositoryOutcome.NotFound;
        }

        if (await dbContext.EntryTags.AsNoTracking().AnyAsync(item => item.TagId == tagId && item.EntryId == entryId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return OrganizationRepositoryOutcome.NoChange;
        }

        var count = await (
            from relation in dbContext.EntryTags.AsNoTracking()
            join tag in dbContext.Tags.AsNoTracking() on relation.TagId equals tag.Id
            where relation.EntryId == entryId && tag.UserId == userId
            select relation).CountAsync(cancellationToken);
        if (count >= MaximumTagsPerEntry) return OrganizationRepositoryOutcome.EntryLimitExceeded;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO entry_tags (tag_id, entry_id, attached_at)
            VALUES ({tagId}, {entryId}, {now})
            ON CONFLICT (tag_id, entry_id) DO NOTHING
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OrganizationRepositoryOutcome.Created;
    }

    public Task DetachTagAsync(Guid userId, Guid entryId, Guid tagId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM entry_tags AS relation
            USING tags AS tag
            WHERE relation.tag_id = tag.id AND relation.tag_id = {tagId}
              AND relation.entry_id = {entryId} AND tag.user_id = {userId}
            """,
            cancellationToken);

    private async Task<IReadOnlyList<Guid>?> LockEntryScopeAsync(
        Guid entryId,
        IReadOnlyList<long> additionalKeys,
        CancellationToken cancellationToken)
    {
        var initial = await ListEntryScopeAsync(entryId, cancellationToken);
        if (initial.Count == 0) return null;
        await AcquireLocksAsync(initial.Select(ToEntryLockKey).Concat(additionalKeys).Distinct().Order(), cancellationToken);
        var locked = await ListEntryScopeAsync(entryId, cancellationToken);
        return initial.SequenceEqual(locked) ? locked : null;
    }

    private async Task<IReadOnlyList<Guid>> ListEntryScopeAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            """
            WITH RECURSIVE ancestors AS (
              SELECT id, parent_id, 0 AS depth, ARRAY[id]::uuid[] AS visited, FALSE AS is_cycle
              FROM file_entries WHERE id = @entry_id
              UNION ALL
              SELECT parent.id, parent.parent_id, child.depth + 1, child.visited || parent.id,
                     parent.id = ANY(child.visited)
              FROM ancestors AS child JOIN file_entries AS parent ON parent.id = child.parent_id
              WHERE child.depth < @maximum_depth AND NOT child.is_cycle)
            SELECT id FROM ancestors WHERE NOT is_cycle ORDER BY id;
            """,
            connection,
            dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        command.Parameters.AddWithValue("entry_id", NpgsqlDbType.Uuid, entryId);
        command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task<bool> IsAuthorizedAsync(
        Guid userId,
        Guid entryId,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var statuses = activeOnly ? "ARRAY['ACTIVE']" : "ARRAY['ACTIVE','MISSING_CANDIDATE','MISSING']";
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                $"""
                WITH RECURSIVE eligible AS (
                  SELECT id, owner_user_id, parent_id, relative_path FROM file_entries AS entry
                  WHERE id = @entry_id AND status = ANY({statuses})
                    AND NOT EXISTS (
                      SELECT 1 FROM file_operations AS operation
                      JOIN file_entries AS target ON target.id = operation.file_entry_id
                       AND target.owner_user_id = operation.owner_user_id
                      WHERE operation.owner_user_id = entry.owner_user_id
                        AND operation.status IN ('PENDING','FILESYSTEM_DONE','RECOVERY_REQUIRED')
                        AND (target.id = entry.id OR starts_with(entry.relative_path, target.relative_path || '/')))),
                ancestors AS (
                  SELECT entry.id, entry.parent_id, entry.entry_type, entry.status, 0 AS depth,
                         ARRAY[entry.id]::uuid[] AS visited, TRUE AS path_active, FALSE AS is_cycle
                  FROM eligible JOIN file_entries AS entry ON entry.id = eligible.id
                  UNION ALL
                  SELECT parent.id, parent.parent_id, parent.entry_type, parent.status, child.depth + 1,
                         child.visited || parent.id,
                         child.path_active AND parent.entry_type = 'FOLDER' AND parent.status = 'ACTIVE',
                         parent.id = ANY(child.visited)
                  FROM ancestors AS child JOIN file_entries AS parent ON parent.id = child.parent_id
                  WHERE child.depth < @maximum_depth AND NOT child.is_cycle)
                SELECT EXISTS (
                  SELECT 1 FROM eligible
                  JOIN users AS actor ON actor.id = @actor_user_id AND upper(actor.status) = 'ACTIVE'
                  WHERE eligible.owner_user_id = actor.id OR EXISTS (
                    SELECT 1 FROM share_members AS member
                    JOIN shares AS share ON share.id = member.share_id
                    JOIN ancestors ON ancestors.id = share.target_entry_id
                     AND ancestors.path_active AND NOT ancestors.is_cycle
                    WHERE member.user_id = actor.id
                      AND member.permission IN ('VIEWER','CONTRIBUTOR','EDITOR','MANAGER')
                      AND share.owner_user_id = eligible.owner_user_id));
                """,
                connection,
                dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
            command.Parameters.AddWithValue("entry_id", NpgsqlDbType.Uuid, entryId);
            command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, userId);
            command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private async Task AcquireLocksAsync(IEnumerable<long> lockKeys, CancellationToken cancellationToken)
    {
        foreach (var key in lockKeys)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
        }
    }

    private static NpgsqlCommand CreateFavoritesCommand(string sql, NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("actor_user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("maximum_depth", NpgsqlDbType.Integer, MaximumHierarchyDepth);
        return command;
    }

    private static long ToEntryLockKey(Guid id) => Hash(id.ToByteArray());

    private static long ToOrganizationLockKey(Guid id) => Hash(Encoding.UTF8.GetBytes($"organization:{id:N}"));

    private static long Hash(ReadOnlySpan<byte> value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(value, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
