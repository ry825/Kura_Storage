using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class DerivativeStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"kurastorage-derivatives-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteAndPublish_UsesOnlyDeterministicServerPathsAndDurableAtomicMove()
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(StorageStatus.Available);
        var owner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var job = Guid.NewGuid();

        var temporary = await store.WriteTemporaryAsync(
            job,
            1,
            new MemoryStream([1, 2, 3]),
            3,
            CancellationToken.None);
        var published = await store.PublishAsync(
            temporary,
            owner,
            source,
            2,
            3,
            DerivativeType.ImageLow,
            "webp",
            3,
            CancellationToken.None);

        Assert.Equal($"derivatives/{owner:N}/{source:N}/2/3/image-low.webp", published.Path.Value);
        Assert.Equal(3, published.Size);
        Assert.False(File.Exists(Path.Combine(root, "derivative-temp", job.ToString("N"), "1.part")));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(root, published.Path.Value)));
    }

    [Theory]
    [InlineData("/absolute")]
    [InlineData("../escape")]
    [InlineData("webp/escape")]
    [InlineData("webp.part")]
    public async Task Publish_WhenExtensionIsUnsafe_RejectsBeforeWriting(string extension)
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(StorageStatus.Available);
        var temporary = await store.WriteTemporaryAsync(
            Guid.NewGuid(), 1, new MemoryStream([1]), 1, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => store.PublishAsync(
            temporary,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            1,
            DerivativeType.Thumbnail,
            extension,
            1,
            CancellationToken.None));
    }

    [Fact]
    public async Task Write_WhenStorageUnavailable_DoesNotCreateFallbackDirectories()
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(StorageStatus.Unavailable);

        await Assert.ThrowsAsync<DerivativeStorageUnavailableException>(() => store.WriteTemporaryAsync(
            Guid.NewGuid(), 1, new MemoryStream([1]), 1, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(root, "derivative-temp")));
        Assert.False(Directory.Exists(Path.Combine(root, "derivatives")));
    }

    [Fact]
    public async Task Write_WhenInputFails_RemovesPartialFile()
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(StorageStatus.Available);
        var job = Guid.NewGuid();

        await Assert.ThrowsAsync<IOException>(() => store.WriteTemporaryAsync(
            job, 2, new InterruptedStream(), 10, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(root, "derivative-temp", job.ToString("N"), "2.part")));
    }

    [Fact]
    public async Task Publish_WhenFormalPathExists_PreservesBothFilesForRecovery()
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(StorageStatus.Available);
        var owner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var first = await store.WriteTemporaryAsync(
            Guid.NewGuid(), 1, new MemoryStream([1]), 1, CancellationToken.None);
        var path = await store.PublishAsync(
            first, owner, source, 1, 1, DerivativeType.Thumbnail, "webp", 1, CancellationToken.None);
        var second = await store.WriteTemporaryAsync(
            Guid.NewGuid(), 1, new MemoryStream([2]), 1, CancellationToken.None);

        await Assert.ThrowsAsync<DerivativePublishConflictException>(() => store.PublishAsync(
            second, owner, source, 1, 1, DerivativeType.Thumbnail, "webp", 1, CancellationToken.None));

        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(root, path.Path.Value)));
        Assert.True(File.Exists(Path.Combine(root, second.Path.Value)));
    }

    [Fact]
    public async Task Recovery_FindsPublishedResultAndDeletesOnlyExactTemporaryAttempt()
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(StorageStatus.Available);
        var owner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var job = Guid.NewGuid();
        var temporary = await store.WriteTemporaryAsync(
            job, 2, new MemoryStream([1, 2, 3]), 3, CancellationToken.None);
        var published = await store.PublishAsync(
            temporary, owner, source, 4, 5, DerivativeType.VideoLow, "mp4", 3, CancellationToken.None);
        var work = Path.Combine(root, "derivative-temp", job.ToString("N"), "2-work");
        Directory.CreateDirectory(work);
        await File.WriteAllBytesAsync(Path.Combine(work, "generated.mp4"), [9]);
        await store.WriteTemporaryAsync(job, 3, new MemoryStream([8]), 1, CancellationToken.None);
        var context = new MediaGenerationContext(
            job, Guid.NewGuid(), owner, source, 4, RelativeStoragePath.Create("users/source.mp4"),
            3, "video/mp4", DerivativeType.VideoLow, 5, 2, Guid.NewGuid());

        var found = await store.FindPublishedAsync(context, "mp4", CancellationToken.None);
        await store.DeleteTemporaryAsync(job, 2, CancellationToken.None);

        Assert.Equal(published, found);
        Assert.False(Directory.Exists(work));
        Assert.True(File.Exists(Path.Combine(root, "derivative-temp", job.ToString("N"), "3.part")));
    }

    [Fact]
    public async Task Publish_WhenAncestorIsSymbolicLink_RejectsRootEscape()
    {
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-derivative-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "derivatives"), outside);
            var store = CreateStore(StorageStatus.Available);
            var temporary = await store.WriteTemporaryAsync(
                Guid.NewGuid(), 1, new MemoryStream([1]), 1, CancellationToken.None);

            await Assert.ThrowsAsync<UnsafeDerivativePathException>(() => store.PublishAsync(
                temporary,
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                1,
                DerivativeType.ImageMedium,
                "webp",
                1,
                CancellationToken.None));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private DerivativeStore CreateStore(StorageStatus status) =>
        new(
            Options.Create(new StorageOptions { RootPath = root, StorageId = "test", MinimumFreeBytes = 1 }),
            Options.Create(new MediaOptions()),
            new FixedStorageGuard(status));

    private sealed class FixedStorageGuard(StorageStatus status) : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class InterruptedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 10;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("interrupted");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
