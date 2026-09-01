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
    public async Task Move_WhenTargetExists_DoesNotOverwriteEitherFileOrDirectory()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var store = CreateStore();
        await store.EnsureUserAreaAsync(owner, CancellationToken.None);
        var files = Path.Combine(directory, "users", owner.ToString("N"), "files");
        await File.WriteAllTextAsync(Path.Combine(files, "source.txt"), "source");
        await File.WriteAllTextAsync(Path.Combine(files, "target.txt"), "target");

        await Assert.ThrowsAsync<IOException>(
            () => store.MoveAsync(
                RelativeStoragePath.Create($"users/{owner:N}/files/source.txt"),
                RelativeStoragePath.Create($"users/{owner:N}/files/target.txt"),
                false,
                CancellationToken.None));

        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(files, "source.txt")));
        Assert.Equal("target", await File.ReadAllTextAsync(Path.Combine(files, "target.txt")));
    }

    [Fact]
    public async Task Replace_AtomicallyOverwritesCurrentFileAndConsumesValidatedTemporaryFile()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var store = CreateStore();
        await store.EnsureUserAreaAsync(owner, CancellationToken.None);
        var currentPath = Path.Combine(directory, "users", owner.ToString("N"), "files", "note.txt");
        await File.WriteAllTextAsync(currentPath, "before");
        var replacement = await store.WriteUploadTempAsync(
            owner,
            operation,
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("after")),
            5,
            CancellationToken.None);

        await store.ReplaceAsync(
            replacement.Path,
            RelativeStoragePath.Create($"users/{owner:N}/files/note.txt"),
            CancellationToken.None);

        Assert.Equal("after", await File.ReadAllTextAsync(currentPath));
        Assert.False(File.Exists(Path.Combine(
            directory, "upload-temp", owner.ToString("N"), $"{operation:N}.upload")));
    }

    [Fact]
    public async Task Replace_WhenTargetMissingOrSymlinkedFailsWithoutPublishing()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var store = CreateStore();
        await store.EnsureUserAreaAsync(owner, CancellationToken.None);
        var replacement = await store.WriteUploadTempAsync(
            owner, Guid.NewGuid(), new MemoryStream([1]), 1, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => store.ReplaceAsync(
            replacement.Path,
            RelativeStoragePath.Create($"users/{owner:N}/files/missing.txt"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Move_WhenTargetParentIsSymlink_RejectsStorageEscape()
    {
        Directory.CreateDirectory(directory);
        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var owner = Guid.NewGuid();
            var store = CreateStore();
            await store.EnsureUserAreaAsync(owner, CancellationToken.None);
            var files = Path.Combine(directory, "users", owner.ToString("N"), "files");
            await File.WriteAllTextAsync(Path.Combine(files, "source.txt"), "source");
            Directory.CreateSymbolicLink(Path.Combine(files, "linked"), outside);

            await Assert.ThrowsAsync<IOException>(
                () => store.MoveAsync(
                    RelativeStoragePath.Create($"users/{owner:N}/files/source.txt"),
                    RelativeStoragePath.Create($"users/{owner:N}/files/linked/target.txt"),
                    false,
                    CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(outside, "target.txt")));
            Assert.True(File.Exists(Path.Combine(files, "source.txt")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
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

    [Fact]
    public async Task DeleteTreeIfExists_TrashContainer_DeletesNestedTreeAndIsIdempotent()
    {
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var store = CreateStore();
        await store.EnsureUserAreaAsync(owner, CancellationToken.None);
        var container = Path.Combine(directory, "users", owner.ToString("N"), "trash", rootId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(container, "folder", "nested"));
        await File.WriteAllTextAsync(Path.Combine(container, "folder", "nested", "file.txt"), "value");
        var relative = RelativeStoragePath.Create($"users/{owner:N}/trash/{rootId:N}");

        await store.DeleteTreeIfExistsAsync(relative, CancellationToken.None);
        await store.DeleteTreeIfExistsAsync(relative, CancellationToken.None);

        Assert.False(Directory.Exists(container));
    }

    [Theory]
    [InlineData("users/11111111111111111111111111111111")]
    [InlineData("users/11111111111111111111111111111111/trash")]
    [InlineData("users/11111111111111111111111111111111/files")]
    public async Task DeleteTreeIfExists_ManagementRoot_IsRejected(string path)
    {
        Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<KuraStorage.Application.Files.UnsafeStorageTreeException>(
            () => CreateStore().DeleteTreeIfExistsAsync(RelativeStoragePath.Create(path), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteTreeIfExists_WhenTargetIsSymbolicLink_RejectsWithoutFollowingIt()
    {
        Directory.CreateDirectory(directory);
        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-delete-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var owner = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var store = CreateStore();
            await store.EnsureUserAreaAsync(owner, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(outside, "keep.txt"), "keep");
            Directory.CreateSymbolicLink(
                Path.Combine(directory, "users", owner.ToString("N"), "trash", rootId.ToString("N")),
                outside);

            await Assert.ThrowsAsync<KuraStorage.Application.Files.UnsafeStorageTreeException>(
                () => store.DeleteTreeIfExistsAsync(
                    RelativeStoragePath.Create($"users/{owner:N}/trash/{rootId:N}"),
                    CancellationToken.None));

            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteTreeIfExists_WhenAncestorIsSymbolicLink_RejectsWithoutFollowingIt()
    {
        Directory.CreateDirectory(directory);
        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-delete-ancestor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var owner = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var userRoot = Path.Combine(directory, "users", owner.ToString("N"));
            Directory.CreateDirectory(userRoot);
            Directory.CreateSymbolicLink(Path.Combine(userRoot, "trash"), outside);
            var target = Path.Combine(outside, rootId.ToString("N"));
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "keep.txt"), "keep");

            await Assert.ThrowsAsync<KuraStorage.Application.Files.UnsafeStorageTreeException>(
                () => CreateStore().DeleteTreeIfExistsAsync(
                    RelativeStoragePath.Create($"users/{owner:N}/trash/{rootId:N}"),
                    CancellationToken.None));

            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteTreeIfExists_WhenDescendantIsSymbolicLink_RejectsWithoutFollowingIt()
    {
        Directory.CreateDirectory(directory);
        var outside = Path.Combine(Path.GetTempPath(), $"kurastorage-delete-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var owner = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var store = CreateStore();
            await store.EnsureUserAreaAsync(owner, CancellationToken.None);
            var container = Path.Combine(directory, "users", owner.ToString("N"), "trash", rootId.ToString("N"));
            Directory.CreateDirectory(container);
            await File.WriteAllTextAsync(Path.Combine(outside, "keep.txt"), "keep");
            Directory.CreateSymbolicLink(Path.Combine(container, "linked"), outside);

            await Assert.ThrowsAsync<KuraStorage.Application.Files.UnsafeStorageTreeException>(
                () => store.DeleteTreeIfExistsAsync(
                    RelativeStoragePath.Create($"users/{owner:N}/trash/{rootId:N}"),
                    CancellationToken.None));

            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
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
