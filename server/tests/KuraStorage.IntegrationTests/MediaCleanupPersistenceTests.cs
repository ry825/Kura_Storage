using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using KuraStorage.Worker.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class MediaCleanupPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private const string KeyHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string FingerprintHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task Cleanup_ClaimsOnlyEligibleCacheInStableOrderAndExcludesActiveLeasesAndThumbnails()
    {
        await using var postgres = CreatePostgres("media_cleanup_claim");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid oldestId;
        Guid leasedId;
        Guid thumbnailId;
        Guid lifecycleDeletingId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var (user, file) = await SeedCatalogAsync(seed);
            var oldest = Ready(user.Id, file, DerivativeType.ImageLow, Now.AddHours(-3), Now.AddHours(-1));
            var newer = Ready(user.Id, file, DerivativeType.ImageMedium, Now.AddHours(-2), Now.AddHours(-1));
            var leased = Ready(user.Id, file, DerivativeType.VideoLow, Now.AddHours(-4), Now.AddHours(-1));
            var thumbnail = Ready(user.Id, file, DerivativeType.Thumbnail, Now, null);
            var lifecycleDeleting = Ready(user.Id, file, DerivativeType.VideoMedium, Now.AddHours(-5), Now.AddHours(-1));
            lifecycleDeleting.BeginDeleting(Now.AddMinutes(-1));
            oldestId = oldest.Id;
            leasedId = leased.Id;
            thumbnailId = thumbnail.Id;
            lifecycleDeletingId = lifecycleDeleting.Id;
            seed.AddRange(oldest, newer, leased, thumbnail, lifecycleDeleting);
            seed.Add(new DerivativeLease(
                Guid.NewGuid(), leased.Id, DerivativeLeaseType.Delivery, Guid.NewGuid(), Now.AddMinutes(1), Now));
            var queuedDerivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.ImageLow, 2, Now);
            var runningDerivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.ImageMedium, 2, Now);
            var failedDerivative = new FileDerivative(
                Guid.NewGuid(), file.Id, file.FileVersion, DerivativeType.VideoLow, 2, Now);
            var queuedJob = new MediaJob(Guid.NewGuid(), queuedDerivative.Id, queuedDerivative.DerivativeType, user.Id, Now);
            var runningJob = new MediaJob(Guid.NewGuid(), runningDerivative.Id, runningDerivative.DerivativeType, user.Id, Now);
            var failedJob = new MediaJob(Guid.NewGuid(), failedDerivative.Id, failedDerivative.DerivativeType, user.Id, Now);
            var runningToken = Guid.NewGuid();
            var failedToken = Guid.NewGuid();
            runningJob.Start(runningToken, Now);
            failedJob.Start(failedToken, Now);
            failedJob.Fail(failedToken, "GENERATION_FAILED", retryable: false, Now);
            seed.AddRange(queuedDerivative, runningDerivative, failedDerivative, queuedJob, runningJob, failedJob);
            await seed.SaveChangesAsync();
        }

        await using var firstDatabase = new KuraStorageDbContext(options);
        await using var secondDatabase = new KuraStorageDbContext(options);
        var first = new PostgreSqlMediaCleanupRepository(firstDatabase);
        var second = new PostgreSqlMediaCleanupRepository(secondDatabase);
        var snapshot = await first.GetCacheSnapshotAsync(CancellationToken.None);
        Assert.Equal(10, snapshot.ImageLowBytes);
        Assert.Equal(10, snapshot.ImageMediumBytes);
        Assert.Equal(10, snapshot.VideoLowBytes);
        Assert.Equal(0, snapshot.VideoMediumBytes);
        Assert.Equal(1, snapshot.QueuedJobCount);
        Assert.Equal(1, snapshot.RunningJobCount);
        Assert.Equal(1, snapshot.FailedJobCount);
        await using var firstLock = await first.TryAcquireCleanupLockAsync(CancellationToken.None);
        Assert.NotNull(firstLock);
        Assert.Null(await second.TryAcquireCleanupLockAsync(CancellationToken.None));

        var claimed = await first.ClaimExpiredAsync(Now, 1, CancellationToken.None);
        Assert.Equal(oldestId, Assert.Single(claimed).DerivativeId);
        var crashRecovery = await first.ClaimDeletingAsync(Now, 10, CancellationToken.None);
        Assert.Contains(crashRecovery, item => item.DerivativeId == oldestId && item.RestoreReadyOnFailure);
        Assert.Contains(crashRecovery, item => item.DerivativeId == lifecycleDeletingId && !item.RestoreReadyOnFailure);
        await first.CompleteDeleteAsync(oldestId, CancellationToken.None);
        await first.CompleteDeleteAsync(lifecycleDeletingId, CancellationToken.None);

        var secondClaim = await first.ClaimExpiredAsync(Now, 10, CancellationToken.None);
        Assert.Single(secondClaim);
        Assert.DoesNotContain(secondClaim, item => item.DerivativeId == leasedId || item.DerivativeId == thumbnailId);
        await first.RestoreReadyAsync(secondClaim[0].DerivativeId, Now, CancellationToken.None);

        await using var verify = new KuraStorageDbContext(options);
        Assert.DoesNotContain(await verify.FileDerivatives.ToListAsync(), item => item.Id == oldestId);
        Assert.Equal(DerivativeStatus.Ready, (await verify.FileDerivatives.SingleAsync(item => item.Id == leasedId)).Status);
        Assert.Equal(DerivativeStatus.Ready, (await verify.FileDerivatives.SingleAsync(item => item.Id == thumbnailId)).Status);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyOldTerminalHistoryWithoutActiveRetryReference()
    {
        await using var postgres = CreatePostgres("media_cleanup_jobs");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid removableId;
        Guid retainedWithRetryId;
        Guid recentId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var (user, file) = await SeedCatalogAsync(seed);
            var removableDerivative = Ready(user.Id, file, DerivativeType.ImageLow, Now, Now.AddHours(24));
            var retryDerivative = Ready(user.Id, file, DerivativeType.ImageMedium, Now, Now.AddHours(24));
            var recentDerivative = Ready(user.Id, file, DerivativeType.VideoLow, Now, Now.AddHours(24));
            var removable = CompletedJob(removableDerivative, user.Id, Now.AddDays(-8));
            var retained = CompletedJob(retryDerivative, user.Id, Now.AddDays(-8));
            var recent = CompletedJob(recentDerivative, user.Id, Now.AddDays(-6));
            removableId = removable.Id;
            retainedWithRetryId = retained.Id;
            recentId = recent.Id;
            seed.AddRange(removableDerivative, retryDerivative, recentDerivative, removable, retained, recent);
            seed.Add(new MediaJob(Guid.NewGuid(), retryDerivative.Id, retryDerivative.DerivativeType, user.Id, Now));
            await seed.SaveChangesAsync();
        }

        await using var database = new KuraStorageDbContext(options);
        var repository = new PostgreSqlMediaCleanupRepository(database);
        Assert.Equal(1, await repository.DeleteTerminalJobsAsync(Now.AddDays(-7), 100, CancellationToken.None));

        var remaining = await database.MediaJobs.AsNoTracking().Select(job => job.Id).ToListAsync();
        Assert.DoesNotContain(removableId, remaining);
        Assert.Contains(retainedWithRetryId, remaining);
        Assert.Contains(recentId, remaining);
    }

    [Fact]
    public async Task CleanupRun_ManualIdempotencyLeaseRecoveryAndWorkerOwnershipArePersistent()
    {
        await using var postgres = CreatePostgres("media_cleanup_runs");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid adminId;
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var admin = new User(
                Guid.NewGuid(), $"ADMIN{Guid.NewGuid():N}".ToUpperInvariant(), "cleanup-admin",
                "integration-hash", UserRole.Admin, Now);
            adminId = admin.Id;
            seed.Add(admin);
            await seed.SaveChangesAsync();
        }

        MediaCleanupRequestPersistenceResult[] concurrent;
        await using (var firstDatabase = new KuraStorageDbContext(options))
        await using (var secondDatabase = new KuraStorageDbContext(options))
        {
            var first = new PostgreSqlMediaCleanupRepository(firstDatabase);
            var second = new PostgreSqlMediaCleanupRepository(secondDatabase);
            concurrent = await Task.WhenAll(
                first.CreateOrGetManualRunAsync(adminId, KeyHash, FingerprintHash, Now, CancellationToken.None),
                second.CreateOrGetManualRunAsync(adminId, KeyHash, FingerprintHash, Now, CancellationToken.None));
        }

        Assert.Equal(concurrent[0].Run.Id, concurrent[1].Run.Id);
        Assert.All(concurrent, result => Assert.False(result.Conflict));

        await using var database = new KuraStorageDbContext(options);
        var repository = new PostgreSqlMediaCleanupRepository(database);
        var conflict = await repository.CreateOrGetManualRunAsync(
            adminId,
            KeyHash,
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            Now,
            CancellationToken.None);
        Assert.True(conflict.Conflict);
        Assert.Equal(concurrent[0].Run.Id, conflict.Run.Id);
        var pendingSnapshot = await repository.GetCacheSnapshotAsync(CancellationToken.None);
        Assert.Equal(1, pendingSnapshot.PendingRunCount);
        Assert.Equal(0, pendingSnapshot.RunningRunCount);

        var firstWorker = Guid.NewGuid();
        var claimed = await repository.ClaimNextRunAsync(
            firstWorker, Now, Now.AddMinutes(15), CancellationToken.None);
        Assert.NotNull(claimed);
        Assert.Equal(MediaCleanupRunStatus.Running, claimed.Status);
        var runningSnapshot = await repository.GetCacheSnapshotAsync(CancellationToken.None);
        Assert.Equal(0, runningSnapshot.PendingRunCount);
        Assert.Equal(1, runningSnapshot.RunningRunCount);
        Assert.Null(await repository.ClaimNextRunAsync(
            Guid.NewGuid(), Now.AddMinutes(1), Now.AddMinutes(16), CancellationToken.None));

        var recoveringWorker = Guid.NewGuid();
        var recovered = await repository.ClaimNextRunAsync(
            recoveringWorker, Now.AddMinutes(16), Now.AddMinutes(31), CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(claimed.Id, recovered.Id);
        Assert.False(await repository.CompleteRunAsync(
            recovered.Id,
            firstWorker,
            Now.AddMinutes(17),
            new MediaCleanupResult(true, 1, 10, 0, 20, 0, 1),
            CancellationToken.None));
        Assert.True(await repository.CompleteRunAsync(
            recovered.Id,
            recoveringWorker,
            Now.AddMinutes(17),
            new MediaCleanupResult(true, 1, 10, 0, 20, 0, 1),
            CancellationToken.None));

        var latest = await repository.FindLatestRunAsync(CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal(MediaCleanupRunStatus.Completed, latest.Status);
        Assert.Equal(1, latest.DeletedCount);
        Assert.Equal(10, latest.ReleasedBytes);
        Assert.Equal(20, latest.RemainingCacheBytes);
        Assert.Single(await database.MediaCleanupRuns.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CleanupRun_ScheduledRequestsConvergeAndPartialFailureIsRecorded()
    {
        await using var postgres = CreatePostgres("media_cleanup_scheduled_runs");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using (var migration = new KuraStorageDbContext(options))
        {
            await migration.Database.MigrateAsync();
        }

        await using var database = new KuraStorageDbContext(options);
        var repository = new PostgreSqlMediaCleanupRepository(database);
        var first = await repository.EnsureScheduledRunAsync(Now, TimeSpan.FromHours(1), CancellationToken.None);
        var duplicate = await repository.EnsureScheduledRunAsync(Now.AddMinutes(1), TimeSpan.FromHours(1), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(first.Id, duplicate?.Id);

        var worker = Guid.NewGuid();
        var claimed = await repository.ClaimNextRunAsync(worker, Now, Now.AddMinutes(15), CancellationToken.None);
        Assert.NotNull(claimed);
        Assert.True(await repository.CompleteRunAsync(
            claimed.Id,
            worker,
            Now.AddMinutes(2),
            new MediaCleanupResult(true, 2, 20, 1, 5, 0, 1),
            CancellationToken.None));

        var completed = await repository.FindLatestRunAsync(CancellationToken.None);
        Assert.NotNull(completed);
        Assert.Equal(MediaCleanupRunStatus.Failed, completed.Status);
        Assert.Equal(MediaCleanupFailureCode.PartialDeleteFailure, completed.FailureCode);
        Assert.Null(await repository.EnsureScheduledRunAsync(
            Now.AddMinutes(30), TimeSpan.FromHours(1), CancellationToken.None));
        Assert.NotNull(await repository.EnsureScheduledRunAsync(
            Now.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None));
    }

    [Fact]
    public async Task CleanupWorker_ProcessesManualRunAgainstStorageWithoutDeletingSourceLeaseOrThumbnail()
    {
        await using var postgres = CreatePostgres("media_cleanup_worker_storage");
        await postgres.StartAsync();
        var databaseOptions = Options(postgres.GetConnectionString());
        var storageRoot = Path.Combine(Path.GetTempPath(), $"kurastorage-cleanup-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            Guid expiredId;
            Guid leasedId;
            Guid thumbnailId;
            Guid manualRunId;
            string sourceRelativePath;
            string expiredRelativePath;
            string leasedRelativePath;
            string thumbnailRelativePath;
            await using (var seed = new KuraStorageDbContext(databaseOptions))
            {
                await seed.Database.MigrateAsync();
                var (owner, file) = await SeedCatalogAsync(seed);
                var admin = new User(
                    Guid.NewGuid(), $"WORKER{Guid.NewGuid():N}".ToUpperInvariant(), "worker-admin",
                    "integration-hash", UserRole.Admin, Now);
                var expired = Ready(owner.Id, file, DerivativeType.ImageLow, Now.AddHours(-3), Now.AddMinutes(-1));
                var leased = Ready(owner.Id, file, DerivativeType.ImageMedium, Now.AddHours(-2), Now.AddMinutes(-1));
                var thumbnail = Ready(owner.Id, file, DerivativeType.Thumbnail, Now.AddHours(-4), null);
                var run = MediaCleanupRun.CreateManual(
                    Guid.NewGuid(), admin.Id, KeyHash, FingerprintHash, Now.AddMinutes(-2));
                expiredId = expired.Id;
                leasedId = leased.Id;
                thumbnailId = thumbnail.Id;
                manualRunId = run.Id;
                sourceRelativePath = file.RelativePath;
                expiredRelativePath = expired.RelativePath!;
                leasedRelativePath = leased.RelativePath!;
                thumbnailRelativePath = thumbnail.RelativePath!;
                seed.AddRange(admin, expired, leased, thumbnail, run);
                seed.Add(new DerivativeLease(
                    Guid.NewGuid(), leased.Id, DerivativeLeaseType.Delivery, Guid.NewGuid(), Now.AddMinutes(10), Now));
                await seed.SaveChangesAsync();
            }

            await WriteStorageFileAsync(storageRoot, sourceRelativePath, [1, 2, 3, 4]);
            await WriteStorageFileAsync(storageRoot, expiredRelativePath, new byte[10]);
            await WriteStorageFileAsync(storageRoot, leasedRelativePath, new byte[10]);
            await WriteStorageFileAsync(storageRoot, thumbnailRelativePath, new byte[10]);

            var guard = new AvailableStorageGuard();
            var cleanupOptions = new MediaCleanupOptions
            {
                IntervalMinutes = 30,
                RunLeaseMinutes = 15,
                BatchSize = 10,
                CacheHighWatermarkBytes = 1_000,
                CacheLowWatermarkBytes = 500,
            };
            await using var services = new ServiceCollection()
                .AddDbContext<KuraStorageDbContext>(builder => builder.UseNpgsql(postgres.GetConnectionString()))
                .AddScoped<IMediaCleanupRepository, PostgreSqlMediaCleanupRepository>()
                .AddScoped<IMediaCleanupService, MediaCleanupService>()
                .AddSingleton<IStorageGuard>(guard)
                .AddSingleton<ISystemClock>(new FixedClock())
                .AddSingleton(cleanupOptions)
                .AddSingleton<IDerivativeStore>(new DerivativeStore(
                    Microsoft.Extensions.Options.Options.Create(new StorageOptions
                    {
                        RootPath = storageRoot,
                        StorageId = "cleanup-worker-test",
                        MinimumFreeBytes = 1,
                    }),
                    Microsoft.Extensions.Options.Options.Create(new MediaOptions()),
                    guard))
                .BuildServiceProvider();
            var worker = new MediaCleanupWorker(
                services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<ISystemClock>(),
                cleanupOptions,
                new MediaCleanupMetrics(),
                new SystemMediaCleanupDelay(),
                NullLogger<MediaCleanupWorker>.Instance);

            var result = await worker.RunOnceAsync(CancellationToken.None);

            Assert.True(result.AcquiredLock);
            Assert.Equal(1, result.DeletedCount);
            Assert.Equal(10, result.DeletedBytes);
            await using var verify = new KuraStorageDbContext(databaseOptions);
            Assert.False(await verify.FileDerivatives.AnyAsync(item => item.Id == expiredId));
            Assert.True(await verify.FileDerivatives.AnyAsync(item => item.Id == leasedId));
            Assert.True(await verify.FileDerivatives.AnyAsync(item => item.Id == thumbnailId));
            var completed = await verify.MediaCleanupRuns.AsNoTracking().SingleAsync(run => run.Id == manualRunId);
            Assert.Equal(MediaCleanupRunStatus.Completed, completed.Status);
            Assert.Equal(10, completed.ReleasedBytes);
            Assert.True(File.Exists(ResolveStoragePath(storageRoot, sourceRelativePath)));
            Assert.False(File.Exists(ResolveStoragePath(storageRoot, expiredRelativePath)));
            Assert.True(File.Exists(ResolveStoragePath(storageRoot, leasedRelativePath)));
            Assert.True(File.Exists(ResolveStoragePath(storageRoot, thumbnailRelativePath)));
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private static MediaJob CompletedJob(FileDerivative derivative, Guid userId, DateTimeOffset completedAt)
    {
        var job = new MediaJob(Guid.NewGuid(), derivative.Id, derivative.DerivativeType, userId, completedAt.AddMinutes(-1));
        var worker = Guid.NewGuid();
        job.Start(worker, completedAt.AddMinutes(-1));
        job.Complete(worker, completedAt);
        return job;
    }

    private static FileDerivative Ready(
        Guid ownerId,
        FileEntry file,
        DerivativeType type,
        DateTimeOffset accessedAt,
        DateTimeOffset? expiresAt)
    {
        var derivative = new FileDerivative(Guid.NewGuid(), file.Id, file.FileVersion, type, 1, accessedAt);
        derivative.Start(accessedAt);
        derivative.MarkReady(
            $"derivatives/{ownerId:N}/{file.Id:N}/1/1/{type.ToString().ToLowerInvariant()}-{derivative.Id:N}.bin",
            10,
            accessedAt,
            expiresAt);
        return derivative;
    }

    private static async Task<(User User, FileEntry File)> SeedCatalogAsync(KuraStorageDbContext database)
    {
        var user = new User(
            Guid.NewGuid(), $"CLEAN{Guid.NewGuid():N}".ToUpperInvariant(), "cleanup-user", "integration-hash", UserRole.Member, Now);
        var root = FileEntry.CreateRoot(user.Id, Now);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), user.Id, root.Id, FileName.Create("source.bin"),
            RelativeStoragePath.Create($"users/{user.Id:N}/files/source.bin"), "application/octet-stream", 100, Now);
        database.AddRange(user, root, file);
        await database.SaveChangesAsync();
        return (user, file);
    }

    private static async Task WriteStorageFileAsync(string root, string relativePath, byte[] content)
    {
        var path = ResolveStoragePath(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content);
    }

    private static string ResolveStoragePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();

    private static DbContextOptions<KuraStorageDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<KuraStorageDbContext>().UseNpgsql(connectionString).Options;

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class AvailableStorageGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }
}
