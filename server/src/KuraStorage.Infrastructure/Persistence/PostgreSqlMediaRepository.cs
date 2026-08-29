using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class PostgreSqlMediaRepository(KuraStorageDbContext database) : IMediaRepository
{
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
            var job = new MediaJob(Guid.NewGuid(), existing.Id, derivativeType, requestedByUserId, now);
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

        derivative.MarkReady(published.Path.Value, published.Size, now, expiresAt);
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
              AND derivative_type NOT IN ('THUMBNAIL', 'PDF_THUMBNAIL');
            """,
            cancellationToken);
        database.ChangeTracker.Clear();
        return updated == 1 || await database.FileDerivatives.AsNoTracking().AnyAsync(
            item => item.Id == derivativeId && item.Status == DerivativeStatus.Ready &&
                (item.DerivativeType == DerivativeType.Thumbnail ||
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
