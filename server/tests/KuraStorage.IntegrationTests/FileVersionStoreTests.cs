using System.Text;
using KuraStorage.Application.Files;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class FileVersionStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"kurastorage-version-{Guid.NewGuid():N}");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_StreamsValidUtf8ToDeterministicImmutablePath()
    {
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("first line\n日本語\n");
        var store = CreateStore();

        var published = await store.TryPublishAsync(
            ownerId, fileId, 3, Guid.NewGuid(), new MemoryStream(content), content.Length, default);

        Assert.NotNull(published);
        Assert.Equal(content.Length, published.Size);
        Assert.Matches("^[0-9a-f]{64}$", published.Sha256);
        Assert.Equal(
            $"versions/{ownerId:N}/{fileId:N}/3/{published.Sha256}.bin",
            published.Path.Value);
        Assert.Equal(content, await File.ReadAllBytesAsync(Resolve(published.Path.Value)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "version-temp"), "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Publish_SameContentRetryReusesPublishedFileWithoutOverwrite()
    {
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("stable");
        var store = CreateStore();
        var first = await store.TryPublishAsync(
            ownerId, fileId, 1, Guid.NewGuid(), new MemoryStream(content), content.Length, default);
        var firstWrite = File.GetLastWriteTimeUtc(Resolve(first!.Path.Value));

        var second = await store.TryPublishAsync(
            ownerId, fileId, 1, Guid.NewGuid(), new MemoryStream(content), content.Length, default);

        Assert.Equal(first.Path, second!.Path);
        Assert.Equal(first.Size, second.Size);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.NotEqual(first.TemporaryPath, second.TemporaryPath);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(Resolve(first.Path.Value)));
    }

    [Fact]
    public async Task Publish_RejectsInvalidUtf8AndDoesNotPublish()
    {
        var store = CreateStore();

        var published = await store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
            new MemoryStream([0xc3, 0x28]), 2, default);

        Assert.Null(published);
        Assert.False(Directory.Exists(Path.Combine(root, "versions")));
    }

    [Fact]
    public async Task Publish_RejectsShortLongAndOverLimitInputsWithoutPartialFile()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<FileVersionContentSizeException>(() => store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), new MemoryStream([1]), 2, default));
        await Assert.ThrowsAsync<FileVersionContentSizeException>(() => store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), new MemoryStream([1, 2]), 1, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Stream.Null,
            KuraStorage.Domain.Files.FileVersionRecord.MaximumContentBytes + 1, default));
        Assert.False(Directory.Exists(Path.Combine(root, "versions")));
    }

    [Fact]
    public async Task Publish_RejectsMissingIdsAndNonPositiveVersion()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.TryPublishAsync(
            Guid.Empty, Guid.NewGuid(), 1, Guid.NewGuid(), Stream.Null, 0, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Stream.Null, 0, default));
    }

    [Fact]
    public async Task Publish_RemovesStaleTemporaryFileForSameOperationBeforeWriting()
    {
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var temporary = Resolve($"version-temp/{ownerId:N}/{fileId:N}/1/{operationId:N}.part");
        Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
        await File.WriteAllTextAsync(temporary, "stale");

        var published = await CreateStore().TryPublishAsync(
            ownerId, fileId, 1, operationId, new MemoryStream([1]), 1, default);

        Assert.NotNull(published);
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public async Task Publish_RejectsSymlinkedVersionTree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-version-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(root, "versions"), outside);
        try
        {
            await Assert.ThrowsAsync<FileVersionStorageUnavailableException>(() => CreateStore().TryPublishAsync(
                Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), new MemoryStream([1]), 1, default));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(Path.Combine(root, "versions"));
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Publish_CorruptExistingArtifactFailsClosedWithoutOverwritingIt()
    {
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("immutable");
        var store = CreateStore();
        var published = await store.TryPublishAsync(
            ownerId, fileId, 1, operationId, new MemoryStream(content), content.Length, default);
        await File.WriteAllBytesAsync(Resolve(published!.Path.Value), Encoding.UTF8.GetBytes("corrupted"));

        await Assert.ThrowsAsync<FileVersionConsistencyException>(() => store.TryPublishAsync(
            ownerId, fileId, 1, operationId, new MemoryStream(content), content.Length, default));

        Assert.Equal("corrupted", await File.ReadAllTextAsync(Resolve(published.Path.Value)));
    }

    [Fact]
    public async Task Publish_TruncatedExistingArtifactFailsClosed()
    {
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("immutable");
        var store = CreateStore();
        var published = await store.TryPublishAsync(
            ownerId, fileId, 1, Guid.NewGuid(), new MemoryStream(content), content.Length, default);
        await File.WriteAllBytesAsync(Resolve(published!.Path.Value), [1]);

        await Assert.ThrowsAsync<FileVersionConsistencyException>(() => store.TryPublishAsync(
            ownerId, fileId, 1, Guid.NewGuid(), new MemoryStream(content), content.Length, default));
    }

    [Fact]
    public async Task Publish_MidstreamFailureRemovesOnlyTemporaryArtifact()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<FileVersionStorageUnavailableException>(() => store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
            new InterruptedStream(Encoding.UTF8.GetBytes("partial")), 14, default));

        Assert.False(Directory.Exists(Path.Combine(root, "versions")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "version-temp"), "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Publish_InsufficientCapacityFailsBeforeCreatingArtifacts()
    {
        var store = CreateStore(long.MaxValue);

        await Assert.ThrowsAsync<FileVersionStorageUnavailableException>(() => store.TryPublishAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), new MemoryStream([1]), 1, default));

        Assert.False(Directory.Exists(Path.Combine(root, "versions")));
        Assert.False(Directory.Exists(Path.Combine(root, "version-temp")));
    }

    private FileVersionStore CreateStore(long minimumFreeBytes = 1) => new(Options.Create(new StorageOptions
    {
        RootPath = root,
        StorageId = "integration",
        MinimumFreeBytes = minimumFreeBytes,
        CapacityWarningFreeBytes = 1,
    }));

    private string Resolve(string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private sealed class InterruptedStream(byte[] content) : Stream
    {
        private bool returnedContent;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (returnedContent)
            {
                throw new IOException("Injected stream interruption.");
            }

            returnedContent = true;
            content.AsSpan().CopyTo(buffer.Span);
            return ValueTask.FromResult(content.Length);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
