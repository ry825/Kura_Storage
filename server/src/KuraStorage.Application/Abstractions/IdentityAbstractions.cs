using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Identity;

namespace KuraStorage.Application.Abstractions;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerification Verify(string password, string encodedHash);
}

public readonly record struct PasswordVerification(bool IsValid, bool NeedsRehash);

public interface IRefreshTokenService
{
    string Generate();

    byte[] Hash(string token);
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(Guid userId, Guid deviceId, Guid sessionFamilyId, DateTimeOffset now);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IIdentityRepository
{
    Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<User?> FindUserAsync(string normalizedUsername, CancellationToken cancellationToken);

    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<Device?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    Task<int> CountActiveDevicesAsync(Guid userId, CancellationToken cancellationToken);

    Task<RefreshSession?> FindRefreshSessionForUpdateAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task RevokeCurrentSessionsAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAllDeviceSessionsAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeSessionFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> IsSessionFamilyActiveAsync(Guid userId, Guid deviceId, Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<Device>> ListDevicesAsync(Guid userId, CancellationToken cancellationToken);

    void Add(User user);

    void Add(Device device);

    void Add(RefreshSession session);

    void Add(AuthenticationAttempt attempt);

    void Add(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IStorageGuard
{
    Task<StorageStatus> InspectAsync(bool requireWrite, CancellationToken cancellationToken);
}

public enum StorageStatus
{
    Available,
    ReadOnly,
    Unavailable,
}
