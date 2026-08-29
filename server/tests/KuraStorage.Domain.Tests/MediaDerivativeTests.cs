using KuraStorage.Domain.Media;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class MediaDerivativeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DerivativeType.Thumbnail)]
    [InlineData(DerivativeType.PdfThumbnail)]
    [InlineData(DerivativeType.ImageLow)]
    [InlineData(DerivativeType.ImageMedium)]
    [InlineData(DerivativeType.VideoLow)]
    [InlineData(DerivativeType.VideoMedium)]
    public void FileDerivative_Create_RepresentsEverySupportedType(DerivativeType type)
    {
        var derivative = CreateDerivative(type);

        Assert.Equal(type, derivative.DerivativeType);
        Assert.Equal(DerivativeStatus.Pending, derivative.Status);
        Assert.Equal(1, derivative.SourceVersion);
        Assert.Equal(1, derivative.ProfileVersion);
        Assert.Equal(0, derivative.Size);
        Assert.Equal(1, derivative.Revision);
    }

    [Fact]
    public void FileDerivative_LogicalKey_DoesNotContainFileNameOrLocation()
    {
        var sourceId = Guid.NewGuid();
        var derivative = CreateDerivative(
            DerivativeType.ImageLow,
            sourceFileId: sourceId,
            sourceVersion: 3,
            profileVersion: 2);

        Assert.Equal(new DerivativeLogicalKey(sourceId, 3, DerivativeType.ImageLow, 2), derivative.LogicalKey);
        Assert.Equal(sourceId, derivative.LogicalKey.SourceFileId);
        Assert.Equal(3, derivative.LogicalKey.SourceVersion);
        Assert.Equal(DerivativeType.ImageLow, derivative.LogicalKey.DerivativeType);
        Assert.Equal(2, derivative.LogicalKey.ProfileVersion);
    }

    [Fact]
    public void FileDerivative_Ready_RequiresRunningStateVerifiedSizeAndFormalPath()
    {
        var derivative = CreateDerivative(DerivativeType.ImageLow);

        Assert.Throws<InvalidOperationException>(() =>
            derivative.MarkReady("derivatives/a.webp", 1, Now.AddSeconds(1), Now.AddDays(1)));
        derivative.Start(Now.AddSeconds(1));
        Assert.Throws<ArgumentException>(() =>
            derivative.MarkReady("", 1, Now.AddSeconds(2), Now.AddDays(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            derivative.MarkReady("derivatives/a.webp", 0, Now.AddSeconds(2), Now.AddDays(1)));

        derivative.MarkReady("derivatives/a.webp", 42, Now.AddSeconds(2), Now.AddDays(1));

        Assert.Equal(DerivativeStatus.Ready, derivative.Status);
        Assert.Equal(42, derivative.Size);
        Assert.Equal(Now.AddDays(1), derivative.ExpiresAt);
        Assert.Equal(Now.AddSeconds(2), derivative.LastAccessedAt);
    }

    [Theory]
    [InlineData(DerivativeType.Thumbnail)]
    [InlineData(DerivativeType.PdfThumbnail)]
    public void FileDerivative_ThumbnailCannotExpire(DerivativeType type)
    {
        var derivative = CreateDerivative(type);
        derivative.Start(Now.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() =>
            derivative.MarkReady("derivatives/t.webp", 10, Now.AddSeconds(2), Now.AddDays(1)));

        derivative.MarkReady("derivatives/t.webp", 10, Now.AddSeconds(2), null);
        Assert.Null(derivative.ExpiresAt);
        Assert.Null(derivative.LastAccessedAt);
    }

    [Fact]
    public void FileDerivative_OnlyAllowsDefinedTransitions()
    {
        var derivative = CreateDerivative(DerivativeType.VideoLow);

        Assert.Throws<InvalidOperationException>(() => derivative.Fail("MEDIA_FAILED", Now));
        Assert.Throws<InvalidOperationException>(() => derivative.BeginDeleting(Now));
        derivative.Start(Now);
        derivative.Requeue(Now.AddSeconds(1), "MEDIA_RETRYABLE");
        derivative.Start(Now.AddSeconds(2));
        derivative.Fail("MEDIA_FAILED", Now.AddSeconds(3));
        derivative.Retry(Now.AddSeconds(4));
        derivative.BlockSourceMissing(Now.AddSeconds(5));
        derivative.BeginDeleting(Now.AddSeconds(6));

        Assert.Equal(DerivativeStatus.Deleting, derivative.Status);
        Assert.Throws<InvalidOperationException>(() => derivative.Start(Now.AddSeconds(7)));
    }

    [Fact]
    public void FileDerivative_LeaseProjectionOnlyMovesThroughExplicitUpdates()
    {
        var derivative = CreateDerivative(DerivativeType.ImageMedium);

        derivative.ProjectLeaseUntil(Now.AddMinutes(2), Now);
        derivative.ProjectLeaseUntil(Now.AddMinutes(1), Now.AddSeconds(1));
        Assert.Equal(Now.AddMinutes(2), derivative.LeaseUntil);

        derivative.ClearLeaseProjection(Now.AddSeconds(2));
        Assert.Null(derivative.LeaseUntil);
    }

    [Fact]
    public void FileDerivative_ReadyAccessAndLifecycleRejectInvalidBoundaries()
    {
        var derivative = CreateDerivative(DerivativeType.ImageLow);
        derivative.Start(Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            derivative.MarkReady("derivatives/a.webp", 1, Now, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            derivative.MarkReady("derivatives/a.webp", 1, Now, Now));

        derivative.MarkReady("derivatives/a.webp", 1, Now, Now.AddDays(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => derivative.RecordAccess(Now, Now));
        derivative.RecordAccess(Now.AddHours(1), Now.AddDays(2));
        Assert.Equal(Now.AddHours(1), derivative.LastAccessedAt);
        Assert.Equal(Now.AddDays(2), derivative.ExpiresAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => derivative.ProjectLeaseUntil(Now, Now));

        derivative.BlockSourceMissing(Now.AddHours(2));
        derivative.BeginDeleting(Now.AddHours(3));
        Assert.Throws<InvalidOperationException>(() => derivative.BlockSourceMissing(Now.AddHours(4)));

        var thumbnail = CreateDerivative(DerivativeType.Thumbnail);
        thumbnail.Start(Now);
        thumbnail.MarkReady("derivatives/t.webp", 1, Now, null);
        Assert.Throws<InvalidOperationException>(() => thumbnail.RecordAccess(Now, Now.AddDays(1)));
    }

    [Fact]
    public void FileDerivative_ErrorCodeMustBePresentAndBounded()
    {
        var derivative = CreateDerivative(DerivativeType.ImageLow);
        derivative.Start(Now);

        Assert.Throws<ArgumentException>(() => derivative.Fail("", Now));
        Assert.Throws<ArgumentException>(() => derivative.Fail(new string('X', 65), Now));
    }

    [Fact]
    public void MediaJob_RunHeartbeatAndComplete_RequireCurrentWorkerToken()
    {
        var job = CreateJob();
        var worker = Guid.NewGuid();

        job.Start(worker, Now);
        Assert.Equal(1, job.AttemptCount);
        Assert.Throws<InvalidOperationException>(() =>
            job.RecordHeartbeat(Guid.NewGuid(), Now.AddSeconds(10), 10, 100, 1000));

        job.RecordHeartbeat(worker, Now.AddSeconds(10), 10, 100, 1000);
        job.Complete(worker, Now.AddSeconds(20));

        Assert.Equal(MediaJobStatus.Completed, job.Status);
        Assert.Equal(10, job.ProgressPercent);
        Assert.Equal(Now.AddSeconds(20), job.CompletedAt);
        Assert.Null(job.WorkerToken);
        Assert.Throws<InvalidOperationException>(() => job.Start(Guid.NewGuid(), Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void MediaJob_HeartbeatRejectsInvalidProgress(int progress)
    {
        var job = CreateJob();
        var worker = Guid.NewGuid();
        job.Start(worker, Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            job.RecordHeartbeat(worker, Now.AddSeconds(1), progress, null, null));
    }

    [Fact]
    public void MediaJob_RetryUsesBoundedExponentialBackoffAndFailsAtThirdAttempt()
    {
        var job = CreateJob();

        var first = Guid.NewGuid();
        job.Start(first, Now);
        job.Fail(first, "STORAGE_UNAVAILABLE", true, Now);
        Assert.Equal(MediaJobStatus.Queued, job.Status);
        Assert.Equal(Now.AddSeconds(30), job.AvailableAt);

        var second = Guid.NewGuid();
        job.Start(second, Now.AddSeconds(30));
        job.Fail(second, "STORAGE_UNAVAILABLE", true, Now.AddSeconds(30));
        Assert.Equal(Now.AddMinutes(2).AddSeconds(30), job.AvailableAt);

        var third = Guid.NewGuid();
        job.Start(third, job.AvailableAt);
        job.Fail(third, "STORAGE_UNAVAILABLE", true, job.AvailableAt);

        Assert.Equal(MediaJobStatus.Failed, job.Status);
        Assert.Equal(3, job.AttemptCount);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public void MediaJob_StaleAndRetentionUseApprovedBoundaries()
    {
        var job = CreateJob();
        job.Start(Guid.NewGuid(), Now);

        Assert.False(job.IsStaleAt(Now.AddMinutes(2).AddTicks(-1)));
        Assert.True(job.IsStaleAt(Now.AddMinutes(2)));

        job.Cancel("MEDIA_SOURCE_CHANGED", Now.AddMinutes(3));
        Assert.False(job.IsHistoryExpiredAt(Now.AddDays(7).AddMinutes(3).AddTicks(-1)));
        Assert.True(job.IsHistoryExpiredAt(Now.AddDays(7).AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => job.Cancel("MEDIA_SOURCE_CHANGED", Now.AddDays(8)));
    }

    [Fact]
    public void MediaJob_ProgressErrorAndBackoffRejectInvalidBoundaries()
    {
        var job = CreateJob();
        var worker = Guid.NewGuid();
        job.Start(worker, Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            job.RecordHeartbeat(worker, Now, null, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            job.RecordHeartbeat(worker, Now, null, 2, 1));
        Assert.Throws<ArgumentException>(() => job.Fail(worker, "", false, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaJob.RetryDelayAfter(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaJob.RetryDelayAfter(0));
    }

    [Fact]
    public void DerivativeLease_RenewAndReleaseRequireOwnerAndValidExpiry()
    {
        var owner = Guid.NewGuid();
        var lease = new DerivativeLease(
            Guid.NewGuid(), Guid.NewGuid(), DerivativeLeaseType.Delivery, owner, Now.AddMinutes(2), Now);

        Assert.Throws<InvalidOperationException>(() =>
            lease.Renew(Guid.NewGuid(), Now.AddMinutes(3), Now.AddSeconds(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            lease.Renew(owner, Now, Now.AddSeconds(10)));

        lease.Renew(owner, Now.AddMinutes(3), Now.AddSeconds(10));
        Assert.False(lease.IsExpiredAt(Now.AddMinutes(3).AddTicks(-1)));
        Assert.True(lease.IsExpiredAt(Now.AddMinutes(3)));
        Assert.False(lease.Release(Guid.NewGuid()));
        Assert.True(lease.Release(owner));
        Assert.True(lease.IsReleased);
        Assert.Throws<InvalidOperationException>(() =>
            lease.Renew(owner, Now.AddMinutes(4), Now.AddMinutes(3)));
    }

    [Fact]
    public void DerivativeLease_CreateRejectsInvalidIdentityTypeAndExpiry()
    {
        Assert.Throws<ArgumentException>(() => new DerivativeLease(
            Guid.Empty, Guid.NewGuid(), DerivativeLeaseType.Generation, Guid.NewGuid(), Now.AddMinutes(1), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DerivativeLease(
            Guid.NewGuid(), Guid.NewGuid(), (DerivativeLeaseType)999, Guid.NewGuid(), Now.AddMinutes(1), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DerivativeLease(
            Guid.NewGuid(), Guid.NewGuid(), DerivativeLeaseType.Generation, Guid.NewGuid(), Now, Now));
    }

    private static FileDerivative CreateDerivative(
        DerivativeType type,
        Guid? sourceFileId = null,
        long sourceVersion = 1,
        int profileVersion = 1) =>
        new(
            Guid.NewGuid(),
            sourceFileId ?? Guid.NewGuid(),
            sourceVersion,
            type,
            profileVersion,
            Now);

    private static MediaJob CreateJob() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DerivativeType.VideoLow,
            Guid.NewGuid(),
            Now);
}
