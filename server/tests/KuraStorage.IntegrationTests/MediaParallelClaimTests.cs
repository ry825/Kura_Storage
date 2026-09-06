using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class MediaParallelClaimTests
{
    [Fact]
    public async Task Queue_EnforcesThumbnailLimitAndKeepsVideoOutsideThumbnailSlots()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("media_parallel_claim")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        await using (var seed = new KuraStorageDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var user = new User(Guid.NewGuid(), "PARALLEL", "Parallel", "integration-hash", UserRole.Member, now);
            var root = FileEntry.CreateRoot(user.Id, now);
            seed.AddRange(user, root);
            foreach (var (type, index) in new[]
                     {
                         DerivativeType.Thumbnail,
                         DerivativeType.PdfThumbnail,
                         DerivativeType.Thumbnail,
                         DerivativeType.PdfThumbnail,
                         DerivativeType.VideoLow,
                         DerivativeType.ImageLow,
                     }.Select((type, index) => (type, index)))
            {
                var file = FileEntry.CreateFile(
                    Guid.NewGuid(),
                    user.Id,
                    root.Id,
                    FileName.Create($"source-{index}.bin"),
                    RelativeStoragePath.Create(root.RelativePath).Append(FileName.Create($"source-{index}.bin")),
                    "application/octet-stream",
                    1,
                    now);
                var derivative = new FileDerivative(Guid.NewGuid(), file.Id, 1, type, 1, now);
                seed.AddRange(file, derivative, new MediaJob(Guid.NewGuid(), derivative.Id, type, user.Id, now));
            }

            await seed.SaveChangesAsync();
        }

        var databases = Enumerable.Range(0, 8).Select(_ => new KuraStorageDbContext(options)).ToArray();
        try
        {
            var thumbnailClaims = databases.Take(5).Select(database =>
                new PostgreSqlMediaJobQueue(database).TryAcquireNextAsync(
                    Guid.NewGuid(), now, MediaJobClaimScope.Thumbnail, 2, CancellationToken.None));
            var nonThumbnailClaims = databases.Skip(5).Select(database =>
                new PostgreSqlMediaJobQueue(database).TryAcquireNextAsync(
                    Guid.NewGuid(), now, MediaJobClaimScope.NonThumbnail, 1, CancellationToken.None));

            var thumbnails = (await Task.WhenAll(thumbnailClaims)).Where(job => job is not null).Cast<MediaJob>().ToArray();
            var nonThumbnails = (await Task.WhenAll(nonThumbnailClaims)).Where(job => job is not null).Cast<MediaJob>().ToArray();

            Assert.Equal(2, thumbnails.Length);
            Assert.All(thumbnails, job =>
                Assert.Contains(job.JobType, new[] { DerivativeType.Thumbnail, DerivativeType.PdfThumbnail }));
            Assert.Single(nonThumbnails);
            Assert.DoesNotContain(
                nonThumbnails[0].JobType,
                new[] { DerivativeType.Thumbnail, DerivativeType.PdfThumbnail });

            await using var verify = new KuraStorageDbContext(options);
            Assert.Equal(2, await verify.MediaJobs.CountAsync(job =>
                job.Status == MediaJobStatus.Running &&
                (job.JobType == DerivativeType.Thumbnail || job.JobType == DerivativeType.PdfThumbnail)));
            Assert.Equal(1, await verify.MediaJobs.CountAsync(job =>
                job.Status == MediaJobStatus.Running &&
                job.JobType != DerivativeType.Thumbnail &&
                job.JobType != DerivativeType.PdfThumbnail));
        }
        finally
        {
            foreach (var database in databases)
            {
                await database.DisposeAsync();
            }
        }
    }
}
