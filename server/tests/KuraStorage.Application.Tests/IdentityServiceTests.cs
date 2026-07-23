using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Identity;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Identity;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task RefreshAsync_WhenUsedTokenIsPresented_RevokesEntireSessionFamily()
    {
        var fixture = new Fixture();
        var userId = await fixture.CreateUserAsync();
        var registration = await fixture.Service.RegisterDeviceAsync(
            "alice",
            "password",
            "phone",
            null,
            "request",
            CancellationToken.None);
        var first = registration.Value!;
        var familyId = fixture.Repository.Sessions.Single().FamilyId;

        var rotated = await fixture.Service.RefreshAsync(
            first.DeviceId,
            first.RefreshToken,
            "request",
            CancellationToken.None);
        var reuse = await fixture.Service.RefreshAsync(
            first.DeviceId,
            first.RefreshToken,
            "request",
            CancellationToken.None);

        Assert.True(rotated.IsSuccess);
        Assert.Equal(IdentityErrorCodes.RefreshTokenReused, reuse.Failure?.Code);
        Assert.False(
            await fixture.Service.ValidateSessionAsync(
                userId,
                first.DeviceId,
                familyId,
                CancellationToken.None));
    }

    [Fact]
    public async Task RegisterDeviceAsync_WhenTenActiveDevicesExist_RejectsEleventhDevice()
    {
        var fixture = new Fixture();
        await fixture.CreateUserAsync();

        for (var index = 0; index < 10; index++)
        {
            var result = await fixture.Service.RegisterDeviceAsync(
                "alice",
                "password",
                $"phone-{index}",
                null,
                null,
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var rejected = await fixture.Service.RegisterDeviceAsync(
            "alice",
            "password",
            "phone-10",
            null,
            null,
            CancellationToken.None);

        Assert.Equal(IdentityErrorCodes.DeviceLimitReached, rejected.Failure?.Code);
    }

    [Fact]
    public async Task LoginAsync_WhenTenFailuresOccur_LocksUserAndPersistsAttemptsWithoutSecrets()
    {
        var fixture = new Fixture();
        await fixture.CreateUserAsync();
        var registration = await fixture.Service.RegisterDeviceAsync(
            "alice",
            "password",
            "phone",
            null,
            null,
            CancellationToken.None);

        for (var index = 0; index < 10; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            await fixture.Service.LoginAsync(
                "alice",
                "incorrect",
                registration.Value!.DeviceId,
                null,
                null,
                CancellationToken.None);
        }

        var locked = await fixture.Service.LoginAsync(
            "alice",
            "password",
            registration.Value!.DeviceId,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(IdentityErrorCodes.AccountLocked, locked.Failure?.Code);
        Assert.Equal(UserLockType.Security, fixture.Repository.Users.Single().LockType);
        Assert.DoesNotContain(
            fixture.Repository.Attempts,
            attempt => Convert.ToHexString(attempt.UsernameHash).Contains("PASSWORD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevokeDeviceAsync_WhenDeviceIsActive_RevokesDeviceAndEverySession()
    {
        var fixture = new Fixture();
        var userId = await fixture.CreateUserAsync();
        var registration = await fixture.Service.RegisterDeviceAsync(
            "alice",
            "password",
            "phone",
            null,
            null,
            CancellationToken.None);
        var deviceId = registration.Value!.DeviceId;

        var revoked = await fixture.Service.RevokeDeviceAsync(
            userId,
            deviceId,
            "request",
            CancellationToken.None);

        Assert.True(revoked);
        Assert.Equal(DeviceStatus.Revoked, fixture.Repository.Devices.Single().Status);
        Assert.All(fixture.Repository.Sessions, session => Assert.NotNull(session.RevokedAt));
    }

    private sealed class Fixture
    {
        public FakeRepository Repository { get; } = new();

        public FakeClock Clock { get; } = new();

        public IdentityService Service { get; }

        public Fixture()
        {
            Service = new IdentityService(
                Repository,
                new FakePasswordHasher(),
                new FakeRefreshTokenService(),
                new FakeAccessTokenIssuer(),
                Clock);
        }

        public async Task<Guid> CreateUserAsync()
        {
            var result = await Service.CreateUserAsync(
                "alice",
                "Alice",
                "password",
                UserRole.Member,
                CancellationToken.None);
            return result.Value;
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-07-23T00:00:00Z");

        public void Advance(TimeSpan value) => UtcNow = UtcNow.Add(value);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"HASH:{password}";

        public PasswordVerification Verify(string password, string encodedHash) =>
            new(encodedHash == $"HASH:{password}", false);
    }

    private sealed class FakeRefreshTokenService : IRefreshTokenService
    {
        private int sequence;

        public string Generate() => $"refresh-token-{++sequence:D20}";

        public byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    private sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
    {
        public AccessToken Issue(Guid userId, Guid deviceId, Guid sessionFamilyId, DateTimeOffset now) =>
            new($"access-{userId}-{deviceId}-{sessionFamilyId}", now.AddMinutes(15));
    }

    private sealed class FakeRepository : IIdentityRepository
    {
        public List<User> Users { get; } = [];

        public List<Device> Devices { get; } = [];

        public List<RefreshSession> Sessions { get; } = [];

        public List<AuthenticationAttempt> Attempts { get; } = [];

        public List<AuditLog> Audits { get; } = [];

        public Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IIdentityTransaction>(new FakeTransaction());

        public Task<User?> FindUserAsync(string normalizedUsername, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.UsernameNormalized == normalizedUsername));

        public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<Device?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(Devices.SingleOrDefault(device => device.Id == deviceId));

        public Task<int> CountActiveDevicesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Devices.Count(device => device.UserId == userId && device.Status == DeviceStatus.Active));

        public Task<RefreshSession?> FindRefreshSessionForUpdateAsync(
            byte[] tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session => session.TokenHash.SequenceEqual(tokenHash)));

        public Task RevokeCurrentSessionsAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var session in Sessions.Where(session => session.DeviceId == deviceId && session.IsCurrentAt(now)))
            {
                session.Revoke(now);
            }

            return Task.CompletedTask;
        }

        public Task RevokeAllDeviceSessionsAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var session in Sessions.Where(session => session.DeviceId == deviceId))
            {
                session.Revoke(now);
            }

            return Task.CompletedTask;
        }

        public Task RevokeSessionFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var session in Sessions.Where(session => session.FamilyId == familyId))
            {
                session.Revoke(now);
            }

            return Task.CompletedTask;
        }

        public Task<bool> IsSessionFamilyActiveAsync(
            Guid userId,
            Guid deviceId,
            Guid familyId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Users.Any(user => user.Id == userId && user.Status == UserStatus.Active) &&
                Devices.Any(device => device.Id == deviceId && device.Status == DeviceStatus.Active) &&
                Sessions.Any(
                    session =>
                        session.FamilyId == familyId &&
                        session.DeviceId == deviceId &&
                        session.RevokedAt is null &&
                        session.UsedAt is null &&
                        session.ReplacedBySessionId is null &&
                        session.ExpiresAt > now));

        public Task<IReadOnlyList<Device>> ListDevicesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Device>>(Devices.Where(device => device.UserId == userId).ToList());

        public void Add(User user) => Users.Add(user);

        public void Add(Device device) => Devices.Add(device);

        public void Add(RefreshSession session) => Sessions.Add(session);

        public void Add(AuthenticationAttempt attempt) => Attempts.Add(attempt);

        public void Add(AuditLog auditLog) => Audits.Add(auditLog);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class FakeTransaction : IIdentityTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
