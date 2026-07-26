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
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<FileOperationType>(value, true))
            .HasMaxLength(32);
        builder.Property(operation => operation.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(operation => operation.FileEntryId).HasColumnName("file_entry_id");
        builder.Property(operation => operation.SourceRelativePath).HasColumnName("source_relative_path").HasMaxLength(2048);
        builder.Property(operation => operation.TargetRelativePath).HasColumnName("target_relative_path").HasMaxLength(2048);
        builder.Property(operation => operation.ExpectedSize).HasColumnName("expected_size");
        builder.Property(operation => operation.ExpectedSha256).HasColumnName("expected_sha256").HasMaxLength(64);
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
        builder.HasIndex(operation => operation.FileEntryId)
            .HasDatabaseName("ix_file_operations_file_entry_id");
        builder.HasIndex(operation => new { operation.OwnerUserId, operation.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"idempotency_key\" IS NOT NULL")
            .HasDatabaseName("ux_file_operations_owner_idempotency_key");
        builder.HasIndex(operation => new { operation.Status, operation.UpdatedAt })
            .HasDatabaseName("ix_file_operations_status_updated_at");
    }
}
