using KuraStorage.Application.Files;
using KuraStorage.Application.Transfers;
using KuraStorage.Infrastructure;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData("TrashPurge:RetentionDays", "29")]
    [InlineData("TrashPurge:IntervalHours", "0")]
    [InlineData("TrashPurge:IntervalHours", "169")]
    [InlineData("TrashPurge:BatchSize", "0")]
    [InlineData("TrashPurge:BatchSize", "501")]
    [InlineData("TrashPurge:RetryDelayMinutes", "0")]
    [InlineData("TrashPurge:RetryDelayMinutes", "1441")]
    public void TrashPurgeOptions_InvalidValue_IsRejected(string key, string value)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TrashPurgeOptions>>().Value);
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("99", "100")]
    public void StorageOptions_InvalidWarningThreshold_IsRejected(string warning, string minimum)
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = Path.GetTempPath(),
                ["Storage:StorageId"] = "test-storage",
                ["Storage:CapacityWarningFreeBytes"] = warning,
                ["Storage:MinimumFreeBytes"] = minimum,
            });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<StorageOptions>>().Value);
    }

    [Theory]
    [InlineData("UploadSession:PreferredChunkBytes", "1")]
    [InlineData("UploadSession:MaximumChunkBytes", "67108865")]
    [InlineData("UploadSession:MaximumFileBytes", "0")]
    [InlineData("UploadSession:IdleExpirationHours", "0")]
    [InlineData("UploadSession:AbsoluteExpirationHours", "721")]
    [InlineData("UploadSession:CleanupBatchSize", "501")]
    [InlineData("UploadSession:MaximumActiveSessionsPerDevice", "0")]
    [InlineData("UploadSession:MaximumConcurrentChunkWrites", "17")]
    [InlineData("UploadSession:OverloadRetryAfterSeconds", "301")]
    public void UploadSessionOptions_InvalidValue_IsRejected(string key, string value)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<UploadSessionOptions>>().Value);
    }

    [Theory]
    [InlineData("Indexing:BatchSize", "9")]
    [InlineData("Indexing:BatchSize", "5001")]
    [InlineData("Indexing:MissingConfirmationDelayMinutes", "0")]
    [InlineData("Indexing:MissingConfirmationDelayMinutes", "1441")]
    [InlineData("Indexing:StagingRetentionHours", "0")]
    [InlineData("Indexing:StagingRetentionHours", "169")]
    public void IndexingOptions_InvalidValue_IsRejected(string key, string value)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<IndexingOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new ServiceCollection()
            .AddKuraStorageInfrastructure(configuration)
            .BuildServiceProvider();
    }
}
