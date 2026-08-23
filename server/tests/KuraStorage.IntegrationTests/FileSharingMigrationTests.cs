using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class FileSharingMigrationTests
{
    [Fact]
    public async Task Migration_BackfillsUploadsEnforcesSharingSchemaAndBlocksLossyRollback()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("file_sharing_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync("20260822082843_AddExternalIndexReconciliation");

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand(
                """
                INSERT INTO users
                    (id, username_normalized, display_name, password_hash, role, status,
                     failed_login_count, lock_type, created_at, updated_at)
                VALUES
                    (@owner, 'SHAREOWNER', 'Share Owner', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                    (@member, 'SHAREMEMBER', 'Share Member', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
                INSERT INTO devices
                    (id, user_id, device_name, platform, status, registered_at)
                VALUES (@device, @owner, 'Migration Device', 'ANDROID', 'ACTIVE', now());
                INSERT INTO file_entries
                    (id, owner_user_id, parent_id, entry_type, name, relative_path, size, status,
                     file_version, created_at, updated_at)
                VALUES
                    (@root, @owner, NULL, 'FOLDER', 'Files', @root_path, 0, 'ACTIVE', 1, now(), now()),
                    (@file, @owner, @root, 'FILE', 'shared.txt', @file_path, 1, 'ACTIVE', 1, now(), now());
                INSERT INTO upload_sessions
                    (id, owner_user_id, device_id, destination_folder_id, file_entry_id,
                     idempotency_key, file_name, expected_size, received_bytes,
                     temporary_relative_path, status, created_at, updated_at, expires_at,
                     absolute_expires_at)
                VALUES
                    (@session, @owner, @device, @root, @upload_file, @key, 'pending.bin', 1, 0,
                     @temp_path, 'ACTIVE', now(), now(), now() + interval '1 hour',
                     now() + interval '1 day');
                """,
                connection);
            seed.Parameters.AddWithValue("owner", ownerId);
            seed.Parameters.AddWithValue("member", memberId);
            seed.Parameters.AddWithValue("device", deviceId);
            seed.Parameters.AddWithValue("root", rootId);
            seed.Parameters.AddWithValue("file", fileId);
            seed.Parameters.AddWithValue("upload_file", Guid.NewGuid());
            seed.Parameters.AddWithValue("session", sessionId);
            seed.Parameters.AddWithValue("key", Guid.NewGuid().ToString());
            seed.Parameters.AddWithValue("root_path", $"users/{ownerId:N}/files");
            seed.Parameters.AddWithValue("file_path", $"users/{ownerId:N}/files/shared.txt");
            seed.Parameters.AddWithValue("temp_path", $"upload-sessions/{ownerId:N}/{sessionId:N}.upload");
            await seed.ExecuteNonQueryAsync();
        }

        await database.Database.MigrateAsync();

        var shareId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using (var backfill = new NpgsqlCommand(
                "SELECT actor_user_id = target_owner_user_id AND actor_user_id = @owner FROM upload_sessions WHERE id = @session",
                connection))
            {
                backfill.Parameters.AddWithValue("owner", ownerId);
                backfill.Parameters.AddWithValue("session", sessionId);
                Assert.True((bool)(await backfill.ExecuteScalarAsync())!);
            }

            await using (var indexes = new NpgsqlCommand(
                "SELECT count(*) FROM pg_indexes WHERE indexname IN ('ux_shares_target_entry_id', 'ix_shares_owner_updated_id', 'ix_share_members_user_share', 'ux_upload_sessions_actor_idempotency_key', 'ix_upload_sessions_actor_status')",
                connection))
            {
                Assert.Equal(5L, await indexes.ExecuteScalarAsync());
            }

            await using (var addShare = new NpgsqlCommand(
                "INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at) VALUES (@share, @file, @owner, now(), now()); INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at) VALUES (@share, @member, 'VIEWER', now(), now())",
                connection))
            {
                addShare.Parameters.AddWithValue("share", shareId);
                addShare.Parameters.AddWithValue("file", fileId);
                addShare.Parameters.AddWithValue("owner", ownerId);
                addShare.Parameters.AddWithValue("member", memberId);
                await addShare.ExecuteNonQueryAsync();
            }

            await using (var duplicate = new NpgsqlCommand(
                "INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at) VALUES (@share, @file, @owner, now(), now())",
                connection))
            {
                duplicate.Parameters.AddWithValue("share", Guid.NewGuid());
                duplicate.Parameters.AddWithValue("file", fileId);
                duplicate.Parameters.AddWithValue("owner", ownerId);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            }

            await using (var invalidPermission = new NpgsqlCommand(
                "UPDATE share_members SET permission = 'OWNER' WHERE share_id = @share AND user_id = @member",
                connection))
            {
                invalidPermission.Parameters.AddWithValue("share", shareId);
                invalidPermission.Parameters.AddWithValue("member", memberId);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidPermission.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            }

            await using (var duplicateMember = new NpgsqlCommand(
                "INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at) VALUES (@share, @member, 'EDITOR', now(), now())",
                connection))
            {
                duplicateMember.Parameters.AddWithValue("share", shareId);
                duplicateMember.Parameters.AddWithValue("member", memberId);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicateMember.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            }

            await using (var deleteMember = new NpgsqlCommand("DELETE FROM users WHERE id = @member", connection))
            {
                deleteMember.Parameters.AddWithValue("member", memberId);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => deleteMember.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            }
        }

        var shareRollbackBlocked = await Assert.ThrowsAsync<PostgresException>(() =>
            database.Database.MigrateAsync("20260822082843_AddExternalIndexReconciliation"));
        Assert.Equal(PostgresErrorCodes.RaiseException, shareRollbackBlocked.SqlState);

        await database.Database.ExecuteSqlRawAsync("DELETE FROM file_entries WHERE id = {0}", fileId);
        Assert.Equal(0, await database.Shares.CountAsync());
        Assert.Equal(0, await database.ShareMembers.CountAsync());

        await database.Database.ExecuteSqlRawAsync(
            "UPDATE upload_sessions SET target_owner_user_id = {0} WHERE id = {1}",
            memberId,
            sessionId);
        var uploadRollbackBlocked = await Assert.ThrowsAsync<PostgresException>(() =>
            database.Database.MigrateAsync("20260822082843_AddExternalIndexReconciliation"));
        Assert.Equal(PostgresErrorCodes.RaiseException, uploadRollbackBlocked.SqlState);

        await database.Database.ExecuteSqlRawAsync(
            "UPDATE upload_sessions SET target_owner_user_id = actor_user_id WHERE id = {0}",
            sessionId);
        await database.Database.MigrateAsync("20260822082843_AddExternalIndexReconciliation");
        await using var rolledBack = new NpgsqlConnection(postgres.GetConnectionString());
        await rolledBack.OpenAsync();
        await using var columns = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'upload_sessions' AND column_name IN ('owner_user_id', 'actor_user_id', 'target_owner_user_id')",
            rolledBack);
        Assert.Equal(1L, await columns.ExecuteScalarAsync());
    }
}
