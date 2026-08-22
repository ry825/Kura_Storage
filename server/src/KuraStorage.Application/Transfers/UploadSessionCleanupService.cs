using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Transfers;

namespace KuraStorage.Application.Transfers;

public sealed class UploadSessionCleanupService(
    IUploadSessionRepository sessions,
    IFileRepository files,
    IUploadSessionStore store,
    IStorageGuard storageGuard,
    ISystemClock clock,
    UploadSessionOptions options)
{
    private static readonly Guid GlobalCleanupLockId = Guid.Parse("3ce3bde4-175b-43f0-a7fd-4678285ea7c0");
    private static readonly Meter Meter = new("KuraStorage.Transfers");
    private static readonly Counter<long> CleanupCounter = Meter.CreateCounter<long>("kurastorage.upload.cleanup");
    private static readonly UpDownCounter<long> ActiveSessions = Meter.CreateUpDownCounter<long>("kurastorage.upload.active_sessions");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (await storageGuard.InspectAsync(StorageIntent.Delete, cancellationToken) != StorageStatus.Available)
        {
            return;
        }

        await using var globalLock = await files.AcquireMutationLocksAsync([GlobalCleanupLockId], cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var candidates = await sessions.ListCleanupCandidatesAsync(
                clock.UtcNow,
                options.CleanupBatchSize,
                cancellationToken);
            if (candidates.Count == 0)
            {
                return;
            }

            var hadFailure = false;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CleanupCounter.Add(1, new KeyValuePair<string, object?>("result", "inspected"));
                hadFailure |= !await CleanOneAsync(candidate.Id, cancellationToken);
            }

            if (hadFailure || candidates.Count < options.CleanupBatchSize)
            {
                return;
            }
        }
    }

    private async Task<bool> CleanOneAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var sessionLock = await files.AcquireMutationLocksAsync([sessionId], cancellationToken);
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null || session.Status is UploadSessionStatus.Completed or
            UploadSessionStatus.Completing or UploadSessionStatus.RecoveryRequired || session.CleanedAt is not null)
        {
            return true;
        }

        var now = clock.UtcNow;
        if (session.Status == UploadSessionStatus.Active)
        {
            var deviceActive = await sessions.IsDeviceActiveAsync(
                session.OwnerUserId,
                session.DeviceId,
                cancellationToken);
            if (!deviceActive)
            {
                session.Cancel("DEVICE_REVOKED", now);
                ActiveSessions.Add(-1);
                CleanupCounter.Add(1, new KeyValuePair<string, object?>("result", "device_revoked"));
            }
            else if (session.IsExpiredAt(now))
            {
                session.Expire(now);
                ActiveSessions.Add(-1);
                CleanupCounter.Add(1, new KeyValuePair<string, object?>("result", "expired"));
            }
            else
            {
                return true;
            }

            await sessions.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await store.DeleteIfExistsAsync(
                RelativeStoragePath.Create(session.TemporaryRelativePath),
                cancellationToken);
            session.MarkCleaned(clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
            CleanupCounter.Add(1, new KeyValuePair<string, object?>("result", "cleaned"));
            return true;
        }
        catch (IOException)
        {
            CleanupCounter.Add(1, new KeyValuePair<string, object?>("result", "failed"));
            return false;
        }
    }
}
