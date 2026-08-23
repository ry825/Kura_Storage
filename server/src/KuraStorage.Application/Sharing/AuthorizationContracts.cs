using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Sharing;

public enum EffectivePermissionLevel
{
    None = 0,
    Viewer = 1,
    Contributor = 2,
    Editor = 3,
    Manager = 4,
    Owner = 5,
}

public enum PermissionSource
{
    Owner,
    Direct,
    Inherited,
}

public sealed record PermissionCandidate(
    Guid EntryId,
    EffectivePermissionLevel Permission,
    PermissionSource Source,
    Guid? ShareTargetId,
    Guid? ShareId,
    int AncestorDepth);

public sealed record EffectivePermission(
    Guid EntryId,
    EffectivePermissionLevel Permission,
    PermissionSource? Source,
    Guid? ShareTargetId,
    Guid? ShareId)
{
    public bool Allows(ShareOperation operation) => Permission switch
    {
        EffectivePermissionLevel.Owner => true,
        EffectivePermissionLevel.Viewer => SharePermissionPolicy.Allows(SharePermission.Viewer, operation),
        EffectivePermissionLevel.Contributor => SharePermissionPolicy.Allows(SharePermission.Contributor, operation),
        EffectivePermissionLevel.Editor => SharePermissionPolicy.Allows(SharePermission.Editor, operation),
        EffectivePermissionLevel.Manager => SharePermissionPolicy.Allows(SharePermission.Manager, operation),
        _ => false,
    };
}
