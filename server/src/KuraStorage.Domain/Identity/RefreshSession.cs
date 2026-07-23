namespace KuraStorage.Domain.Identity;

public sealed class RefreshSession
{
    private RefreshSession()
    {
    }

    public RefreshSession(
        Guid id,
        Guid familyId,
        Guid userId,
        Guid deviceId,
        byte[] tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        Id = id;
        FamilyId = familyId;
        UserId = userId;
        DeviceId = deviceId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid FamilyId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid DeviceId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedBySessionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsCurrentAt(DateTimeOffset now) =>
        UsedAt is null && RevokedAt is null && ReplacedBySessionId is null && ExpiresAt > now;

    public void RotateTo(Guid replacementId, DateTimeOffset now)
    {
        MarkUsed(now);
        SetReplacement(replacementId);
    }

    public void MarkUsed(DateTimeOffset now)
    {
        if (!IsCurrentAt(now))
        {
            throw new InvalidOperationException("Only a current refresh session can be used.");
        }

        UsedAt = now;
    }

    public void SetReplacement(Guid replacementId)
    {
        if (UsedAt is null || ReplacedBySessionId is not null)
        {
            throw new InvalidOperationException("A replacement can only be assigned to a used session.");
        }

        ReplacedBySessionId = replacementId;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
