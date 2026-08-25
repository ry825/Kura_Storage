using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class RecentFileMigrationTests
{
    private const string PreviousMigration = "20260824113810_AddSearchIndexes";

    [Fact]
    public async Task Migration_RoundTripsSchemaRowsIndexesAndCascades()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("recent_file_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync(PreviousMigration);
        await SeedAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expectedTableCount: 1);

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO recent_files (user_id, file_id, opened_at)
                SELECT id, @file, now() FROM users WHERE username_normalized = 'RECENTMIGRATION';
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("file", FileId);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
            await using var delete = new NpgsqlCommand(
                "DELETE FROM file_entries WHERE id = @file",
                connection,
                transaction);
            delete.Parameters.AddWithValue("file", FileId);
            Assert.Equal(1, await delete.ExecuteNonQueryAsync());
            await using var recentCount = new NpgsqlCommand(
                "SELECT count(*) FROM recent_files",
                connection,
                transaction);
            Assert.Equal(0L, await recentCount.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertSchemaAsync(postgres.GetConnectionString(), expectedTableCount: 0);
        await AssertBaseRowsAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expectedTableCount: 1);
        await AssertBaseRowsAsync(postgres.GetConnectionString());
    }

    private static readonly Guid FileId = Guid.NewGuid();

    private static async Task SeedAsync(string connectionString)
    {
        var userId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            VALUES (@user, 'RECENTMIGRATION', 'Recent Migration', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            VALUES
                (@root, @user, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE', 1, now(), now()),
                (@file, @user, @root, 'FILE', 'Recent.txt', @file_path, 'text/plain', 1, 'ACTIVE', 1, now(), now());
            """,
            connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("root", rootId);
        command.Parameters.AddWithValue("file", FileId);
        command.Parameters.AddWithValue("root_path", $"users/{userId:N}/files");
        command.Parameters.AddWithValue("file_path", $"users/{userId:N}/files/Recent.txt");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSchemaAsync(string connectionString, long expectedTableCount)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var table = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'recent_files'",
            connection);
        Assert.Equal(expectedTableCount, await table.ExecuteScalarAsync());
        if (expectedTableCount == 0)
        {
            return;
        }

        await using var indexes = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_indexes
            WHERE tablename = 'recent_files'
              AND indexname IN ('ix_recent_files_user_opened_at_file_id', 'ix_recent_files_file_id')
              AND (indexname <> 'ix_recent_files_user_opened_at_file_id' OR indexdef LIKE '%opened_at DESC%');
            """,
            connection);
        Assert.Equal(2L, await indexes.ExecuteScalarAsync());
        await using var cascades = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM pg_constraint
            WHERE conrelid = 'recent_files'::regclass
              AND contype = 'f'
              AND confdeltype = 'c';
            """,
            connection);
        Assert.Equal(2L, await cascades.ExecuteScalarAsync());
    }

    private static async Task AssertBaseRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM users), (SELECT count(*) FROM file_entries)",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(2, reader.GetInt64(1));
    }
}
