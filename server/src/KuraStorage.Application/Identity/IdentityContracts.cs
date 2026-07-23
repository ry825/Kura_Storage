namespace KuraStorage.Application.Identity;

public sealed record TokenPair(
    Guid DeviceId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record IdentityFailure(string Code, IdentityFailureKind Kind);

public enum IdentityFailureKind
{
    BadRequest,
    Unauthorized,
    Forbidden,
    Conflict,
}

public sealed record IdentityResult<T>(T? Value, IdentityFailure? Failure)
{
    public bool IsSuccess => Failure is null;

    public static IdentityResult<T> Success(T value) => new(value, null);

    public static IdentityResult<T> Fail(string code, IdentityFailureKind kind) => new(default, new(code, kind));
}

public static class IdentityErrorCodes
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string DeviceLimitReached = "DEVICE_LIMIT_REACHED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string RefreshTokenInvalid = "REFRESH_TOKEN_INVALID";
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";
    public const string UsernameAlreadyExists = "USERNAME_ALREADY_EXISTS";
}
