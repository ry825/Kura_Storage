using System.Diagnostics;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace KuraStorage.IntegrationTests;

public sealed class UserActivityPerformanceTests(ITestOutputHelper output)
{
    private const int ActivityCount = 1_000_000;
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

        var result = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Redacted user-activity capacity: activities={0}, seed_ms={1:F0}, insert_sample={2}, " +
            "insert_us_per_row={3:F1}, table_bytes={4}, index_bytes={5}, total_bytes={6}, logical_backup_bytes={7}",
            ActivityCount,
            seed.Elapsed.TotalMilliseconds,
            InsertSampleCount,
            insert.Elapsed.TotalMicroseconds / InsertSampleCount,
            tableBytes,
            indexBytes,
            totalBytes,
            logicalBackupBytes);
        output.WriteLine(result);
        Console.Error.WriteLine(result);

        Assert.Equal((long)ActivityCount, await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT count(*) FROM user_activities"));
        Assert.True(tableBytes > 0);
        Assert.True(indexBytes > 0);
        Assert.True(logicalBackupBytes > 0);
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
                md5('activity-user-' || ((value % 10) + 1))::uuid,
                'Activity User ' || ((value % 10) + 1),
                'Device ' || ((value % 20) + 1),
                md5('activity-root-' || ((value % 10) + 1))::uuid,
                'FOLDER', 'Files',
                md5('activity-user-' || ((value % 10) + 1))::uuid,
                'Activity User ' || ((value % 10) + 1),
                NULL,
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

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
