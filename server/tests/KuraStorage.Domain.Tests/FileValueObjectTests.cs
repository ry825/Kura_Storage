using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class FileValueObjectTests
{
    [Theory]
    [InlineData("../secret")]
    [InlineData("/etc/passwd")]
    [InlineData("folder\\file")]
    [InlineData("folder\0file")]
    public void RelativeStoragePath_UntrustedPath_RejectsIt(string value)
    {
        Assert.False(RelativeStoragePath.TryCreate(value, out _));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("folder/name")]
    [InlineData("folder\\name")]
    [InlineData("bad\0name")]
    public void FileName_PathLikeOrControlValue_RejectsIt(string value)
    {
        Assert.False(FileName.TryCreate(value, out _));
    }

    [Fact]
    public void FileEntry_TrashThenRestore_PreservesAndClearsRecoveryMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var parentId = Guid.NewGuid();
        var entry = FileEntry.CreateFile(
            Guid.NewGuid(),
            Guid.NewGuid(),
            parentId,
            FileName.Create("photo.jpg"),
            RelativeStoragePath.Create("users/owner/files/photo.jpg"),
            "image/jpeg",
            42,
            now);

        entry.Trash(RelativeStoragePath.Create($"users/owner/trash/{entry.Id:N}/photo.jpg"), now.AddMinutes(1));

        Assert.Equal(FileEntryStatus.Trashed, entry.Status);
        Assert.Equal(parentId, entry.OriginalParentId);
        Assert.Equal("users/owner/files/photo.jpg", entry.OriginalRelativePath);

        entry.Restore(parentId, RelativeStoragePath.Create("users/owner/files/photo.jpg"), now.AddMinutes(2));

        Assert.Equal(FileEntryStatus.Active, entry.Status);
        Assert.Null(entry.OriginalParentId);
        Assert.Null(entry.OriginalRelativePath);
        Assert.Null(entry.TrashedAt);
    }

    [Fact]
    public void FileOperation_InvalidTransition_Throws()
    {
        var operation = new FileOperation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FileOperationType.Upload,
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            "upload-temp/item",
            "users/owner/files/item",
            1,
            null,
            DateTimeOffset.UtcNow);
        operation.Complete(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => operation.MarkFilesystemDone(DateTimeOffset.UtcNow));
    }
}
