using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Identity;

namespace KuraStorage.Application.Identity;

public sealed class IdentityService(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IRefreshTokenService refreshTokens,
    IAccessTokenIssuer accessTokens,
    ISystemClock clock)
{
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromHours(24);
    private const int MaximumFailures = 10;
    private const int MaximumActiveDevices = 10;

    public async Task<IdentityResult<Guid>> CreateUserAsync(
        string username,
        string displayName,
        string password,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var normalized = UsernameNormalizer.Normalize(username);
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrEmpty(password))
        {
            return IdentityResult<Guid>.Fail(IdentityErrorCodes.InvalidCredentials, IdentityFailureKind.BadRequest);
        }

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        if (await repository.FindUserAsync(normalized, cancellationToken) is not null)
        {
            return IdentityResult<Guid>.Fail(IdentityErrorCodes.UsernameAlreadyExists, IdentityFailureKind.Conflict);
        }

        var now = clock.UtcNow;
        var user = new User(Guid.NewGuid(), normalized, displayName.Trim(), passwordHasher.Hash(password), role, now);
        repository.Add(user);
        repository.Add(Audit(null, null, "USER_CREATE", "User", user.Id.ToString(), "SUCCESS", now));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult<Guid>.Success(user.Id);
    }

    public async Task<IdentityResult<TokenPair>> RegisterDeviceAsync(
        string username,
        string password,
        string deviceName,
        string? remoteAddress,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return IdentityResult<TokenPair>.Fail(IdentityErrorCodes.InvalidCredentials, IdentityFailureKind.BadRequest);
        }

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var authentication = await AuthenticateAsync(username, password, null, remoteAddress, requestId, cancellationToken);
        if (!authentication.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
            return IdentityResult<TokenPair>.Fail(authentication.Failure!.Code, authentication.Failure.Kind);
        }

        var user = authentication.Value!;
        if (await repository.CountActiveDevicesAsync(user.Id, cancellationToken) >= MaximumActiveDevices)
        {
            await transaction.CommitAsync(cancellationToken);
            return IdentityResult<TokenPair>.Fail(IdentityErrorCodes.DeviceLimitReached, IdentityFailureKind.Conflict);
        }

        var now = clock.UtcNow;
        var device = new Device(Guid.NewGuid(), user.Id, deviceName.Trim(), now);
        repository.Add(device);
        var pair = CreateTokenPair(user.Id, device.Id, Guid.NewGuid(), now, out var session);
        repository.Add(session);
        repository.Add(Audit(user.Id, device.Id, "DEVICE_REGISTER", "Device", device.Id.ToString(), "SUCCESS", now, requestId));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult<TokenPair>.Success(pair);
    }

    public async Task<IdentityResult<TokenPair>> LoginAsync(
        string username,
        string password,
        Guid deviceId,
        string? remoteAddress,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var authentication = await AuthenticateAsync(username, password, deviceId, remoteAddress, requestId, cancellationToken);
        if (!authentication.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
            return IdentityResult<TokenPair>.Fail(authentication.Failure!.Code, authentication.Failure.Kind);
        }

        var user = authentication.Value!;
        var device = await repository.FindDeviceAsync(deviceId, cancellationToken);
        if (device is null || device.UserId != user.Id || device.Status != DeviceStatus.Active)
        {
            repository.Add(Audit(user.Id, deviceId, "LOGIN", "Device", deviceId.ToString(), "DEVICE_REJECTED", clock.UtcNow, requestId));
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IdentityResult<TokenPair>.Fail(IdentityErrorCodes.DeviceRevoked, IdentityFailureKind.Forbidden);
        }

        var now = clock.UtcNow;
        await repository.RevokeCurrentSessionsAsync(device.Id, now, cancellationToken);
        var pair = CreateTokenPair(user.Id, device.Id, Guid.NewGuid(), now, out var session);
        repository.Add(session);
        repository.Add(Audit(user.Id, device.Id, "LOGIN", "Device", device.Id.ToString(), "SUCCESS", now, requestId));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult<TokenPair>.Success(pair);
    }

    public async Task<IdentityResult<TokenPair>> RefreshAsync(
        Guid deviceId,
        string refreshToken,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var now = clock.UtcNow;
        var session = await repository.FindRefreshSessionForUpdateAsync(refreshTokens.Hash(refreshToken), cancellationToken);
        if (session is null || session.DeviceId != deviceId)
        {
            return IdentityResult<TokenPair>.Fail(IdentityErrorCodes.RefreshTokenInvalid, IdentityFailureKind.Unauthorized);
        }

        if (session.UsedAt is not null)
        {
            await repository.RevokeSessionFamilyAsync(session.FamilyId, now, cancellationToken);
            repository.Add(Audit(session.UserId, deviceId, "REFRESH_REUSE", "SessionFamily", session.FamilyId.ToString(), "REVOKED", now, requestId));
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IdentityResult<TokenPair>.Fail(IdentityErrorCodes.RefreshTokenReused, IdentityFailureKind.Unauthorized);
        }

        var user = await repository.FindUserByIdAsync(session.UserId, cancellationToken);
        var device = await repository.FindDeviceAsync(deviceId, cancellationToken);
        if (!session.IsCurrentAt(now) || user?.CanAuthenticate != true || device?.Status != DeviceStatus.Active)
        {
            return IdentityResult<TokenPair>.Fail(
                device?.Status == DeviceStatus.Revoked ? IdentityErrorCodes.DeviceRevoked : IdentityErrorCodes.RefreshTokenInvalid,
                device?.Status == DeviceStatus.Revoked ? IdentityFailureKind.Forbidden : IdentityFailureKind.Unauthorized);
        }

        var pair = CreateTokenPair(session.UserId, deviceId, session.FamilyId, now, out var replacement);
        session.MarkUsed(now);
        await repository.SaveChangesAsync(cancellationToken);
        repository.Add(replacement);
        await repository.SaveChangesAsync(cancellationToken);
        session.SetReplacement(replacement.Id);
        repository.Add(Audit(session.UserId, deviceId, "REFRESH_ROTATE", "Session", replacement.Id.ToString(), "SUCCESS", now, requestId));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult<TokenPair>.Success(pair);
    }

    public async Task LogoutAsync(Guid deviceId, string refreshToken, string? requestId, CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var session = await repository.FindRefreshSessionForUpdateAsync(refreshTokens.Hash(refreshToken), cancellationToken);
        if (session is not null && session.DeviceId == deviceId)
        {
            var now = clock.UtcNow;
            await repository.RevokeSessionFamilyAsync(session.FamilyId, now, cancellationToken);
            repository.Add(Audit(session.UserId, deviceId, "LOGOUT", "Session", session.Id.ToString(), "SUCCESS", now, requestId));
            await repository.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> ValidateSessionAsync(
        Guid userId,
        Guid deviceId,
        Guid familyId,
        CancellationToken cancellationToken) =>
        await repository.IsSessionFamilyActiveAsync(userId, deviceId, familyId, clock.UtcNow, cancellationToken);

    public async Task<IReadOnlyList<Device>> ListDevicesAsync(Guid userId, CancellationToken cancellationToken) =>
        await repository.ListDevicesAsync(userId, cancellationToken);

    public async Task<bool> RevokeDeviceAsync(Guid userId, Guid deviceId, string? requestId, CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var device = await repository.FindDeviceAsync(deviceId, cancellationToken);
        if (device is null || device.UserId != userId)
        {
            return false;
        }

        var now = clock.UtcNow;
        device.Revoke(now);
        await repository.RevokeAllDeviceSessionsAsync(deviceId, now, cancellationToken);
        repository.Add(Audit(userId, deviceId, "DEVICE_REVOKE", "Device", deviceId.ToString(), "SUCCESS", now, requestId));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnlockUserAsync(string username, CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var user = await repository.FindUserAsync(UsernameNormalizer.Normalize(username), cancellationToken);
        if (user is null)
        {
            return false;
        }

        var now = clock.UtcNow;
        user.Unlock(now);
        repository.Add(Audit(user.Id, null, "USER_UNLOCK", "User", user.Id.ToString(), "SUCCESS", now));
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<IdentityResult<User>> AuthenticateAsync(
        string username,
        string password,
        Guid? deviceId,
        string? remoteAddress,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var normalized = UsernameNormalizer.Normalize(username);
        var user = await repository.FindUserAsync(normalized, cancellationToken);
        var verification = passwordHasher.Verify(password, user?.PasswordHash ?? string.Empty);
        var now = clock.UtcNow;

        if (user is null || !verification.IsValid)
        {
            if (user is not null)
            {
                user.RecordFailedLogin(now, FailureWindow, MaximumFailures);
            }

            repository.Add(Attempt(normalized, deviceId, "INVALID_CREDENTIALS", remoteAddress, now));
            repository.Add(Audit(user?.Id, deviceId, "LOGIN", "User", user?.Id.ToString(), "INVALID_CREDENTIALS", now, requestId));
            await repository.SaveChangesAsync(cancellationToken);
            return IdentityResult<User>.Fail(IdentityErrorCodes.InvalidCredentials, IdentityFailureKind.Unauthorized);
        }

        if (!user.CanAuthenticate)
        {
            repository.Add(Attempt(normalized, deviceId, "ACCOUNT_LOCKED", remoteAddress, now));
            await repository.SaveChangesAsync(cancellationToken);
            return IdentityResult<User>.Fail(IdentityErrorCodes.AccountLocked, IdentityFailureKind.Forbidden);
        }

        user.RecordSuccessfulLogin(now);
        if (verification.NeedsRehash)
        {
            user.ReplacePasswordHash(passwordHasher.Hash(password), now);
        }

        repository.Add(Attempt(normalized, deviceId, "SUCCESS", remoteAddress, now));
        await repository.SaveChangesAsync(cancellationToken);
        return IdentityResult<User>.Success(user);
    }

    private TokenPair CreateTokenPair(
        Guid userId,
        Guid deviceId,
        Guid familyId,
        DateTimeOffset now,
        out RefreshSession session)
    {
        var refreshToken = refreshTokens.Generate();
        var refreshExpiresAt = now.Add(RefreshLifetime);
        session = new RefreshSession(
            Guid.NewGuid(),
            familyId,
            userId,
            deviceId,
            refreshTokens.Hash(refreshToken),
            refreshExpiresAt,
            now);
        var accessToken = accessTokens.Issue(userId, deviceId, familyId, now);
        return new TokenPair(deviceId, accessToken.Value, refreshToken, accessToken.ExpiresAt, refreshExpiresAt);
    }

    private static AuthenticationAttempt Attempt(
        string normalizedUsername,
        Guid? deviceId,
        string result,
        string? remoteAddress,
        DateTimeOffset now)
    {
        _ = System.Net.IPAddress.TryParse(remoteAddress, out var parsedAddress);
        return new AuthenticationAttempt(
            Guid.NewGuid(),
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUsername)),
            deviceId,
            result,
            parsedAddress,
            now);
    }

    private static AuditLog Audit(
        Guid? userId,
        Guid? deviceId,
        string action,
        string? targetType,
        string? targetId,
        string result,
        DateTimeOffset now,
        string? requestId = null) =>
        new(Guid.NewGuid(), userId, deviceId, null, action, targetType, targetId, result, requestId, now);
}
