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
                table.HasCheckConstraint(
                    "ck_file_entries_missing_metadata",
                    "(\"status\" IN ('ACTIVE', 'TRASHED') AND \"missing_detected_at\" IS NULL AND \"missing_last_checked_at\" IS NULL AND \"missing_observation_id\" IS NULL) OR " +
                    "(\"status\" IN ('MISSING_CANDIDATE', 'MISSING') AND \"parent_id\" IS NOT NULL AND \"missing_detected_at\" IS NOT NULL AND \"missing_last_checked_at\" IS NOT NULL AND \"missing_observation_id\" IS NOT NULL)");
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
            .HasConversion(
                value => value == FileEntryStatus.MissingCandidate
                    ? "MISSING_CANDIDATE"
                    : value.ToString().ToUpperInvariant(),
                value => value == "MISSING_CANDIDATE"
                    ? FileEntryStatus.MissingCandidate
                    : Enum.Parse<FileEntryStatus>(value, true))
            .HasMaxLength(32);
        builder.Property(entry => entry.OriginalParentId).HasColumnName("original_parent_id");
        builder.Property(entry => entry.OriginalRelativePath).HasColumnName("original_relative_path").HasMaxLength(2048);
        builder.Property(entry => entry.TrashedAt).HasColumnName("trashed_at");
        builder.Property(entry => entry.SourceModifiedAt).HasColumnName("source_modified_at");
        builder.Property(entry => entry.SourceFileKey).HasColumnName("source_file_key").HasMaxLength(128);
        builder.Property(entry => entry.SourceObservedAt).HasColumnName("source_observed_at");
        builder.Property(entry => entry.MissingDetectedAt).HasColumnName("missing_detected_at");
        builder.Property(entry => entry.MissingLastCheckedAt).HasColumnName("missing_last_checked_at");
        builder.Property(entry => entry.MissingObservationId).HasColumnName("missing_observation_id");
        builder.Property(entry => entry.FileVersion).HasColumnName("file_version");
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at");
        builder.Property(entry => entry.UpdatedAt).HasColumnName("updated_at");
        builder.Property<uint>("xmin").IsRowVersion();
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
            .HasFilter("\"status\" IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')")
            .HasDatabaseName("ux_file_entries_managed_owner_parent_name");
        builder.HasIndex(
                entry => new { entry.OwnerUserId, entry.RelativePath },
                "IX_FileEntries_ManagedOwnerPath")
            .IsUnique()
            .HasFilter("\"status\" IN ('ACTIVE', 'MISSING_CANDIDATE', 'MISSING')")
            .HasDatabaseName("ux_file_entries_managed_owner_path");
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
        builder.HasIndex(entry => new { entry.Status, entry.MissingLastCheckedAt, entry.Id })
            .HasFilter("\"status\" IN ('MISSING_CANDIDATE', 'MISSING')")
            .HasDatabaseName("ix_file_entries_missing_status_checked_at");
        builder.HasIndex(entry => new { entry.Status, entry.ParentId, entry.TrashedAt, entry.Id })
            .HasDatabaseName("ix_file_entries_trash_purge_candidates");
    }
}
