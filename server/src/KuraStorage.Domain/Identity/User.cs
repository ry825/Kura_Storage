namespace KuraStorage.Domain.Identity;

public sealed class User
{
    private User()
    {
    }

    public User(Guid id, string normalizedUsername, string displayName, string passwordHash, UserRole role, DateTimeOffset now)
    {
        Id = id;
        UsernameNormalized = normalizedUsername;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        Status = UserStatus.Active;
        LockType = UserLockType.None;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string UsernameNormalized { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? FailedLoginWindowStartedAt { get; private set; }

    public UserLockType LockType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool CanAuthenticate => Status == UserStatus.Active && LockType == UserLockType.None;

    public void RecordFailedLogin(DateTimeOffset now, TimeSpan window, int maximumAttempts)
    {
        if (FailedLoginWindowStartedAt is null || now - FailedLoginWindowStartedAt >= window)
        {
            FailedLoginWindowStartedAt = now;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= maximumAttempts)
        {
            LockType = UserLockType.Security;
        }

        UpdatedAt = now;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        FailedLoginWindowStartedAt = null;
        UpdatedAt = now;
    }

    public void Unlock(DateTimeOffset now)
    {
        LockType = UserLockType.None;
        RecordSuccessfulLogin(now);
    }

    public void ReplacePasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        UpdatedAt = now;
    }
}
