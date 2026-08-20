using KuraStorage.Application.Abstractions;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class StorageGuardTests
{
    [Fact]
    public void StorageIdentity_WhenConfiguredIdDoesNotMatch_ReturnsFalse()
    {
        Assert.False(
            StorageIdentity.Matches(
                "expected-storage",
                """{"storageId":"different-storage","formatVersion":1}"""));
        Assert.True(
            StorageIdentity.Matches(
                "expected-storage",
                """{"storageId":"expected-storage","formatVersion":1}"""));
        Assert.False(
            StorageIdentity.Matches(
                "expected-storage",
                """{"storageId":"expected-storage","formatVersion":2}"""));
        Assert.False(StorageIdentity.Matches("expected-storage", "not-json"));
    }

    [Fact]
    public async Task InspectAsync_WhenMountPointDoesNotExist_DoesNotCreateFallbackDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kurastorage-missing-{Guid.NewGuid():N}");
        var guard = new StorageGuard(
            Options.Create(
                new StorageOptions
                {
                    RootPath = root,
                    StorageId = "test-storage",
                    MinimumFreeBytes = 1,
                }));

        var result = await guard.InspectAsync(StorageIntent.CreateOrUpdate, CancellationToken.None);

        Assert.Equal(StorageStatus.Unavailable, result);
        Assert.False(Directory.Exists(root));
    }
}
