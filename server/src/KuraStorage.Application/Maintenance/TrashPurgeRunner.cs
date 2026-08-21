using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Maintenance;

namespace KuraStorage.Application.Maintenance;

public sealed class TrashPurgeRunner(
    IFileRepository repository,
    TrashPurgeService purgeService,
    FileOperationRecoveryService recoveryService,
    IStorageGuard storageGuard,
    ISystemClock clock,
    TrashPurgeOptions options) : ITrashPurgeRunner
{
    private static readonly Guid GlobalRunLockId = new("6e8d527c-7388-44f9-b3cb-171d9a23e1f3");

    public async Task<TrashPurgeRunSummary> RunAsync(CancellationToken cancellationToken)
    {
        await using var runLock = await repository.AcquireMutationLocksAsync(
            [GlobalRunLockId],
            cancellationToken);
        await RecoverStoppedRunsAsync(cancellationToken);
        await recoveryService.RecoverAsync(cancellationToken);

        var run = new TrashPurgeRun(Guid.NewGuid(), clock.UtcNow);
        repository.Add(run);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            if (await storageGuard.InspectAsync(StorageIntent.Delete, cancellationToken) != StorageStatus.Available)
            {
                run.Fail(clock.UtcNow);
                await FinishAsync(run, cancellationToken);
                return TrashPurgeRunSummary.From(run);
            }

            var cutoff = clock.UtcNow.AddDays(-options.RetentionDays);
            DateTimeOffset? afterTrashedAt = null;
            Guid? afterId = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = await repository.ListPurgeCandidatesAsync(
                    cutoff,
                    afterTrashedAt,
                    afterId,
                    options.BatchSize,
                    cancellationToken);
                if (candidates.Count == 0)
                {
                    break;
                }

                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    run.RecordExamined();
                    var result = await purgeService.PurgeAsync(
                        new PurgeFileCommand(
                            candidate.OwnerUserId,
                            null,
                            candidate.RootId,
                            CreateIdempotencyKey(candidate),
                            run.Id.ToString(),
                            PurgeTrigger.RetentionWorker),
                        cancellationToken);
                    if (result.IsSuccess)
                    {
                        if (result.Value == true)
                        {
                            run.RecordDeleted(candidate.EstimatedBytes);
                        }
                    }
                    else if (result.Failure!.Code != FileErrorCodes.FileNotFound)
                    {
                        run.RecordError();
                    }
                }

                afterTrashedAt = candidates[^1].TrashedAt;
                afterId = candidates[^1].RootId;
                await repository.SaveChangesAsync(cancellationToken);
                if (candidates.Count < options.BatchSize)
                {
                    break;
                }
            }

            run.Complete(clock.UtcNow);
            await FinishAsync(run, cancellationToken);
            return TrashPurgeRunSummary.From(run);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (run.Status == TrashPurgeRunStatus.Running)
            {
                run.Fail(clock.UtcNow);
                try
                {
                    await FinishAsync(run, CancellationToken.None);
                }
                catch
                {
                    throw new TrashPurgeInfrastructureException("The purge run failed and its failure state could not be persisted.", exception);
                }
            }

            throw new TrashPurgeInfrastructureException("The purge run failed.", exception);
        }
    }

    private async Task RecoverStoppedRunsAsync(CancellationToken cancellationToken)
    {
        var stoppedRuns = await repository.ListRunningPurgeRunsAsync(cancellationToken);
        foreach (var stoppedRun in stoppedRuns)
        {
            stoppedRun.Fail(clock.UtcNow);
            repository.Add(CreateAudit(stoppedRun));
        }

        if (stoppedRuns.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task FinishAsync(TrashPurgeRun run, CancellationToken cancellationToken)
    {
        repository.Add(CreateAudit(run));
        await repository.SaveChangesAsync(cancellationToken);
    }

    private AuditLog CreateAudit(TrashPurgeRun run) =>
        new(
            Guid.NewGuid(),
            null,
            null,
            null,
            "TRASH_PURGE_RUN",
            "TRASH_PURGE_RUN",
            run.Id.ToString(),
            run.Status == TrashPurgeRunStatus.CompletedWithErrors
                ? "COMPLETED_WITH_ERRORS"
                : run.Status.ToString().ToUpperInvariant(),
            run.Id.ToString(),
            clock.UtcNow,
            AuditActorType.SystemWorker);

    private static string CreateIdempotencyKey(TrashPurgeCandidate candidate)
    {
        var input = System.Text.Encoding.UTF8.GetBytes(
            $"{candidate.OwnerUserId:N}:{candidate.RootId:N}:{candidate.TrashedAt.UtcTicks}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]).ToString();
    }
}
