namespace KuraStorage.Domain.Sharing;

public enum SharePermission
{
    Viewer = 1,
    Contributor = 2,
    Editor = 3,
    Manager = 4,
}

public enum ShareOperation
{
    View,
    Contribute,
    Edit,
    Manage,
}

public static class SharePermissionPolicy
{
    public static int Compare(SharePermission left, SharePermission right) =>
        Strength(left).CompareTo(Strength(right));

    public static bool Allows(SharePermission permission, ShareOperation operation) =>
        Strength(permission) >= Strength(MinimumFor(operation));

    public static SharePermission MinimumFor(ShareOperation operation) => operation switch
    {
        ShareOperation.View => SharePermission.Viewer,
        ShareOperation.Contribute => SharePermission.Contributor,
        ShareOperation.Edit => SharePermission.Editor,
        ShareOperation.Manage => SharePermission.Manager,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static int Strength(SharePermission permission) => permission switch
    {
        SharePermission.Viewer => 1,
        SharePermission.Contributor => 2,
        SharePermission.Editor => 3,
        SharePermission.Manager => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(permission)),
    };
}
