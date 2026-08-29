using KuraStorage.Application.Abstractions;
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
builder.Services.AddSingleton<IIndexRescanSignal, IndexRescanSignal>();
builder.Services.AddSingleton<IndexingWorkerMetrics>();
builder.Services.AddSingleton<MediaWorkerMetrics>();
builder.Services.AddSingleton<MediaCleanupMetrics>();
builder.Services.AddSingleton<IMediaCleanupDelay, SystemMediaCleanupDelay>();
builder.Services.AddHostedService<TrashPurgeWorker>();
builder.Services.AddHostedService<IndexEventWorker>();
builder.Services.AddHostedService<FullRescanWorker>();
builder.Services.AddHostedService<MediaGenerationWorker>();
builder.Services.AddHostedService<MediaCleanupWorker>();
await builder.Build().RunAsync();
