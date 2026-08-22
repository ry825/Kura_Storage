using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class ExternalIndexMigrationTests
{
    [Fact]
    public async Task Migration_UpAddsMissingConstraintsAndScanStaging_ThenRollsBack()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("external_index_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync("20260822005905_AddUploadSessions");
        var ownerId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var trashedId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand(
                "INSERT INTO users (id, username_normalized, display_name, password_hash, role, status, failed_login_count, lock_type, created_at, updated_at) VALUES (@owner, 'INDEX', 'Index', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()); INSERT INTO file_entries (id, owner_user_id, parent_id, entry_type, name, relative_path, size, status, trashed_at, file_version, created_at, updated_at) VALUES (@root, @owner, NULL, 'FOLDER', 'Files', @root_path, 0, 'ACTIVE', NULL, 1, now(), now()), (@file, @owner, @root, 'FILE', 'item.txt', @file_path, 1, 'ACTIVE', NULL, 1, now(), now()), (@trashed, @owner, NULL, 'FILE', 'old.txt', @trash_path, 2, 'TRASHED', now(), 1, now(), now())",
                connection);
            seed.Parameters.AddWithValue("owner", ownerId);
            seed.Parameters.AddWithValue("root", rootId);
            seed.Parameters.AddWithValue("file", fileId);
            seed.Parameters.AddWithValue("trashed", trashedId);
            seed.Parameters.AddWithValue("root_path", $"users/{ownerId:N}/files");
            seed.Parameters.AddWithValue("file_path", $"users/{ownerId:N}/files/item.txt");
            seed.Parameters.AddWithValue("trash_path", $"users/{ownerId:N}/trash/{trashedId:N}/old.txt");
            await seed.ExecuteNonQueryAsync();
        }

        await database.Database.MigrateAsync();

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var tables = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('index_scan_runs', 'index_scan_items')",
                connection);
            Assert.Equal(2L, await tables.ExecuteScalarAsync());
            await using var indexes = new NpgsqlCommand(
                "SELECT count(*) FROM pg_indexes WHERE indexname IN ('ux_file_entries_managed_owner_path', 'ux_file_entries_managed_owner_parent_name', 'ix_file_entries_missing_status_checked_at')",
                connection);
            Assert.Equal(3L, await indexes.ExecuteScalarAsync());
            await using var preservedStatuses = new NpgsqlCommand(
                "SELECT count(*) FROM file_entries WHERE (id = @file AND status = 'ACTIVE') OR (id = @trashed AND status = 'TRASHED')",
                connection);
            preservedStatuses.Parameters.AddWithValue("file", fileId);
            preservedStatuses.Parameters.AddWithValue("trashed", trashedId);
            Assert.Equal(2L, await preservedStatuses.ExecuteScalarAsync());

            await using var invalidMissing = new NpgsqlCommand(
                "UPDATE file_entries SET status = 'MISSING' WHERE id = @file",
                connection);
            invalidMissing.Parameters.AddWithValue("file", fileId);
            var missingConstraint = await Assert.ThrowsAsync<PostgresException>(() => invalidMissing.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, missingConstraint.SqlState);

            await using var validCandidate = new NpgsqlCommand(
                "UPDATE file_entries SET status = 'MISSING_CANDIDATE', missing_detected_at = now(), missing_last_checked_at = now(), missing_observation_id = @observation WHERE id = @file",
                connection);
            validCandidate.Parameters.AddWithValue("observation", Guid.NewGuid());
            validCandidate.Parameters.AddWithValue("file", fileId);
            await validCandidate.ExecuteNonQueryAsync();

            await using var duplicate = new NpgsqlCommand(
                "INSERT INTO file_entries (id, owner_user_id, parent_id, entry_type, name, relative_path, size, status, file_version, created_at, updated_at) VALUES (@id, @owner, @root, 'FILE', 'other.txt', @path, 1, 'ACTIVE', 1, now(), now())",
                connection);
            duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicate.Parameters.AddWithValue("owner", ownerId);
            duplicate.Parameters.AddWithValue("root", rootId);
            duplicate.Parameters.AddWithValue("path", $"users/{ownerId:N}/files/item.txt");
            var uniqueness = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, uniqueness.SqlState);

            await using var duplicateName = new NpgsqlCommand(
                "INSERT INTO file_entries (id, owner_user_id, parent_id, entry_type, name, relative_path, size, status, file_version, created_at, updated_at) VALUES (@id, @owner, @root, 'FILE', 'item.txt', @path, 1, 'ACTIVE', 1, now(), now())",
                connection);
            duplicateName.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicateName.Parameters.AddWithValue("owner", ownerId);
            duplicateName.Parameters.AddWithValue("root", rootId);
            duplicateName.Parameters.AddWithValue("path", $"users/{ownerId:N}/files/different-path.txt");
            var nameUniqueness = await Assert.ThrowsAsync<PostgresException>(() => duplicateName.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, nameUniqueness.SqlState);

            await using var invalidRun = new NpgsqlCommand(
                "INSERT INTO index_scan_runs (id, trigger, mode, status, started_at, enumerated_count, added_count, updated_count, moved_count, candidate_count, missing_count, revived_count, isolated_count, error_count) VALUES (@id, 'ADMIN', 'APPLY', 'COMPLETED', now(), 0, 0, 0, 0, 0, 0, 0, 0, 0)",
                connection);
            invalidRun.Parameters.AddWithValue("id", Guid.NewGuid());
            var runConstraint = await Assert.ThrowsAsync<PostgresException>(() => invalidRun.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, runConstraint.SqlState);
        }

        var rollbackBlocked = await Assert.ThrowsAsync<PostgresException>(() =>
            database.Database.MigrateAsync("20260822005905_AddUploadSessions"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, rollbackBlocked.SqlState);
        await database.Database.ExecuteSqlRawAsync(
            "UPDATE file_entries SET status = 'ACTIVE', missing_detected_at = NULL, missing_last_checked_at = NULL, missing_observation_id = NULL WHERE id = {0}",
            fileId);
        await database.Database.MigrateAsync("20260822005905_AddUploadSessions");
        await using var rolledBack = new NpgsqlConnection(postgres.GetConnectionString());
        await rolledBack.OpenAsync();
        await using var removed = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('index_scan_runs', 'index_scan_items')",
            rolledBack);
        Assert.Equal(0L, await removed.ExecuteScalarAsync());
    }
}
