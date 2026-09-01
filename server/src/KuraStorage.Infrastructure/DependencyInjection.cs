using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Identity;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Application.Media;
using KuraStorage.Application.Sharing;
using KuraStorage.Application.Search;
using KuraStorage.Application.Recent;
using KuraStorage.Application.Organization;
using KuraStorage.Application.Indexing;
using KuraStorage.Application.Transfers;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Identity;
using KuraStorage.Infrastructure.Indexing;
using KuraStorage.Infrastructure.Media;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Persistence.Queries;
using KuraStorage.Infrastructure.Storage;
using KuraStorage.Infrastructure.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKuraStorageInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool addFileRecoveryHostedService = true)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsPostgreSqlConnection(options.ConnectionString), "A valid PostgreSQL connection string is required.")
            .ValidateOnStart();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => Path.IsPathFullyQualified(options.RootPath), "Storage:RootPath must be absolute.")
            .Validate(
                options => options.CapacityWarningFreeBytes >= options.MinimumFreeBytes,
                "Storage:CapacityWarningFreeBytes must be at least Storage:MinimumFreeBytes.")
            .ValidateOnStart();
        services.AddOptions<TrashPurgeOptions>()
            .Bind(configuration.GetSection(TrashPurgeOptions.SectionName))
            .Validate(
                options => options.RetentionDays >= TrashPurgeOptions.MinimumRetentionDays,
                "TrashPurge:RetentionDays must be at least 30.")
            .Validate(options => options.IntervalHours is >= 1 and <= 168, "TrashPurge:IntervalHours must be between 1 and 168.")
            .Validate(options => options.BatchSize is >= 1 and <= 500, "TrashPurge:BatchSize must be between 1 and 500.")
            .Validate(options => options.RetryDelayMinutes is >= 1 and <= 1440, "TrashPurge:RetryDelayMinutes must be between 1 and 1440.")
            .ValidateOnStart();
        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.JwtSigningKeyFile), "Authentication:JwtSigningKeyFile must exist.")
            .ValidateOnStart();
        services.AddOptions<UploadSessionOptions>()
            .Bind(configuration.GetSection(UploadSessionOptions.SectionName))
            .Validate(
                options => options.PreferredChunkBytes is >= UploadSessionOptions.MinimumChunkBytes and <= 67_108_864 &&
                    options.MaximumChunkBytes is >= UploadSessionOptions.MinimumChunkBytes and <= 67_108_864 &&
                    options.PreferredChunkBytes <= options.MaximumChunkBytes,
                "UploadSession chunk sizes are invalid.")
            .Validate(options => options.MaximumFileBytes > 0, "UploadSession:MaximumFileBytes must be positive.")
            .Validate(
                options => options.IdleExpirationHours is >= 1 and <= 168 &&
                    options.AbsoluteExpirationHours >= options.IdleExpirationHours &&
                    options.AbsoluteExpirationHours <= 720,
                "UploadSession expiration settings are invalid.")
            .Validate(
                options => options.CleanupIntervalMinutes is >= 1 and <= 1440 &&
                    options.CleanupBatchSize is >= 1 and <= 500,
                "UploadSession cleanup settings are invalid.")
            .Validate(
                options => options.MaximumActiveSessionsPerUser is >= 1 and <= 100 &&
                    options.MaximumActiveSessionsPerDevice is >= 1 and <= 50 &&
                    options.MaximumActiveSessionsPerDevice <= options.MaximumActiveSessionsPerUser &&
                    options.MaximumConcurrentChunkWrites is >= 1 and <= 16 &&
                    options.OverloadRetryAfterSeconds is >= 1 and <= 300,
                "UploadSession resource limits are invalid.")
            .ValidateOnStart();
        services.AddOptions<Configuration.IndexingOptions>()
            .Bind(configuration.GetSection(Configuration.IndexingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MediaOptions>()
            .Bind(configuration.GetSection(MediaOptions.SectionName))
            .Validate(
                options => IsSafeMediaRoot(options.DerivativeRoot) && IsSafeMediaRoot(options.TemporaryRoot) &&
                    !string.Equals(options.DerivativeRoot, options.TemporaryRoot, StringComparison.Ordinal),
                "Media roots must be distinct safe top-level relative segments.")
            .Validate(
                options => options.ImageWaitMilliseconds is >= 1 and <= 60_000 &&
                    options.JobPollMilliseconds is >= 1 && options.JobPollMilliseconds <= options.ImageWaitMilliseconds,
                "Media wait and polling settings are invalid.")
            .Validate(
                options => options.ThumbnailProfileVersion > 0 && options.ImageProfileVersion > 0 && options.VideoProfileVersion > 0 &&
                    options.ThumbnailMaxDimension is >= 16 and <= 8192 && options.ThumbnailWebpQuality is >= 1 and <= 100,
                "Media profile settings are invalid.")
            .Validate(
                options => options.JobHeartbeatSeconds > 0 && options.StaleJobSeconds > options.JobHeartbeatSeconds &&
                    options.MaximumAttempts is >= 1 and <= 10,
                "Media job recovery settings are invalid.")
            .Validate(
                options => options.GenerationLeaseSeconds > options.JobHeartbeatSeconds &&
                    options.DeliveryLeaseSeconds > options.DeliveryLeaseRenewalSeconds &&
                    options.DeliveryLeaseRenewalSeconds > 0,
                "Media lease settings are invalid.")
            .Validate(
                options => options.CacheTtlHours > 0 && options.CacheHighWatermarkBytes > 0 &&
                    options.CacheLowWatermarkBytes > 0 && options.CacheLowWatermarkBytes < options.CacheHighWatermarkBytes &&
                    options.CleanupIntervalMinutes > 0 && options.CleanupBatchSize is >= 1 and <= 500 &&
                    options.TerminalJobRetentionDays > 0,
                "Media cleanup settings are invalid.")
            .Validate(
                options => options.MaximumConcurrentMediaJobs == 1 && options.MaximumConcurrentVideoJobs == 1,
                "Initial media and video concurrency must both be one.")
            .Validate(
                options => Path.IsPathFullyQualified(options.VipsPath) &&
                    Path.IsPathFullyQualified(options.FfmpegPath) &&
                    Path.IsPathFullyQualified(options.FfprobePath) &&
                    Path.IsPathFullyQualified(options.PdftoppmPath),
                "Media tool paths must be absolute.")
            .ValidateOnStart();

        services.AddDbContext<KuraStorageDbContext>(
            (serviceProvider, options) =>
                options.UseNpgsql(
                    serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFileVersionRepository, FileVersionRepository>();
        services.AddScoped<IUploadSessionRepository, UploadSessionRepository>();
        services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        services.AddScoped<ISearchRepository, PostgreSqlSearchRepository>();
        services.AddScoped<IRecentFileRepository, PostgreSqlRecentFileRepository>();
        services.AddScoped<IOrganizationRepository, PostgreSqlOrganizationRepository>();
        services.AddScoped<IShareRepository, ShareRepository>();
        services.AddScoped<IMediaJobQueue, PostgreSqlMediaJobQueue>();
        services.AddScoped<IMediaRepository, PostgreSqlMediaRepository>();
        services.AddScoped<IMediaCleanupRepository, PostgreSqlMediaCleanupRepository>();
        services.AddSingleton<IMediaHeartbeat, PostgreSqlMediaHeartbeat>();
        services.AddSingleton<IMediaProcessRunner, MediaProcessRunner>();
        services.AddSingleton<IMediaWaiter, SystemMediaWaiter>();
        services.AddSingleton<IMediaGenerator, ExternalMediaGenerator>();
        services.AddScoped<SharingDeletionParticipant>();
        services.AddScoped<MediaDeletionParticipant>();
        services.AddScoped<FileVersionDeletionParticipant>();
        services.AddScoped<IPermanentDeleteParticipant>(
            serviceProvider => serviceProvider.GetRequiredService<SharingDeletionParticipant>());
        services.AddScoped<IPermanentDeleteParticipant>(
            serviceProvider => serviceProvider.GetRequiredService<MediaDeletionParticipant>());
        services.AddScoped<IPermanentDeleteParticipant>(
            serviceProvider => serviceProvider.GetRequiredService<FileVersionDeletionParticipant>());
        services.AddScoped<IFileIndexDeletionParticipant>(
            serviceProvider => serviceProvider.GetRequiredService<SharingDeletionParticipant>());
        services.AddScoped<IFileIndexDeletionParticipant>(
            serviceProvider => serviceProvider.GetRequiredService<MediaDeletionParticipant>());
        services.AddScoped<IFileIndexDeletionParticipant>(
            serviceProvider => serviceProvider.GetRequiredService<FileVersionDeletionParticipant>());
        services.AddScoped<IIndexCatalogRepository, IndexCatalogRepository>();
        services.AddScoped<IIndexScanService, IndexScanService>();
        services.AddScoped<IIndexEventService, IndexEventService>();
        services.AddSingleton<IIndexScanObserver, IndexScanLogObserver>();
        services.AddScoped<IdentityService>();
        services.AddScoped<FileService>();
        services.AddScoped<MissingEntryService>();
        services.AddScoped<TrashPurgeService>();
        services.AddScoped<TrashPurgeRunner>();
        services.AddScoped<ITrashPurgeRunner>(serviceProvider => serviceProvider.GetRequiredService<TrashPurgeRunner>());
        services.AddScoped(
            serviceProvider => new AdminStorageService(
                serviceProvider.GetRequiredService<IFileRepository>(),
                serviceProvider.GetRequiredService<IFileStore>(),
                serviceProvider.GetRequiredService<IStorageGuard>(),
                serviceProvider.GetRequiredService<ISystemClock>(),
                serviceProvider.GetRequiredService<TrashPurgeOptions>(),
                serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.CapacityWarningFreeBytes));
        services.AddSingleton(
            serviceProvider => serviceProvider.GetRequiredService<IOptions<TrashPurgeOptions>>().Value);
        services.AddSingleton(
            serviceProvider =>
            {
                var configured = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                return new MediaRuntimeOptions
                {
                    ImageWaitMilliseconds = configured.ImageWaitMilliseconds,
                    JobPollMilliseconds = configured.JobPollMilliseconds,
                    ThumbnailProfileVersion = configured.ThumbnailProfileVersion,
                    ImageProfileVersion = configured.ImageProfileVersion,
                    VideoProfileVersion = configured.VideoProfileVersion,
                    DeliveryLeaseSeconds = configured.DeliveryLeaseSeconds,
                    DeliveryLeaseRenewalSeconds = configured.DeliveryLeaseRenewalSeconds,
                    GenerationLeaseSeconds = configured.GenerationLeaseSeconds,
                    JobHeartbeatSeconds = configured.JobHeartbeatSeconds,
                    CacheTtlHours = configured.CacheTtlHours,
                };
            });
        services.AddSingleton(
            serviceProvider =>
            {
                var configured = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                return new MediaCleanupOptions
                {
                    IntervalMinutes = configured.CleanupIntervalMinutes,
                    FailureBackoffMinutes = Math.Min(5, configured.CleanupIntervalMinutes),
                    BatchSize = configured.CleanupBatchSize,
                    CacheHighWatermarkBytes = configured.CacheHighWatermarkBytes,
                    CacheLowWatermarkBytes = configured.CacheLowWatermarkBytes,
                    TerminalJobRetentionDays = configured.TerminalJobRetentionDays,
                };
            });
        services.AddScoped<FileOperationRecoveryService>();
        services.AddScoped<FileVersionService>();
        services.AddScoped<TextFileService>();
        services.AddScoped<UploadSessionService>();
        services.AddScoped<UploadSessionRecoveryService>();
        services.AddScoped<UploadSessionCleanupService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<SharingService>();
        services.AddScoped<SearchService>();
        services.AddScoped<RecentFileService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<PreviewService>();
        services.AddScoped<MediaJobRunner>();
        services.AddScoped<IMediaJobRunner>(serviceProvider => serviceProvider.GetRequiredService<MediaJobRunner>());
        services.AddScoped<MediaCleanupService>();
        services.AddScoped<IMediaCleanupService>(serviceProvider => serviceProvider.GetRequiredService<MediaCleanupService>());
        services.AddScoped<IUserStorageProvisioner, UserStorageProvisioner>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IStorageGuard, StorageGuard>();
        services.AddSingleton<IManagedFileSystemSnapshotReader, ManagedFileSystemSnapshotReader>();
        services.AddSingleton<IIndexChangeWatcher, LinuxInotifyWatcher>();
        services.AddSingleton<IFileStore, FileStore>();
        services.AddSingleton<IFileVersionStore, FileVersionStore>();
        services.AddSingleton<IDerivativeStore, DerivativeStore>();
        services.AddSingleton<IUploadSessionStore>(
            serviceProvider => (IUploadSessionStore)serviceProvider.GetRequiredService<IFileStore>());
        services.AddSingleton(
            serviceProvider => serviceProvider.GetRequiredService<IOptions<UploadSessionOptions>>().Value);
        services.AddSingleton<UploadChunkLimiter>();
        services.AddSingleton(
            serviceProvider =>
            {
                var configured = serviceProvider.GetRequiredService<IOptions<Configuration.IndexingOptions>>().Value;
                return new global::KuraStorage.Application.Indexing.IndexingOptions
                {
                    Enabled = configured.Enabled,
                    BatchSize = configured.BatchSize,
                    MissingConfirmationDelayMinutes = configured.MissingConfirmationDelayMinutes,
                    StagingRetentionHours = configured.StagingRetentionHours,
                    FullRescanIntervalMinutes = configured.FullRescanIntervalMinutes,
                    RunOnStartup = configured.RunOnStartup,
                    EventDebounceMilliseconds = configured.EventDebounceMilliseconds,
                    MovePairingWindowMilliseconds = configured.MovePairingWindowMilliseconds,
                    EventQueueCapacity = configured.EventQueueCapacity,
                    RetryBackoffSeconds = configured.RetryBackoffSeconds,
                };
            });
        if (addFileRecoveryHostedService)
        {
            services.AddHostedService<FileRecoveryHostedService>();
        }
        return services;
    }

    private static bool IsPostgreSqlConnection(string connectionString)
    {
        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.Host) &&
                !string.IsNullOrWhiteSpace(builder.Database) &&
                !string.IsNullOrWhiteSpace(builder.Username);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeMediaRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathFullyQualified(value) ||
            value is "." or ".." || value.Contains('/') || value.Contains('\\'))
        {
            return false;
        }

        return value is not ("users" or "upload-temp" or "upload-sessions");
    }
}
