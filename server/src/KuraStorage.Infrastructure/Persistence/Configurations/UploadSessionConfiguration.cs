using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable(
            "upload_sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_upload_sessions_byte_range",
                    "\"expected_size\" >= 0 AND \"received_bytes\" >= 0 AND \"received_bytes\" <= \"expected_size\"");
                table.HasCheckConstraint(
                    "ck_upload_sessions_expiration",
                    "\"expires_at\" <= \"absolute_expires_at\"");
            });
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasColumnName("id");
        builder.Property(session => session.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(session => session.TargetOwnerUserId).HasColumnName("target_owner_user_id");
        builder.Property(session => session.DeviceId).HasColumnName("device_id");
        builder.Property(session => session.DestinationFolderId).HasColumnName("destination_folder_id");
        builder.Property(session => session.FileEntryId).HasColumnName("file_entry_id");
        builder.Property(session => session.FileOperationId).HasColumnName("file_operation_id");
        builder.Property(session => session.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(session => session.FileName).HasColumnName("file_name").HasMaxLength(FileName.MaximumLength);
        builder.Property(session => session.ContentType).HasColumnName("content_type").HasMaxLength(255);
        builder.Property(session => session.ExpectedSize).HasColumnName("expected_size");
        builder.Property(session => session.ExpectedSha256).HasColumnName("expected_sha256").HasMaxLength(64);
        builder.Property(session => session.ReceivedBytes).HasColumnName("received_bytes");
        builder.Property(session => session.LastChunkOffset).HasColumnName("last_chunk_offset");
        builder.Property(session => session.LastChunkLength).HasColumnName("last_chunk_length");
        builder.Property(session => session.LastChunkSha256).HasColumnName("last_chunk_sha256").HasMaxLength(64);
        builder.Property(session => session.TemporaryRelativePath).HasColumnName("temporary_relative_path").HasMaxLength(2048);
        builder.Property(session => session.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<UploadSessionStatus>(value, true))
            .HasMaxLength(32);
        builder.Property(session => session.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(session => session.CreatedAt).HasColumnName("created_at");
        builder.Property(session => session.UpdatedAt).HasColumnName("updated_at");
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at");
        builder.Property(session => session.AbsoluteExpiresAt).HasColumnName("absolute_expires_at");
        builder.Property(session => session.CompletedAt).HasColumnName("completed_at");
        builder.Property(session => session.CleanedAt).HasColumnName("cleaned_at");
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.TargetOwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(session => session.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(session => session.DestinationFolderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(session => new { session.ActorUserId, session.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_upload_sessions_actor_idempotency_key");
        builder.HasIndex(session => session.FileOperationId)
            .IsUnique()
            .HasFilter("\"file_operation_id\" IS NOT NULL")
            .HasDatabaseName("ux_upload_sessions_file_operation_id");
        builder.HasIndex(session => new { session.Status, session.ExpiresAt, session.Id })
            .HasDatabaseName("ix_upload_sessions_cleanup_candidates");
        builder.HasIndex(session => new { session.DeviceId, session.Status, session.Id })
            .HasDatabaseName("ix_upload_sessions_device_status");
        builder.HasIndex(session => new { session.ActorUserId, session.Status })
            .HasDatabaseName("ix_upload_sessions_actor_status");
    }
}
