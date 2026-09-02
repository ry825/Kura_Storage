using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class BackupReceiptConfiguration : IEntityTypeConfiguration<BackupReceipt>
{
    public void Configure(EntityTypeBuilder<BackupReceipt> builder)
    {
        builder.ToTable(
            "backup_receipts",
            table =>
            {
                table.HasCheckConstraint("ck_backup_receipts_size", "\"size\" >= 0");
                table.HasCheckConstraint("ck_backup_receipts_remote_version", "\"remote_file_version\" >= 1");
            });
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).HasColumnName("id");
        builder.Property(receipt => receipt.UserId).HasColumnName("user_id");
        builder.Property(receipt => receipt.DeviceId).HasColumnName("device_id");
        builder.Property(receipt => receipt.LocalDocumentKey).HasColumnName("local_document_key").HasMaxLength(36);
        builder.Property(receipt => receipt.RemoteFileId).HasColumnName("remote_file_id");
        builder.Property(receipt => receipt.RelativePath)
            .HasColumnName("relative_path")
            .HasMaxLength(BackupDocumentMetadata.MaximumRelativePathLength);
        builder.Property(receipt => receipt.Size).HasColumnName("size");
        builder.Property(receipt => receipt.SourceModifiedAt).HasColumnName("source_modified_at");
        builder.Property(receipt => receipt.Checksum).HasColumnName("checksum").HasMaxLength(64);
        builder.Property(receipt => receipt.RemoteFileVersion).HasColumnName("remote_file_version");
        builder.Property(receipt => receipt.UploadedAt).HasColumnName("uploaded_at");
        builder.Property(receipt => receipt.CreatedAt).HasColumnName("created_at");
        builder.Property(receipt => receipt.UpdatedAt).HasColumnName("updated_at");
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<User>().WithMany().HasForeignKey(receipt => receipt.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>().WithMany().HasForeignKey(receipt => receipt.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileEntry>().WithMany().HasForeignKey(receipt => receipt.RemoteFileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(receipt => new { receipt.UserId, receipt.DeviceId, receipt.LocalDocumentKey })
            .IsUnique()
            .HasDatabaseName("ux_backup_receipts_user_device_document");
        builder.HasIndex(receipt => receipt.RemoteFileId)
            .HasDatabaseName("ix_backup_receipts_remote_file");
        builder.HasIndex(receipt => new { receipt.UserId, receipt.DeviceId, receipt.UpdatedAt })
            .HasDatabaseName("ix_backup_receipts_compare");
    }
}
