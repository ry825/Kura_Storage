using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class FileVersionRecordConfiguration : IEntityTypeConfiguration<FileVersionRecord>
{
    public void Configure(EntityTypeBuilder<FileVersionRecord> builder)
    {
        builder.ToTable(
            "file_version_records",
            table =>
            {
                table.HasCheckConstraint("ck_file_version_records_version_positive", "\"version\" >= 1");
                table.HasCheckConstraint(
                    "ck_file_version_records_size_bounded",
                    $"\"size\" >= 0 AND \"size\" <= {FileVersionRecord.MaximumContentBytes}");
                table.HasCheckConstraint(
                    "ck_file_version_records_sha256_lower_hex",
                    "\"sha256\" ~ '^[0-9a-f]{64}$'");
            });
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.FileEntryId).HasColumnName("file_entry_id");
        builder.Property(record => record.Version).HasColumnName("version");
        builder.Property(record => record.Size).HasColumnName("size");
        builder.Property(record => record.Sha256).HasColumnName("sha256").HasMaxLength(64);
        builder.Property(record => record.ContentRelativePath)
            .HasColumnName("content_relative_path")
            .HasMaxLength(2048);
        builder.Property(record => record.ChangeKind)
            .HasColumnName("change_kind")
            .HasConversion(value => ToDatabase(value), value => FromDatabase(value))
            .HasMaxLength(32);
        builder.Property(record => record.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(record => record.ActorDeviceId).HasColumnName("actor_device_id");
        builder.Property(record => record.CreatedAt).HasColumnName("created_at");
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(record => record.FileEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<KuraStorage.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(record => record.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<KuraStorage.Domain.Identity.Device>()
            .WithMany()
            .HasForeignKey(record => record.ActorDeviceId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(record => new { record.FileEntryId, record.Version })
            .IsUnique()
            .IsDescending(false, true)
            .HasDatabaseName("ux_file_version_records_file_version");
        builder.HasIndex(record => new { record.FileEntryId, record.CreatedAt, record.Id })
            .HasDatabaseName("ix_file_version_records_file_created_id");
        builder.HasIndex(record => record.ActorUserId)
            .HasDatabaseName("ix_file_version_records_actor_user_id");
        builder.HasIndex(record => record.ActorDeviceId)
            .HasDatabaseName("ix_file_version_records_actor_device_id");
    }

    private static string ToDatabase(FileVersionChangeKind value) => value switch
    {
        FileVersionChangeKind.TextEdit => "TEXT_EDIT",
        FileVersionChangeKind.ExternalChange => "EXTERNAL_CHANGE",
        _ => value.ToString().ToUpperInvariant(),
    };

    private static FileVersionChangeKind FromDatabase(string value) => value switch
    {
        "TEXT_EDIT" => FileVersionChangeKind.TextEdit,
        "EXTERNAL_CHANGE" => FileVersionChangeKind.ExternalChange,
        _ => Enum.Parse<FileVersionChangeKind>(value, true),
    };
}
