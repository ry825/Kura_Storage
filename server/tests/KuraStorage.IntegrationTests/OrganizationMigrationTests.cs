using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class OrganizationMigrationTests
{
    private const string PreviousMigration = "20260825110444_AddRecentFiles";

    [Fact]
    public async Task Migration_RoundTripsSchemaIndexesConstraintsAndCascades()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("organization_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync(PreviousMigration);
        await SeedBaseRowsAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), 3);
        await AssertConstraintsAndCascadesAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertSchemaAsync(postgres.GetConnectionString(), 0);
        await AssertBaseRowsAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), 3);
        await AssertBaseRowsAsync(postgres.GetConnectionString());
    }

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid EntryId = Guid.NewGuid();

    private static async Task SeedBaseRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            VALUES (@user, 'ORGANIZATIONMIGRATION', 'Organization Migration', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            VALUES
                (@root, @user, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE', 1, now(), now()),
                (@entry, @user, @root, 'FILE', 'Entry.txt', @entry_path, 'text/plain', 1, 'ACTIVE', 1, now(), now());
            """,
            connection);
        command.Parameters.AddWithValue("user", UserId);
        command.Parameters.AddWithValue("root", RootId);
        command.Parameters.AddWithValue("entry", EntryId);
        command.Parameters.AddWithValue("root_path", $"users/{UserId:N}/files");
        command.Parameters.AddWithValue("entry_path", $"users/{UserId:N}/files/Entry.txt");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSchemaAsync(string connectionString, long expectedTableCount)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tables = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.tables
            WHERE table_name IN ('favorite_entries', 'tags', 'entry_tags');
            """,
            connection);
        Assert.Equal(expectedTableCount, await tables.ExecuteScalarAsync());
        if (expectedTableCount == 0)
        {
            return;
        }

        await using var indexes = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN (
                'ix_favorite_entries_user_favorited_at_entry_id',
                'ix_favorite_entries_entry_id',
                'ux_tags_user_name_key',
                'ix_tags_user_name_key_id',
                'ix_entry_tags_entry_id_tag_id');
            """,
            connection);
        Assert.Equal(5L, await indexes.ExecuteScalarAsync());
        await using var cascadeCount = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid IN ('favorite_entries'::regclass, 'tags'::regclass, 'entry_tags'::regclass)
              AND contype = 'f' AND confdeltype = 'c';
            """,
            connection);
        Assert.Equal(5L, await cascadeCount.ExecuteScalarAsync());
    }

    private static async Task AssertConstraintsAndCascadesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var tagId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO favorite_entries (user_id, entry_id, favorited_at) VALUES (@user, @entry, now());
            INSERT INTO tags (id, user_id, name, name_key, created_at, updated_at)
            VALUES (@tag, @user, 'Work', 'WORK', now(), now());
            INSERT INTO entry_tags (tag_id, entry_id, attached_at) VALUES (@tag, @entry, now());
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("user", UserId);
            insert.Parameters.AddWithValue("entry", EntryId);
            insert.Parameters.AddWithValue("tag", tagId);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var duplicate = new NpgsqlCommand(
            "INSERT INTO tags (id, user_id, name, name_key, created_at, updated_at) VALUES (@id, @user, 'work', 'WORK', now(), now())",
            connection,
            transaction))
        {
            duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicate.Parameters.AddWithValue("user", UserId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        }

        await transaction.RollbackAsync();

        await using var cascadeTransaction = await connection.BeginTransactionAsync();
        await using (var seed = new NpgsqlCommand(
            """
            INSERT INTO favorite_entries (user_id, entry_id, favorited_at) VALUES (@user, @entry, now());
            INSERT INTO tags (id, user_id, name, name_key, created_at, updated_at)
            VALUES (@tag, @user, 'Work', 'WORK', now(), now());
            INSERT INTO entry_tags (tag_id, entry_id, attached_at) VALUES (@tag, @entry, now());
            DELETE FROM file_entries WHERE id = @entry;
            """,
            connection,
            cascadeTransaction))
        {
            seed.Parameters.AddWithValue("user", UserId);
            seed.Parameters.AddWithValue("entry", EntryId);
            seed.Parameters.AddWithValue("tag", tagId);
            await seed.ExecuteNonQueryAsync();
        }

        await using (var counts = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM favorite_entries), (SELECT count(*) FROM entry_tags), (SELECT count(*) FROM tags)",
            connection,
            cascadeTransaction))
        await using (var reader = await counts.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt64(0));
            Assert.Equal(0, reader.GetInt64(1));
            Assert.Equal(1, reader.GetInt64(2));
        }

        await cascadeTransaction.RollbackAsync();
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
