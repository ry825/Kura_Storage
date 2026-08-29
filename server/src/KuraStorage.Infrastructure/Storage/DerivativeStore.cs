using System.Buffers;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Storage;

public sealed class DerivativeStore(
    IOptions<StorageOptions> storageOptions,
    IOptions<MediaOptions> mediaOptions,
    IStorageGuard storageGuard) : IDerivativeStore
{
    private readonly string root = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(storageOptions.Value.RootPath));
    private readonly MediaOptions media = mediaOptions.Value;

    public Task<PublishedDerivative?> FindPublishedAsync(
        MediaGenerationContext context,
        string extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.OwnerUserId == Guid.Empty || context.SourceFileId == Guid.Empty ||
            context.SourceVersion < 1 || context.ProfileVersion < 1 || !IsSafeExtension(extension))
        {
            throw new ArgumentException("Published derivative metadata is invalid.", nameof(context));
        }

        var relative = PublishedPath(
            context.OwnerUserId,
            context.SourceFileId,
            context.SourceVersion,
            context.ProfileVersion,
            context.DerivativeType,
            extension);
        var path = Resolve(relative, false);
        if (!File.Exists(path))
        {
            return Task.FromResult<PublishedDerivative?>(null);
        }

        EnsureNoSymbolicLink(path);
        var size = new FileInfo(path).Length;
        return Task.FromResult<PublishedDerivative?>(size > 0 ? new PublishedDerivative(relative, size) : null);
    }

    public async Task<DerivativeTemporaryFile> WriteTemporaryAsync(
        Guid jobId,
        int attempt,
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty || attempt < 1 || expectedSize < 0)
        {
            throw new ArgumentException("Temporary derivative metadata is invalid.");
        }

        await EnsureWritableAsync(cancellationToken);
        var relative = RelativeStoragePath.Create($"{media.TemporaryRoot}/{jobId:N}/{attempt}.part");
        var path = Resolve(relative, false);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        EnsureNoSymbolicLink(directory);
        if (File.Exists(path))
        {
            throw new DerivativePublishConflictException();
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
            long total = 0;
            while (total < expectedSize)
            {
                var count = (int)Math.Min(buffer.Length, expectedSize - total);
                var read = await source.ReadAsync(buffer.AsMemory(0, count), cancellationToken);
                if (read == 0)
                {
                    throw new IOException("The derivative output ended before its verified size.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total = checked(total + read);
            }

            if (await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            {
                throw new IOException("The derivative output exceeded its verified size.");
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
            return new DerivativeTemporaryFile(relative, total, jobId, attempt);
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

    public async Task<PublishedDerivative> PublishAsync(
        DerivativeTemporaryFile temporary,
        Guid ownerUserId,
        Guid sourceFileId,
        long sourceVersion,
        int profileVersion,
        DerivativeType derivativeType,
        string extension,
        long verifiedSize,
        CancellationToken cancellationToken)
    {
        if (ownerUserId == Guid.Empty || sourceFileId == Guid.Empty || sourceVersion < 1 ||
            profileVersion < 1 || verifiedSize <= 0 || !IsSafeExtension(extension))
        {
            throw new ArgumentException("Published derivative metadata is invalid.");
        }

        var expectedTemporary = RelativeStoragePath.Create(
            $"{media.TemporaryRoot}/{temporary.JobId:N}/{temporary.Attempt}.part");
        if (temporary.Path != expectedTemporary || temporary.Size != verifiedSize)
        {
            throw new UnsafeDerivativePathException();
        }

        await EnsureWritableAsync(cancellationToken);
        var formal = PublishedPath(
            ownerUserId, sourceFileId, sourceVersion, profileVersion, derivativeType, extension);
        var temporaryPath = Resolve(temporary.Path, true);
        var formalPath = Resolve(formal, false);
        var temporaryInfo = new FileInfo(temporaryPath);
        if (!temporaryInfo.Exists || temporaryInfo.Length != verifiedSize ||
            !string.Equals(temporaryInfo.Extension, ".part", StringComparison.Ordinal))
        {
            throw new IOException("The temporary derivative failed publication validation.");
        }

        var formalDirectory = Path.GetDirectoryName(formalPath)!;
        Directory.CreateDirectory(formalDirectory);
        EnsureNoSymbolicLink(formalDirectory);
        if (File.Exists(formalPath) || Directory.Exists(formalPath))
        {
            throw new DerivativePublishConflictException();
        }

        File.Move(temporaryPath, formalPath, overwrite: false);
        return new PublishedDerivative(formal, verifiedSize);
    }

    public async Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureManagedPath(path);
        var resolved = Resolve(path, true);
        Stream stream = new FileStream(
            resolved,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await Task.FromResult(stream);
    }

    public async Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        await EnsureWritableAsync(cancellationToken);
        EnsureManagedPath(path);
        var resolved = Resolve(path, false);
        if (Directory.Exists(resolved))
        {
            throw new UnsafeDerivativePathException();
        }

        if (File.Exists(resolved))
        {
            EnsureNoSymbolicLink(resolved);
            File.Delete(resolved);
        }
    }

    public async Task DeleteTemporaryAsync(
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty || attempt < 1)
        {
            throw new ArgumentException("Temporary derivative metadata is invalid.");
        }

        await EnsureWritableAsync(cancellationToken);
        var jobRoot = RelativeStoragePath.Create($"{media.TemporaryRoot}/{jobId:N}");
        var part = RelativeStoragePath.Create($"{jobRoot.Value}/{attempt}.part");
        await DeleteIfExistsAsync(part, cancellationToken);

        var workspace = Resolve(RelativeStoragePath.Create($"{jobRoot.Value}/{attempt}-work"), false);
        if (Directory.Exists(workspace))
        {
            EnsureNoSymbolicLink(workspace);
            Directory.Delete(workspace, recursive: true);
        }

        var jobDirectory = Resolve(jobRoot, false);
        if (Directory.Exists(jobDirectory) && !Directory.EnumerateFileSystemEntries(jobDirectory).Any())
        {
            Directory.Delete(jobDirectory);
        }
    }

    private async Task EnsureWritableAsync(CancellationToken cancellationToken)
    {
        var status = await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken);
        if (status != StorageStatus.Available)
        {
            throw new DerivativeStorageUnavailableException(status);
        }
    }

    private string Resolve(RelativeStoragePath relativePath, bool requireExisting)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Value.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UnsafeDerivativePathException();
        }

        EnsureNoSymbolicLink(requireExisting ? candidate : Path.GetDirectoryName(candidate)!);
        return candidate;
    }

    private void EnsureManagedPath(RelativeStoragePath path)
    {
        if (!path.Value.StartsWith(media.DerivativeRoot + "/", StringComparison.Ordinal) &&
            !path.Value.StartsWith(media.TemporaryRoot + "/", StringComparison.Ordinal))
        {
            throw new UnsafeDerivativePathException();
        }
    }

    private void EnsureNoSymbolicLink(string path)
    {
        var current = new DirectoryInfo(path);
        while (current.FullName.StartsWith(root, StringComparison.Ordinal))
        {
            if (current.Exists && current.LinkTarget is not null)
            {
                throw new UnsafeDerivativePathException();
            }

            if (string.Equals(current.FullName, root, StringComparison.Ordinal) || current.Parent is null)
            {
                break;
            }

            current = current.Parent;
        }

        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null)
        {
            throw new UnsafeDerivativePathException();
        }
    }

    private static bool IsSafeExtension(string extension) =>
        extension is { Length: >= 1 and <= 10 } && extension.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9');

    private RelativeStoragePath PublishedPath(
        Guid ownerUserId,
        Guid sourceFileId,
        long sourceVersion,
        int profileVersion,
        DerivativeType derivativeType,
        string extension) =>
        RelativeStoragePath.Create(
            $"{media.DerivativeRoot}/{ownerUserId:N}/{sourceFileId:N}/{sourceVersion}/{profileVersion}/" +
            $"{TypeSegment(derivativeType)}.{extension}");

    private static string TypeSegment(DerivativeType type) => type switch
    {
        DerivativeType.Thumbnail => "thumbnail",
        DerivativeType.PdfThumbnail => "pdf-thumbnail",
        DerivativeType.ImageLow => "image-low",
        DerivativeType.ImageMedium => "image-medium",
        DerivativeType.VideoLow => "video-low",
        DerivativeType.VideoMedium => "video-medium",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

public sealed class DerivativeStorageUnavailableException(StorageStatus status)
    : IOException($"Derivative storage is unavailable ({status}).");

public sealed class DerivativePublishConflictException()
    : IOException("The derivative publication target already exists.");

public sealed class UnsafeDerivativePathException()
    : IOException("The derivative path is unsafe.");
