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
        Assert.False(StorageIdentity.Matches("expected-storage", "different-storage\n"));
        Assert.True(StorageIdentity.Matches("expected-storage", "expected-storage\n"));
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

        var result = await guard.InspectAsync(true, CancellationToken.None);

        Assert.Equal(StorageStatus.Unavailable, result);
        Assert.False(Directory.Exists(root));
    }
}
