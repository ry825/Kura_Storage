using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class PersistentLowMigrationTests
{
    private const string PreviousMigration = "20260904072152_AddMediaCleanupRuns";

    [Fact]
    public async Task Migration_PreservesReadyLowIdentityAndMakesItNonExpiring()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("persistent_low_migration")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var user = new User(Guid.NewGuid(), $"LOW{Guid.NewGuid():N}".ToUpperInvariant(), "low-user", "hash", UserRole.Member, now);
        var root = FileEntry.CreateRoot(user.Id, now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), user.Id, root.Id, FileName.Create("photo.jpg"),
            RelativeStoragePath.Create($"users/{user.Id:N}/files/photo.jpg"), "image/jpeg", 100, now);
        var derivativeId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        const string path = "derivatives/preserved-low.webp";
        await using (var database = new KuraStorageDbContext(options))
        {
            await database.Database.MigrateAsync(PreviousMigration);
            database.AddRange(user, root, file);
            await database.SaveChangesAsync();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO file_derivatives
                    (id, source_file_id, source_version, derivative_type, profile_version, relative_path,
                     size, status, last_accessed_at, expires_at, revision, created_at, updated_at)
                VALUES ({derivativeId}, {file.Id}, 1, 'IMAGE_LOW', 1, {path}, 25, 'READY',
                        {now}, {now.AddDays(1)}, 3, {now}, {now});
                INSERT INTO media_jobs
                    (id, derivative_id, job_type, status, requested_by_user_id, attempt_count,
                     available_at, completed_at, created_at, updated_at)
                VALUES ({jobId}, {derivativeId}, 'IMAGE_LOW', 'COMPLETED', {user.Id}, 1,
                        {now}, {now}, {now}, {now});
                """);
            await database.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT source_file_id, source_version, derivative_type, profile_version,
                   relative_path, size, status, expires_at, last_accessed_at
            FROM file_derivatives
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", derivativeId);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(file.Id, row.GetGuid(0));
        Assert.Equal(1, row.GetInt64(1));
        Assert.Equal("IMAGE_LOW", row.GetString(2));
        Assert.Equal(1, row.GetInt32(3));
        Assert.Equal(path, row.GetString(4));
        Assert.Equal(25, row.GetInt64(5));
        Assert.Equal("READY", row.GetString(6));
        Assert.True(row.IsDBNull(7));
        Assert.True(row.IsDBNull(8));
        await row.CloseAsync();

        await using var job = new NpgsqlCommand("SELECT origin, priority FROM media_jobs WHERE id = @id", connection);
        job.Parameters.AddWithValue("id", jobId);
        await using var jobRow = await job.ExecuteReaderAsync();
        Assert.True(await jobRow.ReadAsync());
        Assert.Equal("INTERACTIVE_REPAIR", jobRow.GetString(0));
        Assert.Equal(0, jobRow.GetInt32(1));
    }

    [Fact]
    public async Task Migration_AppliesToEmptyDatabaseWithPersistentLowAndPriorityConstraints()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("persistent_low_empty")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;

        await using (var database = new KuraStorageDbContext(options))
        {
            await database.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE constraint_schema = 'public'
              AND constraint_name IN (
                  'ck_file_derivatives_persistent_expiry',
                  'ck_media_jobs_origin',
                  'ck_media_jobs_priority')
            ORDER BY constraint_name
            """,
            connection);
        await using var rows = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await rows.ReadAsync()) names.Add(rows.GetString(0));

        Assert.Equal(
            [
                "ck_file_derivatives_persistent_expiry",
                "ck_media_jobs_origin",
                "ck_media_jobs_priority",
            ],
            names);
    }
}
