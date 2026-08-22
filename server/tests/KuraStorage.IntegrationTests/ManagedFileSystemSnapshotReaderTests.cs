using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace KuraStorage.IntegrationTests;

public sealed class ManagedFileSystemSnapshotReaderTests
{
    [Fact]
    public async Task EnumerateAsync_StreamsOnlyManagedRegularEntriesAndPreservesSourceKeyAcrossRename()
    {
        var root = Directory.CreateTempSubdirectory("kurastorage-index-");
        try
        {
            var ownerId = Guid.NewGuid();
            var files = Directory.CreateDirectory(Path.Combine(root.FullName, "users", ownerId.ToString("N"), "files"));
            var folder = Directory.CreateDirectory(Path.Combine(files.FullName, "docs"));
            var originalPath = Path.Combine(folder.FullName, "before.txt");
            await File.WriteAllTextAsync(originalPath, "content");
            Directory.CreateDirectory(Path.Combine(files.FullName, "empty"));
            var deep = files.FullName;
            for (var depth = 0; depth < 12; depth++)
            {
                deep = Directory.CreateDirectory(Path.Combine(deep, $"深い-{depth:D2}")).FullName;
            }

            await File.WriteAllTextAsync(Path.Combine(deep, "é.txt"), "unicode");
            await File.WriteAllTextAsync(Path.Combine(files.FullName, "Case.txt"), "upper");
            await File.WriteAllTextAsync(Path.Combine(files.FullName, "case.txt"), "lower");
            var fifoPath = Path.Combine(files.FullName, "special-fifo");
            Assert.Equal(0, MakeFifo(fifoPath, Convert.ToUInt32("600", 8)));
            Directory.CreateDirectory(Path.Combine(root.FullName, "users", ownerId.ToString("N"), "trash"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "users", "not-a-user", "files", "ignored"));
            Directory.CreateSymbolicLink(Path.Combine(files.FullName, "unsafe-link"), folder.FullName);
            var reader = CreateReader(root.FullName);

            var first = await ReadAllAsync(reader);

            Assert.Contains(first, entry => entry.Name.Value == "empty" && entry.EntryType == FileEntryType.Folder);
            Assert.Contains(first, entry => entry.Name.Value == "é.txt" && entry.IsolationReason is null);
            Assert.Contains(first, entry => entry.Name.Value == "Case.txt" && entry.IsolationReason is null);
            Assert.Contains(first, entry => entry.Name.Value == "case.txt" && entry.IsolationReason is null);
            Assert.Contains(first, entry => entry.Name.Value == "special-fifo" && entry.IsolationReason == "SPECIAL_FILE");
            var file = Assert.Single(first, entry => entry.Name.Value == "before.txt");
            Assert.Equal("text/plain", file.MimeType);
            Assert.NotNull(file.SourceFileKey);

            var renamedPath = Path.Combine(folder.FullName, "after.txt");
            File.Move(originalPath, renamedPath);
            var second = await ReadAllAsync(reader);
            var renamed = Assert.Single(second, entry => entry.Name.Value == "after.txt");
            Assert.Equal(file.SourceFileKey, renamed.SourceFileKey);
            var isolated = Assert.Single(second, entry => entry.Name.Value == "unsafe-link");
            Assert.Equal("SYMBOLIC_LINK", isolated.IsolationReason);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_PathOutsideManagedNamespace_IsRejected()
    {
        var root = Directory.CreateTempSubdirectory("kurastorage-index-");
        try
        {
            var reader = CreateReader(root.FullName);
            await Assert.ThrowsAsync<IOException>(() => reader.InspectAsync(
                RelativeStoragePath.Create("upload-temp/item"),
                CancellationToken.None));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EnumerateAsync_UnreadableManagedDirectory_MarksSnapshotIncomplete()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("kurastorage-index-");
        string? restrictedPath = null;
        try
        {
            var ownerId = Guid.NewGuid();
            var files = Directory.CreateDirectory(Path.Combine(root.FullName, "users", ownerId.ToString("N"), "files"));
            restrictedPath = Directory.CreateDirectory(Path.Combine(files.FullName, "restricted")).FullName;
            File.SetUnixFileMode(restrictedPath, UnixFileMode.None);
            var reader = CreateReader(root.FullName);

            await Assert.ThrowsAsync<IndexSnapshotIncompleteException>(() => ReadAllAsync(reader));
        }
        finally
        {
            if (restrictedPath is not null)
            {
                File.SetUnixFileMode(
                    restrictedPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            root.Delete(recursive: true);
        }
    }

    private static ManagedFileSystemSnapshotReader CreateReader(string root) =>
        new(Options.Create(new StorageOptions { RootPath = root, StorageId = "test" }));

    private static async Task<List<ObservedStorageEntry>> ReadAllAsync(
        ManagedFileSystemSnapshotReader reader)
    {
        var result = new List<ObservedStorageEntry>();
        await foreach (var entry in reader.EnumerateAsync(
                           new StorageSnapshotContext(Guid.NewGuid(), 10),
                           CancellationToken.None))
        {
            result.Add(entry);
        }

        return result;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);
}
