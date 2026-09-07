using System.Data;
using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaRepository(KuraStorageDbContext database) :
    IMediaRepository,
    IMediaDerivativeStatusRepository
{
    private static readonly string[] SupportedPhotoMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif", "image/heic", "image/heif"];

    public async Task<MediaDerivativeStatusSnapshot> GetLowStatusAsync(
        int profileVersion,
        CancellationToken cancellationToken)
    {
        var photos = database.FileEntries.AsNoTracking().Where(file =>
            file.EntryType == FileEntryType.File && file.Status == FileEntryStatus.Active &&
            file.MimeType != null && SupportedPhotoMimeTypes.Contains(file.MimeType));
        var current =
            from file in photos
            join derivative in database.FileDerivatives.AsNoTracking().Where(item =>
                    item.DerivativeType == DerivativeType.ImageLow && item.ProfileVersion == profileVersion)
                on new { FileId = file.Id, Version = file.FileVersion }
                equals new { FileId = derivative.SourceFileId, Version = derivative.SourceVersion }
                into matches
            from derivative in matches.DefaultIfEmpty()
            select derivative;
        var rows = await current.ToListAsync(cancellationToken);
        var orphanCount = await database.FileDerivatives.AsNoTracking().LongCountAsync(item =>
            item.DerivativeType == DerivativeType.ImageLow &&
            (item.ProfileVersion != profileVersion || !database.FileEntries.Any(file =>
                file.Id == item.SourceFileId && file.EntryType == FileEntryType.File &&
                file.Status == FileEntryStatus.Active && file.FileVersion == item.SourceVersion)),
            cancellationToken);
        return new MediaDerivativeStatusSnapshot(
            profileVersion,
            rows.Count,
            rows.LongCount(item => item?.Status == DerivativeStatus.Ready),
            rows.LongCount(item => item?.Status == DerivativeStatus.Pending),
            rows.LongCount(item => item?.Status == DerivativeStatus.Running),
            rows.LongCount(item => item?.Status == DerivativeStatus.Failed),
            rows.LongCount(item => item?.Status == DerivativeStatus.BlockedSourceMissing),
            rows.LongCount(item => item is null || item.Status == DerivativeStatus.Deleting),
            rows.Where(item => item?.Status == DerivativeStatus.Ready).Sum(item => item!.Size),
            0,
            orphanCount,
            DateTimeOffset.UtcNow);
    }

    public async Task<bool> StageRequiredLowAsync(
        FileEntry source,
        int profileVersion,
        MediaJobOrigin origin,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is not null)
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({RequiredLowLockKey(source, profileVersion)})",
                cancellationToken);
        }

        // A profile change creates a new persistent representation for the same
        // source version.  Retire an older profile only when no producer or
        // delivery lease is still using it; ClaimDeletingAsync then owns the
        // physical deletion.  Keeping the transition here makes backfill and
        // normal ingest converge on the same lifecycle rule.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE media_jobs AS job
            SET status = 'CANCELLED', worker_token = NULL, heartbeat_at = NULL,
                completed_at = {now}, error_code = 'MEDIA_PROFILE_SUPERSEDED', updated_at = {now}
            FROM file_derivatives AS derivative
            WHERE derivative.source_file_id = {source.Id}
              AND derivative.source_version = {source.FileVersion}
              AND derivative.derivative_type = 'IMAGE_LOW'
              AND derivative.profile_version <> {profileVersion}
              AND derivative.status <> 'DELETING'
              AND NOT EXISTS (
                  SELECT 1 FROM derivative_leases AS lease
                  WHERE lease.derivative_id = derivative.id AND lease.expires_at > {now})
              AND job.derivative_id = derivative.id
              AND job.status IN ('QUEUED', 'RUNNING');

            UPDATE file_derivatives AS derivative
            SET status = 'DELETING', error_code = 'MEDIA_PROFILE_SUPERSEDED',
                revision = revision + 1, updated_at = {now}
            WHERE derivative.source_file_id = {source.Id}
              AND derivative.source_version = {source.FileVersion}
              AND derivative.derivative_type = 'IMAGE_LOW'
              AND derivative.profile_version <> {profileVersion}
              AND derivative.status <> 'DELETING'
              AND NOT EXISTS (
                  SELECT 1 FROM derivative_leases AS lease
                  WHERE lease.derivative_id = derivative.id AND lease.expires_at > {now});
            """,
            cancellationToken);

        var tracked = database.FileDerivatives.Local.SingleOrDefault(
            item => item.SourceFileId == source.Id && item.SourceVersion == source.FileVersion &&
                item.DerivativeType == DerivativeType.ImageLow && item.ProfileVersion == profileVersion);
        var existing = tracked ?? await database.FileDerivatives.SingleOrDefaultAsync(
            item => item.SourceFileId == source.Id && item.SourceVersion == source.FileVersion &&
                item.DerivativeType == DerivativeType.ImageLow && item.ProfileVersion == profileVersion,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == DerivativeStatus.BlockedSourceMissing)
            {
                if (existing.RestoreAfterSourceAvailable(now))
                {
                    return false;
                }
                database.MediaJobs.Add(new MediaJob(
                    Guid.NewGuid(), existing.Id, DerivativeType.ImageLow, source.OwnerUserId, now, origin));
                return true;
            }
            if (existing.Status == DerivativeStatus.Failed && existing.ErrorCode == "MEDIA_SOURCE_TRASHED")
            {
                existing.Retry(now);
                database.MediaJobs.Add(new MediaJob(
                    Guid.NewGuid(), existing.Id, DerivativeType.ImageLow, source.OwnerUserId, now, origin));
                return true;
            }
            return false;
        }

        var derivative = new FileDerivative(
            Guid.NewGuid(), source.Id, source.FileVersion, DerivativeType.ImageLow, profileVersion, now);
        var job = new MediaJob(
            Guid.NewGuid(), derivative.Id, DerivativeType.ImageLow, source.OwnerUserId, now, origin);
        database.AddRange(derivative, job);
        return true;
    }

    private static long RequiredLowLockKey(FileEntry source, int profileVersion)
    {
        var key = Encoding.UTF8.GetBytes(
            $"{source.Id:N}:{source.FileVersion}:{DerivativeType.ImageLow}:{profileVersion}");
        return BitConverter.ToInt64(SHA256.HashData(key));
    }

    public async Task<MediaRequestSnapshot> GetOrCreateRequestAsync(
        FileEntry source,
        DerivativeType derivativeType,
        int profileVersion,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await database.FileDerivatives.SingleOrDefaultAsync(
            item => item.SourceFileId == source.Id && item.SourceVersion == source.FileVersion &&
                item.DerivativeType == derivativeType && item.ProfileVersion == profileVersion,
            cancellationToken);
        if (existing is not null)
        {
            return new MediaRequestSnapshot(source, existing, await ActiveOrLatestJobAsync(existing.Id, cancellationToken));
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        existing = await database.FileDerivatives.SingleOrDefaultAsync(
            item => item.SourceFileId == source.Id && item.SourceVersion == source.FileVersion &&
                item.DerivativeType == derivativeType && item.ProfileVersion == profileVersion,
            cancellationToken);
        if (existing is null)
        {
            existing = new FileDerivative(Guid.NewGuid(), source.Id, source.FileVersion, derivativeType, profileVersion, now);
            var job = new MediaJob(
                Guid.NewGuid(), existing.Id, derivativeType, requestedByUserId, now,
                MediaJobOrigin.InteractiveRepair);
            database.AddRange(existing, job);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MediaRequestSnapshot(source, existing, job);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MediaRequestSnapshot(source, existing, await ActiveOrLatestJobAsync(existing.Id, cancellationToken));
    }

    public async Task<MediaRequestSnapshot?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await database.MediaJobs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var derivative = await database.FileDerivatives.AsNoTracking()
            .SingleAsync(item => item.Id == job.DerivativeId, cancellationToken);
        var source = await database.FileEntries.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == derivative.SourceFileId, cancellationToken);
        return source is null ? null : new MediaRequestSnapshot(source, derivative, job);
    }

    public async Task<MediaGenerationContext?> TryAcquireGenerationAsync(
        Guid jobId,
        Guid workerToken,
        Guid leaseOwnerToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty || workerToken == Guid.Empty || leaseOwnerToken == Guid.Empty ||
            leaseDuration <= TimeSpan.Zero)
        {
            return null;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var job = await database.MediaJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is not { Status: MediaJobStatus.Running } || job.WorkerToken != workerToken)
        {
            return null;
        }

        var derivative = await database.FileDerivatives.SingleAsync(item => item.Id == job.DerivativeId, cancellationToken);
        var source = await database.FileEntries.SingleOrDefaultAsync(item => item.Id == derivative.SourceFileId, cancellationToken);
        if (source is not { Status: FileEntryStatus.Active, EntryType: FileEntryType.File } ||
            source.FileVersion != derivative.SourceVersion || derivative.Status != DerivativeStatus.Running)
        {
            return null;
        }

        var expiresAt = now.Add(leaseDuration);
        var lease = new DerivativeLease(Guid.NewGuid(), derivative.Id, DerivativeLeaseType.Generation, leaseOwnerToken, expiresAt, now);
        database.DerivativeLeases.Add(lease);
        derivative.ProjectLeaseUntil(expiresAt, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MediaGenerationContext(
            job.Id,
            derivative.Id,
            source.OwnerUserId,
            source.Id,
            source.FileVersion,
            RelativeStoragePath.Create(source.RelativePath),
            source.Size,
            source.MimeType,
            derivative.DerivativeType,
            derivative.ProfileVersion,
            job.AttemptCount,
            leaseOwnerToken);
    }

    public async Task<bool> CompleteGenerationAsync(
        Guid jobId,
        Guid workerToken,
        Guid leaseOwnerToken,
        PublishedDerivative published,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var job = await database.MediaJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is not { Status: MediaJobStatus.Running } || job.WorkerToken != workerToken)
        {
            return false;
        }

        var derivative = await database.FileDerivatives.SingleAsync(item => item.Id == job.DerivativeId, cancellationToken);
        var source = await database.FileEntries.SingleOrDefaultAsync(item => item.Id == derivative.SourceFileId, cancellationToken);
        var lease = await database.DerivativeLeases.SingleOrDefaultAsync(item =>
            item.DerivativeId == derivative.Id && item.LeaseType == DerivativeLeaseType.Generation &&
            item.OwnerToken == leaseOwnerToken && item.ExpiresAt > now, cancellationToken);
        if (source is not { Status: FileEntryStatus.Active } || source.FileVersion != derivative.SourceVersion ||
            derivative.Status != DerivativeStatus.Running || lease is null)
        {
            return false;
        }

        derivative.MarkReady(
            published.Path.Value,
            published.Size,
            now,
            derivative.IsPersistent ? null : expiresAt);
        job.Complete(workerToken, now);
        database.DerivativeLeases.Remove(lease);
        derivative.ClearLeaseProjection(now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DerivativeLeaseHandle?> TryAcquireDeliveryAsync(
        Guid derivativeId,
        Guid ownerToken,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (derivativeId == Guid.Empty || ownerToken == Guid.Empty || duration <= TimeSpan.Zero)
        {
            return null;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var expiresAt = now.Add(duration);
        var leaseId = Guid.NewGuid();
        var inserted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO derivative_leases
                (id, derivative_id, lease_type, owner_token, expires_at, created_at, updated_at)
            SELECT {leaseId}, derivative.id, 'DELIVERY', {ownerToken}, {expiresAt}, {now}, {now}
            FROM file_derivatives AS derivative
            INNER JOIN file_entries AS source ON source.id = derivative.source_file_id
            WHERE derivative.id = {derivativeId}
              AND derivative.status = 'READY'
              AND source.status = 'ACTIVE'
              AND source.file_version = derivative.source_version
            ON CONFLICT (derivative_id, lease_type, owner_token) DO NOTHING;
            """,
            cancellationToken);
        if (inserted != 1)
        {
            return null;
        }

        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE file_derivatives
            SET lease_until = GREATEST(COALESCE(lease_until, {expiresAt}), {expiresAt}),
                revision = revision + 1,
                updated_at = {now}
            WHERE id = {derivativeId};
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return new DerivativeLeaseHandle(derivativeId, ownerToken, expiresAt);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid derivativeId,
        DerivativeLeaseType leaseType,
        Guid ownerToken,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (derivativeId == Guid.Empty || ownerToken == Guid.Empty || !Enum.IsDefined(leaseType) ||
            duration <= TimeSpan.Zero)
        {
            return false;
        }

        var expiresAt = now.Add(duration);
        var leaseTypeValue = leaseType.ToString().ToUpperInvariant();
        var updated = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH renewed AS (
                UPDATE derivative_leases
                SET expires_at = {expiresAt}, updated_at = {now}
                WHERE derivative_id = {derivativeId}
                  AND lease_type = {leaseTypeValue}
                  AND owner_token = {ownerToken}
                  AND expires_at > {now}
                RETURNING derivative_id
            )
            UPDATE file_derivatives
            SET lease_until = GREATEST(COALESCE(lease_until, {expiresAt}), {expiresAt}),
                revision = revision + 1,
                updated_at = {now}
            WHERE id = {derivativeId}
              AND EXISTS (SELECT 1 FROM renewed);
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return updated == 1;
    }

    public async Task<bool> ReleaseLeaseAsync(
        Guid derivativeId,
        DerivativeLeaseType leaseType,
        Guid ownerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (derivativeId == Guid.Empty || ownerToken == Guid.Empty || !Enum.IsDefined(leaseType))
        {
            return false;
        }

        var leaseTypeValue = leaseType.ToString().ToUpperInvariant();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var released = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM derivative_leases
            WHERE derivative_id = {derivativeId}
              AND lease_type = {leaseTypeValue}
              AND owner_token = {ownerToken};
            """,
            cancellationToken);
        if (released != 1)
        {
            return false;
        }

        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE file_derivatives
            SET lease_until = (
                    SELECT max(expires_at)
                    FROM derivative_leases
                    WHERE derivative_id = {derivativeId} AND expires_at > {now}),
                revision = revision + 1,
                updated_at = {now}
            WHERE id = {derivativeId};
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return true;
    }

    public async Task<bool> RecordDeliveryAccessAsync(
        Guid derivativeId,
        DateTimeOffset now,
        TimeSpan cacheTtl,
        CancellationToken cancellationToken)
    {
        if (derivativeId == Guid.Empty || cacheTtl <= TimeSpan.Zero)
        {
            return false;
        }

        var expiresAt = now.Add(cacheTtl);
        var updated = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE file_derivatives
            SET last_accessed_at = {now},
                expires_at = {expiresAt},
                revision = revision + 1,
                updated_at = {now}
            WHERE id = {derivativeId}
              AND status = 'READY'
              AND derivative_type NOT IN ('THUMBNAIL', 'PDF_THUMBNAIL', 'IMAGE_LOW');
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return updated == 1 || await database.FileDerivatives.AsNoTracking().AnyAsync(
            item => item.Id == derivativeId && item.Status == DerivativeStatus.Ready &&
                (item.DerivativeType == DerivativeType.Thumbnail ||
                    item.DerivativeType == DerivativeType.ImageLow ||
                    item.DerivativeType == DerivativeType.PdfThumbnail),
            cancellationToken);
    }

    private async Task<MediaJob?> ActiveOrLatestJobAsync(Guid derivativeId, CancellationToken cancellationToken) =>
        await database.MediaJobs
            .Where(item => item.DerivativeId == derivativeId)
            .OrderBy(item => item.Status == MediaJobStatus.Queued || item.Status == MediaJobStatus.Running ? 0 : 1)
            .ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
