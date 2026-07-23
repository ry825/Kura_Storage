using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Identity;
using KuraStorage.Application.Files;
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
        IConfiguration configuration)
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
        services.AddScoped<FileOperationRecoveryService>();
        services.AddScoped<IUserStorageProvisioner, UserStorageProvisioner>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IStorageGuard, StorageGuard>();
        services.AddSingleton<IFileStore, FileStore>();
        services.AddHostedService<FileRecoveryHostedService>();
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
