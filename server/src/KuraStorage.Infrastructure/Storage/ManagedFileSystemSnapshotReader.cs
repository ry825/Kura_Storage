using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Storage;

public sealed class ManagedFileSystemSnapshotReader(IOptions<StorageOptions> configuredOptions)
    : IManagedFileSystemSnapshotReader
{
    private readonly string root = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(configuredOptions.Value.RootPath));

    public async IAsyncEnumerable<ObservedStorageEntry> EnumerateAsync(
        StorageSnapshotContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (context.ObservationId == Guid.Empty || context.BatchSize <= 0)
        {
            throw new ArgumentException("A valid snapshot context is required.", nameof(context));
        }

        var usersRoot = Path.Combine(root, "users");
        if (!Directory.Exists(usersRoot))
        {
            yield break;
        }

        EnsureSafeDirectory(usersRoot);
        var observedSinceCancellation = 0;
        foreach (var ownerDirectory in Directory.EnumerateDirectories(usersRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(ownerDirectory), "N", out var ownerUserId))
            {
                continue;
            }

            EnsureSafeDirectory(ownerDirectory);

            var filesRoot = Path.Combine(ownerDirectory, "files");
            if (!Directory.Exists(filesRoot))
            {
                continue;
            }

            EnsureSafeDirectory(filesRoot);
            var pending = new Queue<string>();
            pending.Enqueue(filesRoot);
            while (pending.TryDequeue(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var child in EnumerateChildren(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var observed = CreateObservation(ownerUserId, child);
                    if (observed is null)
                    {
                        continue;
                    }

                    if (observed.EntryType == FileEntryType.Folder)
                    {
                        pending.Enqueue(child);
                    }

                    yield return observed;
                    if (++observedSinceCancellation >= context.BatchSize)
                    {
                        observedSinceCancellation = 0;
                        await Task.Yield();
                    }
                }
            }
        }
    }

    public Task<ObservedStorageEntry?> InspectAsync(RelativeStoragePath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ownerUserId = ParseManagedOwner(path);
        var physicalPath = Resolve(path);
        return Task.FromResult(CreateObservation(ownerUserId, physicalPath));
    }

    private ObservedStorageEntry? CreateObservation(Guid ownerUserId, string physicalPath)
    {
        var relativeValue = Path.GetRelativePath(root, physicalPath).Replace(Path.DirectorySeparatorChar, '/');
        if (!RelativeStoragePath.TryCreate(relativeValue, out var relativePath) ||
            !FileName.TryCreate(Path.GetFileName(physicalPath), out var name))
        {
            return null;
        }

        var parentValue = Path.GetRelativePath(root, Path.GetDirectoryName(physicalPath)!)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!RelativeStoragePath.TryCreate(parentValue, out var parentPath))
        {
            return null;
        }

        if (NativeMethods.Statx(
                NativeMethods.AtCurrentWorkingDirectory,
                physicalPath,
                NativeMethods.AtSymlinkNoFollow,
                NativeMethods.StatxBasicStats,
                out var stat) != 0)
        {
            if (Marshal.GetLastPInvokeError() is NativeMethods.NoSuchFileOrDirectory or NativeMethods.NotADirectory)
            {
                return null;
            }

            return Isolated(ownerUserId, relativePath, parentPath, name, "ENTRY_INSPECTION_FAILED");
        }

        var fileType = stat.Mode & NativeMethods.FileTypeMask;
        if (fileType == NativeMethods.SymbolicLink)
        {
            return Isolated(ownerUserId, relativePath, parentPath, name, "SYMBOLIC_LINK");
        }

        if (fileType is not (NativeMethods.RegularFile or NativeMethods.Directory))
        {
            return Isolated(ownerUserId, relativePath, parentPath, name, "SPECIAL_FILE");
        }

        var isDirectory = fileType == NativeMethods.Directory;
        var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(physicalPath) : new FileInfo(physicalPath);
        if (info.LinkTarget is not null)
        {
            return Isolated(ownerUserId, relativePath, parentPath, name, "SYMBOLIC_LINK");
        }

        var size = isDirectory ? 0 : ((FileInfo)info).Length;
        return new ObservedStorageEntry(
            ownerUserId,
            relativePath,
            parentPath,
            name,
            isDirectory ? FileEntryType.Folder : FileEntryType.File,
            size,
            isDirectory ? null : MimeTypeFor(name.Value),
            TruncateToPostgreSqlPrecision(info.LastWriteTimeUtc),
            $"{stat.DeviceMajor:x8}:{stat.DeviceMinor:x8}:{stat.Inode:x16}");
    }

    private static DateTimeOffset TruncateToPostgreSqlPrecision(DateTimeOffset value) =>
        new(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond,
            value.Offset);

    private static ObservedStorageEntry Isolated(
        Guid ownerUserId,
        RelativeStoragePath relativePath,
        RelativeStoragePath parentPath,
        FileName name,
        string reason) =>
        new(
            ownerUserId,
            relativePath,
            parentPath,
            name,
            FileEntryType.File,
            0,
            null,
            DateTimeOffset.UnixEpoch,
            null,
            reason);

    private Guid ParseManagedOwner(RelativeStoragePath path)
    {
        var segments = path.Value.Split('/');
        if (segments.Length < 4 || segments[0] != "users" || segments[2] != "files" ||
            !Guid.TryParseExact(segments[1], "N", out var ownerUserId))
        {
            throw new IOException("The path is outside the managed user file namespace.");
        }

        return ownerUserId;
    }

    private string Resolve(RelativeStoragePath path)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, path.Value.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("The storage path is outside the configured root.");
        }

        return candidate;
    }

    private static void EnsureSafeDirectory(string path)
    {
        if (new DirectoryInfo(path).LinkTarget is not null)
        {
            throw new IndexSnapshotIncompleteException("A symbolic link was found in the managed directory chain.");
        }
    }

    private static IEnumerable<string> EnumerateChildren(string directory)
    {
        IEnumerator<string> enumerator;
        try
        {
            enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IndexSnapshotIncompleteException("A managed directory could not be enumerated.", exception);
        }

        using (enumerator)
        {
            while (true)
            {
                string child;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    child = enumerator.Current;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IndexSnapshotIncompleteException("A managed directory could not be enumerated.", exception);
                }

                yield return child;
            }
        }
    }

    private static string? MimeTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".json" => "application/json",
        ".mp4" => "video/mp4",
        ".mp3" => "audio/mpeg",
        _ => "application/octet-stream",
    };

    private static class NativeMethods
    {
        internal const int AtCurrentWorkingDirectory = -100;
        internal const int AtSymlinkNoFollow = 0x100;
        internal const uint StatxBasicStats = 0x7ff;
        internal const int NoSuchFileOrDirectory = 2;
        internal const int NotADirectory = 20;
        internal const ushort FileTypeMask = 0xf000;
        internal const ushort RegularFile = 0x8000;
        internal const ushort Directory = 0x4000;
        internal const ushort SymbolicLink = 0xa000;

        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        internal static extern int Statx(int directoryFileDescriptor, string path, int flags, uint mask, out StatxBuffer buffer);

        [StructLayout(LayoutKind.Sequential)]
        internal struct StatxBuffer
        {
            internal uint Mask;
            internal uint BlockSize;
            internal ulong Attributes;
            internal uint HardLinkCount;
            internal uint UserId;
            internal uint GroupId;
            internal ushort Mode;
            internal ushort Spare0;
            internal ulong Inode;
            internal ulong Size;
            internal ulong Blocks;
            internal ulong AttributesMask;
            internal StatxTimestamp AccessTime;
            internal StatxTimestamp BirthTime;
            internal StatxTimestamp ChangeTime;
            internal StatxTimestamp ModifiedTime;
            internal uint RDeviceMajor;
            internal uint RDeviceMinor;
            internal uint DeviceMajor;
            internal uint DeviceMinor;
            internal ulong MountId;
            internal uint DirectIoMemoryAlignment;
            internal uint DirectIoOffsetAlignment;
            internal ulong Spare1;
            internal ulong Spare2;
            internal ulong Spare3;
            internal ulong Spare4;
            internal ulong Spare5;
            internal ulong Spare6;
            internal ulong Spare7;
            internal ulong Spare8;
            internal ulong Spare9;
            internal ulong Spare10;
            internal ulong Spare11;
            internal ulong Spare12;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StatxTimestamp
        {
            internal long Seconds;
            internal uint Nanoseconds;
            internal int Reserved;
        }
    }
}
