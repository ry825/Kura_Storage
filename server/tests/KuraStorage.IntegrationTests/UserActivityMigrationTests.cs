using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class UserActivityMigrationTests
{
    private const string PreviousMigration = "20260901083940_AddTextFileVersions";

    [Fact]
    public async Task Migration_RoundTripsActivitySchemaConstraintsIndexesAndExistingRows()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("user_activity_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync(PreviousMigration);
        var seeded = await SeedBaseRowsAsync(postgres.GetConnectionString());

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expected: true);
        await AssertConstraintsAndSetNullAsync(postgres.GetConnectionString(), seeded);

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertSchemaAsync(postgres.GetConnectionString(), expected: false);
        await AssertBaseRowsAsync(postgres.GetConnectionString(), seeded.AuditId, seeded.ShareId);

        await database.Database.MigrateAsync();
        await AssertSchemaAsync(postgres.GetConnectionString(), expected: true);
        await AssertBaseRowsAsync(postgres.GetConnectionString(), seeded.AuditId, seeded.ShareId);
    }

    private static async Task<SeededRows> SeedBaseRowsAsync(string connectionString)
    {
        var ownerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var sharedOwnerId = Guid.NewGuid();
        var shareMemberId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var sharedRootId = Guid.NewGuid();
        var sharedFileId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            VALUES
                (@owner, 'ACTIVITYOWNER', 'Activity Owner', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                (@actor, 'ACTIVITYACTOR', 'Activity Actor', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                (@recipient, 'ACTIVITYRECIPIENT', 'Activity Recipient', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                (@shared_owner, 'ACTIVITYSHAREDOWNER', 'Shared Owner', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                (@share_member, 'ACTIVITYSHAREMEMBER', 'Share Member', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
                 size, status, file_version, created_at, updated_at)
            VALUES
                (@root, @owner, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE', 1, now(), now()),
                (@file, @owner, @root, 'FILE', 'note.txt', @file_path, 'text/plain', 1, 'ACTIVE', 1, now(), now()),
                (@shared_root, @shared_owner, NULL, 'FOLDER', 'Files', @shared_root_path, NULL, 0, 'ACTIVE', 1, now(), now()),
                (@shared_file, @shared_owner, @shared_root, 'FILE', 'shared.txt', @shared_path, 'text/plain', 1, 'ACTIVE', 1, now(), now());
            INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
            VALUES (@share, @shared_file, @shared_owner, now(), now());
            INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at)
            VALUES (@share, @share_member, 'VIEWER', now(), now());
            INSERT INTO audit_logs
                (id, actor_user_id, actor_type, action, target_type, target_id, result_code, created_at)
            VALUES (@audit, @actor, 'SYSTEM', 'MIGRATION_SENTINEL', 'FILE_ENTRY', @file_text, 'SUCCESS', now());
            """,
            connection);
        command.Parameters.AddWithValue("owner", ownerId);
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("recipient", recipientId);
        command.Parameters.AddWithValue("shared_owner", sharedOwnerId);
        command.Parameters.AddWithValue("share_member", shareMemberId);
        command.Parameters.AddWithValue("root", rootId);
        command.Parameters.AddWithValue("file", fileId);
        command.Parameters.AddWithValue("shared_root", sharedRootId);
        command.Parameters.AddWithValue("shared_file", sharedFileId);
        command.Parameters.AddWithValue("share", shareId);
        command.Parameters.AddWithValue("audit", auditId);
        command.Parameters.AddWithValue("root_path", $"users/{ownerId:N}/files");
        command.Parameters.AddWithValue("file_path", $"users/{ownerId:N}/files/note.txt");
        command.Parameters.AddWithValue("shared_root_path", $"users/{sharedOwnerId:N}/files");
        command.Parameters.AddWithValue("shared_path", $"users/{sharedOwnerId:N}/files/shared.txt");
        command.Parameters.AddWithValue("file_text", fileId.ToString());
        await command.ExecuteNonQueryAsync();
        return new SeededRows(ownerId, actorId, recipientId, rootId, fileId, auditId, shareId);
    }

    private static async Task AssertSchemaAsync(string connectionString, bool expected)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var table = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'user_activities'",
            connection);
        Assert.Equal(expected ? 1L : 0L, await table.ExecuteScalarAsync());
        await using var journalActor = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'file_operations' AND column_name = 'actor_user_id';
            """,
            connection);
        Assert.Equal(expected ? 1L : 0L, await journalActor.ExecuteScalarAsync());
        if (!expected)
        {
            return;
        }

        await using var indexes = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN (
                'ux_user_activities_operation_id',
                'ix_user_activities_actor_occurred_id',
                'ix_user_activities_owner_occurred_id',
                'ix_user_activities_target_occurred_id',
                'ix_user_activities_type_occurred_id');
            """,
            connection);
        Assert.Equal(5L, await indexes.ExecuteScalarAsync());
    }

    private static async Task AssertConstraintsAndSetNullAsync(string connectionString, SeededRows seeded)
    {
        var activityId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO user_activities
                (id, operation_id, activity_type, occurred_at, actor_user_id, actor_display_name,
                 actor_device_name, target_entry_id, target_type, target_name, owner_user_id,
                 owner_display_name, parent_entry_id, detail_kind, recipient_user_id,
                 recipient_display_name, share_permission, share_action)
            VALUES
                (@id, @operation, 'SHARE', now(), @actor, 'Activity Actor', 'Phone', @file,
                 'FILE', 'note.txt', @owner, 'Activity Owner', @root, 'SHARE', @recipient,
                 'Activity Recipient', 'EDITOR', 'CREATED');
            """,
            connection))
        {
            insert.Parameters.AddWithValue("id", activityId);
            insert.Parameters.AddWithValue("operation", operationId);
            insert.Parameters.AddWithValue("actor", seeded.ActorId);
            insert.Parameters.AddWithValue("file", seeded.FileId);
            insert.Parameters.AddWithValue("owner", seeded.OwnerId);
            insert.Parameters.AddWithValue("root", seeded.RootId);
            insert.Parameters.AddWithValue("recipient", seeded.RecipientId);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var duplicate = new NpgsqlCommand(
            """
            INSERT INTO user_activities
                (id, operation_id, activity_type, occurred_at, actor_display_name, target_type,
                 target_name, owner_display_name, detail_kind, resulting_file_version)
            VALUES (@id, @operation, 'UPLOAD', now(), 'Actor', 'FILE', 'other.txt', 'Owner', 'UPLOAD', 1);
            """,
            connection))
        {
            duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicate.Parameters.AddWithValue("operation", operationId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        }

        await using (var invalidDetail = new NpgsqlCommand(
            """
            INSERT INTO user_activities
                (id, operation_id, activity_type, occurred_at, actor_display_name, target_type,
                 target_name, owner_display_name, detail_kind, delete_kind)
            VALUES (@id, @operation, 'UPLOAD', now(), 'Actor', 'FILE', 'bad.txt', 'Owner', 'UPLOAD', 'PURGED');
            """,
            connection))
        {
            invalidDetail.Parameters.AddWithValue("id", Guid.NewGuid());
            invalidDetail.Parameters.AddWithValue("operation", Guid.NewGuid());
            var error = await Assert.ThrowsAsync<PostgresException>(() => invalidDetail.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }

        await using (var deleteFile = new NpgsqlCommand("DELETE FROM file_entries WHERE id IN (@file, @root)", connection))
        {
            deleteFile.Parameters.AddWithValue("file", seeded.FileId);
            deleteFile.Parameters.AddWithValue("root", seeded.RootId);
            await deleteFile.ExecuteNonQueryAsync();
        }

        await using (var deleteUsers = new NpgsqlCommand(
            "DELETE FROM users WHERE id IN (@owner, @actor, @recipient)", connection))
        {
            deleteUsers.Parameters.AddWithValue("owner", seeded.OwnerId);
            deleteUsers.Parameters.AddWithValue("actor", seeded.ActorId);
            deleteUsers.Parameters.AddWithValue("recipient", seeded.RecipientId);
            await deleteUsers.ExecuteNonQueryAsync();
        }

        await using var preserved = new NpgsqlCommand(
            """
            SELECT actor_user_id IS NULL AND target_entry_id IS NULL AND owner_user_id IS NULL
                AND parent_entry_id IS NULL AND recipient_user_id IS NULL
                AND actor_display_name = 'Activity Actor' AND target_name = 'note.txt'
                AND owner_display_name = 'Activity Owner' AND recipient_display_name = 'Activity Recipient'
            FROM user_activities WHERE id = @id;
            """,
            connection);
        preserved.Parameters.AddWithValue("id", activityId);
        Assert.True((bool)(await preserved.ExecuteScalarAsync())!);
    }

    private static async Task AssertBaseRowsAsync(string connectionString, Guid auditId, Guid shareId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM audit_logs WHERE id = @audit) + (SELECT count(*) FROM shares WHERE id = @share)",
            connection);
        command.Parameters.AddWithValue("audit", auditId);
        command.Parameters.AddWithValue("share", shareId);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    private sealed record SeededRows(
        Guid OwnerId,
        Guid ActorId,
        Guid RecipientId,
        Guid RootId,
        Guid FileId,
        Guid AuditId,
        Guid ShareId);
}
