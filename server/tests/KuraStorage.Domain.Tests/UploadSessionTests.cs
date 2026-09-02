using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Transfers;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class UploadSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptChunk_WhenContiguous_AdvancesOffsetAndCapsIdleExpiry()
    {
        var session = Create(expectedSize: 10, expiresAt: Now.AddHours(1), absoluteExpiresAt: Now.AddHours(2));

        session.AcceptChunk(0, 4, new string('a', 64), Now.AddHours(1), TimeSpan.FromHours(24));

        Assert.Equal(4, session.ReceivedBytes);
        Assert.Equal(0, session.LastChunkOffset);
        Assert.Equal(4, session.LastChunkLength);
        Assert.Equal(Now.AddHours(2), session.ExpiresAt);
        Assert.True(session.IsLastChunk(0, 4, new string('A', 64)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 11)]
    [InlineData(0, 0)]
    public void AcceptChunk_WhenRangeIsInvalid_RejectsWithoutProgress(long offset, long length)
    {
        var session = Create(expectedSize: 10);

        Assert.Throws<InvalidOperationException>(() =>
            session.AcceptChunk(offset, length, new string('a', 64), Now, TimeSpan.FromHours(24)));
        Assert.Equal(0, session.ReceivedBytes);
    }

    [Fact]
    public void Completion_RequiresAllBytesAndCannotTransitionAgain()
    {
        var session = Create(expectedSize: 2);
        Assert.Throws<InvalidOperationException>(() => session.BeginCompletion(Guid.NewGuid(), Now));
        session.AcceptChunk(0, 2, new string('a', 64), Now, TimeSpan.FromHours(24));

        session.BeginCompletion(Guid.NewGuid(), Now);
        session.Complete(Now.AddMinutes(1));

        Assert.Equal(UploadSessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => session.Cancel(null, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => session.Complete(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => session.RequireRecovery("RECOVERY_REQUIRED", Now.AddMinutes(2)));
    }

    [Fact]
    public void CancelAndExpire_AreIdempotentButMutuallyExclusive()
    {
        var cancelled = Create();
        cancelled.Cancel(null, Now);
        cancelled.Cancel(null, Now.AddMinutes(1));
        Assert.Equal(UploadSessionStatus.Cancelled, cancelled.Status);
        Assert.Throws<InvalidOperationException>(() => cancelled.Expire(Now.AddDays(1)));

        var expired = Create(expiresAt: Now.AddHours(1));
        Assert.Throws<InvalidOperationException>(() => expired.Expire(Now));
        expired.Expire(Now.AddHours(1));
        expired.Expire(Now.AddHours(2));
        Assert.Equal(UploadSessionStatus.Expired, expired.Status);
        Assert.Throws<InvalidOperationException>(() => expired.Cancel(null, Now.AddHours(2)));
    }

    [Fact]
    public void ResetAfterChecksumFailure_ReturnsSessionToOffsetZero()
    {
        var session = Create(expectedSize: 2);
        session.AcceptChunk(0, 2, new string('a', 64), Now, TimeSpan.FromHours(24));

        session.ResetAfterChecksumFailure(Now.AddMinutes(1), TimeSpan.FromHours(24));

        Assert.Equal(0, session.ReceivedBytes);
        Assert.Null(session.LastChunkOffset);
        Assert.Equal("UPLOAD_CHECKSUM_MISMATCH", session.ErrorCode);
        Assert.Equal(UploadSessionStatus.Active, session.Status);
    }

    [Fact]
    public void SameMetadata_RequiresCreatingDeviceAndAllPayloadFields()
    {
        var deviceId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var session = Create(deviceId: deviceId, destinationFolderId: folderId, expectedSize: 7);

        Assert.True(session.SameMetadata(deviceId, folderId, "file.bin", "APPLICATION/OCTET-STREAM", 7, new string('A', 64)));
        Assert.False(session.SameMetadata(Guid.NewGuid(), folderId, "file.bin", "application/octet-stream", 7, new string('a', 64)));
        Assert.False(session.SameMetadata(deviceId, folderId, "other.bin", "application/octet-stream", 7, new string('a', 64)));
    }

    [Fact]
    public void Constructor_SeparatesActorFromTargetOwner()
    {
        var actorUserId = Guid.NewGuid();
        var targetOwnerUserId = Guid.NewGuid();

        var session = Create(actorUserId: actorUserId, targetOwnerUserId: targetOwnerUserId);

        Assert.Equal(actorUserId, session.ActorUserId);
        Assert.Equal(targetOwnerUserId, session.TargetOwnerUserId);
    }

    [Fact]
    public void Constructor_PersistsImmutableBackupContextAndIncludesItInIdempotencyMetadata()
    {
        var backup = new BackupUploadContext(
            new BackupDocumentMetadata(
                Guid.NewGuid().ToString("D"),
                "Photos/file.jpg",
                7,
                Now,
                new string('a', 64)),
            BackupUploadDecision.New,
            null,
            null);
        var session = Create(expectedSize: 7, backup: backup);

        Assert.Equal(backup.LocalDocumentKey, session.BackupLocalDocumentKey);
        Assert.True(session.SameMetadata(
            session.DeviceId,
            session.DestinationFolderId!.Value,
            "file.bin",
            "application/octet-stream",
            7,
            new string('a', 64),
            backup));
        Assert.False(session.SameMetadata(
            session.DeviceId,
            session.DestinationFolderId!.Value,
            "file.bin",
            "application/octet-stream",
            7,
            new string('a', 64),
            null));
    }

    private static UploadSession Create(
        Guid? actorUserId = null,
        Guid? targetOwnerUserId = null,
        Guid? deviceId = null,
        Guid? destinationFolderId = null,
        long expectedSize = 1,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? absoluteExpiresAt = null,
        BackupUploadContext? backup = null) =>
        new(
            Guid.NewGuid(),
            actorUserId ?? Guid.NewGuid(),
            targetOwnerUserId ?? Guid.NewGuid(),
            deviceId ?? Guid.NewGuid(),
            destinationFolderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            "file.bin",
            "application/octet-stream",
            expectedSize,
            new string('a', 64),
            $"upload-sessions/{Guid.NewGuid():N}.upload",
            Now,
            expiresAt ?? Now.AddHours(24),
            absoluteExpiresAt ?? Now.AddDays(7),
            backup);
}
