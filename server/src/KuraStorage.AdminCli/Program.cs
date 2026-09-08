using KuraStorage.Application.Identity;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Application.Activity;
using KuraStorage.Application.Files;
using KuraStorage.Application.Media;
using KuraStorage.Application.Maintenance;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Indexing;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

const string help = """
KuraStorage administration CLI

Usage:
  kurastorage-admin database migrate
  kurastorage-admin user create <username> <display-name> <ADMIN|MEMBER> --password-stdin
  kurastorage-admin user unlock <username>
  kurastorage-admin device list <user-id>
  kurastorage-admin device revoke <user-id> <device-id>
  kurastorage-admin index rescan [--dry-run]
  kurastorage-admin media low status
  kurastorage-admin media low backfill --dry-run|--enqueue [--batch-size <1-1000>] [--max-items <n>]
  kurastorage-admin media low retry-failed --dry-run|--enqueue [--error-code <code>]
      [--batch-size <1-1000>] [--max-items <n>]
  kurastorage-admin media medium purge --dry-run
  kurastorage-admin media medium purge --apply [--batch-size <1-1000>] [--max-items <n>]
  kurastorage-admin activity search [--actor-user <id-or-username>] [--owner-user <id-or-username>]
      [--type <UPLOAD|MOVE|EDIT|SHARE|DELETE>] [--from <UTC>] [--to <UTC>]
      [--file-id <id>] [--limit <1-1000>] [--cursor <token>] [--json]
  kurastorage-admin help

Passwords are accepted only from standard input and are never accepted as command arguments.
""";

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    Console.WriteLine(help);
    return 0;
}

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddEnvironmentVariables("KURASTORAGE_");
var secretsDirectory = Environment.GetEnvironmentVariable("KURASTORAGE_SECRETS_DIR");
if (!string.IsNullOrWhiteSpace(secretsDirectory))
{
    builder.Configuration.AddKeyPerFile(secretsDirectory, optional: false);
}

builder.Services.AddKuraStorageInfrastructure(builder.Configuration);
using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return args switch
    {
        ["database", "migrate"] => await MigrateAsync(scope.ServiceProvider),
        ["user", "create", var username, var displayName, var role, "--password-stdin"] =>
            await CreateUserAsync(scope.ServiceProvider, username, displayName, role),
        ["user", "unlock", var username] => await UnlockUserAsync(scope.ServiceProvider, username),
        ["device", "list", var userId] => await ListDevicesAsync(scope.ServiceProvider, userId),
        ["device", "revoke", var userId, var deviceId] =>
            await RevokeDeviceAsync(scope.ServiceProvider, userId, deviceId),
        ["index", "rescan"] => await RescanIndexAsync(scope.ServiceProvider, dryRun: false),
        ["index", "rescan", "--dry-run"] => await RescanIndexAsync(scope.ServiceProvider, dryRun: true),
        ["media", "low", "status"] => await ShowLowStatusAsync(scope.ServiceProvider, cancellation.Token),
        ["media", "low", "backfill", .. var mediaArgs] =>
            await BackfillLowAsync(scope.ServiceProvider, mediaArgs, retryFailed: false, cancellation.Token),
        ["media", "low", "retry-failed", .. var mediaArgs] =>
            await BackfillLowAsync(scope.ServiceProvider, mediaArgs, retryFailed: true, cancellation.Token),
        ["media", "medium", "purge", .. var purgeArgs] =>
            await PurgeMediumAsync(scope.ServiceProvider, purgeArgs, cancellation.Token),
        ["activity", "search", .. var activityArgs] =>
            await SearchActivitiesAsync(scope.ServiceProvider, activityArgs, cancellation.Token),
        _ => UnknownCommand(),
    };
}
catch (IndexScanAlreadyRunningException)
{
    Console.Error.WriteLine("Index scan failed: INDEX_SCAN_ALREADY_RUNNING");
    return 3;
}
catch (IndexStorageUnavailableException)
{
    Console.Error.WriteLine("Index scan failed: STORAGE_UNAVAILABLE");
    return 4;
}
catch (IndexSnapshotIncompleteException)
{
    Console.Error.WriteLine("Index scan failed: INDEX_SCAN_FAILED");
    return 1;
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Activity search cancelled.");
    return 130;
}

static async Task<int> MigrateAsync(IServiceProvider services)
{
    await services.GetRequiredService<KuraStorageDbContext>().Database.MigrateAsync();
    Console.WriteLine("Database migrations applied.");
    return 0;
}

static async Task<int> CreateUserAsync(
    IServiceProvider services,
    string username,
    string displayName,
    string roleValue)
{
    if (!Enum.TryParse<UserRole>(roleValue, ignoreCase: true, out var role))
    {
        throw new ArgumentException("Role must be ADMIN or MEMBER.");
    }

    var password = await Console.In.ReadToEndAsync();
    password = password.TrimEnd('\r', '\n');
    if (password.Length == 0)
    {
        throw new ArgumentException("A password must be supplied through standard input.");
    }

    var result = await services.GetRequiredService<IdentityService>().CreateUserAsync(
        username,
        displayName,
        password,
        role,
        CancellationToken.None,
        AuditActorType.AdminCli,
        Environment.UserName);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"User creation failed: {result.Failure!.Code}");
        return 1;
    }

    Console.WriteLine($"User created: {result.Value}");
    return 0;
}

static async Task<int> UnlockUserAsync(IServiceProvider services, string username)
{
    var unlocked = await services.GetRequiredService<IdentityService>().UnlockUserAsync(
        username,
        CancellationToken.None,
        AuditActorType.AdminCli,
        Environment.UserName);
    Console.WriteLine(unlocked ? "User unlocked." : "User was not found.");
    return unlocked ? 0 : 1;
}

static async Task<int> ListDevicesAsync(IServiceProvider services, string userIdValue)
{
    var userId = Guid.Parse(userIdValue);
    var devices = await services.GetRequiredService<IdentityService>().ListDevicesAsync(
        userId,
        CancellationToken.None);
    foreach (var device in devices)
    {
        Console.WriteLine(
            $"{device.Id}\t{device.Status.ToString().ToUpperInvariant()}\t{device.DeviceName}\t{device.RegisteredAt:O}");
    }

    return 0;
}

static async Task<int> RevokeDeviceAsync(
    IServiceProvider services,
    string userIdValue,
    string deviceIdValue)
{
    var revoked = await services.GetRequiredService<IdentityService>().RevokeDeviceAsync(
        Guid.Parse(userIdValue),
        Guid.Parse(deviceIdValue),
        requestId: null,
        CancellationToken.None,
        AuditActorType.AdminCli,
        Environment.UserName);
    Console.WriteLine(revoked ? "Device revoked." : "Device was not found.");
    return revoked ? 0 : 1;
}

static async Task<int> RescanIndexAsync(IServiceProvider services, bool dryRun)
{
    var summary = await services.GetRequiredService<IIndexScanService>().RunAsync(
        new IndexScanRequest(IndexScanTrigger.Admin, dryRun ? IndexScanMode.DryRun : IndexScanMode.Apply),
        CancellationToken.None);
    Console.WriteLine($"run_id={summary.RunId}");
    Console.WriteLine($"status={StatusText(summary.Status)}");
    Console.WriteLine($"enumerated={summary.EnumeratedCount}");
    Console.WriteLine($"added={summary.AddedCount}");
    Console.WriteLine($"updated={summary.UpdatedCount}");
    Console.WriteLine($"moved={summary.MovedCount}");
    Console.WriteLine($"candidate={summary.CandidateCount}");
    Console.WriteLine($"missing={summary.MissingCount}");
    Console.WriteLine($"revived={summary.RevivedCount}");
    Console.WriteLine($"isolated={summary.IsolatedCount}");
    Console.WriteLine($"errors={summary.ErrorCount}");
    return summary.Status == IndexScanStatus.Completed ? 0 : 1;
}

static async Task<int> ShowLowStatusAsync(IServiceProvider services, CancellationToken cancellationToken)
{
    var database = services.GetRequiredService<KuraStorageDbContext>();
    var profile = services.GetRequiredService<MediaRuntimeOptions>().ImageProfileVersion;
    string[] supportedPhotoMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif", "image/heic", "image/heif"];
    var photos = database.FileEntries.AsNoTracking().Where(file =>
        file.EntryType == FileEntryType.File && file.Status == FileEntryStatus.Active &&
        file.MimeType != null && supportedPhotoMimeTypes.Contains(file.MimeType));
    var derivatives = database.FileDerivatives.AsNoTracking().Where(item =>
        item.DerivativeType == DerivativeType.ImageLow && item.ProfileVersion == profile);
    var current =
        from file in photos
        join derivative in derivatives
            on new { FileId = file.Id, Version = file.FileVersion }
            equals new { FileId = derivative.SourceFileId, Version = derivative.SourceVersion }
            into matches
        from derivative in matches.DefaultIfEmpty()
        select new { OriginalBytes = file.Size, Derivative = derivative };
    var rows = await current.ToListAsync(cancellationToken);
    var readySizes = rows.Where(row => row.Derivative?.Status == DerivativeStatus.Ready)
        .Select(row => row.Derivative!.Size).Order().ToArray();
    var originalBytes = rows.Aggregate(0L, (total, row) => SaturatingAdd(total, row.OriginalBytes));
    var lowBytes = readySizes.Aggregate(0L, SaturatingAdd);
    var average = readySizes.Length == 0 ? 0 : (long)readySizes.Average();
    var estimated = SaturatingMultiply(rows.Count(row => row.Derivative is null), average);
    Console.WriteLine($"profile={profile}");
    Console.WriteLine($"photos={rows.Count}");
    foreach (var status in Enum.GetValues<DerivativeStatus>())
    {
        Console.WriteLine($"{status.ToString().ToLowerInvariant()}={rows.Count(row => row.Derivative?.Status == status)}");
    }
    Console.WriteLine($"missing={rows.Count(row => row.Derivative is null)}");
    var duplicateGroupCounts = await derivatives
        .GroupBy(item => new { item.SourceFileId, item.SourceVersion, item.ProfileVersion })
        .Where(group => group.Count() > 1)
        .Select(group => group.Count())
        .ToListAsync(cancellationToken);
    var duplicateCount = duplicateGroupCounts.Aggregate(
        0L,
        (total, count) => SaturatingAdd(total, count - 1));
    var orphanCount = await database.FileDerivatives.AsNoTracking().LongCountAsync(item =>
        item.DerivativeType == DerivativeType.ImageLow &&
        (item.ProfileVersion != profile || !database.FileEntries.Any(file =>
            file.Id == item.SourceFileId && file.Status == FileEntryStatus.Active &&
            file.FileVersion == item.SourceVersion)), cancellationToken);
    Console.WriteLine($"duplicates={duplicateCount}");
    Console.WriteLine($"orphan_or_old={orphanCount}");
    Console.WriteLine($"low_bytes={lowBytes}");
    Console.WriteLine($"average_bytes={average}");
    Console.WriteLine($"p50_bytes={Percentile(readySizes, 0.50)}");
    Console.WriteLine($"p95_bytes={Percentile(readySizes, 0.95)}");
    Console.WriteLine($"original_bytes={originalBytes}");
    Console.WriteLine($"low_to_original_ratio={(originalBytes == 0 ? 0 : (double)lowBytes / originalBytes):F4}");
    Console.WriteLine($"estimated_additional_bytes={estimated}");
    var capacity = await services.GetRequiredService<StorageCapacityService>().GetAsync(cancellationToken);
    Console.WriteLine($"storage={capacity.Storage}");
    Console.WriteLine($"storage_total_bytes={capacity.TotalBytes?.ToString() ?? "unavailable"}");
    Console.WriteLine($"storage_available_bytes={capacity.AvailableBytes?.ToString() ?? "unavailable"}");
    var storageOptions = services.GetRequiredService<IOptions<StorageOptions>>().Value;
    Console.WriteLine($"storage_protected_free_bytes={storageOptions.MinimumFreeBytes}");
    Console.WriteLine($"storage_writable={(capacity.AvailableBytes is long available && available >= storageOptions.MinimumFreeBytes ? "true" : "false")}");
    return 0;
}

static async Task<int> BackfillLowAsync(
    IServiceProvider services,
    IReadOnlyList<string> args,
    bool retryFailed,
    CancellationToken cancellationToken)
{
    var enqueue = args.Contains("--enqueue", StringComparer.Ordinal);
    var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
    if (enqueue == dryRun) throw new ArgumentException("Specify exactly one of --dry-run or --enqueue.");
    string? retryErrorCode = null;
    var boundedArgs = new List<string>();
    for (var index = 0; index < args.Count; index++)
    {
        if (args[index] is "--enqueue" or "--dry-run") continue;
        if (args[index] == "--error-code")
        {
            if (!retryFailed || index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException("--error-code requires an approved retryable media error code.");
            retryErrorCode = args[++index];
            if (!IsRetryableMediaError(retryErrorCode))
                throw new ArgumentException("The requested media error code is not retryable.");
            continue;
        }
        if (index + 1 >= args.Count) throw new ArgumentException("A media option value is missing.");
        boundedArgs.Add(args[index]);
        boundedArgs.Add(args[++index]);
    }
    if (dryRun) return await ShowLowStatusAsync(services, cancellationToken);

    var (batchSize, maxItems) = ParseBoundedOptions(boundedArgs);
    var database = services.GetRequiredService<KuraStorageDbContext>();
    var provisioner = services.GetRequiredService<IRequiredPhotoDerivativeProvisioner>();
    var profile = services.GetRequiredService<MediaRuntimeOptions>().ImageProfileVersion;
    string[] supportedPhotoMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif", "image/heic", "image/heif"];
    var processed = 0;
    while (processed < maxItems)
    {
        if (await services.GetRequiredService<IStorageGuard>().InspectAsync(
                StorageIntent.CreateOrUpdate,
                cancellationToken) != StorageStatus.Available)
        {
            Console.Error.WriteLine("Backfill stopped: STORAGE_NOT_WRITABLE");
            return 4;
        }
        var capacity = await services.GetRequiredService<StorageCapacityService>().GetAsync(cancellationToken);
        if (capacity.Storage != "AVAILABLE")
        {
            Console.Error.WriteLine("Backfill stopped: STORAGE_UNAVAILABLE");
            return 4;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(5424388352608932947);",
            cancellationToken);
        var take = Math.Min(batchSize, maxItems - processed);
        var photos = database.FileEntries
            .Where(file => file.EntryType == FileEntryType.File &&
                file.Status == FileEntryStatus.Active && file.MimeType != null &&
                supportedPhotoMimeTypes.Contains(file.MimeType))
            .Where(file => retryFailed
                ? database.FileDerivatives.Any(item => item.SourceFileId == file.Id &&
                    item.SourceVersion == file.FileVersion && item.DerivativeType == DerivativeType.ImageLow &&
                    item.ProfileVersion == profile && item.Status == DerivativeStatus.Failed &&
                    (item.ErrorCode == FileErrorCodes.StorageUnavailable ||
                     item.ErrorCode == MediaErrorCodes.ToolUnavailable ||
                     item.ErrorCode == MediaErrorCodes.WorkerUnavailable ||
                     item.ErrorCode == MediaErrorCodes.CompletionUnknown ||
                     item.ErrorCode == "MEDIA_WORKER_STOPPED" || item.ErrorCode == "MEDIA_WORKER_STALE") &&
                    (retryErrorCode == null || item.ErrorCode == retryErrorCode))
                : !database.FileDerivatives.Any(item => item.SourceFileId == file.Id &&
                    item.SourceVersion == file.FileVersion && item.DerivativeType == DerivativeType.ImageLow &&
                    item.ProfileVersion == profile));
        var files = await photos.OrderBy(file => file.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (files.Count == 0) break;

        foreach (var file in files)
        {
            if (retryFailed)
            {
                var failed = await database.FileDerivatives.SingleOrDefaultAsync(item =>
                    item.SourceFileId == file.Id && item.SourceVersion == file.FileVersion &&
                    item.DerivativeType == DerivativeType.ImageLow && item.ProfileVersion == profile &&
                    item.Status == DerivativeStatus.Failed, cancellationToken);
                if (failed is not null && IsRetryableMediaError(failed.ErrorCode) &&
                    (retryErrorCode == null || failed.ErrorCode == retryErrorCode))
                {
                    failed.Retry(DateTimeOffset.UtcNow);
                    database.MediaJobs.Add(new MediaJob(
                        Guid.NewGuid(), failed.Id, DerivativeType.ImageLow, file.OwnerUserId,
                        DateTimeOffset.UtcNow, MediaJobOrigin.Backfill));
                }
            }
            else
            {
                _ = await provisioner.EnsureLowAsync(
                    file, MediaJobOrigin.Backfill, DateTimeOffset.UtcNow, cancellationToken);
            }
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            continue;
        }
        processed += files.Count;
        database.ChangeTracker.Clear();
        Console.WriteLine($"processed={processed}");
    }

    Console.WriteLine($"completed={processed}");
    return 0;
}

static async Task<int> PurgeMediumAsync(
    IServiceProvider services,
    IReadOnlyList<string> args,
    CancellationToken cancellationToken)
{
    var apply = args.Contains("--apply", StringComparer.Ordinal);
    var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
    if (apply == dryRun) throw new ArgumentException("Specify exactly one of --dry-run or --apply.");
    var optionArgs = args.Where(arg => arg is not "--apply" and not "--dry-run").ToArray();
    var (batchSize, maxItems) = ParseBoundedOptions(optionArgs);
    var database = services.GetRequiredService<KuraStorageDbContext>();
    var query = database.FileDerivatives.Where(item => item.DerivativeType == DerivativeType.ImageMedium);
    var total = await query.CountAsync(cancellationToken);
    var bytes = await query.Where(item => item.Status == DerivativeStatus.Ready).SumAsync(item => (long?)item.Size, cancellationToken) ?? 0;
    Console.WriteLine($"medium_count={total}");
    Console.WriteLine($"medium_bytes={bytes}");
    if (!apply) return 0;

    var store = services.GetRequiredService<IDerivativeStore>();
    var deleted = 0;
    while (deleted < maxItems)
    {
        var take = Math.Min(batchSize, maxItems - deleted);
        var now = DateTimeOffset.UtcNow;
        var candidates = await query
            .Where(item => !database.DerivativeLeases.Any(lease =>
                lease.DerivativeId == item.Id && lease.ExpiresAt > now))
            .OrderBy(item => item.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) break;
        var batchDeleted = 0;
        foreach (var derivative in candidates)
        {
            var leased = await database.DerivativeLeases.AnyAsync(
                lease => lease.DerivativeId == derivative.Id && lease.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
            if (leased) continue;
            if (!string.IsNullOrWhiteSpace(derivative.RelativePath))
            {
                await store.DeleteIfExistsAsync(RelativeStoragePath.Create(derivative.RelativePath), cancellationToken);
            }
            database.FileDerivatives.Remove(derivative);
            deleted++;
            batchDeleted++;
        }
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
        if (batchDeleted == 0) break;
    }
    Console.WriteLine($"deleted={deleted}");
    return 0;
}

static (int BatchSize, int MaxItems) ParseBoundedOptions(IReadOnlyList<string> args)
{
    var batchSize = 100;
    var maxItems = int.MaxValue;
    for (var index = 0; index < args.Count; index += 2)
    {
        if (index + 1 >= args.Count || !int.TryParse(args[index + 1], out var value) || value < 1)
            throw new ArgumentException("Media batch options require a positive integer value.");
        switch (args[index])
        {
            case "--batch-size" when value <= 1000: batchSize = value; break;
            case "--max-items": maxItems = value; break;
            default: throw new ArgumentException("Unknown or out-of-range media option.");
        }
    }
    return (batchSize, maxItems);
}

static long Percentile(IReadOnlyList<long> sorted, double percentile) =>
    sorted.Count == 0 ? 0 : sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];

static long SaturatingAdd(long left, long right) =>
    left > long.MaxValue - right ? long.MaxValue : left + right;

static long SaturatingMultiply(long left, long right) =>
    left == 0 || right == 0 ? 0 : left > long.MaxValue / right ? long.MaxValue : left * right;

static bool IsRetryableMediaError(string? errorCode) => errorCode is
    FileErrorCodes.StorageUnavailable or MediaErrorCodes.ToolUnavailable or
    MediaErrorCodes.WorkerUnavailable or MediaErrorCodes.CompletionUnknown or
    "MEDIA_WORKER_STOPPED" or "MEDIA_WORKER_STALE";

static async Task<int> SearchActivitiesAsync(
    IServiceProvider services,
    IReadOnlyList<string> args,
    CancellationToken cancellationToken)
{
    if (!AdminActivityCommandParser.TryParse(args, out var command))
    {
        Console.Error.WriteLine("Activity search failed: INVALID_ACTIVITY_REQUEST");
        return 2;
    }

    var result = await services.GetRequiredService<AdminActivityService>().SearchAsync(
        command!.Request,
        Environment.UserName,
        cancellationToken);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"Activity search failed: {result.Failure!.Code}");
        return 2;
    }

    try
    {
        AdminActivityOutput.Write(result.Value!, command.Json, Console.Out, cancellationToken);
        return 0;
    }
    catch (IOException)
    {
        return 1;
    }
}

static string StatusText(IndexScanStatus status) => status switch
{
    IndexScanStatus.CompletedWithWarnings => "COMPLETED_WITH_WARNINGS",
    _ => status.ToString().ToUpperInvariant(),
};

static int UnknownCommand()
{
    Console.Error.WriteLine("Unknown or incomplete command.");
    Console.Error.WriteLine(help);
    return 2;
}
