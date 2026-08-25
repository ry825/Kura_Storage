using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class SearchIndexMigrationTests
{
    private const string PreviousMigration = "20260823023404_AddFileSharing";

    [Fact]
    public async Task Migration_EnablesTrigramBuildsExpressionIndexesAndRoundTripsWithoutDataLoss()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("search_index_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync(PreviousMigration);

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var missingFileId = Guid.NewGuid();
        var missingObservationId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand(
                """
                INSERT INTO users
                    (id, username_normalized, display_name, password_hash, role, status,
                     failed_login_count, lock_type, created_at, updated_at)
                VALUES
                    (@owner, 'SEARCHOWNER', 'Search Owner', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                    (@member, 'SEARCHMEMBER', 'Search Member', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
                INSERT INTO file_entries
                    (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                     size, status, missing_detected_at, missing_last_checked_at,
                     missing_observation_id, file_version, created_at, updated_at)
                VALUES
                    (@root, @owner, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE', NULL, NULL, NULL, 1, now(), now()),
                    (@file, @owner, @root, 'FILE', 'Report.txt', @file_path, 'text/plain', 10,
                     'ACTIVE', NULL, NULL, NULL, 1, now(), now()),
                    (@missing_file, @owner, @root, 'FILE', 'Missing.txt', @missing_path, 'text/plain', 11,
                     'MISSING', now(), now(), @missing_observation, 1, now(), now());
                INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
                VALUES (@share, @file, @owner, now(), now());
                INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at)
                VALUES (@share, @member, 'VIEWER', now(), now());
                """,
                connection);
            seed.Parameters.AddWithValue("owner", ownerId);
            seed.Parameters.AddWithValue("member", memberId);
            seed.Parameters.AddWithValue("root", rootId);
            seed.Parameters.AddWithValue("file", fileId);
            seed.Parameters.AddWithValue("missing_file", missingFileId);
            seed.Parameters.AddWithValue("missing_observation", missingObservationId);
            seed.Parameters.AddWithValue("share", shareId);
            seed.Parameters.AddWithValue("root_path", $"users/{ownerId:N}/files");
            seed.Parameters.AddWithValue("file_path", $"users/{ownerId:N}/files/Report.txt");
            seed.Parameters.AddWithValue("missing_path", $"users/{ownerId:N}/files/Missing.txt");
            await seed.ExecuteNonQueryAsync();
        }

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expectedIndexCount: 2, expectedExtensionCount: 1);
        await AssertDataPreservedAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertSchemaAsync(postgres.GetConnectionString(), expectedIndexCount: 0, expectedExtensionCount: 1);
        await AssertDataPreservedAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expectedIndexCount: 2, expectedExtensionCount: 1);
        await AssertDataPreservedAsync(postgres.GetConnectionString());
    }

    private static async Task AssertSchemaAsync(
        string connectionString,
        long expectedIndexCount,
        long expectedExtensionCount)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var indexes = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM pg_indexes
            WHERE tablename = 'file_entries'
              AND indexname IN (
                'ix_file_entries_lower_name_trgm',
                'ix_file_entries_lower_name_prefix_id')
              AND indexdef LIKE '%WHERE%MISSING_CANDIDATE%'
              AND (
                (indexname = 'ix_file_entries_lower_name_trgm' AND indexdef LIKE '%gin_trgm_ops%')
                OR (indexname = 'ix_file_entries_lower_name_prefix_id' AND indexdef LIKE '%text_pattern_ops%id%'));
            """,
            connection))
        {
            Assert.Equal(expectedIndexCount, await indexes.ExecuteScalarAsync());
        }

        await using var extension = new NpgsqlCommand(
            "SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm'",
            connection);
        Assert.Equal(expectedExtensionCount, await extension.ExecuteScalarAsync());
    }

    private static async Task AssertDataPreservedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM file_entries),
                (SELECT count(*) FROM file_entries WHERE status = 'MISSING'),
                (SELECT count(*) FROM shares),
                (SELECT count(*) FROM share_members);
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(1, reader.GetInt64(3));
    }
}
