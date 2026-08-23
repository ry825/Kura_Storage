using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class SharePermissionPolicyTests
{
    [Fact]
    public void Strength_FollowsPublishedPermissionOrder()
    {
        Assert.True(SharePermissionPolicy.Compare(SharePermission.Viewer, SharePermission.Contributor) < 0);
        Assert.True(SharePermissionPolicy.Compare(SharePermission.Contributor, SharePermission.Editor) < 0);
        Assert.True(SharePermissionPolicy.Compare(SharePermission.Editor, SharePermission.Manager) < 0);
    }

    [Theory]
    [InlineData(SharePermission.Viewer, ShareOperation.View, true)]
    [InlineData(SharePermission.Viewer, ShareOperation.Contribute, false)]
    [InlineData(SharePermission.Contributor, ShareOperation.Contribute, true)]
    [InlineData(SharePermission.Contributor, ShareOperation.Edit, false)]
    [InlineData(SharePermission.Editor, ShareOperation.Edit, true)]
    [InlineData(SharePermission.Editor, ShareOperation.Manage, false)]
    [InlineData(SharePermission.Manager, ShareOperation.Manage, true)]
    public void Allows_EnforcesOperationBoundary(
        SharePermission permission,
        ShareOperation operation,
        bool expected)
    {
        Assert.Equal(expected, SharePermissionPolicy.Allows(permission, operation));
    }

    [Fact]
    public void Policy_DoesNotPersistOwnerOrNoneAsSharePermissions()
    {
        Assert.Equal(
            new[]
            {
                SharePermission.Viewer,
                SharePermission.Contributor,
                SharePermission.Editor,
                SharePermission.Manager,
            },
            Enum.GetValues<SharePermission>());
    }

    [Fact]
    public void Policy_RejectsUnknownPermissionAndOperation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SharePermissionPolicy.Compare((SharePermission)999, SharePermission.Viewer));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SharePermissionPolicy.MinimumFor((ShareOperation)999));
    }
}
