using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Maintenance;
using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class StorageCapacityServiceTests
{
    [Fact]
    public async Task Get_ReturnsCheckedVolumeUsage()
    {
        var result = await new StorageCapacityService(
            new CapacityStore(new StorageCapacity(1_000, 250)),
            new FixedGuard(StorageStatus.Available)).GetAsync(CancellationToken.None);

        Assert.Equal("AVAILABLE", result.Storage);
        Assert.Equal(1_000, result.TotalBytes);
        Assert.Equal(750, result.UsedBytes);
        Assert.Equal(250, result.AvailableBytes);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(100, -1)]
    [InlineData(100, 101)]
    public async Task Get_InvalidCapacityFailsClosed(long total, long available)
    {
        var result = await new StorageCapacityService(
            new CapacityStore(new StorageCapacity(total, available)),
            new FixedGuard(StorageStatus.Available)).GetAsync(CancellationToken.None);

        Assert.Equal("UNAVAILABLE", result.Storage);
        Assert.Null(result.TotalBytes);
        Assert.Null(result.UsedBytes);
        Assert.Null(result.AvailableBytes);
    }

    [Fact]
    public async Task Get_StorageInspectionIoFailureReturnsTypedUnavailableStatus()
    {
        var result = await new StorageCapacityService(
            new CapacityStore(new StorageCapacity(1_000, 250)),
            new ThrowingGuard()).GetAsync(CancellationToken.None);

        Assert.Equal(new StorageCapacityStatus("UNAVAILABLE", null, null, null), result);
    }

    private sealed class FixedGuard(StorageStatus status) : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class ThrowingGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            throw new IOException("inspection failed");
    }

    private sealed class CapacityStore(StorageCapacity capacity) : IFileStore
    {
        public Task<StorageCapacity> GetCapacityAsync(CancellationToken cancellationToken) => Task.FromResult(capacity);
        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredUpload> WriteUploadTempAsync(Guid ownerUserId, Guid operationId, Stream source, long expectedSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveAsync(RelativeStoragePath source, RelativeStoragePath target, bool sourceIsDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(RelativeStoragePath path, bool directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
