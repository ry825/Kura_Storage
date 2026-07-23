using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class IdentityRepository(KuraStorageDbContext dbContext) : IIdentityRepository
{
    public async Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return new IdentityTransaction(transaction);
    }

    public async Task<User?> FindUserAsync(string normalizedUsername, CancellationToken cancellationToken) =>
        await dbContext.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE username_normalized = {normalizedUsername} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<Device?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await dbContext.Devices.SingleOrDefaultAsync(device => device.Id == deviceId, cancellationToken);

    public async Task<int> CountActiveDevicesAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Devices.CountAsync(
            device => device.UserId == userId && device.Status == DeviceStatus.Active,
            cancellationToken);

    public async Task<RefreshSession?> FindRefreshSessionForUpdateAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken) =>
        await dbContext.RefreshSessions
            .FromSqlInterpolated($"SELECT * FROM refresh_sessions WHERE token_hash = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task RevokeCurrentSessionsAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.RefreshSessions
            .Where(session =>
                session.DeviceId == deviceId &&
                session.RevokedAt == null &&
                session.UsedAt == null &&
                session.ReplacedBySessionId == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now);
        }
    }

    public async Task RevokeAllDeviceSessionsAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.RefreshSessions
            .Where(session => session.DeviceId == deviceId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now);
        }
    }

    public async Task RevokeSessionFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.RefreshSessions
            .Where(session => session.FamilyId == familyId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now);
        }
    }

    public async Task<bool> IsSessionFamilyActiveAsync(
        Guid userId,
        Guid deviceId,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await dbContext.Users.AnyAsync(user => user.Id == userId && user.Status == UserStatus.Active, cancellationToken) &&
        await dbContext.Devices.AnyAsync(
            device => device.Id == deviceId && device.UserId == userId && device.Status == DeviceStatus.Active,
            cancellationToken) &&
        await dbContext.RefreshSessions.AnyAsync(
            session =>
                session.UserId == userId &&
                session.DeviceId == deviceId &&
                session.FamilyId == familyId &&
                session.RevokedAt == null &&
                session.UsedAt == null &&
                session.ReplacedBySessionId == null &&
                session.ExpiresAt > now,
            cancellationToken);

    public async Task<IReadOnlyList<Device>> ListDevicesAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Devices
            .AsNoTracking()
            .Where(device => device.UserId == userId)
            .OrderBy(device => device.RegisteredAt)
            .ToListAsync(cancellationToken);

    public void Add(User user) => dbContext.Users.Add(user);

    public void Add(Device device) => dbContext.Devices.Add(device);

    public void Add(RefreshSession session) => dbContext.RefreshSessions.Add(session);

    public void Add(AuthenticationAttempt attempt) => dbContext.AuthenticationAttempts.Add(attempt);

    public void Add(AuditLog auditLog) => dbContext.AuditLogs.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);

    private sealed class IdentityTransaction(IDbContextTransaction transaction) : IIdentityTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken) =>
            await transaction.CommitAsync(cancellationToken);

        public async ValueTask DisposeAsync() => await transaction.DisposeAsync();
    }
}
