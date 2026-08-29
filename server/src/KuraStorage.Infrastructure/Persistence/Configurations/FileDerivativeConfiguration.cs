using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class FileDerivativeConfiguration : IEntityTypeConfiguration<FileDerivative>
{
    public void Configure(EntityTypeBuilder<FileDerivative> builder)
    {
        builder.ToTable(
            "file_derivatives",
            table =>
            {
                table.HasCheckConstraint("ck_file_derivatives_source_version", "source_version >= 1");
                table.HasCheckConstraint("ck_file_derivatives_profile_version", "profile_version >= 1");
                table.HasCheckConstraint("ck_file_derivatives_size", "size >= 0");
                table.HasCheckConstraint("ck_file_derivatives_revision", "revision >= 1");
                table.HasCheckConstraint(
                    "ck_file_derivatives_status",
                    "status IN ('PENDING', 'RUNNING', 'READY', 'FAILED', 'BLOCKED_SOURCE_MISSING', 'DELETING')");
                table.HasCheckConstraint(
                    "ck_file_derivatives_ready",
                    "(status = 'READY' AND size > 0 AND relative_path IS NOT NULL) OR " +
                    "(status IN ('PENDING', 'RUNNING', 'FAILED') AND size = 0 AND relative_path IS NULL) OR " +
                    "(status IN ('BLOCKED_SOURCE_MISSING', 'DELETING') AND " +
                    "((size = 0 AND relative_path IS NULL) OR (size > 0 AND relative_path IS NOT NULL)))");
                table.HasCheckConstraint(
                    "ck_file_derivatives_thumbnail_expiry",
                    "derivative_type NOT IN ('THUMBNAIL', 'PDF_THUMBNAIL') OR (expires_at IS NULL AND last_accessed_at IS NULL)");
                table.HasCheckConstraint(
                    "ck_file_derivatives_cache_expiry",
                    "derivative_type IN ('THUMBNAIL', 'PDF_THUMBNAIL') OR status <> 'READY' OR " +
                    "(last_accessed_at IS NOT NULL AND expires_at > last_accessed_at)");
                table.HasCheckConstraint(
                    "ck_file_derivatives_failed_error",
                    "status <> 'FAILED' OR error_code IS NOT NULL");
            });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.SourceFileId).HasColumnName("source_file_id");
        builder.Property(item => item.SourceVersion).HasColumnName("source_version");
        builder.Property(item => item.DerivativeType)
            .HasColumnName("derivative_type")
            .HasConversion(value => ToDatabase(value), value => FromDerivativeType(value))
            .HasMaxLength(32);
        builder.Property(item => item.ProfileVersion).HasColumnName("profile_version");
        builder.Property(item => item.RelativePath).HasColumnName("relative_path").HasMaxLength(2048);
        builder.Property(item => item.Size).HasColumnName("size");
        builder.Property(item => item.Status)
            .HasColumnName("status")
            .HasConversion(value => ToDatabase(value), value => FromDerivativeStatus(value))
            .HasMaxLength(32);
        builder.Property(item => item.LastAccessedAt).HasColumnName("last_accessed_at");
        builder.Property(item => item.ExpiresAt).HasColumnName("expires_at");
        builder.Property(item => item.LeaseUntil).HasColumnName("lease_until");
        builder.Property(item => item.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.Property(item => item.Revision).HasColumnName("revision");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(item => item.LogicalKey);
        builder.Ignore(item => item.IsThumbnail);
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(item => item.SourceFileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new
        {
            item.SourceFileId,
            item.SourceVersion,
            item.DerivativeType,
            item.ProfileVersion,
        })
            .IsUnique()
            .HasDatabaseName("ux_file_derivatives_logical_key");
        builder.HasIndex(item => new { item.SourceFileId, item.Status })
            .HasDatabaseName("ix_file_derivatives_source_status");
        builder.HasIndex(item => new { item.Status, item.ExpiresAt, item.LastAccessedAt, item.Id })
            .HasDatabaseName("ix_file_derivatives_cleanup");
        builder.HasIndex(item => new { item.DerivativeType, item.Status, item.LastAccessedAt, item.Id })
            .HasDatabaseName("ix_file_derivatives_type_lru");
        builder.HasIndex(item => new { item.Status, item.LeaseUntil })
            .HasDatabaseName("ix_file_derivatives_status_lease");
    }

    internal static string ToDatabase(DerivativeType value) => value switch
    {
        DerivativeType.PdfThumbnail => "PDF_THUMBNAIL",
        DerivativeType.ImageLow => "IMAGE_LOW",
        DerivativeType.ImageMedium => "IMAGE_MEDIUM",
        DerivativeType.VideoLow => "VIDEO_LOW",
        DerivativeType.VideoMedium => "VIDEO_MEDIUM",
        _ => value.ToString().ToUpperInvariant(),
    };

    internal static DerivativeType FromDerivativeType(string value) => value switch
    {
        "PDF_THUMBNAIL" => DerivativeType.PdfThumbnail,
        "IMAGE_LOW" => DerivativeType.ImageLow,
        "IMAGE_MEDIUM" => DerivativeType.ImageMedium,
        "VIDEO_LOW" => DerivativeType.VideoLow,
        "VIDEO_MEDIUM" => DerivativeType.VideoMedium,
        _ => Enum.Parse<DerivativeType>(value, true),
    };

    internal static string ToDatabase(DerivativeStatus value) => value switch
    {
        DerivativeStatus.BlockedSourceMissing => "BLOCKED_SOURCE_MISSING",
        _ => value.ToString().ToUpperInvariant(),
    };

    internal static DerivativeStatus FromDerivativeStatus(string value) => value switch
    {
        "BLOCKED_SOURCE_MISSING" => DerivativeStatus.BlockedSourceMissing,
        _ => Enum.Parse<DerivativeStatus>(value, true),
    };
}
