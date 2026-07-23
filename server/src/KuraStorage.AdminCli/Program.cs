using KuraStorage.Application.Identity;
using KuraStorage.Domain.Identity;
using KuraStorage.Infrastructure;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string help = """
KuraStorage administration CLI

Usage:
  kurastorage-admin database migrate
  kurastorage-admin user create <username> <display-name> <ADMIN|MEMBER> --password-stdin
  kurastorage-admin user unlock <username>
  kurastorage-admin device list <user-id>
  kurastorage-admin device revoke <user-id> <device-id>
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
        _ => UnknownCommand(),
    };
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
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
        CancellationToken.None);
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
        CancellationToken.None);
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
        CancellationToken.None);
    Console.WriteLine(revoked ? "Device revoked." : "Device was not found.");
    return revoked ? 0 : 1;
}

static int UnknownCommand()
{
    Console.Error.WriteLine("Unknown or incomplete command.");
    Console.Error.WriteLine(help);
    return 2;
}
