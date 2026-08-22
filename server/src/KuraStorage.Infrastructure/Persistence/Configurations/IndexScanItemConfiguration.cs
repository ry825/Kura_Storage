using KuraStorage.Domain.Files;
using KuraStorage.Domain.Indexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class IndexScanItemConfiguration : IEntityTypeConfiguration<IndexScanItem>
{
    public void Configure(EntityTypeBuilder<IndexScanItem> builder)
    {
        builder.ToTable("index_scan_items", table => table.HasCheckConstraint("ck_index_scan_items_size", "size >= 0"));
        builder.HasKey(item => new { item.ScanId, item.RelativePath });
        builder.Property(item => item.ScanId).HasColumnName("scan_id");
        builder.Property(item => item.RelativePath).HasColumnName("relative_path").HasMaxLength(2048);
        builder.Property(item => item.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(item => item.ParentRelativePath).HasColumnName("parent_relative_path").HasMaxLength(2048);
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(FileName.MaximumLength);
        builder.Property(item => item.EntryType)
            .HasColumnName("entry_type")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<FileEntryType>(value, true))
            .HasMaxLength(16);
        builder.Property(item => item.Size).HasColumnName("size");
        builder.Property(item => item.MimeType).HasColumnName("mime_type").HasMaxLength(255);
        builder.Property(item => item.SourceModifiedAt).HasColumnName("source_modified_at");
        builder.Property(item => item.SourceFileKey).HasColumnName("source_file_key").HasMaxLength(128);
        builder.Property(item => item.IsolationReason).HasColumnName("isolation_reason").HasMaxLength(64);
        builder.HasOne<IndexScanRun>().WithMany().HasForeignKey(item => item.ScanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.ScanId, item.OwnerUserId, item.ParentRelativePath })
            .HasDatabaseName("ix_index_scan_items_scan_owner_parent");
        builder.HasIndex(item => new { item.ScanId, item.SourceFileKey })
            .HasFilter("source_file_key IS NOT NULL")
            .HasDatabaseName("ix_index_scan_items_scan_source_key");
    }
}
