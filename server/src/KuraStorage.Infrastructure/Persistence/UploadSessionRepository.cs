using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class UploadSessionRepository(KuraStorageDbContext dbContext) : IUploadSessionRepository
{
    public async Task<UploadSession?> FindByActorAndKeyAsync(
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await dbContext.UploadSessions.SingleOrDefaultAsync(
            session => session.ActorUserId == actorUserId && session.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<UploadSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await dbContext.UploadSessions.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task ReloadAsync(UploadSession session, CancellationToken cancellationToken) =>
        await dbContext.Entry(session).ReloadAsync(cancellationToken);

    public async Task<bool> IsDeviceActiveAsync(
        Guid actorUserId,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        await dbContext.Devices.AnyAsync(
            device => device.Id == deviceId && device.UserId == actorUserId && device.Status == DeviceStatus.Active,
            cancellationToken);

    public async Task<int> CountActiveForActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        await dbContext.UploadSessions.CountAsync(
            session => session.ActorUserId == actorUserId && session.Status == UploadSessionStatus.Active,
            cancellationToken);

    public async Task<int> CountActiveForDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await dbContext.UploadSessions.CountAsync(
            session => session.DeviceId == deviceId && session.Status == UploadSessionStatus.Active,
            cancellationToken);

    public async Task<IReadOnlyList<UploadSession>> ListCleanupCandidatesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.UploadSessions
            .AsNoTracking()
            .Where(session =>
                (session.Status == UploadSessionStatus.Active &&
                 (session.ExpiresAt <= now || !dbContext.Devices.Any(device =>
                     device.Id == session.DeviceId && device.Status == DeviceStatus.Active))) ||
                ((session.Status == UploadSessionStatus.Cancelled ||
                  session.Status == UploadSessionStatus.Expired) && session.CleanedAt == null))
            .OrderBy(session => session.ExpiresAt)
            .ThenBy(session => session.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UploadSession>> ListRecoveryCandidatesAsync(
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.UploadSessions
            .AsNoTracking()
            .Where(session =>
                session.Status == UploadSessionStatus.Active ||
                session.Status == UploadSessionStatus.Completing ||
                session.Status == UploadSessionStatus.RecoveryRequired)
            .OrderBy(session => session.UpdatedAt)
            .ThenBy(session => session.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(UploadSession session) => dbContext.UploadSessions.Add(session);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new FilePersistenceConflictException(exception);
        }
    }
}
