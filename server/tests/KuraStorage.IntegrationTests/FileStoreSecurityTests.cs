using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class FileStoreSecurityTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"kurastorage-file-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task OpenRead_WhenPathEndsInSymbolicLink_RejectsIt()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var store = CreateStore();
        await store.EnsureUserAreaAsync(owner, CancellationToken.None);
        var files = Path.Combine(directory, "users", owner.ToString("N"), "files");
        var target = Path.Combine(files, "target.txt");
        var link = Path.Combine(files, "link.txt");
        await File.WriteAllTextAsync(target, "secret");
        File.CreateSymbolicLink(link, target);

        await Assert.ThrowsAsync<IOException>(
            () => store.OpenReadAsync(
                RelativeStoragePath.Create($"users/{owner:N}/files/link.txt"),
                CancellationToken.None));
    }

    [Fact]
    public async Task WriteUploadTemp_WhenBodyExceedsDeclaredSize_DeletesTemporaryFile()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var store = CreateStore();

        await Assert.ThrowsAsync<KuraStorage.Application.Files.UploadSizeMismatchException>(
            () => store.WriteUploadTempAsync(
                owner,
                operation,
                new MemoryStream([1, 2, 3]),
                2,
                CancellationToken.None));

        Assert.False(
            File.Exists(
                Path.Combine(
                    directory,
                    "upload-temp",
                    owner.ToString("N"),
                $"{operation:N}.upload")));
    }

    [Fact]
    public async Task WriteUploadTemp_WhenSourceIsInterrupted_DeletesTemporaryFile()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var store = CreateStore();

        await Assert.ThrowsAsync<IOException>(
            () => store.WriteUploadTempAsync(
                owner,
                operation,
                new InterruptedStream(),
                10,
                CancellationToken.None));

        Assert.False(
            File.Exists(
                Path.Combine(
                    directory,
                    "upload-temp",
                    owner.ToString("N"),
                    $"{operation:N}.upload")));
    }

    [Fact]
    public async Task HasCapacity_WhenRequiredSizeWouldOverflowSafetyReserve_ReturnsFalse()
    {
        Directory.CreateDirectory(directory);
        Assert.False(await CreateStore().HasCapacityAsync(long.MaxValue, CancellationToken.None));
    }

    [Fact]
    public async Task WriteUploadTemp_WhenTemporaryAreaIsReadOnly_DoesNotPublishAFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var store = CreateStore();
        await store.EnsureUserAreaAsync(owner, CancellationToken.None);
        var uploadDirectory = Path.Combine(directory, "upload-temp", owner.ToString("N"));
        File.SetUnixFileMode(
            uploadDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => store.WriteUploadTempAsync(
                    owner,
                    operation,
                    new MemoryStream([1]),
                    1,
                    CancellationToken.None));
            Assert.True(exception is UnauthorizedAccessException or IOException);
            Assert.False(File.Exists(Path.Combine(uploadDirectory, $"{operation:N}.upload")));
        }
        finally
        {
            File.SetUnixFileMode(
                uploadDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private FileStore CreateStore() =>
        new(
            Options.Create(
                new StorageOptions
                {
                    RootPath = directory,
                    StorageId = "test",
                    MinimumFreeBytes = 1,
                }));

    private sealed class InterruptedStream : Stream
    {
        private bool read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (read)
            {
                throw new IOException("Simulated network interruption.");
            }

            read = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
