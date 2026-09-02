using System.Text;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Domain.Activity;

public sealed class UserActivity
{
    public const int MaximumUserDisplayNameLength = 128;
    public const int MaximumDeviceNameLength = 128;
    public const int MaximumEntryNameLength = 255;

    private UserActivity()
    {
    }

    private UserActivity(
        UserActivityContext context,
        UserActivityType activityType,
        UserActivityDetailKind detailKind)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Actor);
        ArgumentNullException.ThrowIfNull(context.Target);

        if (context.Id == Guid.Empty || context.OperationId == Guid.Empty)
        {
            throw new ArgumentException("Activity and operation IDs are required.");
        }

        if (context.Actor.UserId == Guid.Empty)
        {
            throw new ArgumentException("An actor user ID must not be empty.", nameof(context));
        }

        if (context.Actor.UserId is null && context.Actor.DeviceName is not null)
        {
            throw new ArgumentException("A system actor cannot have a device snapshot.", nameof(context));
        }

        if (context.Target.EntryId == Guid.Empty || context.Target.OwnerUserId == Guid.Empty)
        {
            throw new ArgumentException("Target and owner IDs are required.", nameof(context));
        }

        if (context.Target.ParentEntryId == Guid.Empty)
        {
            throw new ArgumentException("A parent ID must not be empty.", nameof(context));
        }

        if (context.OccurredAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The activity timestamp must be UTC.", nameof(context));
        }

        EnsureDefined(context.Target.EntryType, nameof(context));
        EnsureSnapshot(context.Actor.DisplayName, MaximumUserDisplayNameLength, "actor display name");
        EnsureOptionalSnapshot(context.Actor.DeviceName, MaximumDeviceNameLength, "device name");
        EnsureSnapshot(context.Target.Name, MaximumEntryNameLength, "target name");
        EnsureSnapshot(context.Target.OwnerDisplayName, MaximumUserDisplayNameLength, "owner display name");

        Id = context.Id;
        OperationId = context.OperationId;
        ActivityType = activityType;
        DetailKind = detailKind;
        OccurredAt = context.OccurredAt;
        ActorUserId = context.Actor.UserId;
        ActorDisplayName = context.Actor.DisplayName;
        ActorDeviceName = context.Actor.DeviceName;
        TargetEntryId = context.Target.EntryId;
        TargetType = context.Target.EntryType == FileEntryType.File
            ? ActivityTargetType.File
            : ActivityTargetType.Folder;
        TargetName = context.Target.Name;
        OwnerUserId = context.Target.OwnerUserId;
        OwnerDisplayName = context.Target.OwnerDisplayName;
        ParentEntryId = context.Target.ParentEntryId;
    }

    public Guid Id { get; private set; }

    public Guid OperationId { get; private set; }

    public UserActivityType ActivityType { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string ActorDisplayName { get; private set; } = string.Empty;

    public string? ActorDeviceName { get; private set; }

    public Guid? TargetEntryId { get; private set; }

    public ActivityTargetType TargetType { get; private set; }

    public string TargetName { get; private set; } = string.Empty;

    public Guid? OwnerUserId { get; private set; }

    public string OwnerDisplayName { get; private set; } = string.Empty;

    public Guid? ParentEntryId { get; private set; }

    public UserActivityDetailKind DetailKind { get; private set; }

    public Guid? SourceParentId { get; private set; }

    public string? SourceParentName { get; private set; }

    public Guid? DestinationParentId { get; private set; }

    public string? DestinationParentName { get; private set; }

    public long? ResultingFileVersion { get; private set; }

    public ActivityEditKind? EditKind { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public string? RecipientDisplayName { get; private set; }

    public SharePermission? SharePermission { get; private set; }

    public ActivityShareAction? ShareAction { get; private set; }

    public ActivityDeleteKind? DeleteKind { get; private set; }

    public static UserActivity CreateUpload(UserActivityContext context, long resultingFileVersion)
    {
        EnsureVersion(resultingFileVersion);
        return new UserActivity(context, UserActivityType.Upload, UserActivityDetailKind.Upload)
        {
            ResultingFileVersion = resultingFileVersion,
        };
    }

    public static UserActivity CreateMove(
        UserActivityContext context,
        ActivityFolderSnapshot source,
        ActivityFolderSnapshot destination)
    {
        EnsureFolder(source, "source");
        EnsureFolder(destination, "destination");
        return new UserActivity(context, UserActivityType.Move, UserActivityDetailKind.Move)
        {
            SourceParentId = source.EntryId,
            SourceParentName = source.Name,
            DestinationParentId = destination.EntryId,
            DestinationParentName = destination.Name,
        };
    }

    public static UserActivity CreateEdit(
        UserActivityContext context,
        long resultingFileVersion,
        ActivityEditKind editKind)
    {
        EnsureVersion(resultingFileVersion);
        EnsureDefined(editKind, nameof(editKind));
        return new UserActivity(context, UserActivityType.Edit, UserActivityDetailKind.Edit)
        {
            ResultingFileVersion = resultingFileVersion,
            EditKind = editKind,
        };
    }

    public static UserActivity CreateShare(
        UserActivityContext context,
        ActivityRecipientSnapshot recipient,
        SharePermission permission,
        ActivityShareAction action)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (recipient.UserId == Guid.Empty)
        {
            throw new ArgumentException("A recipient user ID is required.", nameof(recipient));
        }

        EnsureSnapshot(recipient.DisplayName, MaximumUserDisplayNameLength, "recipient display name");
        EnsureDefined(permission, nameof(permission));
        EnsureDefined(action, nameof(action));
        return new UserActivity(context, UserActivityType.Share, UserActivityDetailKind.Share)
        {
            RecipientUserId = recipient.UserId,
            RecipientDisplayName = recipient.DisplayName,
            SharePermission = permission,
            ShareAction = action,
        };
    }

    public static UserActivity CreateDelete(UserActivityContext context, ActivityDeleteKind deleteKind)
    {
        EnsureDefined(deleteKind, nameof(deleteKind));
        return new UserActivity(context, UserActivityType.Delete, UserActivityDetailKind.Delete)
        {
            DeleteKind = deleteKind,
        };
    }

    private static void EnsureFolder(ActivityFolderSnapshot snapshot, string name)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.EntryId == Guid.Empty)
        {
            throw new ArgumentException($"The {name} folder ID is required.", name);
        }

        EnsureSnapshot(snapshot.Name, MaximumEntryNameLength, $"{name} folder name");
    }

    private static void EnsureVersion(long version)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
    }

    private static void EnsureOptionalSnapshot(string? value, int maximumLength, string name)
    {
        if (value is not null)
        {
            EnsureSnapshot(value, maximumLength, name);
        }
    }

    private static void EnsureSnapshot(string? value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            !value.IsNormalized(NormalizationForm.FormC) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException($"The {name} snapshot is invalid.", name);
        }
    }

    private static void EnsureDefined<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
