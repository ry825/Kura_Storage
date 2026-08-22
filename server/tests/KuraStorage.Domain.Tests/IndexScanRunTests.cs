using KuraStorage.Domain.Indexing;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class IndexScanRunTests
{
    [Fact]
    public void Complete_RecordsSummaryAndWarningStatus()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var run = new IndexScanRun(Guid.NewGuid(), IndexScanTrigger.Admin, IndexScanMode.Apply, startedAt);

        run.RecordEnumerated();
        run.RecordAdded();
        run.RecordIsolated();
        run.RecordError();
        run.Complete(startedAt.AddMinutes(1));

        Assert.Equal(IndexScanStatus.CompletedWithWarnings, run.Status);
        Assert.Equal(1, run.EnumeratedCount);
        Assert.Equal(1, run.AddedCount);
        Assert.Equal(1, run.IsolatedCount);
        Assert.Equal(1, run.ErrorCount);
        Assert.Throws<InvalidOperationException>(run.RecordEnumerated);
    }

    [Fact]
    public void FailAndCancel_CloseRunningScanWithoutLeakingExceptionDetails()
    {
        var now = DateTimeOffset.UtcNow;
        var failed = new IndexScanRun(Guid.NewGuid(), IndexScanTrigger.Scheduled, IndexScanMode.Apply, now);
        failed.Fail("STORAGE_UNAVAILABLE", now.AddMinutes(1));

        Assert.Equal(IndexScanStatus.Failed, failed.Status);
        Assert.Equal("STORAGE_UNAVAILABLE", failed.ErrorCode);
        Assert.Equal(1, failed.ErrorCount);

        var cancelled = new IndexScanRun(Guid.NewGuid(), IndexScanTrigger.Admin, IndexScanMode.Apply, now);
        cancelled.Cancel(now.AddMinutes(1));
        Assert.Equal(IndexScanStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ErrorCode);
    }
}
