using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class UserActivityConfiguration : IEntityTypeConfiguration<UserActivity>
{
    public void Configure(EntityTypeBuilder<UserActivity> builder)
    {
        builder.ToTable(
            "user_activities",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_user_activities_type_detail",
                    "activity_type = detail_kind");
                table.HasCheckConstraint(
                    "ck_user_activities_detail_shape",
                    """
                    (activity_type = 'UPLOAD'
                        AND resulting_file_version >= 1
                        AND source_parent_id IS NULL AND source_parent_name IS NULL
                        AND destination_parent_id IS NULL AND destination_parent_name IS NULL
                        AND edit_kind IS NULL AND recipient_user_id IS NULL
                        AND recipient_display_name IS NULL AND share_permission IS NULL
                        AND share_action IS NULL AND delete_kind IS NULL)
                    OR
                    (activity_type = 'MOVE'
                        AND source_parent_name IS NOT NULL AND destination_parent_name IS NOT NULL
                        AND resulting_file_version IS NULL AND edit_kind IS NULL
                        AND recipient_user_id IS NULL AND recipient_display_name IS NULL
                        AND share_permission IS NULL AND share_action IS NULL AND delete_kind IS NULL)
                    OR
                    (activity_type = 'EDIT'
                        AND resulting_file_version >= 1 AND edit_kind IS NOT NULL
                        AND source_parent_id IS NULL AND source_parent_name IS NULL
                        AND destination_parent_id IS NULL AND destination_parent_name IS NULL
                        AND recipient_user_id IS NULL AND recipient_display_name IS NULL
                        AND share_permission IS NULL AND share_action IS NULL AND delete_kind IS NULL)
                    OR
                    (activity_type = 'SHARE'
                        AND recipient_display_name IS NOT NULL
                        AND share_permission IS NOT NULL AND share_action IS NOT NULL
                        AND source_parent_id IS NULL AND source_parent_name IS NULL
                        AND destination_parent_id IS NULL AND destination_parent_name IS NULL
                        AND resulting_file_version IS NULL AND edit_kind IS NULL AND delete_kind IS NULL)
                    OR
                    (activity_type = 'DELETE'
                        AND delete_kind IS NOT NULL
                        AND source_parent_id IS NULL AND source_parent_name IS NULL
                        AND destination_parent_id IS NULL AND destination_parent_name IS NULL
                        AND resulting_file_version IS NULL AND edit_kind IS NULL
                        AND recipient_user_id IS NULL AND recipient_display_name IS NULL
                        AND share_permission IS NULL AND share_action IS NULL)
                    """);
            });
        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Id).HasColumnName("id");
        builder.Property(activity => activity.OperationId).HasColumnName("operation_id");
        builder.Property(activity => activity.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion(value => ToDatabase(value), value => ParseActivityType(value))
            .HasMaxLength(16);
        builder.Property(activity => activity.OccurredAt).HasColumnName("occurred_at");
        builder.Property(activity => activity.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(activity => activity.ActorDisplayName)
            .HasColumnName("actor_display_name")
            .HasMaxLength(UserActivity.MaximumUserDisplayNameLength);
        builder.Property(activity => activity.ActorDeviceName)
            .HasColumnName("actor_device_name")
            .HasMaxLength(UserActivity.MaximumDeviceNameLength);
        builder.Property(activity => activity.TargetEntryId).HasColumnName("target_entry_id");
        builder.Property(activity => activity.TargetType)
            .HasColumnName("target_type")
            .HasConversion(value => ToDatabase(value), value => ParseTargetType(value))
            .HasMaxLength(16);
        builder.Property(activity => activity.TargetName)
            .HasColumnName("target_name")
            .HasMaxLength(UserActivity.MaximumEntryNameLength);
        builder.Property(activity => activity.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(activity => activity.OwnerDisplayName)
            .HasColumnName("owner_display_name")
            .HasMaxLength(UserActivity.MaximumUserDisplayNameLength);
        builder.Property(activity => activity.ParentEntryId).HasColumnName("parent_entry_id");
        builder.Property(activity => activity.DetailKind)
            .HasColumnName("detail_kind")
            .HasConversion(value => ToDatabase(value), value => ParseDetailKind(value))
            .HasMaxLength(16);
        builder.Property(activity => activity.SourceParentId).HasColumnName("source_parent_id");
        builder.Property(activity => activity.SourceParentName)
            .HasColumnName("source_parent_name")
            .HasMaxLength(UserActivity.MaximumEntryNameLength);
        builder.Property(activity => activity.DestinationParentId).HasColumnName("destination_parent_id");
        builder.Property(activity => activity.DestinationParentName)
            .HasColumnName("destination_parent_name")
            .HasMaxLength(UserActivity.MaximumEntryNameLength);
        builder.Property(activity => activity.ResultingFileVersion).HasColumnName("resulting_file_version");
        builder.Property(activity => activity.EditKind)
            .HasColumnName("edit_kind")
            .HasConversion(
                value => value == null ? null : ToDatabase(value.Value),
                value => value == null ? null : ParseEditKind(value))
            .HasMaxLength(32);
        builder.Property(activity => activity.RecipientUserId).HasColumnName("recipient_user_id");
        builder.Property(activity => activity.RecipientDisplayName)
            .HasColumnName("recipient_display_name")
            .HasMaxLength(UserActivity.MaximumUserDisplayNameLength);
        builder.Property(activity => activity.SharePermission)
            .HasColumnName("share_permission")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(activity => activity.ShareAction)
            .HasColumnName("share_action")
            .HasConversion(
                value => value == null ? null : ToDatabase(value.Value),
                value => value == null ? null : ParseShareAction(value))
            .HasMaxLength(16);
        builder.Property(activity => activity.DeleteKind)
            .HasColumnName("delete_kind")
            .HasConversion(
                value => value == null ? null : ToDatabase(value.Value),
                value => value == null ? null : ParseDeleteKind(value))
            .HasMaxLength(16);

        SetNullReference<User>(builder, activity => activity.ActorUserId);
        SetNullReference<User>(builder, activity => activity.OwnerUserId);
        SetNullReference<User>(builder, activity => activity.RecipientUserId);
        SetNullReference<FileEntry>(builder, activity => activity.TargetEntryId);
        SetNullReference<FileEntry>(builder, activity => activity.ParentEntryId);
        SetNullReference<FileEntry>(builder, activity => activity.SourceParentId);
        SetNullReference<FileEntry>(builder, activity => activity.DestinationParentId);

        builder.HasIndex(activity => activity.OperationId)
            .IsUnique()
            .HasDatabaseName("ux_user_activities_operation_id");
        builder.HasIndex(activity => new { activity.OccurredAt, activity.Id })
            .IsDescending(true, true)
            .HasDatabaseName("ix_user_activities_occurred_id");
        builder.HasIndex(activity => new { activity.ActorUserId, activity.OccurredAt, activity.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_user_activities_actor_occurred_id");
        builder.HasIndex(activity => new { activity.OwnerUserId, activity.OccurredAt, activity.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_user_activities_owner_occurred_id");
        builder.HasIndex(activity => new { activity.TargetEntryId, activity.OccurredAt, activity.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_user_activities_target_occurred_id");
        builder.HasIndex(activity => new { activity.ActivityType, activity.OccurredAt, activity.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_user_activities_type_occurred_id");
    }

    private static void SetNullReference<TPrincipal>(
        EntityTypeBuilder<UserActivity> builder,
        global::System.Linq.Expressions.Expression<Func<UserActivity, object?>> foreignKey)
        where TPrincipal : class =>
        builder.HasOne<TPrincipal>()
            .WithMany()
            .HasForeignKey(foreignKey)
            .OnDelete(DeleteBehavior.SetNull);

    private static string ToDatabase(UserActivityType value) => value.ToString().ToUpperInvariant();

    private static UserActivityType ParseActivityType(string value) => Enum.Parse<UserActivityType>(value, true);

    private static string ToDatabase(ActivityTargetType value) => value.ToString().ToUpperInvariant();

    private static ActivityTargetType ParseTargetType(string value) => Enum.Parse<ActivityTargetType>(value, true);

    private static string ToDatabase(UserActivityDetailKind value) => value.ToString().ToUpperInvariant();

    private static UserActivityDetailKind ParseDetailKind(string value) => Enum.Parse<UserActivityDetailKind>(value, true);

    private static string ToDatabase(ActivityEditKind value) => value switch
    {
        ActivityEditKind.TextSave => "TEXT_SAVE",
        ActivityEditKind.VersionRestore => "VERSION_RESTORE",
        ActivityEditKind.BackupUpload => "BACKUP_UPLOAD",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static ActivityEditKind ParseEditKind(string value) => value switch
    {
        "TEXT_SAVE" => ActivityEditKind.TextSave,
        "VERSION_RESTORE" => ActivityEditKind.VersionRestore,
        "BACKUP_UPLOAD" => ActivityEditKind.BackupUpload,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(ActivityShareAction value) => value.ToString().ToUpperInvariant();

    private static ActivityShareAction ParseShareAction(string value) => Enum.Parse<ActivityShareAction>(value, true);

    private static string ToDatabase(ActivityDeleteKind value) => value.ToString().ToUpperInvariant();

    private static ActivityDeleteKind ParseDeleteKind(string value) => Enum.Parse<ActivityDeleteKind>(value, true);
}
