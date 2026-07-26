using System.Buffers;
using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Storage;

public sealed class FileStore(IOptions<StorageOptions> configuredOptions) : IFileStore
{
    private readonly StorageOptions options = configuredOptions.Value;
    private readonly string root = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(configuredOptions.Value.RootPath));

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

    public async Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"users/{ownerUserId:N}/files"), false));
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"users/{ownerUserId:N}/trash"), false));
        Directory.CreateDirectory(Resolve(RelativeStoragePath.Create($"upload-temp/{ownerUserId:N}"), false));
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

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        EnsureNoSymbolicLink(Path.GetDirectoryName(targetPath)!);
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

    private string Resolve(RelativeStoragePath relativePath, bool requireExisting)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Value.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new IOException("The storage path is outside the configured root.");
        }

        EnsureNoSymbolicLink(requireExisting ? candidate : Path.GetDirectoryName(candidate)!);
        return candidate;
    }

    private void EnsureNoSymbolicLink(string path)
    {
        var current = new DirectoryInfo(path);
        while (current.FullName.StartsWith(root, StringComparison.Ordinal))
        {
            if (current.Exists && current.LinkTarget is not null)
            {
                throw new IOException("Symbolic links are not allowed in storage paths.");
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
            throw new IOException("Symbolic links are not allowed in storage paths.");
        }
    }
}
