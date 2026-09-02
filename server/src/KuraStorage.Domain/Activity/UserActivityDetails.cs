using KuraStorage.Domain.Files;

namespace KuraStorage.Domain.Activity;

public enum UserActivityType
{
    Upload,
    Move,
    Edit,
    Share,
    Delete,
}

public enum ActivityTargetType
{
    File,
    Folder,
}

public enum UserActivityDetailKind
{
    Upload,
    Move,
    Edit,
    Share,
    Delete,
}

public enum ActivityEditKind
{
    TextSave,
    VersionRestore,
    BackupUpload,
}

public enum ActivityShareAction
{
    Created,
    Updated,
    Revoked,
}

public enum ActivityDeleteKind
{
    Trashed,
    Purged,
}

public sealed record ActivityActorSnapshot(
    Guid? UserId,
    string DisplayName,
    string? DeviceName);

public sealed record ActivityTargetSnapshot(
    Guid EntryId,
    FileEntryType EntryType,
    string Name,
    Guid OwnerUserId,
    string OwnerDisplayName,
    Guid? ParentEntryId);

public sealed record ActivityFolderSnapshot(Guid EntryId, string Name);

public sealed record ActivityRecipientSnapshot(Guid UserId, string DisplayName);

public sealed record UserActivityContext(
    Guid Id,
    Guid OperationId,
    ActivityActorSnapshot Actor,
    ActivityTargetSnapshot Target,
    DateTimeOffset OccurredAt);
