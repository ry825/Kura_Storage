using System.Diagnostics;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace KuraStorage.IntegrationTests;

public sealed class FileVersionPerformanceTests(ITestOutputHelper output)
{
    private const int FileEntryCount = 300_000;
    private const int ManagedFileCount = FileEntryCount - 1;
    private const int VersionCount = 1_000_000;

    [Fact]
    public async Task MetadataQueries_OnThreeHundredThousandEntriesAndOneMillionVersions_UseIndexes()
    {
        if (Environment.GetEnvironmentVariable("KURASTORAGE_RUN_FILE_VERSION_PERF") != "1")
        {
            return;
        }

        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("file_version_performance")
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

        var targetId = await database.FileEntries
            .Where(entry => entry.Name == "performance-000042.txt")
            .Select(entry => entry.Id)
            .SingleAsync();
        var durations = new List<TimeSpan>();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            var records = await database.FileVersionRecords
                .AsNoTracking()
                .Where(record => record.FileEntryId == targetId)
                .OrderByDescending(record => record.Version)
                .Take(100)
                .ToListAsync();
            stopwatch.Stop();
            Assert.Equal(4, records.Count);
            Assert.Equal(4, records[0].Version);
            durations.Add(stopwatch.Elapsed);
        }

        var historyPlan = await ExplainAsync(
            postgres.GetConnectionString(),
            "SELECT * FROM file_version_records WHERE file_entry_id = @file ORDER BY version DESC LIMIT 100",
            targetId);
        var lookupPlan = await ExplainAsync(
            postgres.GetConnectionString(),
            "SELECT * FROM file_version_records WHERE file_entry_id = @file AND version = 4",
            targetId);
        var purge = Stopwatch.StartNew();
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(
                "DELETE FROM file_version_records WHERE file_entry_id = @file", connection, transaction);
            command.Parameters.AddWithValue("file", targetId);
            Assert.Equal(4, await command.ExecuteNonQueryAsync());
            await transaction.RollbackAsync();
        }
        purge.Stop();

        var metadataBytes = await ScalarAsync<long>(
            postgres.GetConnectionString(),
            "SELECT pg_total_relation_size('file_version_records')");
        var ordered = durations.Order().ToArray();
        var p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
        var result = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Redacted file-version performance: files={0}, versions={1}, seed_ms={2:F0}, history_p95_ms={3:F1}, purge_ms={4:F1}, metadata_bytes={5}",
            FileEntryCount,
            VersionCount,
            seed.Elapsed.TotalMilliseconds,
            p95.TotalMilliseconds,
            purge.Elapsed.TotalMilliseconds,
            metadataBytes);
        output.WriteLine(result);
        Console.Error.WriteLine(result);

        Assert.Contains("Index", historyPlan, StringComparison.Ordinal);
        Assert.Contains("Index", lookupPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on file_version_records", historyPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on file_version_records", lookupPlan, StringComparison.Ordinal);
        Assert.True(p95 < TimeSpan.FromSeconds(2), $"Version history p95 was {p95.TotalMilliseconds:F1} ms.");
        Assert.True(purge.Elapsed < TimeSpan.FromSeconds(2), $"Targeted purge was {purge.Elapsed.TotalMilliseconds:F1} ms.");
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
            VALUES
                ('10000000-0000-4000-8000-000000000001', 'VERSIONPERFORMANCE',
                 'Version Performance', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());

            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            VALUES
                ('10000000-0000-4000-8000-000000000002',
                 '10000000-0000-4000-8000-000000000001', NULL, 'FOLDER', 'Files',
                 'users/10000000000040008000000000000001/files', NULL, 0, 'ACTIVE', 1, now(), now());

            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            SELECT
                md5('version-performance-file-' || value)::uuid,
                '10000000-0000-4000-8000-000000000001'::uuid,
                '10000000-0000-4000-8000-000000000002'::uuid,
                'FILE',
                'performance-' || lpad(value::text, 6, '0') || '.txt',
                'users/10000000000040008000000000000001/files/performance-' || lpad(value::text, 6, '0') || '.txt',
                'text/plain', 1, 'ACTIVE',
                CASE WHEN value <= {{VersionCount - (ManagedFileCount * 3)}} THEN 4 ELSE 3 END,
                now(), now()
            FROM generate_series(1, {{ManagedFileCount}}) AS value;

            INSERT INTO file_version_records
                (id, file_entry_id, version, size, sha256, content_relative_path,
                 change_kind, created_at)
            SELECT
                md5('version-performance-record-' || value)::uuid,
                md5('version-performance-file-' || (((value - 1) % {{ManagedFileCount}}) + 1))::uuid,
                ((value - 1) / {{ManagedFileCount}}) + 1,
                1,
                md5('version-performance-sha-' || value) || md5('version-performance-sha2-' || value),
                'versions/10000000000040008000000000000001/' ||
                    replace(md5('version-performance-file-' || (((value - 1) % {{ManagedFileCount}}) + 1))::uuid::text, '-', '') ||
                    '/' || (((value - 1) / {{ManagedFileCount}}) + 1) || '/' ||
                    md5('version-performance-sha-' || value) || md5('version-performance-sha2-' || value) || '.bin',
                CASE WHEN value <= {{ManagedFileCount}} THEN 'UPLOAD' ELSE 'EXTERNAL_CHANGE' END,
                now()
            FROM generate_series(1, {{VersionCount}}) AS value;

            ANALYZE file_entries;
            ANALYZE file_version_records;
            """,
            connection)
        {
            CommandTimeout = 600,
        };
        await command.ExecuteNonQueryAsync();
        Assert.Equal(
            (long)FileEntryCount,
            await ScalarAsync<long>(connectionString, "SELECT count(*) FROM file_entries"));
        Assert.Equal(
            (long)VersionCount,
            await ScalarAsync<long>(connectionString, "SELECT count(*) FROM file_version_records"));
    }

    private static async Task<string> ExplainAsync(
        string connectionString,
        string query,
        Guid fileId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {query}", connection);
        command.Parameters.AddWithValue("file", fileId);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
