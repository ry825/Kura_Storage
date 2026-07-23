using KuraStorage.Domain.Identity;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class UserAuthenticationTests
{
    [Fact]
    public void RecordFailedLogin_WhenTenFailuresOccurWithinFifteenMinutes_AppliesSecurityLock()
    {
        var now = DateTimeOffset.Parse("2026-07-23T00:00:00Z");
        var user = CreateUser(now);

        for (var index = 0; index < 10; index++)
        {
            user.RecordFailedLogin(now.AddMinutes(index), TimeSpan.FromMinutes(15), 10);
        }

        Assert.Equal(UserLockType.Security, user.LockType);
        Assert.False(user.CanAuthenticate);
    }

    [Fact]
    public void RecordSuccessfulLogin_WhenFailuresExist_ResetsFailureWindow()
    {
        var now = DateTimeOffset.Parse("2026-07-23T00:00:00Z");
        var user = CreateUser(now);
        user.RecordFailedLogin(now, TimeSpan.FromMinutes(15), 10);

        user.RecordSuccessfulLogin(now.AddMinutes(1));

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.FailedLoginWindowStartedAt);
    }

    private static User CreateUser(DateTimeOffset now) =>
        new(Guid.NewGuid(), "USER", "User", "encoded", UserRole.Member, now);
}
