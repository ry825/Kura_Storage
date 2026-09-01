using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class FileVersionRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Sha256 = new('a', 64);

    [Theory]
    [InlineData(FileVersionChangeKind.Upload)]
    [InlineData(FileVersionChangeKind.TextEdit)]
    [InlineData(FileVersionChangeKind.ExternalChange)]
    [InlineData(FileVersionChangeKind.Restore)]
    public void Create_RepresentsSupportedChangeKinds(FileVersionChangeKind kind)
    {
        var fileId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var actorDeviceId = Guid.NewGuid();
        var record = new FileVersionRecord(
            Guid.NewGuid(), fileId, 3, 42, Sha256,
            $"versions/{actorUserId:N}/{fileId:N}/3/{Sha256}.bin",
            kind, actorUserId, actorDeviceId, Now);

        Assert.Equal(fileId, record.FileEntryId);
        Assert.Equal(3, record.Version);
        Assert.Equal(42, record.Size);
        Assert.Equal(Sha256, record.Sha256);
        Assert.Equal(kind, record.ChangeKind);
        Assert.Equal(actorUserId, record.ActorUserId);
        Assert.Equal(actorDeviceId, record.ActorDeviceId);
        Assert.Equal(Now, record.CreatedAt);
    }

    [Fact]
    public void Create_RejectsInvalidIdentityVersionSizeAndTimestamp()
    {
        Assert.Throws<ArgumentException>(() => Create(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(fileEntryId: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(version: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(size: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(size: FileVersionRecord.MaximumContentBytes + 1));
        Assert.Throws<ArgumentException>(() => Create(actorUserId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(actorDeviceId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(actorDeviceId: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => Create(createdAt: Now.ToOffset(TimeSpan.FromHours(10))));
    }

    [Fact]
    public void PersistenceConstructor_CreatesEmptyRecordForMaterialization()
    {
        var record = Assert.IsType<FileVersionRecord>(
            Activator.CreateInstance(typeof(FileVersionRecord), nonPublic: true));

        Assert.Equal(Guid.Empty, record.Id);
        Assert.Equal(string.Empty, record.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Create_RejectsInvalidChecksum(string checksum)
    {
        Assert.Throws<ArgumentException>(() => Create(sha256: checksum));
    }

    [Theory]
    [InlineData("")]
    [InlineData("users/a/file.txt")]
    [InlineData("versions/../escape")]
    public void Create_RejectsInvalidManagedPath(string path)
    {
        Assert.Throws<ArgumentException>(() => Create(contentRelativePath: path));
    }

    [Fact]
    public void Create_RejectsUndefinedChangeKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(changeKind: (FileVersionChangeKind)999));
    }

    private static FileVersionRecord Create(
        Guid? id = null,
        Guid? fileEntryId = null,
        long version = 1,
        long size = 0,
        string? sha256 = null,
        string? contentRelativePath = null,
        FileVersionChangeKind changeKind = FileVersionChangeKind.Upload,
        Guid? actorUserId = null,
        Guid? actorDeviceId = null,
        DateTimeOffset? createdAt = null)
    {
        var effectiveFileId = fileEntryId ?? Guid.NewGuid();
        var effectiveOwnerId = Guid.NewGuid();
        var effectiveSha = sha256 ?? Sha256;
        return new FileVersionRecord(
            id ?? Guid.NewGuid(),
            effectiveFileId,
            version,
            size,
            effectiveSha,
            contentRelativePath ?? $"versions/{effectiveOwnerId:N}/{effectiveFileId:N}/{version}/{effectiveSha}.bin",
            changeKind,
            actorUserId,
            actorDeviceId,
            createdAt ?? Now);
    }
}
