using KuraStorage.Infrastructure;
using KuraStorage.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);
var secretsDirectory = Environment.GetEnvironmentVariable("KURASTORAGE_SECRETS_DIR");
if (!string.IsNullOrWhiteSpace(secretsDirectory))
{
    builder.Configuration.AddKeyPerFile(secretsDirectory, optional: false);
}

builder.Services.AddKuraStorageInfrastructure(builder.Configuration, addFileRecoveryHostedService: false);
builder.Services.AddSingleton<ITrashPurgeDelay, SystemTrashPurgeDelay>();
builder.Services.AddHostedService<TrashPurgeWorker>();
await builder.Build().RunAsync();
