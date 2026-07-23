namespace KuraStorage.Domain.Identity;

public enum UserRole
{
    Admin,
    Member,
}

public enum UserStatus
{
    Active,
    Disabled,
}

public enum UserLockType
{
    None,
    Security,
}

public enum DevicePlatform
{
    Android,
}

public enum DeviceStatus
{
    Active,
    Revoked,
}
