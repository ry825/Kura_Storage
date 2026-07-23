namespace KuraStorage.Domain.Audit;

public sealed class AuditLog
{
    private AuditLog()
    {
    }

    public AuditLog(
        Guid id,
        Guid? actorUserId,
        Guid? actorDeviceId,
        string? actorOsUser,
        string action,
        string? targetType,
        string? targetId,
        string resultCode,
        string? requestId,
        DateTimeOffset createdAt)
    {
        Id = id;
        ActorUserId = actorUserId;
        ActorDeviceId = actorDeviceId;
        ActorOsUser = actorOsUser;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        ResultCode = resultCode;
        RequestId = requestId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public Guid? ActorDeviceId { get; private set; }

    public string? ActorOsUser { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string ResultCode { get; private set; } = string.Empty;

    public string? RequestId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
