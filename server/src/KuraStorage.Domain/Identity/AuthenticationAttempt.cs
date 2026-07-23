namespace KuraStorage.Domain.Identity;

public sealed class AuthenticationAttempt
{
    private AuthenticationAttempt()
    {
    }

    public AuthenticationAttempt(
        Guid id,
        byte[] usernameHash,
        Guid? deviceId,
        string resultCode,
        System.Net.IPAddress? remoteAddress,
        DateTimeOffset createdAt)
    {
        Id = id;
        UsernameHash = usernameHash;
        DeviceId = deviceId;
        ResultCode = resultCode;
        RemoteAddress = remoteAddress;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public byte[] UsernameHash { get; private set; } = [];

    public Guid? DeviceId { get; private set; }

    public string ResultCode { get; private set; } = string.Empty;

    public System.Net.IPAddress? RemoteAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
