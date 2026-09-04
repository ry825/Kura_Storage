using KuraStorage.Domain.Media;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class MediaCleanupRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void ManualRun_ClaimsCompletesAndKeepsOnlyHashedRequestIdentity()
    {
        var adminId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var run = MediaCleanupRun.CreateManual(Guid.NewGuid(), adminId, Hash, new string('b', 64), Now);

        run.Claim(token, Now.AddSeconds(1), Now.AddMinutes(10));
        run.Complete(token, Now.AddSeconds(2), 3, 2, 120, 0, 80);

        Assert.Equal(MediaCleanupTrigger.Manual, run.Trigger);
        Assert.Equal(MediaCleanupRunStatus.Completed, run.Status);
        Assert.Equal(adminId, run.RequestedByAdminUserId);
        Assert.Equal(Hash, run.IdempotencyKeyHash);
        Assert.Null(run.WorkerToken);
        Assert.Null(run.LeaseExpiresAt);
        Assert.Equal(3, run.ExaminedCount);
        Assert.Equal(2, run.DeletedCount);
        Assert.Equal(120, run.ReleasedBytes);
        Assert.Equal(80, run.RemainingCacheBytes);
    }

    [Fact]
    public void ExpiredRunningLease_CanBeReclaimedButActiveLeaseAndWrongTokenAreRejected()
    {
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        var run = MediaCleanupRun.CreateScheduled(Guid.NewGuid(), Now);
        run.Claim(firstToken, Now, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.Claim(secondToken, Now.AddSeconds(30), Now.AddMinutes(2)));
        run.Claim(secondToken, Now.AddMinutes(1), Now.AddMinutes(3));
        Assert.Throws<InvalidOperationException>(() => run.Complete(firstToken, Now.AddMinutes(2), 0, 0, 0, 0, 0));
        run.Fail(secondToken, Now.AddMinutes(2), MediaCleanupFailureCode.StorageUnavailable);

        Assert.Equal(MediaCleanupRunStatus.Failed, run.Status);
        Assert.Equal(MediaCleanupFailureCode.StorageUnavailable, run.FailureCode);
    }

    [Fact]
    public void PartialFailure_CompletesAsFailedWithoutFreeFormError()
    {
        var token = Guid.NewGuid();
        var run = MediaCleanupRun.CreateScheduled(Guid.NewGuid(), Now);
        run.Claim(token, Now, Now.AddMinutes(1));

        run.Complete(token, Now.AddSeconds(1), 2, 1, 50, 1, 50);

        Assert.Equal(MediaCleanupRunStatus.Failed, run.Status);
        Assert.Equal(MediaCleanupFailureCode.PartialDeleteFailure, run.FailureCode);
        Assert.Equal(1, run.FailureCount);
    }

    [Fact]
    public void Creation_RejectsMissingIdentityAndInvalidHashes()
    {
        Assert.Throws<ArgumentException>(() => MediaCleanupRun.CreateScheduled(Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => MediaCleanupRun.CreateManual(Guid.NewGuid(), Guid.Empty, Hash, Hash, Now));
        Assert.Throws<ArgumentException>(() => MediaCleanupRun.CreateManual(
            Guid.NewGuid(), Guid.NewGuid(), "plaintext", Hash, Now));
        Assert.Throws<ArgumentException>(() => MediaCleanupRun.CreateManual(
            Guid.NewGuid(), Guid.NewGuid(), Hash, new string('z', 64), Now));
    }

    [Fact]
    public void ClaimReleaseAndCompletion_ValidateWorkerAndCounters()
    {
        var token = Guid.NewGuid();
        var run = MediaCleanupRun.CreateScheduled(Guid.NewGuid(), Now);
        Assert.Throws<ArgumentException>(() => run.Claim(Guid.Empty, Now, Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => run.Claim(token, Now, Now));

        run.Claim(token, Now, Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => run.Release(Guid.NewGuid()));
        run.Release(token);
        Assert.Equal(MediaCleanupRunStatus.Pending, run.Status);
        Assert.Null(run.WorkerToken);
        Assert.Null(run.LeaseExpiresAt);

        run.Claim(token, Now.AddMinutes(1), Now.AddMinutes(2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            run.Complete(token, Now.AddMinutes(2), 0, 1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            run.Fail(token, Now.AddMinutes(2), (MediaCleanupFailureCode)999));
    }
}
