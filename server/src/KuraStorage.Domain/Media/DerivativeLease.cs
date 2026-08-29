namespace KuraStorage.Domain.Media;

public sealed class DerivativeLease
{
    private DerivativeLease()
    {
    }

    public DerivativeLease(
        Guid id,
        Guid derivativeId,
        DerivativeLeaseType leaseType,
        Guid ownerToken,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || derivativeId == Guid.Empty || ownerToken == Guid.Empty)
        {
            throw new ArgumentException("Lease, derivative, and owner IDs are required.");
        }

        if (!Enum.IsDefined(leaseType))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseType));
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        Id = id;
        DerivativeId = derivativeId;
        LeaseType = leaseType;
        OwnerToken = ownerToken;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid DerivativeId { get; private set; }

    public DerivativeLeaseType LeaseType { get; private set; }

    public Guid OwnerToken { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsReleased { get; private set; }

    public bool IsExpiredAt(DateTimeOffset now) => IsReleased || ExpiresAt <= now;

    public void Renew(Guid ownerToken, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        EnsureOwner(ownerToken);
        if (IsReleased)
        {
            throw new InvalidOperationException("A released lease cannot be renewed.");
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        ExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    public bool Release(Guid ownerToken)
    {
        if (ownerToken == Guid.Empty || OwnerToken != ownerToken || IsReleased)
        {
            return false;
        }

        IsReleased = true;
        return true;
    }

    private void EnsureOwner(Guid ownerToken)
    {
        if (ownerToken == Guid.Empty || OwnerToken != ownerToken)
        {
            throw new InvalidOperationException("The lease is not owned by this token.");
        }
    }
}
