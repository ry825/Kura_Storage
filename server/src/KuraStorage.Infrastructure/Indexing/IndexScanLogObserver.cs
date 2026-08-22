using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Indexing;
using Microsoft.Extensions.Logging;

namespace KuraStorage.Infrastructure.Indexing;

public sealed class IndexScanLogObserver(ILogger<IndexScanLogObserver> logger) : IIndexScanObserver
{
    public void Started(Guid runId, IndexScanTrigger trigger, IndexScanMode mode) =>
        logger.LogInformation(
            "Index scan {RunId} started with trigger {Trigger} and mode {Mode}",
            runId,
            trigger,
            mode);

    public void Completed(IndexScanSummary summary) =>
        logger.LogInformation(
            "Index scan {RunId} completed with status {Status}, enumerated {EnumeratedCount}, added {AddedCount}, updated {UpdatedCount}, moved {MovedCount}, candidate {CandidateCount}, missing {MissingCount}, revived {RevivedCount}, isolated {IsolatedCount}, errors {ErrorCount}",
            summary.RunId,
            summary.Status,
            summary.EnumeratedCount,
            summary.AddedCount,
            summary.UpdatedCount,
            summary.MovedCount,
            summary.CandidateCount,
            summary.MissingCount,
            summary.RevivedCount,
            summary.IsolatedCount,
            summary.ErrorCount);

    public void Failed(Guid runId, string errorCode) =>
        logger.LogError("Index scan {RunId} failed with code {ErrorCode}", runId, errorCode);
}
