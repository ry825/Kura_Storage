using KuraStorage.Application.Abstractions;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Storage;

public sealed class StorageGuard(IOptions<StorageOptions> options) : IStorageGuard
{
    private readonly StorageOptions options = options.Value;

    public async Task<StorageStatus> InspectAsync(bool requireWrite, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(options.RootPath) || !Directory.Exists(options.RootPath))
        {
            return StorageStatus.Unavailable;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        if (!await IsMountPointAsync(root, cancellationToken) || ContainsSymbolicLink(root))
        {
            return StorageStatus.Unavailable;
        }

        var identityPath = Path.Combine(root, ".storage-identity");
        if (!File.Exists(identityPath) || ContainsSymbolicLink(identityPath))
        {
            return StorageStatus.Unavailable;
        }

        var identity = (await File.ReadAllTextAsync(identityPath, cancellationToken)).Trim();
        if (!string.Equals(identity, options.StorageId, StringComparison.Ordinal))
        {
            return StorageStatus.Unavailable;
        }

        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.AvailableFreeSpace < options.MinimumFreeBytes)
        {
            return StorageStatus.Unavailable;
        }

        if (!requireWrite)
        {
            return StorageStatus.Available;
        }

        var probe = Path.Combine(root, $".kurastorage-write-probe-{Guid.NewGuid():N}");
        try
        {
            await using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(new byte[] { 0x4b }, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return StorageStatus.Available;
        }
        catch (UnauthorizedAccessException)
        {
            return StorageStatus.ReadOnly;
        }
        catch (IOException)
        {
            return StorageStatus.ReadOnly;
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

    private static bool ContainsSymbolicLink(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (current.LinkTarget is not null)
            {
                return true;
            }

            current = current.Parent;
        }

        return new FileInfo(path).LinkTarget is not null;
    }

    private static async Task<bool> IsMountPointAsync(string root, CancellationToken cancellationToken)
    {
        var mountInfo = await File.ReadAllLinesAsync("/proc/self/mountinfo", cancellationToken);
        foreach (var line in mountInfo)
        {
            var fields = line.Split(' ');
            if (fields.Length > 4 && string.Equals(UnescapeMountPath(fields[4]), root, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string UnescapeMountPath(string value) =>
        value.Replace(@"\040", " ", StringComparison.Ordinal)
            .Replace(@"\011", "\t", StringComparison.Ordinal)
            .Replace(@"\012", "\n", StringComparison.Ordinal)
            .Replace(@"\134", "\\", StringComparison.Ordinal);
}
