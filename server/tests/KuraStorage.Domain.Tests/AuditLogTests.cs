using KuraStorage.Domain.Audit;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_UserDevice_PreservesMinimalAuditContract()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var audit = new AuditLog(
            id, userId, deviceId, null, "FILE_VERSION_CREATE", "FILE_ENTRY", "target",
            "SUCCESS", "request", Now);

        Assert.Equal(id, audit.Id);
        Assert.Equal(userId, audit.ActorUserId);
        Assert.Equal(deviceId, audit.ActorDeviceId);
        Assert.Null(audit.ActorOsUser);
        Assert.Equal(AuditActorType.UserDevice, audit.ActorType);
        Assert.Equal("FILE_VERSION_CREATE", audit.Action);
        Assert.Equal("FILE_ENTRY", audit.TargetType);
        Assert.Equal("target", audit.TargetId);
        Assert.Equal("SUCCESS", audit.ResultCode);
        Assert.Equal("request", audit.RequestId);
        Assert.Equal(Now, audit.CreatedAt);
    }

    [Fact]
    public void Create_InfersSystemAndAdminCliAndHonorsExplicitWorkerActor()
    {
        Assert.Equal(
            AuditActorType.System,
            new AuditLog(Guid.NewGuid(), null, null, null, "A", null, null, "OK", null, Now).ActorType);
        Assert.Equal(
            AuditActorType.AdminCli,
            new AuditLog(Guid.NewGuid(), null, null, "operator", "A", null, null, "OK", null, Now).ActorType);
        Assert.Equal(
            AuditActorType.SystemWorker,
            new AuditLog(
                Guid.NewGuid(), null, null, null, "A", null, null, "OK", null, Now,
                AuditActorType.SystemWorker).ActorType);
    }
}
