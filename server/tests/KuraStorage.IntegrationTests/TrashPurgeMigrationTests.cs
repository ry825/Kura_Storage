using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class TrashPurgeMigrationTests
{
    [Fact]
    public async Task Migration_UpBackfillsActorAndCreatesIndexes_ThenRollsBack()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("purge_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync("20260723131233_AddFileOperations");
        var id = Guid.NewGuid();
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO audit_logs (id, actor_user_id, actor_device_id, action, result_code, created_at) VALUES ({0}, {1}, {2}, 'LOGIN', 'SUCCESS', now())",
            id,
            Guid.NewGuid(),
            Guid.NewGuid());

        await database.Database.MigrateAsync();

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var actor = new NpgsqlCommand("SELECT actor_type FROM audit_logs WHERE id = @id", connection);
            actor.Parameters.AddWithValue("id", id);
            Assert.Equal("USER_DEVICE", await actor.ExecuteScalarAsync());
            await using var indexes = new NpgsqlCommand(
                "SELECT count(*) FROM pg_indexes WHERE indexname IN ('ix_file_entries_trash_purge_candidates', 'ux_file_operations_incomplete_purge_target', 'ux_audit_logs_purge_success')",
                connection);
            Assert.Equal(3L, await indexes.ExecuteScalarAsync());
            await using var runTable = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'trash_purge_runs'",
                connection);
            Assert.Equal(1L, await runTable.ExecuteScalarAsync());
            await using var uploadTable = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'upload_sessions'",
                connection);
            Assert.Equal(1L, await uploadTable.ExecuteScalarAsync());
            await using var uploadIndexes = new NpgsqlCommand(
                "SELECT count(*) FROM pg_indexes WHERE indexname IN ('ux_upload_sessions_actor_idempotency_key', 'ix_upload_sessions_cleanup_candidates', 'ix_upload_sessions_device_status')",
                connection);
            Assert.Equal(3L, await uploadIndexes.ExecuteScalarAsync());
            await using var invalidRun = new NpgsqlCommand(
                "INSERT INTO trash_purge_runs (id, started_at, completed_at, status, examined_root_count, deleted_root_count, released_bytes, error_count) VALUES (@id, now(), NULL, 'COMPLETED', 0, 0, 0, 0)",
                connection);
            invalidRun.Parameters.AddWithValue("id", Guid.NewGuid());
            var constraint = await Assert.ThrowsAsync<PostgresException>(() => invalidRun.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, constraint.SqlState);

            var owner = Guid.NewGuid();
            var target = Guid.NewGuid();
            await using (var seed = new NpgsqlCommand(
                "INSERT INTO users (id, username_normalized, display_name, password_hash, role, status, failed_login_count, lock_type, created_at, updated_at) VALUES (@owner, 'PURGE', 'Purge', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now())",
                connection))
            {
                seed.Parameters.AddWithValue("owner", owner);
                await seed.ExecuteNonQueryAsync();
            }
            await using (var first = new NpgsqlCommand(
                "INSERT INTO file_operations (id, owner_user_id, operation_type, file_entry_id, source_relative_path, status, created_at, updated_at) VALUES (@id, @owner, 'PURGE', @target, 'users/x/trash/y', 'PENDING', now(), now())",
                connection))
            {
                first.Parameters.AddWithValue("id", Guid.NewGuid());
                first.Parameters.AddWithValue("owner", owner);
                first.Parameters.AddWithValue("target", target);
                await first.ExecuteNonQueryAsync();
            }
            await using (var duplicate = new NpgsqlCommand(
                "INSERT INTO file_operations (id, owner_user_id, operation_type, file_entry_id, source_relative_path, status, created_at, updated_at) VALUES (@id, @owner, 'PURGE', @target, 'users/x/trash/y', 'FILESYSTEM_DONE', now(), now())",
                connection))
            {
                duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
                duplicate.Parameters.AddWithValue("owner", owner);
                duplicate.Parameters.AddWithValue("target", target);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            }
        }

        await database.Database.MigrateAsync("20260820125242_AddTrashPurgeRuns");
        await using (var uploadRolledBack = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await uploadRolledBack.OpenAsync();
            await using var uploadTable = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'upload_sessions'",
                uploadRolledBack);
            Assert.Equal(0L, await uploadTable.ExecuteScalarAsync());
        }

        await database.Database.MigrateAsync("20260820114500_AddTrashPurgeFoundation");
        await using (var runRolledBack = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await runRolledBack.OpenAsync();
            await using var runTable = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'trash_purge_runs'",
                runRolledBack);
            Assert.Equal(0L, await runTable.ExecuteScalarAsync());
        }

        await database.Database.MigrateAsync("20260723131233_AddFileOperations");
        await using var rolledBack = new NpgsqlConnection(postgres.GetConnectionString());
        await rolledBack.OpenAsync();
        await using var column = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'audit_logs' AND column_name = 'actor_type'",
            rolledBack);
        Assert.Equal(0L, await column.ExecuteScalarAsync());
    }
}
