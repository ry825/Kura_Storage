using KuraStorage.Domain.Maintenance;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class TrashPurgeRunTests
{
    [Fact]
    public void Complete_TracksCountsAndSelectsCompletedStatus()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        var run = new TrashPurgeRun(Guid.NewGuid(), startedAt);

        run.RecordExamined();
        run.RecordDeleted(42);
        run.Complete(startedAt.AddSeconds(1));

        Assert.Equal(TrashPurgeRunStatus.Completed, run.Status);
        Assert.Equal(1, run.ExaminedRootCount);
        Assert.Equal(1, run.DeletedRootCount);
        Assert.Equal(42, run.ReleasedBytes);
        Assert.Equal(startedAt.AddSeconds(1), run.CompletedAt);
        Assert.Throws<InvalidOperationException>(run.RecordExamined);
    }

    [Fact]
    public void Complete_WithErrors_SelectsCompletedWithErrors()
    {
        var run = new TrashPurgeRun(Guid.NewGuid(), DateTimeOffset.UtcNow);
        run.RecordExamined();
        run.RecordError();

        run.Complete(DateTimeOffset.UtcNow);

        Assert.Equal(TrashPurgeRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(1, run.ErrorCount);
    }

    [Fact]
    public void Fail_ClosesStoppedRunningRun()
    {
        var run = new TrashPurgeRun(Guid.NewGuid(), DateTimeOffset.UtcNow);

        run.Fail(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(TrashPurgeRunStatus.Failed, run.Status);
        Assert.Equal(1, run.ErrorCount);
        Assert.Throws<InvalidOperationException>(() => run.Fail(DateTimeOffset.UtcNow));
    }
}
