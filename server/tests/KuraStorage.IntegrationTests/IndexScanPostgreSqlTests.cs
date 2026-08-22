using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Indexing;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class IndexScanPostgreSqlTests
{
    [Fact]
    public async Task DryRunLeavesNoPersistentChangesAndApplyPublishesObservedFile()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("index_scan")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var dbOptions = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(dbOptions);
        await database.Database.MigrateAsync();
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        database.Users.Add(new User(ownerId, "INDEX", "Index", "hash", UserRole.Member, now));
        database.FileEntries.Add(FileEntry.CreateRoot(ownerId, now));
        await database.SaveChangesAsync();
        var storageRoot = Directory.CreateTempSubdirectory("kurastorage-index-scan-");
        try
        {
            var files = Directory.CreateDirectory(
                Path.Combine(storageRoot.FullName, "users", ownerId.ToString("N"), "files"));
            var nested = Directory.CreateDirectory(Path.Combine(files.FullName, "nested"));
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "observed.txt"), "observed");
            var catalog = new IndexCatalogRepository(database);
            var snapshot = new ManagedFileSystemSnapshotReader(
                Options.Create(new StorageOptions { RootPath = storageRoot.FullName, StorageId = "test" }));
            var clock = new MutableClock(now);
            var service = new IndexScanService(
                catalog,
                snapshot,
                new AvailableGuard(),
                clock,
                new KuraStorage.Application.Indexing.IndexingOptions
                {
                    BatchSize = 10,
                    MissingConfirmationDelayMinutes = 5,
                    StagingRetentionHours = 24,
                });

            var dryRun = await service.RunAsync(
                new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.DryRun),
                CancellationToken.None);

            Assert.Equal(2, dryRun.AddedCount);
            Assert.Equal(1, await database.FileEntries.CountAsync());
            Assert.Equal(0, await database.IndexScanRuns.CountAsync());
            Assert.Equal(0, await database.IndexScanItems.CountAsync());

            var applied = await service.RunAsync(
                new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
                CancellationToken.None);

            Assert.Equal(2, applied.AddedCount);
            Assert.Equal(IndexScanStatus.Completed, applied.Status);
            Assert.Equal(3, await database.FileEntries.CountAsync());
            Assert.Equal(1, await database.IndexScanRuns.CountAsync());
            Assert.Equal(0, await database.IndexScanItems.CountAsync());

            File.Delete(Path.Combine(nested.FullName, "observed.txt"));
            var candidateScan = await service.RunAsync(
                new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
                CancellationToken.None);
            Assert.Equal(1, candidateScan.CandidateCount);
            var indexedFile = await database.FileEntries.SingleAsync(entry => entry.Name == "observed.txt");
            Assert.Equal(FileEntryStatus.MissingCandidate, indexedFile.Status);

            clock.UtcNow = now.AddMinutes(6);
            var missingScan = await service.RunAsync(
                new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
                CancellationToken.None);
            Assert.Equal(1, missingScan.MissingCount);
            Assert.Equal(FileEntryStatus.Missing, indexedFile.Status);

            await using (var firstContext = new KuraStorageDbContext(dbOptions))
            await using (var secondContext = new KuraStorageDbContext(dbOptions))
            {
                var firstEntry = await firstContext.FileEntries.SingleAsync(entry => entry.Name == "observed.txt");
                var secondEntry = await secondContext.FileEntries.SingleAsync(entry => entry.Name == "observed.txt");
                firstEntry.ApplySourceObservation(
                    9, "text/plain", now.AddMinutes(1), firstEntry.SourceFileKey, now.AddMinutes(1), true);
                secondEntry.ApplySourceObservation(
                    10, "text/plain", now.AddMinutes(2), secondEntry.SourceFileKey, now.AddMinutes(2), true);
                await new IndexCatalogRepository(firstContext).SaveChangesAsync(CancellationToken.None);
                await Assert.ThrowsAsync<IndexCatalogConcurrencyException>(() =>
                    new IndexCatalogRepository(secondContext).SaveChangesAsync(CancellationToken.None));
            }

            await using var heldLock = await catalog.TryAcquireScanLockAsync(CancellationToken.None);
            Assert.NotNull(heldLock);
            await Assert.ThrowsAsync<IndexScanAlreadyRunningException>(() => service.RunAsync(
                new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.DryRun),
                CancellationToken.None));
        }
        finally
        {
            storageRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FailedEnumerationRetainsApplyStagingUntilRetentionCleanup()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("index_scan_failure")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var dbOptions = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(dbOptions);
        await database.Database.MigrateAsync();
        var now = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        database.Users.Add(new User(ownerId, "FAILEDINDEX", "Index", "hash", UserRole.Member, now));
        database.FileEntries.Add(root);
        await database.SaveChangesAsync();
        var observed = Enumerable.Range(0, 10)
            .Select(index => new ObservedStorageEntry(
                ownerId,
                RelativeStoragePath.Create($"{root.RelativePath}/item-{index:D2}.txt"),
                RelativeStoragePath.Create(root.RelativePath),
                FileName.Create($"item-{index:D2}.txt"),
                FileEntryType.File,
                1,
                "text/plain",
                now,
                $"key-{index}"))
            .ToArray();
        var clock = new MutableClock(now);
        var catalog = new IndexCatalogRepository(database);
        var failedService = CreateService(catalog, new FailingSnapshot(observed), clock);

        await Assert.ThrowsAsync<IndexSnapshotIncompleteException>(() => failedService.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None));

        Assert.Equal(10, await database.IndexScanItems.CountAsync());
        Assert.Equal(IndexScanStatus.Failed, (await database.IndexScanRuns.SingleAsync()).Status);

        clock.UtcNow = now.AddHours(25);
        var cleanupService = CreateService(catalog, new FailingSnapshot([], shouldFail: false), clock);
        await cleanupService.RunAsync(
            new IndexScanRequest(IndexScanTrigger.Admin, IndexScanMode.Apply),
            CancellationToken.None);
        Assert.Equal(0, await database.IndexScanItems.CountAsync());
    }

    private static IndexScanService CreateService(
        IndexCatalogRepository catalog,
        IManagedFileSystemSnapshotReader snapshot,
        ISystemClock clock) =>
        new(
            catalog,
            snapshot,
            new AvailableGuard(),
            clock,
            new KuraStorage.Application.Indexing.IndexingOptions
            {
                BatchSize = 10,
                MissingConfirmationDelayMinutes = 5,
                StagingRetentionHours = 24,
            });

    private sealed class AvailableGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FailingSnapshot(
        IReadOnlyList<ObservedStorageEntry> entries,
        bool shouldFail = true) : IManagedFileSystemSnapshotReader
    {
        public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
            StorageSnapshotContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in entries)
            {
                yield return entry;
            }

            await Task.Yield();
            if (shouldFail)
            {
                throw new IndexSnapshotIncompleteException("Expected test failure.");
            }
        }

        public Task<ObservedStorageEntry?> InspectAsync(
            RelativeStoragePath path,
            CancellationToken cancellationToken) => Task.FromResult<ObservedStorageEntry?>(null);
    }
}
