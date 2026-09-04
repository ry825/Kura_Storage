using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class MediaCleanupMigrationTests
{
    private const string PreviousMigration = "20260902093557_AddBackupReceipts";
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_UpDownAndReUp_PreservesExistingMediaAndEnforcesManualIdempotency()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("media_cleanup_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        Guid adminId;
        Guid derivativeId;
        await using (var database = new KuraStorageDbContext(options))
        {
            await database.Database.MigrateAsync(PreviousMigration);
            var admin = new User(
                Guid.NewGuid(), $"MIGRATION{Guid.NewGuid():N}".ToUpperInvariant(), "migration-admin",
                "integration-hash", UserRole.Admin, Now);
            var root = FileEntry.CreateRoot(admin.Id, Now);
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), admin.Id, root.Id, FileName.Create("preserved.bin"),
                RelativeStoragePath.Create($"users/{admin.Id:N}/files/preserved.bin"),
                "application/octet-stream", 100, Now);
            var derivative = new FileDerivative(Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.ImageLow, 1, Now);
            derivative.Start(Now);
            derivative.MarkReady(
                $"derivatives/{admin.Id:N}/{file.Id:N}/1/1/image-low-{derivative.Id:N}.bin",
                10,
                Now,
                Now.AddDays(1));
            adminId = admin.Id;
            derivativeId = derivative.Id;
            database.AddRange(admin, root, file, derivative);
            await database.SaveChangesAsync();

            await database.Database.MigrateAsync();
        }

        await AssertLatestSchemaAsync(postgres.GetConnectionString(), adminId, derivativeId, insertRun: true);

        await using (var rollback = new KuraStorageDbContext(options))
        {
            await rollback.Database.MigrateAsync(PreviousMigration);
        }

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var runTable = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'media_cleanup_runs'",
                connection);
            Assert.Equal(0L, await runTable.ExecuteScalarAsync());
            await using var derivative = new NpgsqlCommand(
                "SELECT count(*) FROM file_derivatives WHERE id = @id",
                connection);
            derivative.Parameters.AddWithValue("id", derivativeId);
            Assert.Equal(1L, await derivative.ExecuteScalarAsync());
        }

        await using (var reapply = new KuraStorageDbContext(options))
        {
            await reapply.Database.MigrateAsync();
        }

        await AssertLatestSchemaAsync(postgres.GetConnectionString(), adminId, derivativeId, insertRun: false);
    }

    private static async Task AssertLatestSchemaAsync(
        string connectionString,
        Guid adminId,
        Guid derivativeId,
        bool insertRun)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var table = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'media_cleanup_runs'",
            connection))
        {
            Assert.Equal(1L, await table.ExecuteScalarAsync());
        }

        await using (var indexes = new NpgsqlCommand(
            "SELECT count(*) FROM pg_indexes WHERE indexname IN ('ux_media_cleanup_runs_manual_idempotency', 'ux_media_cleanup_runs_active_scheduled', 'ix_media_cleanup_runs_claim', 'ix_media_cleanup_runs_latest')",
            connection))
        {
            Assert.Equal(4L, await indexes.ExecuteScalarAsync());
        }

        await using (var derivative = new NpgsqlCommand(
            "SELECT count(*) FROM file_derivatives WHERE id = @id",
            connection))
        {
            derivative.Parameters.AddWithValue("id", derivativeId);
            Assert.Equal(1L, await derivative.ExecuteScalarAsync());
        }

        if (!insertRun)
        {
            return;
        }

        const string insert =
            "INSERT INTO media_cleanup_runs (id, trigger, status, requested_by_admin_user_id, idempotency_key_hash, request_fingerprint_hash, requested_at, examined_count, deleted_count, released_bytes, failure_count) " +
            "VALUES (@id, 'MANUAL', 'PENDING', @admin, @key, @fingerprint, now(), 0, 0, 0, 0)";
        await using (var first = new NpgsqlCommand(insert, connection))
        {
            first.Parameters.AddWithValue("id", Guid.NewGuid());
            first.Parameters.AddWithValue("admin", adminId);
            first.Parameters.AddWithValue("key", new string('a', 64));
            first.Parameters.AddWithValue("fingerprint", new string('b', 64));
            await first.ExecuteNonQueryAsync();
        }

        await using var duplicate = new NpgsqlCommand(insert, connection);
        duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
        duplicate.Parameters.AddWithValue("admin", adminId);
        duplicate.Parameters.AddWithValue("key", new string('a', 64));
        duplicate.Parameters.AddWithValue("fingerprint", new string('b', 64));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }
}
