using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class FileVersionFoundationTests
{
    [Fact]
    public async Task LazyBaseline_UsesMutationLockAndConvergesWhileUnsafeStatesRemainUnrecorded()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("file_version_foundation")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var databaseOptions = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var storageRoot = Directory.CreateTempSubdirectory("kurastorage-version-foundation-");
        try
        {
            var now = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var ownerId = Guid.NewGuid();
            var root = FileEntry.CreateRoot(ownerId, now);
            var active = CreateFile(ownerId, root.Id, "active.txt", 6, now);
            var missing = CreateFile(ownerId, root.Id, "missing.txt", 1, now);
            missing.MarkMissingCandidate(Guid.NewGuid(), now.AddMinutes(1));
            missing.ConfirmMissing(Guid.NewGuid(), now.AddMinutes(7), TimeSpan.FromMinutes(5));
            var trashed = CreateFile(ownerId, root.Id, "trashed.txt", 1, now);
            trashed.Trash(
                RelativeStoragePath.Create($"users/{ownerId:N}/trash/{trashed.Id:N}/trashed.txt"),
                now.AddMinutes(1));
            var busy = CreateFile(ownerId, root.Id, "busy.txt", 1, now);
            var corrupt = CreateFile(ownerId, root.Id, "corrupt.txt", 5, now);
            await using (var seed = new KuraStorageDbContext(databaseOptions))
            {
                await seed.Database.MigrateAsync();
                seed.Users.Add(new User(ownerId, "VERSIONFOUNDATION", "Version Foundation", "hash", UserRole.Member, now));
                seed.FileEntries.AddRange(root, active, missing, trashed, busy, corrupt);
                seed.FileOperations.Add(new FileOperation(
                    Guid.NewGuid(), ownerId, FileOperationType.Rename, busy.Id, null,
                    busy.RelativePath, $"{root.RelativePath}/busy-renamed.txt", null, null, now));
                await seed.SaveChangesAsync();
                Assert.Empty(await seed.FileVersionRecords.ToListAsync());
            }

            var userFiles = Path.Combine(storageRoot.FullName, "users", ownerId.ToString("N"), "files");
            Directory.CreateDirectory(userFiles);
            await File.WriteAllTextAsync(Path.Combine(userFiles, "active.txt"), "active", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(userFiles, "busy.txt"), "b", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(userFiles, "corrupt.txt"), "bad!", new UTF8Encoding(false));

            var first = CreateService(databaseOptions, storageRoot.FullName, now);
            var second = CreateService(databaseOptions, storageRoot.FullName, now);
            await using var firstDatabase = first.Database;
            await using var secondDatabase = second.Database;
            var results = await Task.WhenAll(
                first.Service.EnsureBaselineAsync(
                    active.Id, FileVersionChangeKind.ExternalChange, Guid.NewGuid(), ownerId, null, default),
                second.Service.EnsureBaselineAsync(
                    active.Id, FileVersionChangeKind.ExternalChange, Guid.NewGuid(), ownerId, null, default));

            Assert.All(results, Assert.NotNull);
            await using (var verify = new KuraStorageDbContext(databaseOptions))
            {
                var record = await verify.FileVersionRecords.SingleAsync(candidate => candidate.FileEntryId == active.Id);
                Assert.Equal(active.FileVersion, record.Version);
                Assert.Equal(6, record.Size);
                Assert.True(File.Exists(Path.Combine(
                    storageRoot.FullName,
                    record.ContentRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            }

            var stateService = CreateService(databaseOptions, storageRoot.FullName, now);
            await using var stateDatabase = stateService.Database;
            Assert.Null(await stateService.Service.EnsureBaselineAsync(
                missing.Id, FileVersionChangeKind.ExternalChange, Guid.NewGuid(), ownerId, null, default));
            Assert.Null(await stateService.Service.EnsureBaselineAsync(
                trashed.Id, FileVersionChangeKind.ExternalChange, Guid.NewGuid(), ownerId, null, default));
            await Assert.ThrowsAsync<FileVersionOperationBlockedException>(() =>
                stateService.Service.EnsureBaselineAsync(
                    busy.Id, FileVersionChangeKind.ExternalChange, Guid.NewGuid(), ownerId, null, default));
            await Assert.ThrowsAsync<FileVersionContentSizeException>(() =>
                stateService.Service.EnsureBaselineAsync(
                    corrupt.Id, FileVersionChangeKind.ExternalChange, Guid.NewGuid(), ownerId, null, default));
            Assert.False(await stateDatabase.FileVersionRecords.AnyAsync(record =>
                record.FileEntryId == missing.Id || record.FileEntryId == trashed.Id ||
                record.FileEntryId == busy.Id || record.FileEntryId == corrupt.Id));
        }
        finally
        {
            storageRoot.Delete(recursive: true);
        }
    }

    private static FileEntry CreateFile(
        Guid ownerId,
        Guid parentId,
        string name,
        long size,
        DateTimeOffset now) =>
        FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, parentId, FileName.Create(name),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/{name}"),
            "text/plain", size, now);

    private static ServiceContext CreateService(
        DbContextOptions<KuraStorageDbContext> databaseOptions,
        string storageRoot,
        DateTimeOffset now)
    {
        var database = new KuraStorageDbContext(databaseOptions);
        var options = Options.Create(new StorageOptions
        {
            RootPath = storageRoot,
            StorageId = "integration",
            MinimumFreeBytes = 1,
            CapacityWarningFreeBytes = 1,
        });
        var files = new FileRepository(database);
        var guard = new AvailableGuard();
        return new ServiceContext(
            database,
            new FileVersionService(
                new FileVersionRepository(database),
                new FileVersionStore(options),
                new FileStore(options),
                guard,
                new FixedClock(now),
                files));
    }

    private sealed record ServiceContext(KuraStorageDbContext Database, FileVersionService Service);

    private sealed class AvailableGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
