namespace KuraStorage.Domain.Identity;

public sealed class Device
{
    private Device()
    {
    }

    public Device(Guid id, Guid userId, string deviceName, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        DeviceName = deviceName;
        Platform = DevicePlatform.Android;
        Status = DeviceStatus.Active;
        RegisteredAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string DeviceName { get; private set; } = string.Empty;

    public DevicePlatform Platform { get; private set; }

    public DeviceStatus Status { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public void Revoke(DateTimeOffset now)
    {
        Status = DeviceStatus.Revoked;
        RevokedAt ??= now;
    }
}
