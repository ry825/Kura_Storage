using KuraStorage.Application.Indexing;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Indexing;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class LinuxInotifyWatcherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"kurastorage-inotify-{Guid.NewGuid():N}");
    private readonly Guid ownerId = Guid.NewGuid();

    [Fact]
    public async Task WatchAsync_ReportsCreateCloseWriteMoveAndDeleteWithRelativePaths()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var filesRoot = PrepareFilesRoot();
        var watcher = CreateWatcher();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var events = watcher.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        var firstPath = $"users/{ownerId:N}/files/first.txt";
        var secondPath = $"users/{ownerId:N}/files/second.txt";

        var create = NextMatchingAsync(events, change => change.RelativePath == firstPath);
        await Task.Delay(100, cancellation.Token);
        await File.WriteAllTextAsync(Path.Combine(filesRoot, "first.txt"), "first", cancellation.Token);
        Assert.Equal(IndexChangeKind.Reconcile, (await create).Kind);

        var move = NextMatchingAsync(events, change => change.Kind == IndexChangeKind.Move);
        File.Move(Path.Combine(filesRoot, "first.txt"), Path.Combine(filesRoot, "second.txt"));
        var moved = await move;
        Assert.Equal(firstPath, moved.PreviousRelativePath);
        Assert.Equal(secondPath, moved.RelativePath);

        var delete = NextMatchingAsync(events, change => change.RelativePath == secondPath);
        File.Delete(Path.Combine(filesRoot, "second.txt"));
        Assert.Equal(IndexChangeKind.Reconcile, (await delete).Kind);
    }

    [Fact]
    public async Task WatchAsync_CancellationClosesReadLoopPromptly()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        PrepareFilesRoot();
        var watcher = CreateWatcher();
        using var cancellation = new CancellationTokenSource();
        await using var events = watcher.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        var pending = events.MoveNextAsync().AsTask();
        await Task.Delay(100);
        cancellation.Cancel();

        var producedAnotherEvent = false;
        try
        {
            producedAnotherEvent = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Either cancellation shape is valid for an async stream.
        }

        Assert.False(producedAnotherEvent);
    }

    [Fact]
    public async Task WatchAsync_NewFolderRecoversChildrenCreatedBeforeWatchWasAdded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var filesRoot = PrepareFilesRoot();
        var watcher = CreateWatcher();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var events = watcher.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        var childPath = $"users/{ownerId:N}/files/new-folder/child.txt";

        var childEvent = NextMatchingAsync(events, change => change.RelativePath == childPath);
        await Task.Delay(100, cancellation.Token);
        var folder = Path.Combine(filesRoot, "new-folder");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "child.txt"), "child", cancellation.Token);

        Assert.Equal(IndexChangeKind.Reconcile, (await childEvent).Kind);
    }

    [Fact]
    public async Task WatchAsync_UnpairedMoveBecomesPathReconciliationAfterPairingWindow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var filesRoot = PrepareFilesRoot();
        var source = Path.Combine(filesRoot, "leaving.txt");
        await File.WriteAllTextAsync(source, "content");
        var watcher = CreateWatcher();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var events = watcher.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        var relativePath = $"users/{ownerId:N}/files/leaving.txt";
        var reconciliation = NextMatchingAsync(events, change =>
            change.Kind == IndexChangeKind.Reconcile && change.RelativePath == relativePath);

        await Task.Delay(100, cancellation.Token);
        File.Move(source, Path.Combine(root, "outside.txt"));

        Assert.Equal(relativePath, (await reconciliation).RelativePath);
    }

    [Fact]
    public async Task WatchAsync_FolderMoveKeepsDescendantWatchActive()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var filesRoot = PrepareFilesRoot();
        var original = Path.Combine(filesRoot, "before-folder");
        Directory.CreateDirectory(original);
        await File.WriteAllTextAsync(Path.Combine(original, "child.txt"), "child");
        var watcher = CreateWatcher();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var events = watcher.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        var movedFolderPath = $"users/{ownerId:N}/files/after-folder";
        var childPath = $"{movedFolderPath}/child.txt";

        var folderMove = NextMatchingAsync(events, change =>
            change.Kind == IndexChangeKind.Move && change.RelativePath == movedFolderPath);
        await Task.Delay(100, cancellation.Token);
        var moved = Path.Combine(filesRoot, "after-folder");
        Directory.Move(original, moved);
        await folderMove;

        var childDelete = NextMatchingAsync(events, change => change.RelativePath == childPath);
        File.Delete(Path.Combine(moved, "child.txt"));

        Assert.Equal(IndexChangeKind.Reconcile, (await childDelete).Kind);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string PrepareFilesRoot()
    {
        var filesRoot = Path.Combine(root, "users", ownerId.ToString("N"), "files");
        Directory.CreateDirectory(filesRoot);
        return filesRoot;
    }

    private LinuxInotifyWatcher CreateWatcher() =>
        new(
            Options.Create(new StorageOptions
            {
                RootPath = root,
                StorageId = "test-storage",
            }),
            Options.Create(new KuraStorage.Infrastructure.Configuration.IndexingOptions
            {
                MovePairingWindowMilliseconds = 1000,
            }));

    private static async Task<IndexChangeEvent> NextMatchingAsync(
        IAsyncEnumerator<IndexChangeEvent> events,
        Func<IndexChangeEvent, bool> predicate)
    {
        while (await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            if (predicate(events.Current))
            {
                return events.Current;
            }
        }

        throw new InvalidOperationException("The watcher stopped before the expected event arrived.");
    }
}
