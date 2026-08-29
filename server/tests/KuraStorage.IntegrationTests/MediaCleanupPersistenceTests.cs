using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class MediaCleanupPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

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
            await seed.SaveChangesAsync();
        }

        await using var firstDatabase = new KuraStorageDbContext(options);
        await using var secondDatabase = new KuraStorageDbContext(options);
        var first = new PostgreSqlMediaCleanupRepository(firstDatabase);
        var second = new PostgreSqlMediaCleanupRepository(secondDatabase);
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

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();

    private static DbContextOptions<KuraStorageDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<KuraStorageDbContext>().UseNpgsql(connectionString).Options;
}
