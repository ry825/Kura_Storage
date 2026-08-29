using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class MediaPersistenceTests
{
    private const string PreviousMigration = "20260828115103_AddFavoritesAndTags";
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_RoundTripsMediaSchemaIndexesConstraintsAndExistingRows()
    {
        await using var postgres = CreatePostgres("media_migration");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync(PreviousMigration);
        await SeedCatalogAsync(database);

        await database.Database.MigrateAsync();
        await AssertMediaSchemaAsync(postgres.GetConnectionString(), 3);
        Assert.Equal(2, await database.FileEntries.CountAsync());

        await database.Database.MigrateAsync(PreviousMigration);
        await AssertMediaSchemaAsync(postgres.GetConnectionString(), 0);
        Assert.Equal(2, await database.FileEntries.CountAsync());

        await database.Database.MigrateAsync();
        await AssertMediaSchemaAsync(postgres.GetConnectionString(), 3);
    }

    [Fact]
    public async Task Queue_UsesStableSkipLockedAcquisitionAndConditionalRecovery()
    {
        await using var postgres = CreatePostgres("media_queue");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid firstJobId;
        Guid secondJobId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var (user, file) = await SeedCatalogAsync(seed);
            var firstDerivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.ImageLow, 1, Now);
            var secondDerivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.ImageMedium, 1, Now);
            var firstJob = new MediaJob(Guid.NewGuid(), firstDerivative.Id, DerivativeType.ImageLow, user.Id, Now);
            var secondJob = new MediaJob(
                Guid.NewGuid(), secondDerivative.Id, DerivativeType.ImageMedium, user.Id, Now.AddMilliseconds(1));
            firstJobId = firstJob.Id;
            secondJobId = secondJob.Id;
            seed.AddRange(firstDerivative, secondDerivative, firstJob, secondJob);
            await seed.SaveChangesAsync();

            seed.MediaJobs.Add(new MediaJob(Guid.NewGuid(), firstDerivative.Id, DerivativeType.ImageLow, user.Id, Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => seed.SaveChangesAsync());
        }

        var firstWorker = Guid.NewGuid();
        var secondWorker = Guid.NewGuid();
        await using var firstDatabase = new KuraStorageDbContext(options);
        await using var secondDatabase = new KuraStorageDbContext(options);
        var firstQueue = new PostgreSqlMediaJobQueue(firstDatabase);
        var secondQueue = new PostgreSqlMediaJobQueue(secondDatabase);

        var acquired = await Task.WhenAll(
            firstQueue.TryAcquireNextAsync(firstWorker, Now.AddMilliseconds(1), CancellationToken.None),
            secondQueue.TryAcquireNextAsync(secondWorker, Now.AddMilliseconds(1), CancellationToken.None));

        var ownedByFirst = Assert.Single(acquired, job => job is not null)!;
        Assert.Contains(ownedByFirst.WorkerToken, new Guid?[] { firstWorker, secondWorker });
        Assert.Equal(firstJobId, ownedByFirst.Id);
        Assert.Contains(acquired, job => job is null);
        Assert.False(await firstQueue.TryRecordHeartbeatAsync(
            ownedByFirst.Id, Guid.NewGuid(), Now.AddSeconds(10), 20, null, null, CancellationToken.None));
        Assert.True(await firstQueue.TryRecordHeartbeatAsync(
            ownedByFirst.Id, ownedByFirst.WorkerToken!.Value, Now.AddSeconds(10), 20, null, null, CancellationToken.None));

        Assert.Equal(0, await firstQueue.RecoverStaleAsync(Now.AddMinutes(2), 100, CancellationToken.None));
        Assert.Equal(1, await firstQueue.RecoverStaleAsync(Now.AddMinutes(2).AddSeconds(10), 100, CancellationToken.None));

        await using var verify = new KuraStorageDbContext(options);
        var recovered = await verify.MediaJobs.SingleAsync(job => job.Id == ownedByFirst.Id);
        var recoveredDerivative = await verify.FileDerivatives.SingleAsync(item => item.Id == recovered.DerivativeId);
        Assert.Equal(MediaJobStatus.Queued, recovered.Status);
        Assert.Equal(DerivativeStatus.Pending, recoveredDerivative.Status);
        Assert.Null(recovered.WorkerToken);
        Assert.Equal(Now.AddMinutes(2).AddSeconds(40), recovered.AvailableAt);
    }

    [Fact]
    public async Task Queue_FailureRetryAndCompletionRemainConditionalAndIdempotent()
    {
        await using var postgres = CreatePostgres("media_retry");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid userId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var (user, file) = await SeedCatalogAsync(seed);
            userId = user.Id;
            var derivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.VideoLow, 1, Now);
            seed.AddRange(
                derivative,
                new MediaJob(Guid.NewGuid(), derivative.Id, DerivativeType.VideoLow, user.Id, Now));
            await seed.SaveChangesAsync();
        }

        await using var firstDatabase = new KuraStorageDbContext(options);
        var firstQueue = new PostgreSqlMediaJobQueue(firstDatabase);
        var firstWorker = Guid.NewGuid();
        var firstRun = await firstQueue.TryAcquireNextAsync(firstWorker, Now, CancellationToken.None);
        Assert.NotNull(firstRun);
        Assert.True(await firstQueue.TryFailAsync(
            firstRun!.Id,
            firstWorker,
            "STORAGE_UNAVAILABLE",
            true,
            Now,
            CancellationToken.None));
        Assert.Null(await firstQueue.TryAcquireNextAsync(Guid.NewGuid(), Now.AddSeconds(29), CancellationToken.None));

        var secondWorker = Guid.NewGuid();
        var secondRun = await firstQueue.TryAcquireNextAsync(
            secondWorker,
            Now.AddSeconds(30),
            CancellationToken.None);
        Assert.NotNull(secondRun);
        Assert.True(await firstQueue.TryFailAsync(
            secondRun!.Id,
            secondWorker,
            "MEDIA_GENERATION_FAILED",
            false,
            Now.AddSeconds(31),
            CancellationToken.None));

        await using var secondDatabase = new KuraStorageDbContext(options);
        var secondQueue = new PostgreSqlMediaJobQueue(secondDatabase);
        var retryIds = await Task.WhenAll(
            firstQueue.TryRetryFailedAsync(
                secondRun.Id, Guid.NewGuid(), userId, Now.AddSeconds(32), CancellationToken.None),
            secondQueue.TryRetryFailedAsync(
                secondRun.Id, Guid.NewGuid(), userId, Now.AddSeconds(32), CancellationToken.None));

        Assert.All(retryIds, retryId => Assert.True(retryId.HasValue));
        Assert.Single(retryIds.Distinct());
        Assert.Equal(1, await firstQueue.GetQueuePositionAsync(retryIds[0]!.Value, Now.AddSeconds(32), CancellationToken.None));
        var finalWorker = Guid.NewGuid();
        var finalRun = await firstQueue.TryAcquireNextAsync(
            finalWorker,
            Now.AddSeconds(32),
            CancellationToken.None);
        Assert.NotNull(finalRun);
        Assert.False(await firstQueue.TryCompleteAsync(
            finalRun!.Id, Guid.NewGuid(), Now.AddSeconds(33), CancellationToken.None));
        Assert.True(await firstQueue.TryCompleteAsync(
            finalRun.Id, finalWorker, Now.AddSeconds(33), CancellationToken.None));

        await using var verify = new KuraStorageDbContext(options);
        Assert.Equal(0, await verify.MediaJobs.CountAsync(job =>
            job.DerivativeId == finalRun.DerivativeId &&
            (job.Status == MediaJobStatus.Queued || job.Status == MediaJobStatus.Running)));
        Assert.Equal(MediaJobStatus.Completed, (await verify.MediaJobs.SingleAsync(job => job.Id == finalRun.Id)).Status);
    }

    [Fact]
    public async Task Queue_DatabaseDisconnectAndNewProcessPreservePendingJob()
    {
        await using var postgres = CreatePostgres("media_disconnect");
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = Options(connectionString);
        Guid jobId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var (user, file) = await SeedCatalogAsync(seed);
            var derivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.PdfThumbnail, 1, Now);
            var job = new MediaJob(Guid.NewGuid(), derivative.Id, DerivativeType.PdfThumbnail, user.Id, Now);
            jobId = job.Id;
            seed.AddRange(derivative, job);
            await seed.SaveChangesAsync();
        }

        await postgres.StopAsync();
        await using (var disconnectedDatabase = new KuraStorageDbContext(options))
        {
            var disconnectedQueue = new PostgreSqlMediaJobQueue(disconnectedDatabase);
            await Assert.ThrowsAnyAsync<NpgsqlException>(() =>
                disconnectedQueue.TryAcquireNextAsync(Guid.NewGuid(), Now, CancellationToken.None));
        }

        await postgres.StartAsync();
        await using var restartedDatabase = new KuraStorageDbContext(Options(postgres.GetConnectionString()));
        var restartedQueue = new PostgreSqlMediaJobQueue(restartedDatabase);
        var acquired = await restartedQueue.TryAcquireNextAsync(Guid.NewGuid(), Now, CancellationToken.None);

        Assert.NotNull(acquired);
        Assert.Equal(jobId, acquired.Id);
        Assert.Equal(MediaJobStatus.Running, acquired.Status);
    }

    [Fact]
    public async Task LifecycleTrigger_ReusesRenameRestoreAndInvalidatesChangedTrashedMissingSources()
    {
        await using var postgres = CreatePostgres("media_lifecycle");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();
        var (user, changedFile) = await SeedCatalogAsync(database);
        var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == user.Id && entry.ParentId == null);
        var trashedFile = FileEntry.CreateFile(
            Guid.NewGuid(), user.Id, root.Id, FileName.Create("trash.jpg"),
            RelativeStoragePath.Create($"users/{user.Id:N}/files/trash.jpg"), "image/jpeg", 10, Now);
        var missingFile = FileEntry.CreateFile(
            Guid.NewGuid(), user.Id, root.Id, FileName.Create("missing.jpg"),
            RelativeStoragePath.Create($"users/{user.Id:N}/files/missing.jpg"), "image/jpeg", 10, Now);
        database.AddRange(trashedFile, missingFile);
        await database.SaveChangesAsync();

        var changedThumbnail = ReadyDerivative(user.Id, changedFile, DerivativeType.Thumbnail, null);
        var trashThumbnail = ReadyDerivative(user.Id, trashedFile, DerivativeType.Thumbnail, null);
        var trashImage = ReadyDerivative(user.Id, trashedFile, DerivativeType.ImageLow, Now.AddDays(1));
        var trashVideo = new FileDerivative(
            Guid.NewGuid(), trashedFile.Id, trashedFile.FileVersion, DerivativeType.VideoLow, 1, Now);
        var trashJob = new MediaJob(
            Guid.NewGuid(), trashVideo.Id, DerivativeType.VideoLow, user.Id, Now);
        var missingThumbnail = ReadyDerivative(user.Id, missingFile, DerivativeType.Thumbnail, null);
        database.AddRange(changedThumbnail, trashThumbnail, trashImage, trashVideo, trashJob, missingThumbnail);
        await database.SaveChangesAsync();

        changedFile.Rename(
            FileName.Create("renamed.jpg"),
            RelativeStoragePath.Create($"users/{user.Id:N}/files/renamed.jpg"),
            Now.AddMinutes(1));
        await database.SaveChangesAsync();
        Assert.Equal(DerivativeStatus.Ready, (await database.FileDerivatives.SingleAsync(x => x.Id == changedThumbnail.Id)).Status);
        Assert.Equal(1, changedFile.FileVersion);

        changedFile.ApplySourceObservation(
            11,
            "image/jpeg",
            Now.AddMinutes(2),
            "changed-source",
            Now.AddMinutes(2),
            contentChanged: true);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        Assert.Equal(DerivativeStatus.Deleting, (await database.FileDerivatives.SingleAsync(x => x.Id == changedThumbnail.Id)).Status);

        trashedFile = await database.FileEntries.SingleAsync(entry => entry.Id == trashedFile.Id);
        trashedFile.Trash(
            RelativeStoragePath.Create($"users/{user.Id:N}/trash/{trashedFile.Id:N}/trash.jpg"),
            Now.AddMinutes(3));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        Assert.Equal(DerivativeStatus.Ready, (await database.FileDerivatives.SingleAsync(x => x.Id == trashThumbnail.Id)).Status);
        Assert.Equal(DerivativeStatus.Deleting, (await database.FileDerivatives.SingleAsync(x => x.Id == trashImage.Id)).Status);
        Assert.Equal(DerivativeStatus.Deleting, (await database.FileDerivatives.SingleAsync(x => x.Id == trashVideo.Id)).Status);
        Assert.Equal(MediaJobStatus.Cancelled, (await database.MediaJobs.SingleAsync(x => x.Id == trashJob.Id)).Status);

        trashedFile = await database.FileEntries.SingleAsync(entry => entry.Id == trashedFile.Id);
        trashedFile.Restore(
            root.Id,
            RelativeStoragePath.Create($"users/{user.Id:N}/files/trash.jpg"),
            Now.AddMinutes(4));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        Assert.Equal(DerivativeStatus.Ready, (await database.FileDerivatives.SingleAsync(x => x.Id == trashThumbnail.Id)).Status);
        Assert.Equal(DerivativeStatus.Deleting, (await database.FileDerivatives.SingleAsync(x => x.Id == trashImage.Id)).Status);

        missingFile = await database.FileEntries.SingleAsync(entry => entry.Id == missingFile.Id);
        var firstObservation = Guid.NewGuid();
        missingFile.MarkMissingCandidate(firstObservation, Now.AddMinutes(5));
        await database.SaveChangesAsync();
        missingFile.ConfirmMissing(Guid.NewGuid(), Now.AddMinutes(10), TimeSpan.FromMinutes(5));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        Assert.Equal(
            DerivativeStatus.BlockedSourceMissing,
            (await database.FileDerivatives.SingleAsync(x => x.Id == missingThumbnail.Id)).Status);
    }

    [Fact]
    public async Task DeletionParticipant_ListsOnlyTargetArtifactsAndCascadesManagementRows()
    {
        await using var postgres = CreatePostgres("media_deletion");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();
        var (user, file) = await SeedCatalogAsync(database);
        var derivative = ReadyDerivative(user.Id, file, DerivativeType.Thumbnail, null);
        var job = new MediaJob(Guid.NewGuid(), derivative.Id, DerivativeType.Thumbnail, user.Id, Now);
        job.Start(Guid.NewGuid(), Now);
        var workerToken = job.WorkerToken!.Value;
        job.Complete(workerToken, Now.AddSeconds(1));
        var lease = new DerivativeLease(
            Guid.NewGuid(), derivative.Id, DerivativeLeaseType.Delivery, Guid.NewGuid(), Now.AddMinutes(2), Now);
        database.AddRange(derivative, job, lease);
        await database.SaveChangesAsync();
        var participant = new MediaDeletionParticipant(
            database,
            Microsoft.Extensions.Options.Options.Create(new MediaOptions()));
        var purgeTarget = new PermanentDeleteTarget(
            file.Id,
            user.Id,
            "FILE",
            RelativeStoragePath.Create($"users/{user.Id:N}/trash/{file.Id:N}"),
            [],
            file.Size);

        var artifacts = await participant.ListPhysicalArtifactsAsync(purgeTarget, CancellationToken.None);

        Assert.Contains(RelativeStoragePath.Create($"derivatives/{user.Id:N}/{file.Id:N}"), artifacts);
        Assert.Contains(RelativeStoragePath.Create($"derivative-temp/{job.Id:N}"), artifacts);
        await participant.DeleteManagementDataAsync(
            new FileIndexDeletionTarget(file.Id, user.Id, [file.Id]),
            CancellationToken.None);
        await database.SaveChangesAsync();
        Assert.Empty(await database.FileDerivatives.ToListAsync());
        Assert.Empty(await database.MediaJobs.ToListAsync());
        Assert.Empty(await database.DerivativeLeases.ToListAsync());
        Assert.Equal(2, await database.FileEntries.CountAsync());
    }

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();

    private static DbContextOptions<KuraStorageDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    private static async Task<(User User, FileEntry File)> SeedCatalogAsync(KuraStorageDbContext database)
    {
        var user = new User(
            Guid.NewGuid(),
            $"MEDIA{Guid.NewGuid():N}".ToUpperInvariant(),
            "media-user",
            "integration-hash",
            UserRole.Member,
            Now);
        var root = FileEntry.CreateRoot(user.Id, Now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(),
            user.Id,
            root.Id,
            FileName.Create("photo.jpg"),
            RelativeStoragePath.Create($"users/{user.Id:N}/files/photo.jpg"),
            "image/jpeg",
            42,
            Now);
        database.AddRange(user, root, file);
        await database.SaveChangesAsync();
        return (user, file);
    }

    private static async Task AssertMediaSchemaAsync(string connectionString, long expectedTables)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tables = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('file_derivatives', 'media_jobs', 'derivative_leases');
            """,
            connection);
        Assert.Equal(expectedTables, await tables.ExecuteScalarAsync());
        if (expectedTables == 0)
        {
            return;
        }

        await using var indexes = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN (
                'ux_file_derivatives_logical_key',
                'ux_media_jobs_active_derivative',
                'ix_media_jobs_queue',
                'ux_derivative_leases_owner');
            """,
            connection);
        Assert.Equal(4L, await indexes.ExecuteScalarAsync());

        await using var cascades = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid IN ('file_derivatives'::regclass, 'media_jobs'::regclass, 'derivative_leases'::regclass)
              AND contype = 'f'
              AND confdeltype = 'c';
            """,
            connection);
        Assert.Equal(4L, await cascades.ExecuteScalarAsync());
    }

    private static FileDerivative ReadyDerivative(
        Guid ownerUserId,
        FileEntry file,
        DerivativeType type,
        DateTimeOffset? expiresAt)
    {
        var derivative = new FileDerivative(Guid.NewGuid(), file.Id, file.FileVersion, type, 1, Now);
        derivative.Start(Now);
        var segment = type.ToString().ToLowerInvariant();
        derivative.MarkReady(
            $"derivatives/{ownerUserId:N}/{file.Id:N}/1/1/{segment}.webp",
            1,
            Now,
            expiresAt);
        return derivative;
    }
}
