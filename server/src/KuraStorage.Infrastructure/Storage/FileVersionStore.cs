using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Storage;

public sealed class FileVersionStore(IOptions<StorageOptions> configuredOptions) : IFileVersionStore
{
    private const int BufferSize = 64 * 1024;
    private readonly string root = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(configuredOptions.Value.RootPath));
    private readonly long minimumFreeBytes = configuredOptions.Value.MinimumFreeBytes;

    public async Task<PublishedFileVersion?> TryPublishAsync(
        Guid ownerUserId,
        Guid fileEntryId,
        long version,
        Guid operationId,
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (ownerUserId == Guid.Empty || fileEntryId == Guid.Empty || operationId == Guid.Empty)
        {
            throw new ArgumentException("Owner, file, and operation IDs are required.");
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (expectedSize is < 0 or > FileVersionRecord.MaximumContentBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSize));
        }

        ArgumentNullException.ThrowIfNull(source);
        try
        {
            if (expectedSize > long.MaxValue - minimumFreeBytes ||
                new DriveInfo(root).AvailableFreeSpace < expectedSize + minimumFreeBytes)
            {
                throw new FileVersionStorageUnavailableException();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new FileVersionStorageUnavailableException();
        }
        var temporary = RelativeStoragePath.Create(
            $"version-temp/{ownerUserId:N}/{fileEntryId:N}/{version}/{operationId:N}.part");
        var temporaryPath = Resolve(temporary, requireExisting: false);

        var bytes = ArrayPool<byte>.Shared.Rent(BufferSize);
        var characters = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(BufferSize));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
            EnsureNoSymbolicLink(Path.GetDirectoryName(temporaryPath)!);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var decoder = new UTF8Encoding(false, true).GetDecoder();
            long total = 0;
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                while (true)
                {
                    var read = await source.ReadAsync(bytes.AsMemory(0, BufferSize), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total = checked(total + read);
                    if (total > expectedSize || total > FileVersionRecord.MaximumContentBytes)
                    {
                        throw new FileVersionContentSizeException();
                    }

                    decoder.Convert(
                        bytes,
                        0,
                        read,
                        characters,
                        0,
                        characters.Length,
                        flush: false,
                        out var bytesUsed,
                        out _,
                        out var completed);
                    if (!completed || bytesUsed != read)
                    {
                        throw new FileVersionEncodingException();
                    }

                    hash.AppendData(bytes, 0, read);
                    await destination.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
                }

                decoder.Convert(
                    [], 0, 0, characters, 0, characters.Length, flush: true,
                    out _, out _, out var finalCompleted);
                if (!finalCompleted || total != expectedSize)
                {
                    throw total != expectedSize
                        ? new FileVersionContentSizeException()
                        : new FileVersionEncodingException();
                }

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var published = RelativeStoragePath.Create(
                $"versions/{ownerUserId:N}/{fileEntryId:N}/{version}/{sha256}.bin");
            var publishedPath = Resolve(published, requireExisting: false);
            Directory.CreateDirectory(Path.GetDirectoryName(publishedPath)!);
            EnsureNoSymbolicLink(Path.GetDirectoryName(publishedPath)!);
            if (File.Exists(publishedPath))
            {
                await ValidateExistingAsync(publishedPath, expectedSize, sha256, cancellationToken);
                File.Delete(temporaryPath);
                return new PublishedFileVersion(temporary, published, expectedSize, sha256);
            }

            File.Move(temporaryPath, publishedPath);
            return new PublishedFileVersion(temporary, published, expectedSize, sha256);
        }
        catch (DecoderFallbackException exception)
        {
            TryDeleteTemporary(temporaryPath);
            _ = exception;
            return null;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is IOException and
                not FileVersionConsistencyException and
                not FileVersionContentSizeException and
                not FileVersionEncodingException and
                not FileVersionStorageUnavailableException)
        {
            TryDeleteTemporary(temporaryPath);
            throw new FileVersionStorageUnavailableException();
        }
        catch
        {
            TryDeleteTemporary(temporaryPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(characters);
        }
    }

    public async Task<Stream> OpenReadAsync(
        RelativeStoragePath contentPath,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (expectedSize is < 0 or > FileVersionRecord.MaximumContentBytes ||
            expectedSha256.Length != 64 ||
            expectedSha256.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            !IsVersionContentPath(contentPath, expectedSha256))
        {
            throw new FileVersionConsistencyException();
        }

        var path = Resolve(contentPath, requireExisting: true);
        await ValidateExistingAsync(path, expectedSize, expectedSha256, cancellationToken);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return stream;
    }

    private async Task ValidateExistingAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != expectedSize || info.LinkTarget is not null)
        {
            throw new FileVersionConsistencyException();
        }

        await using var existing = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(existing, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expectedSha256)))
        {
            throw new FileVersionConsistencyException();
        }
    }

    private static bool IsVersionContentPath(RelativeStoragePath path, string expectedSha256)
    {
        var segments = path.Value.Split('/');
        return segments.Length == 5 &&
               segments[0] == "versions" &&
               Guid.TryParseExact(segments[1], "N", out var ownerId) &&
               ownerId != Guid.Empty &&
               Guid.TryParseExact(segments[2], "N", out var fileId) &&
               fileId != Guid.Empty &&
               long.TryParse(segments[3], out var version) &&
               version >= 1 &&
               string.Equals(segments[4], expectedSha256 + ".bin", StringComparison.Ordinal);
    }

    private string Resolve(RelativeStoragePath relativePath, bool requireExisting)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Value.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("The version path is outside the configured root.");
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
                throw new IOException("Symbolic links are not allowed in version storage.");
            }

            if (string.Equals(current.FullName, root, StringComparison.Ordinal) || current.Parent is null)
            {
                break;
            }

            current = current.Parent;
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
        }
    }
}

public sealed class FileVersionContentSizeException : IOException;

public sealed class FileVersionEncodingException : IOException;
