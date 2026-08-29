using System.Buffers;
using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Application.Transfers;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Storage;

public sealed class FileStore(
    IOptions<StorageOptions> configuredOptions,
    IOptions<MediaOptions>? configuredMediaOptions = null) : IFileStore, IUploadSessionStore
{
    private readonly StorageOptions options = configuredOptions.Value;
    private readonly string root = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(configuredOptions.Value.RootPath));
    private readonly string derivativeRoot = configuredMediaOptions?.Value.DerivativeRoot ?? "derivatives";
    private readonly string derivativeTemporaryRoot = configuredMediaOptions?.Value.TemporaryRoot ?? "derivative-temp";

    public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requiredBytes < 0)
        {
            return Task.FromResult(false);
        }

        if (requiredBytes > long.MaxValue - options.MinimumFreeBytes)
        {
            return Task.FromResult(false);
        }

        var available = new DriveInfo(root).AvailableFreeSpace;
        return Task.FromResult(available >= requiredBytes + options.MinimumFreeBytes);
    }

    public Task<StorageCapacity> GetCapacityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var drive = new DriveInfo(root);
        return Task.FromResult(new StorageCapacity(drive.TotalSize, drive.AvailableFreeSpace));
    }

    public async Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"users/{ownerUserId:N}/files"), false));
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"users/{ownerUserId:N}/trash"), false));
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"upload-temp/{ownerUserId:N}"), false));
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"upload-sessions/{ownerUserId:N}"), false));
        await Task.CompletedTask;
    }

    public async Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path, false);
        if (File.Exists(resolved) || Directory.Exists(resolved))
        {
            throw new IOException("The target already exists.");
        }

        Directory.CreateDirectory(resolved);
        await Task.CompletedTask;
    }

    public async Task<StoredUpload> WriteUploadTempAsync(
        Guid ownerUserId,
        Guid operationId,
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        await EnsureUserAreaAsync(ownerUserId, cancellationToken);
        var relative = RelativeStoragePath.Create($"upload-temp/{ownerUserId:N}/{operationId:N}.upload");
        var path = Resolve(relative, false);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            await using var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > expectedSize)
                {
                    throw new UploadSizeMismatchException();
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            return new StoredUpload(relative, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task MoveAsync(
        RelativeStoragePath source,
        RelativeStoragePath target,
        bool sourceIsDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Resolve(source, true);
        var targetPath = Resolve(target, false);
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            throw new IOException("The target already exists.");
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        if (!Directory.Exists(targetDirectory))
        {
            throw new IOException("The target parent directory does not exist.");
        }

        EnsureNoSymbolicLink(targetDirectory);
        if (sourceIsDirectory)
        {
            Directory.Move(sourcePath, targetPath);
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }

        await Task.CompletedTask;
    }

    public async Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path, false);
        if (File.Exists(resolved))
        {
            File.Delete(resolved);
        }

        await Task.CompletedTask;
    }

    public async Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDeletableTree(path);
        var resolved = Resolve(path, false, unsafeSymbolicLink: true);
        EnsureNoSymbolicLink(resolved, unsafeTree: true);
        if (File.Exists(resolved))
        {
            File.Delete(resolved);
            return;
        }

        if (!Directory.Exists(resolved))
        {
            return;
        }

        await DeleteDirectoryAsync(resolved, cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        RelativeStoragePath path,
        bool directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path, false);
        var exists = directory ? Directory.Exists(resolved) : File.Exists(resolved);
        if (exists)
        {
            EnsureNoSymbolicLink(resolved);
        }

        return await Task.FromResult(exists);
    }

    public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path, true);
        Stream stream = new FileStream(
            resolved,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<TemporaryUploadState> InspectAsync(
        RelativeStoragePath path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path, false);
        if (!File.Exists(resolved))
        {
            return Task.FromResult(new TemporaryUploadState(false, 0));
        }

        EnsureNoSymbolicLink(resolved);
        return Task.FromResult(new TemporaryUploadState(true, new FileInfo(resolved).Length));
    }

    public async Task<StoredChunk> WriteChunkAsync(
        RelativeStoragePath path,
        long offset,
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || expectedLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var resolved = Resolve(path, false);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        EnsureNoSymbolicLink(Path.GetDirectoryName(resolved)!);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        await using var destination = new FileStream(
            resolved,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        if (destination.Length < offset)
        {
            throw new UploadTemporaryFileTooShortException();
        }

        if (destination.Length != offset)
        {
            destination.SetLength(offset);
        }

        destination.Position = offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            while (total < expectedLength)
            {
                var requested = (int)Math.Min(buffer.Length, expectedLength - total);
                var read = await content.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                {
                    throw new UploadChunkSizeMismatchException();
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
            }

            if (await content.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            {
                throw new UploadChunkSizeMismatchException();
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
            return new StoredChunk(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            destination.SetLength(offset);
            destination.Flush(flushToDisk: true);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<StoredChunk> ReadAndHashAsync(
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        if (expectedLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        }

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            while (total < expectedLength)
            {
                var requested = (int)Math.Min(buffer.Length, expectedLength - total);
                var read = await content.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                {
                    throw new UploadChunkSizeMismatchException();
                }

                hash.AppendData(buffer, 0, read);
                total += read;
            }

            if (await content.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            {
                throw new UploadChunkSizeMismatchException();
            }

            return new StoredChunk(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task TruncateAsync(
        RelativeStoragePath path,
        long length,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path, false);
        if (!File.Exists(resolved))
        {
            if (length == 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
                EnsureNoSymbolicLink(Path.GetDirectoryName(resolved)!);
                await using var empty = new FileStream(
                    resolved,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                await empty.FlushAsync(cancellationToken);
                empty.Flush(flushToDisk: true);
                return;
            }

            throw new UploadTemporaryFileTooShortException();
        }

        await using var stream = new FileStream(
            resolved,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        if (stream.Length < length)
        {
            throw new UploadTemporaryFileTooShortException();
        }

        stream.SetLength(length);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public async Task<string> ComputeSha256Async(
        RelativeStoragePath path,
        CancellationToken cancellationToken)
    {
        var resolved = Resolve(path, true);
        await using var stream = new FileStream(
            resolved,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string Resolve(
        RelativeStoragePath relativePath,
        bool requireExisting,
        bool unsafeSymbolicLink = false)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Value.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new IOException("The storage path is outside the configured root.");
        }

        EnsureNoSymbolicLink(
            requireExisting ? candidate : Path.GetDirectoryName(candidate)!,
            unsafeSymbolicLink);
        return candidate;
    }

    private void EnsureDeletableTree(RelativeStoragePath path)
    {
        var segments = path.Value.Split('/');
        var userTree = segments.Length >= 4 && segments[0] == "users" &&
                       segments[2] is "trash" or "derived";
        var derivativeTree = segments.Length >= 3 && segments[0] == derivativeRoot;
        var temporaryTree = segments.Length >= 2 && segments[0] == derivativeTemporaryRoot;
        if (!userTree && !derivativeTree && !temporaryTree)
        {
            throw new UnsafeStorageTreeException("The requested tree is a protected storage area.");
        }
    }

    private async Task DeleteDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        EnsureNoSymbolicLink(directory);
        foreach (var child in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(child);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnsafeStorageTreeException("Symbolic links are not allowed in deletion trees.");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                await DeleteDirectoryAsync(child, cancellationToken);
            }
            else
            {
                File.Delete(child);
            }
        }

        Directory.Delete(directory, false);
    }

    private void EnsureNoSymbolicLink(string path, bool unsafeTree = false)
    {
        var current = new DirectoryInfo(path);
        while (current.FullName.StartsWith(root, StringComparison.Ordinal))
        {
            if (current.Exists && current.LinkTarget is not null)
            {
                ThrowSymbolicLink(unsafeTree);
            }

            if (string.Equals(current.FullName, root, StringComparison.Ordinal))
            {
                break;
            }

            if (current.Parent is null)
            {
                break;
            }

            current = current.Parent;
        }

        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null)
        {
            ThrowSymbolicLink(unsafeTree);
        }
    }

    private static void ThrowSymbolicLink(bool unsafeTree)
    {
        if (unsafeTree)
        {
            throw new UnsafeStorageTreeException("Symbolic links are not allowed in deletion trees.");
        }

        throw new IOException("Symbolic links are not allowed in storage paths.");
    }
}
