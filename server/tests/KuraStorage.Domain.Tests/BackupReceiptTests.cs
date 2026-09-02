using KuraStorage.Domain.Backup;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class BackupReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NormalizesOpaqueKeyPathAndChecksum()
    {
        var key = Guid.NewGuid();
        var receipt = Create(
            localDocumentKey: key.ToString("D").ToUpperInvariant(),
            relativePath: "Photos/e\u0301.jpg",
            checksum: new string('A', 64));

        Assert.Equal(key.ToString("D"), receipt.LocalDocumentKey);
        Assert.Equal("Photos/\u00e9.jpg", receipt.RelativePath);
        Assert.Equal(new string('a', 64), receipt.Checksum);
        Assert.Equal(1, receipt.RemoteFileVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Create_RejectsInvalidDocumentKey(string value)
    {
        Assert.Throws<ArgumentException>(() => Create(localDocumentKey: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    [InlineData("/absolute/path")]
    [InlineData("folder\\file")]
    public void Create_RejectsInvalidRelativePath(string value)
    {
        Assert.Throws<ArgumentException>(() => Create(relativePath: value));
    }

    [Fact]
    public void Create_RejectsInvalidIdentitySizeTimeAndChecksum()
    {
        Assert.Throws<ArgumentException>(() => Create(userId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(deviceId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(remoteFileId: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(size: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(remoteFileVersion: 0));
        Assert.Throws<ArgumentException>(() => Create(sourceModifiedAt: Now.ToOffset(TimeSpan.FromHours(10))));
        Assert.Throws<ArgumentException>(() => Create(checksum: "sha256:unknown"));
    }

    [Fact]
    public void UpdateCompletion_ReplacesOnlyServerConfirmedMetadata()
    {
        var receipt = Create();
        var remoteFileId = receipt.RemoteFileId;

        receipt.UpdateCompletion(
            remoteFileId,
            "Photos/renamed.jpg",
            12,
            Now.AddMinutes(1),
            new string('b', 64),
            2,
            Now.AddMinutes(2));

        Assert.Equal(remoteFileId, receipt.RemoteFileId);
        Assert.Equal("Photos/renamed.jpg", receipt.RelativePath);
        Assert.Equal(12, receipt.Size);
        Assert.Equal(2, receipt.RemoteFileVersion);
        Assert.Equal(Now.AddMinutes(2), receipt.UploadedAt);
    }

    private static BackupReceipt Create(
        Guid? userId = null,
        Guid? deviceId = null,
        string? localDocumentKey = null,
        Guid? remoteFileId = null,
        string relativePath = "Photos/file.jpg",
        long size = 1,
        DateTimeOffset? sourceModifiedAt = null,
        string? checksum = null,
        long remoteFileVersion = 1) =>
        new(
            Guid.NewGuid(),
            userId ?? Guid.NewGuid(),
            deviceId ?? Guid.NewGuid(),
            localDocumentKey ?? Guid.NewGuid().ToString("D"),
            remoteFileId ?? Guid.NewGuid(),
            relativePath,
            size,
            sourceModifiedAt ?? Now,
            checksum,
            remoteFileVersion,
            Now);
}
