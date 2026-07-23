using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class FileEntryConfiguration : IEntityTypeConfiguration<FileEntry>
{
    public void Configure(EntityTypeBuilder<FileEntry> builder)
    {
        builder.ToTable(
            "file_entries",
            table =>
            {
                table.HasCheckConstraint("ck_file_entries_size_nonnegative", "\"size\" >= 0");
                table.HasCheckConstraint("ck_file_entries_file_version_positive", "\"file_version\" >= 1");
            });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id");
        builder.Property(entry => entry.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(entry => entry.ParentId).HasColumnName("parent_id");
        builder.Property(entry => entry.EntryType)
            .HasColumnName("entry_type")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<FileEntryType>(value, true))
            .HasMaxLength(16);
        builder.Property(entry => entry.Name).HasColumnName("name").HasMaxLength(FileName.MaximumLength);
        builder.Property(entry => entry.RelativePath).HasColumnName("relative_path").HasMaxLength(2048);
        builder.Property(entry => entry.MimeType).HasColumnName("mime_type").HasMaxLength(255);
        builder.Property(entry => entry.Size).HasColumnName("size");
        builder.Property(entry => entry.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<FileEntryStatus>(value, true))
            .HasMaxLength(16);
        builder.Property(entry => entry.OriginalParentId).HasColumnName("original_parent_id");
        builder.Property(entry => entry.OriginalRelativePath).HasColumnName("original_relative_path").HasMaxLength(2048);
        builder.Property(entry => entry.TrashedAt).HasColumnName("trashed_at");
        builder.Property(entry => entry.FileVersion).HasColumnName("file_version");
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at");
        builder.Property(entry => entry.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<KuraStorage.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(entry => entry.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(entry => entry.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.OwnerUserId, entry.ParentId, entry.Name })
            .IsUnique()
            .HasFilter("\"status\" = 'ACTIVE'")
            .HasDatabaseName("ux_file_entries_active_owner_parent_name");
        builder.HasIndex(entry => entry.OwnerUserId)
            .IsUnique()
            .HasFilter("\"parent_id\" IS NULL AND \"status\" = 'ACTIVE'")
            .HasDatabaseName("ux_file_entries_active_owner_root");
        builder.HasIndex(entry => new { entry.OwnerUserId, entry.RelativePath })
            .IsUnique()
            .HasFilter("\"status\" = 'TRASHED'")
            .HasDatabaseName("ux_file_entries_trashed_owner_path");
        builder.HasIndex(entry => new { entry.OwnerUserId, entry.ParentId, entry.Status, entry.UpdatedAt })
            .HasDatabaseName("ix_file_entries_owner_parent_status_updated_at");
    }
}
