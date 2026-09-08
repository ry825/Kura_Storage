using System.Diagnostics;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace KuraStorage.IntegrationTests;

public sealed class AdminCliMediaTests
{
    [Fact]
    public async Task LowBackfill_BoundedRestartAndConcurrentRunsConvergeWithoutDuplicateJobs()
    {
        await using var postgres = CreatePostgres("admin_cli_low_backfill");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using (var database = new KuraStorageDbContext(options))
        {
            await database.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new User(Guid.NewGuid(), "BACKFILLUSER", "backfill-user", "integration-hash", UserRole.Member, now);
            var root = FileEntry.CreateRoot(user.Id, now);
            var photos = Enumerable.Range(0, 3).Select(index => FileEntry.CreateFile(
                Guid.NewGuid(), user.Id, root.Id, FileName.Create($"photo-{index}.jpg"),
                RelativeStoragePath.Create($"users/{user.Id:N}/files/photo-{index}.jpg"), "image/jpeg", 42, now));
            database.AddRange(user, root);
            database.FileEntries.AddRange(photos);
            await database.SaveChangesAsync();
        }

        var storageRoot = CreateStorageRoot();
        try
        {
            // A first bounded run represents an interrupted deployment; the two
            // resumed invocations contend on the same advisory lock.
            var first = await RunCliAsync(postgres.GetConnectionString(), storageRoot,
                "media", "low", "backfill", "--enqueue", "--batch-size", "1", "--max-items", "1");
            Assert.Equal(0, first.ExitCode);
            var resumed = await Task.WhenAll(
                RunCliAsync(postgres.GetConnectionString(), storageRoot,
                    "media", "low", "backfill", "--enqueue", "--batch-size", "1", "--max-items", "2"),
                RunCliAsync(postgres.GetConnectionString(), storageRoot,
                    "media", "low", "backfill", "--enqueue", "--batch-size", "1", "--max-items", "2"));
            Assert.All(resumed, result => Assert.Equal(0, result.ExitCode));

            await using var verify = new KuraStorageDbContext(options);
            Assert.Equal(3, await verify.FileDerivatives.CountAsync(item => item.DerivativeType == DerivativeType.ImageLow));
            Assert.Equal(3, await verify.MediaJobs.CountAsync(item => item.JobType == DerivativeType.ImageLow));
            Assert.All(await verify.MediaJobs.ToListAsync(), job => Assert.Equal(MediaJobOrigin.Backfill, job.Origin));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LowBackfillDryRun_ReportsAggregatesWithoutChangingDatabaseOrStorage()
    {
        await using var postgres = CreatePostgres("admin_cli_low_dry_run");
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using (var database = new KuraStorageDbContext(options))
        {
            await database.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new User(Guid.NewGuid(), "CLIUSER", "cli-user", "integration-hash", UserRole.Member, now);
            var root = FileEntry.CreateRoot(user.Id, now);
            var photo = FileEntry.CreateFile(
                Guid.NewGuid(), user.Id, root.Id, FileName.Create("photo.jpg"),
                RelativeStoragePath.Create($"users/{user.Id:N}/files/photo.jpg"), "image/jpeg", 42, now);
            var ready = new FileDerivative(Guid.NewGuid(), photo.Id, 1, DerivativeType.ImageLow, 1, now);
            ready.Start(now);
            ready.MarkReady($"derivatives/{user.Id:N}/{photo.Id:N}/1/1/image-low.webp", 12, now, null);
            database.AddRange(user, root, photo, ready, new MediaJob(
                Guid.NewGuid(), ready.Id, DerivativeType.ImageLow, user.Id, now));
            await database.SaveChangesAsync();
        }

        var storageRoot = CreateStorageRoot();
        var sentinel = Path.Combine(storageRoot, "sentinel.bin");
        await File.WriteAllBytesAsync(sentinel, [1, 2, 3, 4]);
        try
        {
            var result = await RunCliAsync(postgres.GetConnectionString(), storageRoot,
                "media", "low", "backfill", "--dry-run");
            Assert.True(result.ExitCode == 0, result.Error);
            var output = result.Output;
            Assert.Contains("photos=1", output);
            Assert.Contains("ready=1", output);
            Assert.Contains("storage_protected_free_bytes=1", output);
            Assert.DoesNotContain("photo.jpg", output, StringComparison.Ordinal);
            Assert.DoesNotContain("cli-user", output, StringComparison.Ordinal);
            Assert.DoesNotContain(storageRoot, output, StringComparison.Ordinal);
        }
        finally
        {
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(sentinel));
            await using var verify = new KuraStorageDbContext(options);
            Assert.Equal(1, await verify.FileDerivatives.CountAsync());
            Assert.Equal(1, await verify.MediaJobs.CountAsync());
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MediumPurge_IsBoundedIdempotentAndLeavesPersistentDerivativesUntouched()
    {
        await using var postgres = CreatePostgres("admin_cli_medium_purge");
        await postgres.StartAsync();
        var options = Options(postgres.GetConnectionString());
        Guid mediumId;
        await using (var database = new KuraStorageDbContext(options))
        {
            await database.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new User(Guid.NewGuid(), "PURGEUSER", "purge-user", "integration-hash", UserRole.Member, now);
            var root = FileEntry.CreateRoot(user.Id, now);
            var photo = FileEntry.CreateFile(
                Guid.NewGuid(), user.Id, root.Id, FileName.Create("photo.jpg"),
                RelativeStoragePath.Create($"users/{user.Id:N}/files/photo.jpg"), "image/jpeg", 42, now);
            var medium = ReadyDerivative(photo, DerivativeType.ImageMedium, "derivatives/medium.webp", now);
            var low = ReadyDerivative(photo, DerivativeType.ImageLow, "derivatives/low.webp", now);
            var thumbnail = ReadyDerivative(photo, DerivativeType.Thumbnail, "derivatives/thumbnail.webp", now);
            mediumId = medium.Id;
            database.AddRange(user, root, photo, medium, low, thumbnail,
                new MediaJob(Guid.NewGuid(), medium.Id, DerivativeType.ImageMedium, user.Id, now));
            await database.SaveChangesAsync();
        }

        var storageRoot = CreateStorageRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(storageRoot, "derivatives"));
            await File.WriteAllBytesAsync(Path.Combine(storageRoot, "derivatives/medium.webp"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(storageRoot, "derivatives/low.webp"), [2]);
            await File.WriteAllBytesAsync(Path.Combine(storageRoot, "derivatives/thumbnail.webp"), [3]);

            var dryRun = await RunCliAsync(postgres.GetConnectionString(), storageRoot,
                "media", "medium", "purge", "--dry-run");
            Assert.True(dryRun.ExitCode == 0, dryRun.Error);
            Assert.Contains("medium_count=1", dryRun.Output);
            Assert.True(File.Exists(Path.Combine(storageRoot, "derivatives/medium.webp")));

            var applied = await RunCliAsync(postgres.GetConnectionString(), storageRoot,
                "media", "medium", "purge", "--apply", "--batch-size", "1", "--max-items", "1");
            Assert.True(applied.ExitCode == 0, applied.Error);
            Assert.Contains("deleted=1", applied.Output);
            var repeated = await RunCliAsync(postgres.GetConnectionString(), storageRoot,
                "media", "medium", "purge", "--apply", "--batch-size", "1", "--max-items", "1");
            Assert.True(repeated.ExitCode == 0, repeated.Error);
            Assert.Contains("deleted=0", repeated.Output);

            await using var verify = new KuraStorageDbContext(options);
            Assert.False(await verify.FileDerivatives.AnyAsync(item => item.Id == mediumId));
            Assert.Equal(2, await verify.FileDerivatives.CountAsync());
            Assert.False(File.Exists(Path.Combine(storageRoot, "derivatives/medium.webp")));
            Assert.True(File.Exists(Path.Combine(storageRoot, "derivatives/low.webp")));
            Assert.True(File.Exists(Path.Combine(storageRoot, "derivatives/thumbnail.webp")));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private static FileDerivative ReadyDerivative(
        FileEntry photo, DerivativeType type, string path, DateTimeOffset now)
    {
        var derivative = new FileDerivative(Guid.NewGuid(), photo.Id, photo.FileVersion, type, 1, now);
        derivative.Start(now);
        derivative.MarkReady(path, 1, now, type == DerivativeType.ImageMedium ? now.AddDays(1) : null);
        return derivative;
    }

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();

    private static DbContextOptions<KuraStorageDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<KuraStorageDbContext>().UseNpgsql(connectionString).Options;

    private static string CreateStorageRoot()
    {
        // The CLI must exercise the production StorageGuard.  GitHub-hosted
        // Linux runners mount /tmp on the root filesystem, which the guard
        // intentionally rejects; /dev/shm is a separate writable tmpfs mount.
        var parent = OperatingSystem.IsLinux() && Directory.Exists("/dev/shm")
            ? "/dev/shm"
            : Path.GetTempPath();
        var root = Path.Combine(parent, $"kurastorage-admin-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, ".storage-identity"),
            "{\"storageId\":\"admin-cli-test\",\"formatVersion\":1}");
        return root;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        string connectionString,
        string storageRoot,
        params string[] arguments)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Unable to determine the test build configuration.");
        var cliAssembly = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            $"../../../../../src/KuraStorage.AdminCli/bin/{configuration}/net10.0/KuraStorage.AdminCli.dll"));
        Assert.True(File.Exists(cliAssembly), $"Admin CLI assembly was not built: {cliAssembly}");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(cliAssembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["KURASTORAGE_Database__ConnectionString"] = connectionString;
        start.Environment["KURASTORAGE_Storage__RootPath"] = storageRoot;
        start.Environment["KURASTORAGE_Storage__StorageId"] = "admin-cli-test";
        start.Environment["KURASTORAGE_Storage__MinimumFreeBytes"] = "1";
        start.Environment["KURASTORAGE_Storage__CapacityWarningFreeBytes"] = "1";
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
    }
}
