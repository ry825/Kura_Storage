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
    public void FileName_NormalizesUnicodeAndEnforcesThe255CharacterBoundary()
    {
        Assert.True(FileName.TryCreate(" e\u0301.txt ", out var normalized));
        Assert.Equal("é.txt", normalized.Value);
        Assert.True(FileName.TryCreate(new string('a', 255), out _));
        Assert.False(FileName.TryCreate(new string('a', 256), out _));
        Assert.False(FileName.TryCreate("control\u0001name", out _));
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

    [Fact]
    public void FileEntry_RenameAndMove_PreserveContentIdentityAndVersion()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var originalParentId = Guid.NewGuid();
        var targetParentId = Guid.NewGuid();
        var entry = FileEntry.CreateFile(
            Guid.NewGuid(),
            ownerId,
            originalParentId,
            FileName.Create("before.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/before.txt"),
            "text/plain",
            42,
            createdAt);
        var id = entry.Id;
        var version = entry.FileVersion;

        entry.Rename(
            FileName.Create("after.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/after.txt"),
            createdAt.AddMinutes(1));
        entry.MoveTo(
            targetParentId,
            RelativeStoragePath.Create($"users/{ownerId:N}/files/target/after.txt"),
            createdAt.AddMinutes(2));

        Assert.Equal(id, entry.Id);
        Assert.Equal(ownerId, entry.OwnerUserId);
        Assert.Equal(targetParentId, entry.ParentId);
        Assert.Equal("after.txt", entry.Name);
        Assert.Equal("text/plain", entry.MimeType);
        Assert.Equal(42, entry.Size);
        Assert.Equal(version, entry.FileVersion);
        Assert.Equal(createdAt, entry.CreatedAt);
    }

    [Fact]
    public void FileEntry_RootOrTrashed_RelocationIsRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var root = FileEntry.CreateRoot(ownerId, now);
        Assert.Throws<InvalidFileOperationException>(
            () => root.Rename(
                FileName.Create("renamed"),
                RelativeStoragePath.Create($"users/{ownerId:N}/renamed"),
                now));

        var file = FileEntry.CreateFile(
            Guid.NewGuid(),
            ownerId,
            Guid.NewGuid(),
            FileName.Create("item"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/item"),
            null,
            0,
            now);
        file.Trash(
            RelativeStoragePath.Create($"users/{ownerId:N}/trash/{file.Id:N}/item"),
            now);
        Assert.Throws<InvalidFileOperationException>(
            () => file.MoveTo(
                Guid.NewGuid(),
                RelativeStoragePath.Create($"users/{ownerId:N}/files/other/item"),
                now));
    }
}
