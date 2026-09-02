using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Activity;
using KuraStorage.Application.Identity;
using KuraStorage.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace KuraStorage.Infrastructure.Persistence.Queries;

public sealed class PostgreSqlUserActivityAdminQueryRepository(KuraStorageDbContext dbContext)
    : IUserActivityAdminQueryRepository
{
    private const int CommandTimeoutSeconds = 10;

    private const string AdminQuerySql =
        """
        SELECT __projection__
        FROM user_activities AS activity
        WHERE (@actor_user_id IS NULL OR activity.actor_user_id = @actor_user_id)
          AND (@owner_user_id IS NULL OR activity.owner_user_id = @owner_user_id)
          AND (@activity_type IS NULL OR activity.activity_type = @activity_type)
          AND (@from_time IS NULL OR activity.occurred_at >= @from_time)
          AND (@to_time IS NULL OR activity.occurred_at <= @to_time)
          AND (@file_id IS NULL OR activity.target_entry_id = @file_id)
          AND (@cursor_time IS NULL OR (activity.occurred_at, activity.id) < (@cursor_time, @cursor_id))
        ORDER BY activity.occurred_at DESC, activity.id DESC
        LIMIT @limit;
        """;

    public async Task<IReadOnlyList<ActivityRecord>?> SearchAsync(
        AdminActivitySearchFilter filter,
        string actorOsUser,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var actorId = await ResolveUserAsync(filter.ActorUser, cancellationToken);
        var ownerId = await ResolveUserAsync(filter.OwnerUser, cancellationToken);
        if ((filter.ActorUser is not null && actorId is null) || (filter.OwnerUser is not null && ownerId is null))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var sql = AdminQuerySql.Replace(
                "__projection__",
                PostgreSqlUserActivityQueryRepository.Projection,
                StringComparison.Ordinal)
            .Replace("__target_entry_id__", "activity.target_entry_id", StringComparison.Ordinal);
        await using var command = new NpgsqlCommand(
            sql,
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction())
        {
            CommandTimeout = CommandTimeoutSeconds,
        };
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "actor_user_id", NpgsqlDbType.Uuid, actorId);
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "owner_user_id", NpgsqlDbType.Uuid, ownerId);
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "activity_type", NpgsqlDbType.Text, filter.Type?.ToString().ToUpperInvariant());
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "from_time", NpgsqlDbType.TimestampTz, filter.From);
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "to_time", NpgsqlDbType.TimestampTz, filter.To);
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "file_id", NpgsqlDbType.Uuid, filter.FileId);
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "cursor_time", NpgsqlDbType.TimestampTz, filter.Cursor?.OccurredAt);
        PostgreSqlUserActivityQueryRepository.AddNullable(command, "cursor_id", NpgsqlDbType.Uuid, filter.Cursor?.Id);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, checked(filter.Limit + 1));
        var records = await PostgreSqlUserActivityQueryRepository.ReadAsync(command, cancellationToken);

        dbContext.AuditLogs.Add(
            new AuditLog(
                Guid.NewGuid(), null, null, actorOsUser,
                "ACTIVITY_SEARCH", "USER_ACTIVITY",
                CreateAuditSummary(filter, Math.Min(records.Count, filter.Limit)), "SUCCESS", null, occurredAt,
                AuditActorType.AdminCli));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    private async Task<Guid?> ResolveUserAsync(string? selector, CancellationToken cancellationToken)
    {
        if (selector is null)
        {
            return null;
        }

        if (Guid.TryParse(selector, out var id) && id != Guid.Empty)
        {
            return await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == id)
                .Select(user => (Guid?)user.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var normalized = UsernameNormalizer.Normalize(selector);
        return await dbContext.Users.AsNoTracking()
            .Where(user => user.UsernameNormalized == normalized)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string CreateAuditSummary(AdminActivitySearchFilter filter, int count)
    {
        Span<char> flags = stackalloc char[6];
        flags[0] = filter.ActorUser is null ? '-' : 'A';
        flags[1] = filter.OwnerUser is null ? '-' : 'O';
        flags[2] = filter.Type is null ? '-' : 'T';
        flags[3] = filter.From is null && filter.To is null ? '-' : 'D';
        flags[4] = filter.FileId is null ? '-' : 'F';
        flags[5] = filter.Cursor is null ? '-' : 'C';
        return $"{flags.ToString()}:{count}";
    }
}
