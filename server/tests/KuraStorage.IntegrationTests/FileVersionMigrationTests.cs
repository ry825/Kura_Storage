using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class FileVersionMigrationTests
{
    private const string PreviousMigration = "20260829054814_AddMediaDerivativeFoundation";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid OperationId = Guid.NewGuid();

    [Fact]
    public async Task Migration_RoundTripsVersionSchemaIndexesConstraintsAndExistingRows()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("file_version_migration")
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
        await AssertSchemaAsync(postgres.GetConnectionString(), expected: true);
        await AssertConstraintsAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertSchemaAsync(postgres.GetConnectionString(), expected: false);
        await AssertBaseRowsAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expected: true);
        await AssertBaseRowsAsync(postgres.GetConnectionString());
    }

    private static async Task SeedBaseRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            VALUES (@user, 'VERSIONMIGRATION', 'Version Migration', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
            INSERT INTO devices (id, user_id, device_name, platform, status, registered_at)
            VALUES (@device, @user, 'Version Device', 'ANDROID', 'ACTIVE', now());
            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            VALUES
                (@root, @user, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE', 1, now(), now()),
                (@file, @user, @root, 'FILE', 'note.txt', @file_path, 'text/plain', 1, 'ACTIVE', 1, now(), now());
            INSERT INTO file_operations
                (id, owner_user_id, operation_type, file_entry_id, status, created_at, updated_at)
            VALUES (@operation, @user, 'UPLOAD', @file, 'COMPLETED', now(), now());
            """,
            connection);
        command.Parameters.AddWithValue("user", UserId);
        command.Parameters.AddWithValue("device", DeviceId);
        command.Parameters.AddWithValue("root", RootId);
        command.Parameters.AddWithValue("file", FileId);
        command.Parameters.AddWithValue("operation", OperationId);
        command.Parameters.AddWithValue("root_path", $"users/{UserId:N}/files");
        command.Parameters.AddWithValue("file_path", $"users/{UserId:N}/files/note.txt");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSchemaAsync(string connectionString, bool expected)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var table = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'file_version_records'",
            connection);
        Assert.Equal(expected ? 1L : 0L, await table.ExecuteScalarAsync());
        await using var journalColumns = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'file_operations'
              AND column_name IN (
                'previous_file_version',
                'result_file_version',
                'version_temporary_relative_path',
                'version_content_relative_path',
                'version_sha256',
                'version_publish_stage');
            """,
            connection);
        Assert.Equal(expected ? 6L : 0L, await journalColumns.ExecuteScalarAsync());
        if (!expected)
        {
            return;
        }

        await using var indexes = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN (
                'ux_file_version_records_file_version',
                'ix_file_version_records_file_created_id',
                'ix_file_version_records_actor_user_id',
                'ix_file_version_records_actor_device_id')
              AND (indexname <> 'ux_file_version_records_file_version' OR indexdef LIKE '%version DESC%');
            """,
            connection);
        Assert.Equal(4L, await indexes.ExecuteScalarAsync());
    }

    private static async Task AssertConstraintsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var sha = new string('a', 64);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO file_version_records
                (id, file_entry_id, version, size, sha256, content_relative_path, change_kind,
                 actor_user_id, actor_device_id, created_at)
            VALUES (@id, @file, 1, 1, @sha, @path, 'UPLOAD', @user, @device, now());
            """,
            connection))
        {
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("file", FileId);
            insert.Parameters.AddWithValue("sha", sha);
            insert.Parameters.AddWithValue("path", $"versions/{UserId:N}/{FileId:N}/1/{sha}.bin");
            insert.Parameters.AddWithValue("user", UserId);
            insert.Parameters.AddWithValue("device", DeviceId);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var duplicate = new NpgsqlCommand(
            """
            INSERT INTO file_version_records
                (id, file_entry_id, version, size, sha256, content_relative_path, change_kind, created_at)
            VALUES (@id, @file, 1, 1, @sha, @path, 'UPLOAD', now());
            """,
            connection))
        {
            duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicate.Parameters.AddWithValue("file", FileId);
            duplicate.Parameters.AddWithValue("sha", sha);
            duplicate.Parameters.AddWithValue("path", $"versions/{UserId:N}/{FileId:N}/1/{sha}.bin");
            var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        }

        await using (var deleteFile = new NpgsqlCommand("DELETE FROM file_entries WHERE id = @file", connection))
        {
            deleteFile.Parameters.AddWithValue("file", FileId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => deleteFile.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        }
    }

    private static async Task AssertBaseRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM file_entries WHERE id IN (@root, @file)) +
                (SELECT count(*) FROM file_operations WHERE id = @operation)
            """, connection);
        command.Parameters.AddWithValue("root", RootId);
        command.Parameters.AddWithValue("file", FileId);
        command.Parameters.AddWithValue("operation", OperationId);
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }
}
