using System.Buffers.Binary;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace KuraStorage.Infrastructure.Indexing;

public sealed class LinuxInotifyWatcher(
    IOptions<StorageOptions> storageOptions,
    IOptions<Configuration.IndexingOptions> indexingOptions) : IIndexChangeWatcher
{
    private static readonly Meter Meter = new("KuraStorage.Indexing");
    private static readonly UpDownCounter<long> ActiveWatchers =
        Meter.CreateUpDownCounter<long>("kurastorage.index.watcher.active");
    private static readonly Counter<long> OverflowCount =
        Meter.CreateCounter<long>("kurastorage.index.watcher.overflow");
    private static readonly Histogram<long> EventAge =
        Meter.CreateHistogram<long>("kurastorage.index.event.age", "ms");

    private readonly string root = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(storageOptions.Value.RootPath));
    private readonly TimeSpan pairingWindow = TimeSpan.FromMilliseconds(
        indexingOptions.Value.MovePairingWindowMilliseconds);

    public async IAsyncEnumerable<IndexChangeEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Index change monitoring requires Linux inotify.");
        }

        var descriptor = NativeMethods.InotifyInit1(NativeMethods.InNonBlock | NativeMethods.InCloseOnExec);
        if (descriptor < 0)
        {
            throw NativeError("inotify initialization failed");
        }

        using var handle = new SafeInotifyHandle(descriptor);
        var watches = new Dictionary<int, string>();
        var watchByPath = new Dictionary<string, int>(StringComparer.Ordinal);
        var pendingMoves = new Dictionary<uint, PendingMove>();
        var activeRecorded = false;
        try
        {
            var initialWatchFailed = false;
            try
            {
                AddInitialWatches(descriptor, watches, watchByPath);
            }
            catch (IOException)
            {
                initialWatchFailed = true;
            }

            if (initialWatchFailed)
            {
                OverflowCount.Add(1, new KeyValuePair<string, object?>("reason", "WATCH_LIMIT"));
                yield return new IndexChangeEvent(IndexChangeKind.Overflow, string.Empty);
                yield break;
            }

            ActiveWatchers.Add(1);
            activeRecorded = true;
            var buffer = new byte[64 * 1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                var pollDescriptor = new NativeMethods.PollDescriptor
                {
                    FileDescriptor = descriptor,
                    Events = NativeMethods.PollIn,
                };
                var pollResult = NativeMethods.Poll(ref pollDescriptor, 1, 250);
                if (pollResult < 0)
                {
                    if (Marshal.GetLastPInvokeError() == NativeMethods.Interrupted)
                    {
                        continue;
                    }

                    yield return new IndexChangeEvent(IndexChangeKind.WatcherStopped, string.Empty);
                    yield break;
                }

                if (pollResult > 0)
                {
                    var bytesRead = NativeMethods.Read(descriptor, buffer, (nuint)buffer.Length);
                    if (bytesRead < 0)
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (error is not (NativeMethods.TryAgain or NativeMethods.Interrupted))
                        {
                            yield return new IndexChangeEvent(IndexChangeKind.WatcherStopped, string.Empty);
                            yield break;
                        }
                    }
                    else
                    {
                        foreach (var rawEvent in Parse(buffer.AsSpan(0, checked((int)bytesRead))))
                        {
                            if ((rawEvent.Mask & NativeMethods.QueueOverflow) != 0)
                            {
                                pendingMoves.Clear();
                                OverflowCount.Add(1, new KeyValuePair<string, object?>("reason", "KERNEL_QUEUE"));
                                yield return new IndexChangeEvent(IndexChangeKind.Overflow, string.Empty);
                                continue;
                            }

                            if (!watches.TryGetValue(rawEvent.WatchDescriptor, out var directoryPath))
                            {
                                continue;
                            }

                            var relativePath = rawEvent.Name.Length == 0
                                ? directoryPath
                                : $"{directoryPath}/{rawEvent.Name}";
                            if (!IsSafeManagedRelativePath(relativePath))
                            {
                                continue;
                            }

                            if ((rawEvent.Mask & NativeMethods.DeleteSelf) != 0)
                            {
                                _ = NativeMethods.InotifyRemoveWatch(descriptor, rawEvent.WatchDescriptor);
                                RemoveWatch(rawEvent.WatchDescriptor, watches, watchByPath);
                            }
                            else if ((rawEvent.Mask & NativeMethods.Ignored) != 0)
                            {
                                RemoveWatch(rawEvent.WatchDescriptor, watches, watchByPath);
                            }

                            if ((rawEvent.Mask & NativeMethods.MovedFrom) != 0 && rawEvent.Cookie != 0)
                            {
                                pendingMoves[rawEvent.Cookie] = new PendingMove(
                                    relativePath,
                                    DateTimeOffset.UtcNow,
                                    (rawEvent.Mask & NativeMethods.IsDirectory) != 0);
                                continue;
                            }

                            if ((rawEvent.Mask & NativeMethods.MovedTo) != 0 && rawEvent.Cookie != 0 &&
                                pendingMoves.Remove(rawEvent.Cookie, out var previous) &&
                                DateTimeOffset.UtcNow - previous.ObservedAt <= pairingWindow)
                            {
                                if (previous.IsDirectory)
                                {
                                    RebaseWatches(previous.RelativePath, relativePath, watches, watchByPath);
                                }

                                IReadOnlyList<string> discoveredEntries = [];
                                if (previous.IsDirectory &&
                                    !TryAddDirectoryTree(
                                        descriptor,
                                        relativePath,
                                        watches,
                                        watchByPath,
                                        out discoveredEntries))
                                {
                                    OverflowCount.Add(1, new KeyValuePair<string, object?>("reason", "WATCH_LIMIT"));
                                    yield return new IndexChangeEvent(IndexChangeKind.Overflow, string.Empty);
                                }
                                else if (previous.IsDirectory)
                                {
                                    foreach (var discovered in discoveredEntries)
                                    {
                                        yield return new IndexChangeEvent(IndexChangeKind.Reconcile, discovered);
                                    }
                                }

                                EventAge.Record((long)(DateTimeOffset.UtcNow - previous.ObservedAt).TotalMilliseconds);
                                yield return new IndexChangeEvent(
                                    IndexChangeKind.Move,
                                    relativePath,
                                    previous.RelativePath);
                                continue;
                            }

                            if ((rawEvent.Mask & NativeMethods.IsDirectory) != 0 &&
                                (rawEvent.Mask & (NativeMethods.Create | NativeMethods.MovedTo)) != 0)
                            {
                                if (!TryAddDirectoryTree(
                                        descriptor,
                                        relativePath,
                                        watches,
                                        watchByPath,
                                        out var discoveredEntries))
                                {
                                    OverflowCount.Add(1, new KeyValuePair<string, object?>("reason", "WATCH_LIMIT"));
                                    yield return new IndexChangeEvent(IndexChangeKind.Overflow, string.Empty);
                                }
                                else
                                {
                                    foreach (var discovered in discoveredEntries)
                                    {
                                        yield return new IndexChangeEvent(IndexChangeKind.Reconcile, discovered);
                                    }
                                }
                            }

                            if ((rawEvent.Mask & NativeMethods.RelevantChanges) != 0)
                            {
                                yield return new IndexChangeEvent(
                                    IndexChangeKind.Reconcile,
                                    relativePath,
                                    ContentMayHaveChanged: (rawEvent.Mask & NativeMethods.CloseWrite) != 0);
                            }
                        }
                    }
                }

                var staleBefore = DateTimeOffset.UtcNow - pairingWindow;
                foreach (var stale in pendingMoves.Where(pair => pair.Value.ObservedAt <= staleBefore).ToArray())
                {
                    pendingMoves.Remove(stale.Key);
                    if (stale.Value.IsDirectory)
                    {
                        RemoveWatchesBelow(
                            descriptor,
                            stale.Value.RelativePath,
                            watches,
                            watchByPath);
                    }

                    yield return new IndexChangeEvent(IndexChangeKind.Reconcile, stale.Value.RelativePath);
                }

                await Task.Yield();
            }
        }
        finally
        {
            if (activeRecorded)
            {
                ActiveWatchers.Add(-1);
            }
        }
    }

    private void AddInitialWatches(
        int descriptor,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath)
    {
        var usersRoot = Path.Combine(root, "users");
        if (!Directory.Exists(usersRoot))
        {
            return;
        }

        foreach (var ownerDirectory in Directory.EnumerateDirectories(usersRoot))
        {
            if (!Guid.TryParseExact(Path.GetFileName(ownerDirectory), "N", out _))
            {
                continue;
            }

            var filesDirectory = Path.Combine(ownerDirectory, "files");
            if (Directory.Exists(filesDirectory))
            {
                var relativePath = Path.GetRelativePath(root, filesDirectory)
                    .Replace(Path.DirectorySeparatorChar, '/');
                _ = AddDirectoryTree(descriptor, relativePath, watches, watchByPath);
            }
        }
    }

    private IReadOnlyList<string> AddDirectoryTree(
        int descriptor,
        string relativePath,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath)
    {
        var physicalPath = Resolve(relativePath);
        var pending = new Queue<(string Physical, string Relative)>();
        var discovered = new List<string>();
        pending.Enqueue((physicalPath, relativePath));
        while (pending.TryDequeue(out var directory))
        {
            if (new DirectoryInfo(directory.Physical).LinkTarget is not null)
            {
                continue;
            }

            AddWatch(descriptor, directory.Physical, directory.Relative, watches, watchByPath);
            foreach (var child in Directory.EnumerateDirectories(directory.Physical))
            {
                if (new DirectoryInfo(child).LinkTarget is null)
                {
                    var childRelative = $"{directory.Relative}/{Path.GetFileName(child)}";
                    discovered.Add(childRelative);
                    pending.Enqueue((child, childRelative));
                }
            }

            foreach (var child in Directory.EnumerateFiles(directory.Physical))
            {
                discovered.Add($"{directory.Relative}/{Path.GetFileName(child)}");
            }
        }

        return discovered;
    }

    private bool TryAddDirectoryTree(
        int descriptor,
        string relativePath,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath,
        out IReadOnlyList<string> discoveredEntries)
    {
        try
        {
            discoveredEntries = AddDirectoryTree(descriptor, relativePath, watches, watchByPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            discoveredEntries = [];
            return false;
        }
    }

    private static void RebaseWatches(
        string oldPrefix,
        string newPrefix,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath)
    {
        foreach (var watched in watches
                     .Where(pair => pair.Value == oldPrefix ||
                                    pair.Value.StartsWith(oldPrefix + "/", StringComparison.Ordinal))
                     .ToArray())
        {
            watchByPath.Remove(watched.Value);
            var rebased = newPrefix + watched.Value[oldPrefix.Length..];
            watches[watched.Key] = rebased;
            watchByPath[rebased] = watched.Key;
        }
    }

    private static void RemoveWatchesBelow(
        int descriptor,
        string prefix,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath)
    {
        foreach (var watched in watches
                     .Where(pair => pair.Value == prefix ||
                                    pair.Value.StartsWith(prefix + "/", StringComparison.Ordinal))
                     .ToArray())
        {
            _ = NativeMethods.InotifyRemoveWatch(descriptor, watched.Key);
            RemoveWatch(watched.Key, watches, watchByPath);
        }
    }

    private static void AddWatch(
        int descriptor,
        string physicalPath,
        string relativePath,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath)
    {
        if (watchByPath.ContainsKey(relativePath))
        {
            return;
        }

        var watchDescriptor = NativeMethods.InotifyAddWatch(descriptor, physicalPath, NativeMethods.WatchMask);
        if (watchDescriptor < 0)
        {
            throw NativeError("inotify watch creation failed");
        }

        watches[watchDescriptor] = relativePath;
        watchByPath[relativePath] = watchDescriptor;
    }

    private static void RemoveWatch(
        int watchDescriptor,
        IDictionary<int, string> watches,
        IDictionary<string, int> watchByPath)
    {
        if (watches.Remove(watchDescriptor, out var path))
        {
            watchByPath.Remove(path);
        }
    }

    private string Resolve(string relativePath)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("The watched path is outside the configured storage root.");
        }

        return candidate;
    }

    private static bool IsSafeManagedRelativePath(string relativePath)
    {
        if (relativePath.Contains('\0') || relativePath.StartsWith('/') || relativePath.Contains("//"))
        {
            return false;
        }

        var segments = relativePath.Split('/');
        return segments.Length >= 3 && segments[0] == "users" && segments[2] == "files" &&
               Guid.TryParseExact(segments[1], "N", out _) &&
               !segments.Skip(3).Any(segment => segment is "." or ".." || segment.Length == 0);
    }

    private static IReadOnlyList<RawEvent> Parse(ReadOnlySpan<byte> data)
    {
        const int headerLength = 16;
        var result = new List<RawEvent>();
        var offset = 0;
        while (offset + headerLength <= data.Length)
        {
            var watchDescriptor = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            var mask = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            var cookie = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 8)..]);
            var nameLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 12)..]));
            if (nameLength < 0 || offset + headerLength + nameLength > data.Length)
            {
                break;
            }

            var nameBytes = data.Slice(offset + headerLength, nameLength);
            var terminator = nameBytes.IndexOf((byte)0);
            if (terminator >= 0)
            {
                nameBytes = nameBytes[..terminator];
            }

            var name = Encoding.UTF8.GetString(nameBytes);
            result.Add(new RawEvent(watchDescriptor, mask, cookie, name));
            offset += headerLength + nameLength;
        }

        return result;
    }

    private static IOException NativeError(string message) =>
        new($"{message} (errno {Marshal.GetLastPInvokeError()}).");

    private sealed record PendingMove(string RelativePath, DateTimeOffset ObservedAt, bool IsDirectory);
    private sealed record RawEvent(int WatchDescriptor, uint Mask, uint Cookie, string Name);

    private sealed class SafeInotifyHandle : SafeHandleMinusOneIsInvalid
    {
        internal SafeInotifyHandle(int descriptor) : base(true)
        {
            SetHandle((IntPtr)descriptor);
        }

        protected override bool ReleaseHandle() => NativeMethods.Close(handle) == 0;
    }

    private static class NativeMethods
    {
        internal const int InNonBlock = 0x800;
        internal const int InCloseOnExec = 0x80000;
        internal const short PollIn = 0x001;
        internal const int Interrupted = 4;
        internal const int TryAgain = 11;
        internal const uint Create = 0x00000100;
        internal const uint CloseWrite = 0x00000008;
        internal const uint Attribute = 0x00000004;
        internal const uint Delete = 0x00000200;
        internal const uint MovedFrom = 0x00000040;
        internal const uint MovedTo = 0x00000080;
        internal const uint DeleteSelf = 0x00000400;
        internal const uint MoveSelf = 0x00000800;
        internal const uint QueueOverflow = 0x00004000;
        internal const uint Ignored = 0x00008000;
        internal const uint IsDirectory = 0x40000000;
        internal const uint RelevantChanges = Create | CloseWrite | Attribute | Delete | MovedFrom | MovedTo |
                                                 DeleteSelf | MoveSelf;
        internal const uint WatchMask = RelevantChanges | QueueOverflow;

        [DllImport("libc", EntryPoint = "inotify_init1", SetLastError = true)]
        internal static extern int InotifyInit1(int flags);

        [DllImport("libc", EntryPoint = "inotify_add_watch", SetLastError = true)]
        internal static extern int InotifyAddWatch(int descriptor, string path, uint mask);

        [DllImport("libc", EntryPoint = "inotify_rm_watch", SetLastError = true)]
        internal static extern int InotifyRemoveWatch(int descriptor, int watchDescriptor);

        [DllImport("libc", EntryPoint = "read", SetLastError = true)]
        internal static extern nint Read(int descriptor, byte[] buffer, nuint count);

        [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
        internal static extern int Poll(ref PollDescriptor descriptors, nuint count, int timeoutMilliseconds);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static extern int Close(IntPtr descriptor);

        [StructLayout(LayoutKind.Sequential)]
        internal struct PollDescriptor
        {
            internal int FileDescriptor;
            internal short Events;
            internal short ReturnedEvents;
        }
    }
}
