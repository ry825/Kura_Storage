using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class FileOperationConfiguration : IEntityTypeConfiguration<FileOperation>
{
    public void Configure(EntityTypeBuilder<FileOperation> builder)
    {
        builder.ToTable("file_operations");
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).HasColumnName("id");
        builder.Property(operation => operation.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(operation => operation.OperationType)
            .HasColumnName("operation_type")
            .HasConversion(value => ToDatabase(value), value => FromDatabase(value))
            .HasMaxLength(32);
        builder.Property(operation => operation.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(operation => operation.FileEntryId).HasColumnName("file_entry_id");
        builder.Property(operation => operation.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(operation => operation.ActorDeviceId).HasColumnName("actor_device_id");
        builder.Property(operation => operation.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(operation => operation.Trigger).HasColumnName("trigger").HasMaxLength(32);
        builder.Property(operation => operation.SourceRelativePath).HasColumnName("source_relative_path").HasMaxLength(2048);
        builder.Property(operation => operation.TargetRelativePath).HasColumnName("target_relative_path").HasMaxLength(2048);
        builder.Property(operation => operation.ExpectedSize).HasColumnName("expected_size");
        builder.Property(operation => operation.ExpectedSha256).HasColumnName("expected_sha256").HasMaxLength(64);
        builder.Property(operation => operation.PreviousFileVersion).HasColumnName("previous_file_version");
        builder.Property(operation => operation.ResultFileVersion).HasColumnName("result_file_version");
        builder.Property(operation => operation.VersionTemporaryRelativePath)
            .HasColumnName("version_temporary_relative_path")
            .HasMaxLength(2048);
        builder.Property(operation => operation.VersionContentRelativePath)
            .HasColumnName("version_content_relative_path")
            .HasMaxLength(2048);
        builder.Property(operation => operation.VersionSha256)
            .HasColumnName("version_sha256")
            .HasMaxLength(64);
        builder.Property(operation => operation.VersionPublishStage)
            .HasColumnName("version_publish_stage")
            .HasConversion(
                value => value == null ? null : value.Value.ToString().ToUpperInvariant(),
                value => value == null ? null : Enum.Parse<FileVersionPublishStage>(value, true))
            .HasMaxLength(32);
        builder.Property(operation => operation.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<FileOperationStatus>(value, true))
            .HasMaxLength(32);
        builder.Property(operation => operation.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(operation => operation.CreatedAt).HasColumnName("created_at");
        builder.Property(operation => operation.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<KuraStorage.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(operation => operation.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<KuraStorage.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(operation => operation.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(operation => operation.FileEntryId)
            .HasDatabaseName("ix_file_operations_file_entry_id");
        builder.HasIndex(operation => new { operation.OwnerUserId, operation.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"idempotency_key\" IS NOT NULL")
            .HasDatabaseName("ux_file_operations_owner_idempotency_key");
        builder.HasIndex(operation => new { operation.Status, operation.UpdatedAt })
            .HasDatabaseName("ix_file_operations_status_updated_at");
        builder.HasIndex(operation => operation.FileEntryId)
            .IsUnique()
            .HasFilter("\"operation_type\" = 'PURGE' AND \"status\" IN ('PENDING', 'FILESYSTEM_DONE', 'RECOVERY_REQUIRED')")
            .HasDatabaseName("ux_file_operations_incomplete_purge_target");
    }

    private static string ToDatabase(FileOperationType value) => value switch
    {
        FileOperationType.TextEdit => "TEXT_EDIT",
        FileOperationType.VersionRestore => "VERSION_RESTORE",
        _ => value.ToString().ToUpperInvariant(),
    };

    private static FileOperationType FromDatabase(string value) => value switch
    {
        "TEXT_EDIT" => FileOperationType.TextEdit,
        "VERSION_RESTORE" => FileOperationType.VersionRestore,
        _ => Enum.Parse<FileOperationType>(value, true),
    };
}
