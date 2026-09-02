using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class BackupReceiptMigrationTests
{
    private const string PreviousMigration = "20260902045531_AddUserActivityQueryIndex";

    [Fact]
    public async Task Migration_RoundTripsReceiptAndUploadContextWithoutChangingExistingRows()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("backup_receipt_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync(PreviousMigration);
        var seeded = await SeedAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), true);
        await AssertReceiptConstraintsAsync(postgres.GetConnectionString(), seeded);

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertSchemaAsync(postgres.GetConnectionString(), false);
        await AssertExistingRowsAsync(postgres.GetConnectionString(), seeded);

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), true);
        await AssertExistingRowsAsync(postgres.GetConnectionString(), seeded);
    }

    private static async Task<SeededRows> SeedAsync(string connectionString)
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            VALUES (@user, 'BACKUPMIGRATION', 'Backup Migration', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
            INSERT INTO devices (id, user_id, device_name, platform, status, registered_at)
            VALUES (@device, @user, 'Migration Phone', 'ANDROID', 'ACTIVE', now());
            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            VALUES
                (@root, @user, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE', 1, now(), now()),
                (@file, @user, @root, 'FILE', 'photo.jpg', @file_path, 'image/jpeg', 1, 'ACTIVE', 1, now(), now());
            INSERT INTO upload_sessions
                (id, actor_user_id, target_owner_user_id, device_id, destination_folder_id, file_entry_id,
                 idempotency_key, file_name, expected_size, received_bytes, temporary_relative_path,
                 status, created_at, updated_at, expires_at, absolute_expires_at)
            VALUES
                (@upload, @user, @user, @device, @root, @pending_file, @key, 'pending.bin', 1, 0,
                 @temp_path, 'ACTIVE', now(), now(), now() + interval '1 hour', now() + interval '1 day');
            INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
            VALUES (@share, @file, @user, now(), now());
            INSERT INTO user_activities
                (id, operation_id, activity_type, occurred_at, actor_user_id, actor_display_name,
                 actor_device_name, target_entry_id, target_type, target_name, owner_user_id,
                 owner_display_name, parent_entry_id, detail_kind, resulting_file_version)
            VALUES
                (@activity, @activity_operation, 'UPLOAD', now(), @user, 'Backup Migration',
                 'Migration Phone', @file, 'FILE', 'photo.jpg', @user, 'Backup Migration',
                 @root, 'UPLOAD', 1);
            """,
            connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("device", deviceId);
        command.Parameters.AddWithValue("root", rootId);
        command.Parameters.AddWithValue("file", fileId);
        command.Parameters.AddWithValue("upload", uploadId);
        command.Parameters.AddWithValue("pending_file", Guid.NewGuid());
        command.Parameters.AddWithValue("key", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("temp_path", $"upload-sessions/{userId:N}/{uploadId:N}.upload");
        command.Parameters.AddWithValue("share", shareId);
        command.Parameters.AddWithValue("activity", activityId);
        command.Parameters.AddWithValue("activity_operation", Guid.NewGuid());
        command.Parameters.AddWithValue("root_path", $"users/{userId:N}/files");
        command.Parameters.AddWithValue("file_path", $"users/{userId:N}/files/photo.jpg");
        await command.ExecuteNonQueryAsync();
        return new SeededRows(userId, deviceId, rootId, fileId, uploadId, shareId, activityId);
    }

    private static async Task AssertSchemaAsync(string connectionString, bool expected)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tables = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'backup_receipts'",
            connection);
        Assert.Equal(expected ? 1L : 0L, await tables.ExecuteScalarAsync());
        await using var columns = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'upload_sessions' AND column_name LIKE 'backup_%'",
            connection);
        Assert.Equal(expected ? 7L : 0L, await columns.ExecuteScalarAsync());
    }

    private static async Task AssertReceiptConstraintsAsync(string connectionString, SeededRows seeded)
    {
        var key = Guid.NewGuid().ToString("D");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var sentinels = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) FROM file_entries WHERE id = @file) +
              (SELECT count(*) FROM upload_sessions WHERE id = @upload) +
              (SELECT count(*) FROM shares WHERE id = @share) +
              (SELECT count(*) FROM user_activities WHERE id = @activity);
            """,
            connection))
        {
            sentinels.Parameters.AddWithValue("file", seeded.FileId);
            sentinels.Parameters.AddWithValue("upload", seeded.UploadId);
            sentinels.Parameters.AddWithValue("share", seeded.ShareId);
            sentinels.Parameters.AddWithValue("activity", seeded.ActivityId);
            Assert.Equal(4L, await sentinels.ExecuteScalarAsync());
        }
        async Task InsertAsync(Guid id)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO backup_receipts
                    (id, user_id, device_id, local_document_key, remote_file_id, relative_path,
                     size, source_modified_at, checksum, remote_file_version, uploaded_at, created_at, updated_at)
                VALUES (@id, @user, @device, @key, @file, 'Photos/photo.jpg', 1, now(), NULL, 1, now(), now(), now());
                """,
                connection);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("user", seeded.UserId);
            insert.Parameters.AddWithValue("device", seeded.DeviceId);
            insert.Parameters.AddWithValue("key", key);
            insert.Parameters.AddWithValue("file", seeded.FileId);
            await insert.ExecuteNonQueryAsync();
        }

        await InsertAsync(Guid.NewGuid());
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);

        await using (var revoke = new NpgsqlCommand("UPDATE devices SET status = 'REVOKED' WHERE id = @device", connection))
        {
            revoke.Parameters.AddWithValue("device", seeded.DeviceId);
            await revoke.ExecuteNonQueryAsync();
        }
        await using (var preserved = new NpgsqlCommand("SELECT count(*) FROM backup_receipts WHERE device_id = @device", connection))
        {
            preserved.Parameters.AddWithValue("device", seeded.DeviceId);
            Assert.Equal(1L, await preserved.ExecuteScalarAsync());
        }
        await using (var purge = new NpgsqlCommand("DELETE FROM file_entries WHERE id = @file", connection))
        {
            purge.Parameters.AddWithValue("file", seeded.FileId);
            await purge.ExecuteNonQueryAsync();
        }
        await using var removed = new NpgsqlCommand("SELECT count(*) FROM backup_receipts", connection);
        Assert.Equal(0L, await removed.ExecuteScalarAsync());
    }

    private static async Task AssertExistingRowsAsync(string connectionString, SeededRows seeded)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM users WHERE id = @user) + (SELECT count(*) FROM devices WHERE id = @device)",
            connection);
        command.Parameters.AddWithValue("user", seeded.UserId);
        command.Parameters.AddWithValue("device", seeded.DeviceId);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    private sealed record SeededRows(
        Guid UserId,
        Guid DeviceId,
        Guid RootId,
        Guid FileId,
        Guid UploadId,
        Guid ShareId,
        Guid ActivityId);
}
