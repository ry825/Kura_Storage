using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Identity;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Identity;
using KuraStorage.Infrastructure.Persistence;
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

        services.AddDbContext<KuraStorageDbContext>(
            (serviceProvider, options) =>
                options.UseNpgsql(
                    serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IdentityService>();
        services.AddScoped<FileService>();
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
        services.AddScoped<FileOperationRecoveryService>();
        services.AddScoped<IUserStorageProvisioner, UserStorageProvisioner>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IStorageGuard, StorageGuard>();
        services.AddSingleton<IFileStore, FileStore>();
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
}
