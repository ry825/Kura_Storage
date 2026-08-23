namespace KuraStorage.Domain.Sharing;

public sealed class Share
{
    private readonly List<ShareMember> _members = [];

    private Share()
    {
    }

    public Share(Guid id, Guid targetEntryId, Guid ownerUserId, DateTimeOffset now)
    {
        if (id == Guid.Empty || targetEntryId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("The share identity and owner are required.");
        }

        Id = id;
        TargetEntryId = targetEntryId;
        OwnerUserId = ownerUserId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TargetEntryId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ShareMember> Members => _members.AsReadOnly();

    public void AddMember(Guid userId, SharePermission permission, DateTimeOffset now)
    {
        EnsureMemberIdentity(userId);
        EnsurePermission(permission);
        if (_members.Any(member => member.UserId == userId))
        {
            throw new InvalidOperationException("The user is already a member of this share.");
        }

        _members.Add(new ShareMember(Id, userId, permission, now));
        UpdatedAt = now;
    }

    public void SetMemberPermission(Guid userId, SharePermission permission, DateTimeOffset now)
    {
        EnsureMemberIdentity(userId);
        EnsurePermission(permission);
        var member = FindMember(userId);
        member.SetPermission(permission, now);
        UpdatedAt = now;
    }

    public bool RemoveMember(Guid userId, DateTimeOffset now)
    {
        EnsureMemberIdentity(userId);
        var member = FindMember(userId);
        _members.Remove(member);
        UpdatedAt = now;
        return _members.Count == 0;
    }

    private ShareMember FindMember(Guid userId) =>
        _members.SingleOrDefault(member => member.UserId == userId) ??
        throw new KeyNotFoundException("The share member does not exist.");

    private void EnsureMemberIdentity(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The member user ID is required.", nameof(userId));
        }

        if (userId == OwnerUserId)
        {
            throw new ArgumentException("The owner cannot be a share member.", nameof(userId));
        }
    }

    private static void EnsurePermission(SharePermission permission)
    {
        if (!Enum.IsDefined(permission))
        {
            throw new ArgumentOutOfRangeException(nameof(permission));
        }
    }
}

public sealed class ShareMember
{
    private ShareMember()
    {
    }

    internal ShareMember(Guid shareId, Guid userId, SharePermission permission, DateTimeOffset now)
    {
        ShareId = shareId;
        UserId = userId;
        Permission = permission;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid ShareId { get; private set; }

    public Guid UserId { get; private set; }

    public SharePermission Permission { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void SetPermission(SharePermission permission, DateTimeOffset now)
    {
        Permission = permission;
        UpdatedAt = now;
    }
}
