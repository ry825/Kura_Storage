using System.Diagnostics;
using System.Reflection;
using KuraStorage.Application.Activity;
using KuraStorage.Infrastructure.Persistence.Queries;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace KuraStorage.IntegrationTests;

public sealed class UserActivityPerformanceTests(ITestOutputHelper output)
{
    private const int ActivityCount = 1_000_000;
    private const int FileCount = 300_000;
    private const int InsertSampleCount = 1_000;

    [Fact]
    public async Task OneMillionActivitySeed_RecordsCapacityCreationAndInsertOverhead()
    {
        if (Environment.GetEnvironmentVariable("KURASTORAGE_RUN_USER_ACTIVITY_PERF") != "1")
        {
            return;
        }

        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("user_activity_performance")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();

        var seed = Stopwatch.StartNew();
        await SeedAsync(postgres.GetConnectionString());
        seed.Stop();

        var insert = Stopwatch.StartNew();
        await MeasureInsertSampleAsync(postgres.GetConnectionString());
        insert.Stop();

        var tableBytes = await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT pg_table_size('user_activities')");
        var indexBytes = await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT pg_indexes_size('user_activities')");
        var totalBytes = await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT pg_total_relation_size('user_activities')");
        var logicalBackupBytes = await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT coalesce(sum(pg_column_size(activity)), 0)::bigint FROM user_activities AS activity");

        var queryResults = await MeasureQueriesAsync(database);
        var plans = await ExplainQueriesAsync(postgres.GetConnectionString());

        var result = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Redacted user-activity capacity: activities={0}, seed_ms={1:F0}, insert_sample={2}, " +
            "insert_us_per_row={3:F1}, table_bytes={4}, index_bytes={5}, total_bytes={6}, logical_backup_bytes={7}, " +
            "query_p50_ms={8:F1}, query_p95_ms={9:F1}, process_cpu_ms={10:F1}, working_set_bytes={11}",
            ActivityCount,
            seed.Elapsed.TotalMilliseconds,
            InsertSampleCount,
            insert.Elapsed.TotalMicroseconds / InsertSampleCount,
            tableBytes,
            indexBytes,
            totalBytes,
            logicalBackupBytes,
            queryResults.MaximumP50Milliseconds,
            queryResults.MaximumP95Milliseconds,
            queryResults.ProcessCpuMilliseconds,
            queryResults.WorkingSetBytes);
        output.WriteLine(result);
        Console.Error.WriteLine(result);

        Assert.Equal((long)ActivityCount, await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT count(*) FROM user_activities"));
        Assert.True(tableBytes > 0);
        Assert.True(indexBytes > 0);
        Assert.True(logicalBackupBytes > 0);
        Assert.True(queryResults.MaximumP95Milliseconds < 2_000, result);
        Assert.Contains("user_activities", plans, StringComparison.Ordinal);
        Assert.Contains("Buffers:", plans, StringComparison.Ordinal);
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $$"""
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            SELECT
                md5('activity-user-' || value)::uuid,
                'ACTIVITYUSER' || value,
                'Activity User ' || value,
                'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()
            FROM generate_series(1, 10) AS value;

            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, size,
                 status, file_version, created_at, updated_at)
            SELECT
                md5('activity-root-' || value)::uuid,
                md5('activity-user-' || value)::uuid,
                NULL, 'FOLDER', 'Files',
                'users/' || replace(md5('activity-user-' || value)::uuid::text, '-', '') || '/files',
                0, 'ACTIVE', 1, now(), now()
            FROM generate_series(1, 10) AS value;

            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type, size,
                 status, file_version, created_at, updated_at)
            SELECT
                md5('activity-file-' || value)::uuid,
                md5('activity-user-' || (((value - 1) % 10) + 1))::uuid,
                md5('activity-root-' || (((value - 1) % 10) + 1))::uuid,
                'FILE', 'activity-' || value || '.txt',
                'users/' || replace(md5('activity-user-' || (((value - 1) % 10) + 1))::uuid::text, '-', '') ||
                    '/files/activity-' || value || '.txt',
                'text/plain', value % 1048576, 'ACTIVE', 1, now(), now()
            FROM generate_series(1, {{FileCount - 10}}) AS value;

            INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
            VALUES
                (md5('activity-current-share')::uuid, md5('activity-root-2')::uuid,
                 md5('activity-user-2')::uuid, now(), now());
            INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at)
            VALUES
                (md5('activity-current-share')::uuid, md5('activity-user-1')::uuid,
                 'VIEWER', now(), now());

            INSERT INTO user_activities
                (id, operation_id, activity_type, occurred_at,
                 actor_user_id, actor_display_name, actor_device_name,
                 target_entry_id, target_type, target_name,
                 owner_user_id, owner_display_name, parent_entry_id, detail_kind,
                 source_parent_name, destination_parent_name,
                 resulting_file_version, edit_kind,
                 recipient_user_id, recipient_display_name, share_permission, share_action,
                 delete_kind)
            SELECT
                md5('activity-record-' || value)::uuid,
                md5('activity-operation-' || value)::uuid,
                CASE value % 5
                    WHEN 0 THEN 'UPLOAD' WHEN 1 THEN 'MOVE' WHEN 2 THEN 'EDIT'
                    WHEN 3 THEN 'SHARE' ELSE 'DELETE' END,
                timestamptz '2026-09-02 00:00:00+00' + value * interval '1 millisecond',
                CASE WHEN value % 20 = 2
                    THEN md5('activity-user-1')::uuid
                    ELSE md5('activity-user-' || ((((value - 1) % {{FileCount - 10}}) % 10) + 1))::uuid END,
                CASE WHEN value % 20 = 2
                    THEN 'Activity User 1'
                    ELSE 'Activity User ' || ((((value - 1) % {{FileCount - 10}}) % 10) + 1) END,
                'Device ' || ((value % 20) + 1),
                CASE WHEN value % 20 = 0 THEN NULL
                    ELSE md5('activity-file-' || (((value - 1) % {{FileCount - 10}}) + 1))::uuid END,
                'FILE', 'activity-' || (((value - 1) % {{FileCount - 10}}) + 1) || '.txt',
                md5('activity-user-' || ((((value - 1) % {{FileCount - 10}}) % 10) + 1))::uuid,
                'Activity User ' || ((((value - 1) % {{FileCount - 10}}) % 10) + 1),
                md5('activity-root-' || ((((value - 1) % {{FileCount - 10}}) % 10) + 1))::uuid,
                CASE value % 5
                    WHEN 0 THEN 'UPLOAD' WHEN 1 THEN 'MOVE' WHEN 2 THEN 'EDIT'
                    WHEN 3 THEN 'SHARE' ELSE 'DELETE' END,
                CASE WHEN value % 5 = 1 THEN 'Source' END,
                CASE WHEN value % 5 = 1 THEN 'Destination' END,
                CASE WHEN value % 5 IN (0, 2) THEN (value % 100) + 1 END,
                CASE WHEN value % 5 = 2 THEN 'TEXT_SAVE' END,
                CASE WHEN value % 5 = 3 THEN md5('activity-user-' || (((value + 1) % 10) + 1))::uuid END,
                CASE WHEN value % 5 = 3 THEN 'Activity Recipient' END,
                CASE WHEN value % 5 = 3 THEN 'Editor' END,
                CASE WHEN value % 5 = 3 THEN 'CREATED' END,
                CASE WHEN value % 5 = 4 THEN 'TRASHED' END
            FROM generate_series(1, {{ActivityCount}}) AS value;

            ANALYZE user_activities;
            """,
            connection)
        {
            CommandTimeout = 600,
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MeasureInsertSampleAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            $$"""
            INSERT INTO user_activities
                (id, operation_id, activity_type, occurred_at, actor_display_name,
                 target_type, target_name, owner_display_name, detail_kind, resulting_file_version)
            SELECT
                md5('activity-overhead-record-' || value)::uuid,
                md5('activity-overhead-operation-' || value)::uuid,
                'UPLOAD', now(), 'Actor', 'FILE', 'sample.txt', 'Owner', 'UPLOAD', 1
            FROM generate_series(1, {{InsertSampleCount}}) AS value;
            """,
            connection,
            transaction);
        Assert.Equal(InsertSampleCount, await command.ExecuteNonQueryAsync());
        await transaction.RollbackAsync();
    }

    private static async Task<QueryMeasurement> MeasureQueriesAsync(KuraStorageDbContext database)
    {
        var actorId = await database.Users
            .Where(user => user.UsernameNormalized == "ACTIVITYUSER1")
            .Select(user => user.Id)
            .SingleAsync();
        var fileId = await database.FileEntries
            .Where(entry => entry.OwnerUserId == actorId && entry.ParentId != null)
            .Select(entry => entry.Id)
            .FirstAsync();
        var repository = new PostgreSqlUserActivityQueryRepository(database);
        var adminRepository = new PostgreSqlUserActivityAdminQueryRepository(database);
        var first = await repository.ListAsync(
            actorId,
            new ActivityQueryFilter(null, null, 100),
            CancellationToken.None);
        var cursor = new ActivityCursor(first[^1].OccurredAt, first[^1].Id);
        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var measurements = new List<(double P50, double P95)>();

        await MeasureAsync(() => repository.ListAsync(
            actorId, new ActivityQueryFilter(null, null, 100), CancellationToken.None), measurements);
        await MeasureAsync(() => repository.ListAsync(
            actorId, new ActivityQueryFilter(null, cursor, 100), CancellationToken.None), measurements);
        await MeasureAsync(() => repository.ListAsync(
            actorId, new ActivityQueryFilter(KuraStorage.Domain.Activity.UserActivityType.Edit, null, 100),
            CancellationToken.None), measurements);
        await MeasureAsync(() => adminRepository.SearchAsync(
            new AdminActivitySearchFilter("ACTIVITYUSER1", null, null, null, null, null, 100, null),
            "performance-admin", DateTimeOffset.UtcNow, CancellationToken.None), measurements);
        await MeasureAsync(() => adminRepository.SearchAsync(
            new AdminActivitySearchFilter(null, "ACTIVITYUSER1", null, null, null, null, 100, null),
            "performance-admin", DateTimeOffset.UtcNow, CancellationToken.None), measurements);
        await MeasureAsync(() => adminRepository.SearchAsync(
            new AdminActivitySearchFilter(null, null, KuraStorage.Domain.Activity.UserActivityType.Share,
                DateTimeOffset.Parse("2026-09-02T00:00:00Z"), DateTimeOffset.Parse("2026-09-03T00:00:00Z"),
                null, 100, null),
            "performance-admin", DateTimeOffset.UtcNow, CancellationToken.None), measurements);
        await MeasureAsync(() => adminRepository.SearchAsync(
            new AdminActivitySearchFilter(null, null, null, null, null, fileId, 100, null),
            "performance-admin", DateTimeOffset.UtcNow, CancellationToken.None), measurements);

        process.Refresh();
        return new QueryMeasurement(
            measurements.Max(sample => sample.P50),
            measurements.Max(sample => sample.P95),
            (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            process.WorkingSet64);

        static async Task MeasureAsync<T>(Func<Task<T>> action, List<(double P50, double P95)> target)
        {
            var samples = new List<double>();
            for (var iteration = 0; iteration < 10; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                _ = await action();
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            target.Add((
                samples[(int)Math.Ceiling(samples.Count * 0.50) - 1],
                samples[(int)Math.Ceiling(samples.Count * 0.95) - 1]));
        }
    }

    private static async Task<string> ExplainQueriesAsync(string connectionString)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var repositoryType = typeof(PostgreSqlUserActivityQueryRepository);
        var adminRepositoryType = typeof(PostgreSqlUserActivityAdminQueryRepository);
        var projection = (string)repositoryType.GetField("Projection", flags)!.GetRawConstantValue()!;
        var userSql = ((string)repositoryType.GetField("UserQuerySql", flags)!.GetRawConstantValue()!)
            .Replace("__projection__", projection, StringComparison.Ordinal)
            .Replace(
                "__target_entry_id__",
                "CASE WHEN activity.target_entry_id IN (SELECT id FROM visible_entries) THEN activity.target_entry_id ELSE NULL END",
                StringComparison.Ordinal);
        var adminSql = ((string)adminRepositoryType.GetField("AdminQuerySql", flags)!.GetRawConstantValue()!)
            .Replace("__projection__", projection, StringComparison.Ordinal)
            .Replace("__target_entry_id__", "activity.target_entry_id", StringComparison.Ordinal);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var actorId = (Guid)(await new NpgsqlCommand(
            "SELECT id FROM users WHERE username_normalized = 'ACTIVITYUSER1'", connection).ExecuteScalarAsync())!;
        Guid fileId;
        DateTimeOffset cursorTime;
        Guid cursorId;
        await using (var cursorCommand = new NpgsqlCommand(
            "SELECT occurred_at, id, target_entry_id FROM user_activities WHERE target_entry_id IS NOT NULL ORDER BY occurred_at DESC, id DESC OFFSET 100 LIMIT 1",
            connection))
        await using (var cursorReader = await cursorCommand.ExecuteReaderAsync())
        {
            Assert.True(await cursorReader.ReadAsync());
            cursorTime = cursorReader.GetFieldValue<DateTimeOffset>(0);
            cursorId = cursorReader.GetGuid(1);
            fileId = cursorReader.GetGuid(2);
        }

        var specs = new[]
        {
            new ExplainSpec(true, actorId, null, null, null, null, null, null, null),
            new ExplainSpec(true, actorId, null, null, null, null, null, cursorTime, cursorId),
            new ExplainSpec(true, actorId, null, "EDIT", null, null, null, null, null),
            new ExplainSpec(false, actorId, null, null, null, null, null, null, null),
            new ExplainSpec(false, null, actorId, null, null, null, null, null, null),
            new ExplainSpec(false, null, null, "SHARE", null, null, null, null, null),
            new ExplainSpec(false, null, null, null,
                DateTimeOffset.Parse("2026-09-02T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-03T00:00:00Z"), null, null, null),
            new ExplainSpec(false, null, null, null, null, null, fileId, null, null),
        };
        var plans = new List<string>();
        foreach (var spec in specs)
        {
            var sql = spec.UserQuery ? userSql : adminSql;
            await using var command = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {sql}", connection)
            {
                CommandTimeout = 120,
            };
            Add(command, "actor_user_id", NpgsqlTypes.NpgsqlDbType.Uuid, spec.ActorUserId);
            command.Parameters.AddWithValue("maximum_depth", NpgsqlTypes.NpgsqlDbType.Integer, 64);
            Add(command, "owner_user_id", NpgsqlTypes.NpgsqlDbType.Uuid, spec.OwnerUserId);
            Add(command, "activity_type", NpgsqlTypes.NpgsqlDbType.Text, spec.Type);
            Add(command, "from_time", NpgsqlTypes.NpgsqlDbType.TimestampTz, spec.From);
            Add(command, "to_time", NpgsqlTypes.NpgsqlDbType.TimestampTz, spec.To);
            Add(command, "file_id", NpgsqlTypes.NpgsqlDbType.Uuid, spec.FileId);
            Add(command, "cursor_time", NpgsqlTypes.NpgsqlDbType.TimestampTz, spec.CursorTime);
            Add(command, "cursor_id", NpgsqlTypes.NpgsqlDbType.Uuid, spec.CursorId);
            command.Parameters.AddWithValue("limit", NpgsqlTypes.NpgsqlDbType.Integer, 100);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plans.Add(reader.GetString(0));
            }
        }

        return string.Join(Environment.NewLine, plans);

        static void Add(NpgsqlCommand command, string name, NpgsqlTypes.NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
    }

    private sealed record QueryMeasurement(
        double MaximumP50Milliseconds,
        double MaximumP95Milliseconds,
        double ProcessCpuMilliseconds,
        long WorkingSetBytes);

    private sealed record ExplainSpec(
        bool UserQuery,
        Guid? ActorUserId,
        Guid? OwnerUserId,
        string? Type,
        DateTimeOffset? From,
        DateTimeOffset? To,
        Guid? FileId,
        DateTimeOffset? CursorTime,
        Guid? CursorId);

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
