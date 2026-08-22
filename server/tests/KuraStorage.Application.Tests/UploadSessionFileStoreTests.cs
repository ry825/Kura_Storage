using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Transfers;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class UploadSessionFileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"kurastorage-chunks-{Guid.NewGuid():N}");
    private readonly FileStore store;

    public UploadSessionFileStoreTests()
    {
        Directory.CreateDirectory(root);
        store = new FileStore(
            Options.Create(new StorageOptions
            {
                RootPath = root,
                StorageId = "test-storage",
                MinimumFreeBytes = 1,
                CapacityWarningFreeBytes = 1,
            }));
    }

    [Fact]
    public async Task WriteChunk_WhenBodyIsShortOrLong_TruncatesToLastConfirmedOffset()
    {
        var path = RelativeStoragePath.Create("upload-sessions/user/session.upload");
        await store.WriteChunkAsync(path, 0, new MemoryStream([1, 2]), 2, CancellationToken.None);

        await Assert.ThrowsAsync<UploadChunkSizeMismatchException>(() =>
            store.WriteChunkAsync(path, 2, new MemoryStream([3]), 2, CancellationToken.None));
        Assert.Equal(2, (await store.InspectAsync(path, CancellationToken.None)).Length);

        await Assert.ThrowsAsync<UploadChunkSizeMismatchException>(() =>
            store.WriteChunkAsync(path, 2, new MemoryStream([3, 4, 5]), 2, CancellationToken.None));
        Assert.Equal(2, (await store.InspectAsync(path, CancellationToken.None)).Length);
    }

    [Fact]
    public async Task WriteChunk_WhenLargeProducerStreamIsUsed_BuffersAtMost64KiB()
    {
        const int length = 8 * 1024 * 1024;
        var source = new GeneratedStream(length);
        var path = RelativeStoragePath.Create("upload-sessions/user/large.upload");

        var result = await store.WriteChunkAsync(path, 0, source, length, CancellationToken.None);

        Assert.Equal(length, result.Length);
        Assert.Equal(length, (await store.InspectAsync(path, CancellationToken.None)).Length);
        Assert.InRange(source.MaximumRequestedBytes, 1, 64 * 1024);
    }

    [Fact]
    public async Task UploadSessionStore_RejectsShortExistingFileAndSymbolicLink()
    {
        var path = RelativeStoragePath.Create("upload-sessions/user/short.upload");
        await store.WriteChunkAsync(path, 0, new MemoryStream([1]), 1, CancellationToken.None);
        await Assert.ThrowsAsync<UploadTemporaryFileTooShortException>(() =>
            store.WriteChunkAsync(path, 2, new MemoryStream([2]), 1, CancellationToken.None));

        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var linked = Path.Combine(root, "upload-sessions", "linked");
            Directory.CreateSymbolicLink(linked, outside);
            await Assert.ThrowsAsync<IOException>(() =>
                store.WriteChunkAsync(
                    RelativeStoragePath.Create("upload-sessions/linked/escape.upload"),
                    0,
                    new MemoryStream([1]),
                    1,
                    CancellationToken.None));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    [Theory]
    [InlineData("/absolute/path")]
    [InlineData("../outside")]
    [InlineData("upload-sessions/../../outside")]
    public void RelativeStoragePath_RejectsAbsoluteAndTraversal(string value)
    {
        Assert.Throws<ArgumentException>(() => RelativeStoragePath.Create(value));
    }

    [Fact]
    public async Task ChunkLimiter_WhenAtCapacity_RejectsWithoutWaitingAndAllowsRetryAfterRelease()
    {
        var limiter = new UploadChunkLimiter(new UploadSessionOptions { MaximumConcurrentChunkWrites = 2 });
        var first = await limiter.TryEnterAsync(CancellationToken.None);
        await using var second = await limiter.TryEnterAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(await limiter.TryEnterAsync(CancellationToken.None));

        await first!.DisposeAsync();
        await using var retried = await limiter.TryEnterAsync(CancellationToken.None);
        Assert.NotNull(retried);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class GeneratedStream(long length) : Stream
    {
        private long position;

        public int MaximumRequestedBytes { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            MaximumRequestedBytes = Math.Max(MaximumRequestedBytes, buffer.Length);
            var count = (int)Math.Min(buffer.Length, length - position);
            buffer[..count].Fill(0x5a);
            position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
