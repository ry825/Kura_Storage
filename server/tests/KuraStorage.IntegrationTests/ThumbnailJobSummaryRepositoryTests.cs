using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class ThumbnailJobSummaryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Summary_CountsLatestReadableActiveThumbnailJobsWithoutLeakingOtherFiles()
    {
        await using var postgres = CreatePostgres();
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();

        var actor = User("SUMMARYACTOR");
        var owner = User("SUMMARYOWNER");
        database.Users.AddRange(actor, owner);
        var actorRoot = FileEntry.CreateRoot(actor.Id, Now);
        var ownerRoot = FileEntry.CreateRoot(owner.Id, Now);
        var sharedFolder = Folder(owner, ownerRoot, "shared");
        var revokedFolder = Folder(owner, ownerRoot, "revoked");
        var inactiveFolder = Folder(owner, ownerRoot, "inactive");
        database.FileEntries.AddRange(actorRoot, ownerRoot, sharedFolder, revokedFolder, inactiveFolder);

        AddJob(database, File(actor, actorRoot, "queued.jpg"), DerivativeType.Thumbnail, JobState.Queued);
        AddJob(database, File(actor, actorRoot, "running.pdf"), DerivativeType.PdfThumbnail, JobState.Running);
        AddJob(database, File(actor, actorRoot, "failed.jpg"), DerivativeType.Thumbnail, JobState.Failed);
        AddJob(database, File(actor, actorRoot, "completed.jpg"), DerivativeType.Thumbnail, JobState.Completed);
        AddJob(database, File(actor, actorRoot, "video.mp4"), DerivativeType.VideoLow, JobState.Queued);

        var retriedFile = File(actor, actorRoot, "retried.jpg");
        var retriedDerivative = AddJob(
            database, retriedFile, DerivativeType.Thumbnail, JobState.Failed);
        database.MediaJobs.Add(new MediaJob(
            Guid.NewGuid(), retriedDerivative.Id, DerivativeType.Thumbnail, actor.Id, Now.AddMinutes(1)));

        var sharedFile = File(owner, sharedFolder, "shared.jpg");
        AddJob(database, sharedFile, DerivativeType.Thumbnail, JobState.Queued);
        var shared = Share(sharedFolder, owner, actor);
        database.Shares.Add(shared);

        var privateFile = File(owner, ownerRoot, "private.jpg");
        AddJob(database, privateFile, DerivativeType.Thumbnail, JobState.Running);
        var revokedFile = File(owner, revokedFolder, "revoked.jpg");
        AddJob(database, revokedFile, DerivativeType.PdfThumbnail, JobState.Failed);
        var inactiveFile = File(owner, inactiveFolder, "inactive.jpg");
        AddJob(database, inactiveFile, DerivativeType.Thumbnail, JobState.Queued);
        inactiveFolder.Trash(
            RelativeStoragePath.Create($"users/{owner.Id:N}/trash/{inactiveFolder.Id:N}/inactive"),
            Now.AddMinutes(2));

        var trashedFile = File(actor, actorRoot, "trashed.jpg");
        AddJob(database, trashedFile, DerivativeType.Thumbnail, JobState.Queued);
        trashedFile.Trash(
            RelativeStoragePath.Create($"users/{actor.Id:N}/trash/{trashedFile.Id:N}/trashed.jpg"),
            Now.AddMinutes(2));

        var operationFolder = Folder(actor, actorRoot, "operation");
        var operationFile = File(actor, operationFolder, "moving.jpg");
        database.FileEntries.Add(operationFolder);
        AddJob(database, operationFile, DerivativeType.Thumbnail, JobState.Queued);
        database.FileOperations.Add(new FileOperation(
            Guid.NewGuid(),
            actor.Id,
            FileOperationType.Move,
            operationFolder.Id,
            Guid.NewGuid().ToString(),
            operationFolder.RelativePath,
            $"{actorRoot.RelativePath}/moved",
            null,
            null,
            Now));

        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var repository = new PostgreSqlThumbnailJobSummaryRepository(database);
        var summary = await repository.GetAsync(actor.Id, CancellationToken.None);

        Assert.Equal(3, summary.QueuedCount);
        Assert.Equal(1, summary.RunningCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.All(
            new[] { summary.QueuedCount, summary.RunningCount, summary.FailedCount },
            count => Assert.True(count >= 0));

        await database.Database.ExecuteSqlRawAsync(
            "DELETE FROM share_members WHERE share_id = {0} AND user_id = {1}",
            shared.Id,
            actor.Id);
        var afterRevocation = await repository.GetAsync(actor.Id, CancellationToken.None);
        Assert.Equal(2, afterRevocation.QueuedCount);
        Assert.Equal(1, afterRevocation.RunningCount);
        Assert.Equal(1, afterRevocation.FailedCount);

        await database.Database.ExecuteSqlRawAsync(
            "UPDATE users SET status = 'DISABLED' WHERE id = {0}",
            actor.Id);
        Assert.Equal(
            new(0, 0, 0),
            await repository.GetAsync(actor.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Summary_ObservesOneConsistentJobStateAcrossConcurrentUpdate()
    {
        await using var postgres = CreatePostgres();
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid actorId;
        Guid jobId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var actor = User("SUMMARYCONCURRENT");
            actorId = actor.Id;
            var root = FileEntry.CreateRoot(actor.Id, Now);
            seed.Users.Add(actor);
            seed.FileEntries.Add(root);
            var file = File(actor, root, "updating.jpg");
            var derivative = AddJob(seed, file, DerivativeType.Thumbnail, JobState.Queued);
            jobId = seed.ChangeTracker.Entries<MediaJob>()
                .Single(entry => entry.Entity.DerivativeId == derivative.Id)
                .Entity.Id;
            await seed.SaveChangesAsync();
        }

        await using var writer = new NpgsqlConnection(postgres.GetConnectionString());
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand(
            """
            UPDATE media_jobs
            SET status = 'RUNNING',
                attempt_count = 1,
                worker_token = @worker,
                heartbeat_at = @now,
                started_at = @now,
                updated_at = @now
            WHERE id = @job_id;
            """,
            writer,
            transaction))
        {
            update.Parameters.AddWithValue("worker", Guid.NewGuid());
            update.Parameters.AddWithValue("now", Now.AddSeconds(1));
            update.Parameters.AddWithValue("job_id", jobId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await using var readerDatabase = new KuraStorageDbContext(options);
        var repository = new PostgreSqlThumbnailJobSummaryRepository(readerDatabase);
        var whileUncommitted = await repository.GetAsync(actorId, CancellationToken.None);
        Assert.Equal(new(1, 0, 0), whileUncommitted);

        await transaction.CommitAsync();
        var afterCommit = await repository.GetAsync(actorId, CancellationToken.None);
        Assert.Equal(new(0, 1, 0), afterCommit);
    }

    [Fact]
    public async Task Summary_QueryPlanUsesExistingDerivativeTypeAndJobLookupIndexes()
    {
        await using var postgres = CreatePostgres();
        await postgres.StartAsync();
        await using var database = new KuraStorageDbContext(Options(postgres.GetConnectionString()));
        await database.Database.MigrateAsync();
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SET enable_seqscan = off;
            EXPLAIN
            SELECT derivative.id, latest_job.status
            FROM file_derivatives AS derivative
            CROSS JOIN LATERAL (
                SELECT job.status
                FROM media_jobs AS job
                WHERE job.derivative_id = derivative.id
                  AND job.job_type = derivative.derivative_type
                ORDER BY
                    CASE WHEN job.status IN ('QUEUED', 'RUNNING') THEN 0 ELSE 1 END,
                    job.created_at DESC,
                    job.id DESC
                LIMIT 1
            ) AS latest_job
            WHERE derivative.derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL');
            """,
            connection);
        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(0));
        }

        Assert.Contains(plan, line => line.Contains("ix_file_derivatives_type_lru", StringComparison.Ordinal));
        Assert.Contains(plan, line => line.Contains("ix_media_jobs_derivative", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Summary_RejectsEmptyActorId()
    {
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        await using var database = new KuraStorageDbContext(options);
        var repository = new PostgreSqlThumbnailJobSummaryRepository(database);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.GetAsync(Guid.Empty, CancellationToken.None));
    }

    private static PostgreSqlContainer CreatePostgres() =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("thumbnail_job_summary")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();

    private static DbContextOptions<KuraStorageDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    private static User User(string username) =>
        new(Guid.NewGuid(), username, username, "integration-hash", UserRole.Member, Now);

    private static FileEntry Folder(User owner, FileEntry parent, string name) =>
        FileEntry.CreateFolder(
            Guid.NewGuid(),
            owner.Id,
            parent.Id,
            FileName.Create(name),
            RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(name)),
            Now);

    private static FileEntry File(User owner, FileEntry parent, string name) =>
        FileEntry.CreateFile(
            Guid.NewGuid(),
            owner.Id,
            parent.Id,
            FileName.Create(name),
            RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(name)),
            "application/octet-stream",
            1,
            Now);

    private static FileDerivative AddJob(
        KuraStorageDbContext database,
        FileEntry file,
        DerivativeType type,
        JobState state)
    {
        var derivative = new FileDerivative(Guid.NewGuid(), file.Id, file.FileVersion, type, 1, Now);
        var job = new MediaJob(Guid.NewGuid(), derivative.Id, type, file.OwnerUserId, Now);
        var worker = Guid.NewGuid();
        if (state is not JobState.Queued)
        {
            job.Start(worker, Now);
        }

        if (state is JobState.Failed)
        {
            job.Fail(worker, "TEST_FAILURE", retryable: false, Now.AddSeconds(1));
        }
        else if (state is JobState.Completed)
        {
            job.Complete(worker, Now.AddSeconds(1));
        }

        database.AddRange(file, derivative, job);
        return derivative;
    }

    private static Share Share(FileEntry target, User owner, User member)
    {
        var share = new Share(Guid.NewGuid(), target.Id, owner.Id, Now);
        share.AddMember(member.Id, SharePermission.Viewer, Now);
        return share;
    }

    private enum JobState
    {
        Queued,
        Running,
        Failed,
        Completed,
    }
}
