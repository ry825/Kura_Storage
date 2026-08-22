using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Transfers;

namespace KuraStorage.Application.Transfers;

public sealed class UploadSessionRecoveryService(
    IUploadSessionRepository sessions,
    IFileRepository files,
    IUploadSessionStore store,
    IStorageGuard storageGuard,
    ISystemClock clock,
    UploadSessionService uploadService)
{
    private const int RecoveryBatchSize = 100;
    private static readonly Meter Meter = new("KuraStorage.Transfers");
    private static readonly Counter<long> RecoveryCounter = Meter.CreateCounter<long>("kurastorage.upload.recovery");
    private static readonly UpDownCounter<long> ActiveSessions = Meter.CreateUpDownCounter<long>("kurastorage.upload.active_sessions");

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            return;
        }

        var candidates = await sessions.ListRecoveryCandidatesAsync(RecoveryBatchSize, cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var sessionLock = await files.AcquireMutationLocksAsync([candidate.Id], cancellationToken);
            var session = await sessions.FindAsync(candidate.Id, cancellationToken);
            if (session is null)
            {
                continue;
            }

            if (session.Status == UploadSessionStatus.Completing)
            {
                var result = await uploadService.RecoverCompletingAsync(session, cancellationToken);
                RecoveryCounter.Add(1, new KeyValuePair<string, object?>("result", result.IsSuccess ? "published" : "required"));
                if (result.IsSuccess)
                {
                    ActiveSessions.Add(-1);
                }
                continue;
            }

            if (session.Status != UploadSessionStatus.Active)
            {
                continue;
            }

            var state = await store.InspectAsync(
                RelativeStoragePath.Create(session.TemporaryRelativePath),
                cancellationToken);
            if (!state.Exists && session.ReceivedBytes == 0)
            {
                continue;
            }

            if (!state.Exists || state.Length < session.ReceivedBytes)
            {
                session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
                await sessions.SaveChangesAsync(cancellationToken);
                RecoveryCounter.Add(1, new KeyValuePair<string, object?>("result", "required"));
                ActiveSessions.Add(-1);
                continue;
            }

            if (state.Length > session.ReceivedBytes)
            {
                await store.TruncateAsync(
                    RelativeStoragePath.Create(session.TemporaryRelativePath),
                    session.ReceivedBytes,
                    cancellationToken);
                RecoveryCounter.Add(1, new KeyValuePair<string, object?>("result", "truncated"));
            }
        }
    }
}
