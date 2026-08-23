using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class ShareTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddUpdateAndRemoveMember_MaintainAggregateStateAndTimestamp()
    {
        var share = Create();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        share.AddMember(firstUserId, SharePermission.Viewer, Now.AddMinutes(1));
        share.AddMember(secondUserId, SharePermission.Editor, Now.AddMinutes(2));
        share.SetMemberPermission(firstUserId, SharePermission.Manager, Now.AddMinutes(3));

        Assert.Equal(2, share.Members.Count);
        Assert.Equal(SharePermission.Manager, share.Members.Single(member => member.UserId == firstUserId).Permission);
        Assert.Equal(Now.AddMinutes(3), share.UpdatedAt);

        Assert.False(share.RemoveMember(firstUserId, Now.AddMinutes(4)));
        Assert.True(share.RemoveMember(secondUserId, Now.AddMinutes(5)));
        Assert.Empty(share.Members);
        Assert.Equal(Now.AddMinutes(5), share.UpdatedAt);
    }

    [Fact]
    public void MemberMutations_RejectOwnerDuplicateUnknownAndInvalidPermission()
    {
        var ownerUserId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var share = Create(ownerUserId: ownerUserId);

        Assert.Throws<ArgumentException>(() => share.AddMember(Guid.Empty, SharePermission.Viewer, Now));
        Assert.Throws<ArgumentException>(() => share.AddMember(ownerUserId, SharePermission.Viewer, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            share.AddMember(memberUserId, (SharePermission)999, Now));

        share.AddMember(memberUserId, SharePermission.Viewer, Now);

        Assert.Throws<InvalidOperationException>(() =>
            share.AddMember(memberUserId, SharePermission.Editor, Now));
        Assert.Throws<KeyNotFoundException>(() =>
            share.SetMemberPermission(Guid.NewGuid(), SharePermission.Editor, Now));
        Assert.Throws<KeyNotFoundException>(() => share.RemoveMember(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Constructor_RejectsMissingIdentityAndOwnerAsInitialMember()
    {
        var ownerUserId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new Share(Guid.Empty, Guid.NewGuid(), ownerUserId, Now));
        Assert.Throws<ArgumentException>(() => new Share(Guid.NewGuid(), Guid.Empty, ownerUserId, Now));
        Assert.Throws<ArgumentException>(() => new Share(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Now));
    }

    private static Share Create(Guid? ownerUserId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), ownerUserId ?? Guid.NewGuid(), Now);
}
