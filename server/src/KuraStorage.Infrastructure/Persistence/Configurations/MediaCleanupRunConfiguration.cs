using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class MediaCleanupRunConfiguration : IEntityTypeConfiguration<MediaCleanupRun>
{
    public void Configure(EntityTypeBuilder<MediaCleanupRun> builder)
    {
        builder.ToTable(
            "media_cleanup_runs",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_media_cleanup_runs_manual_identity",
                    "(trigger = 'MANUAL' AND requested_by_admin_user_id IS NOT NULL AND idempotency_key_hash IS NOT NULL AND request_fingerprint_hash IS NOT NULL) OR " +
                    "(trigger = 'SCHEDULED' AND requested_by_admin_user_id IS NULL AND idempotency_key_hash IS NULL AND request_fingerprint_hash IS NULL)");
                table.HasCheckConstraint(
                    "ck_media_cleanup_runs_lifecycle",
                    "(status = 'PENDING' AND worker_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NULL) OR " +
                    "(status = 'RUNNING' AND worker_token IS NOT NULL AND lease_expires_at IS NOT NULL AND completed_at IS NULL) OR " +
                    "(status IN ('COMPLETED', 'FAILED') AND worker_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_media_cleanup_runs_counts",
                    "examined_count >= 0 AND deleted_count >= 0 AND deleted_count <= examined_count AND failure_count >= 0 AND released_bytes >= 0 AND (remaining_cache_bytes IS NULL OR remaining_cache_bytes >= 0)");
            });
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasColumnName("id");
        builder.Property(run => run.Trigger)
            .HasColumnName("trigger")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<MediaCleanupTrigger>(value, true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<MediaCleanupRunStatus>(value, true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(run => run.RequestedByAdminUserId).HasColumnName("requested_by_admin_user_id");
        builder.Property(run => run.IdempotencyKeyHash).HasColumnName("idempotency_key_hash").HasMaxLength(64);
        builder.Property(run => run.RequestFingerprintHash).HasColumnName("request_fingerprint_hash").HasMaxLength(64);
        builder.Property(run => run.WorkerToken).HasColumnName("worker_token");
        builder.Property(run => run.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(run => run.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(run => run.StartedAt).HasColumnName("started_at");
        builder.Property(run => run.CompletedAt).HasColumnName("completed_at");
        builder.Property(run => run.ExaminedCount).HasColumnName("examined_count").IsRequired();
        builder.Property(run => run.DeletedCount).HasColumnName("deleted_count").IsRequired();
        builder.Property(run => run.ReleasedBytes).HasColumnName("released_bytes").IsRequired();
        builder.Property(run => run.FailureCount).HasColumnName("failure_count").IsRequired();
        builder.Property(run => run.RemainingCacheBytes).HasColumnName("remaining_cache_bytes");
        builder.Property(run => run.FailureCode)
            .HasColumnName("failure_code")
            .HasConversion(
                value => value == null ? null : FailureCodeToString(value.Value),
                value => value == null ? null : FailureCodeFromString(value))
            .HasMaxLength(32);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(run => run.RequestedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.RequestedByAdminUserId, run.IdempotencyKeyHash })
            .HasDatabaseName("ux_media_cleanup_runs_manual_idempotency")
            .HasFilter("trigger = 'MANUAL'")
            .IsUnique();
        builder.HasIndex(run => new { run.Status, run.LeaseExpiresAt, run.RequestedAt, run.Id })
            .HasDatabaseName("ix_media_cleanup_runs_claim");
        builder.HasIndex(run => run.Trigger)
            .HasDatabaseName("ux_media_cleanup_runs_active_scheduled")
            .HasFilter("trigger = 'SCHEDULED' AND status IN ('PENDING', 'RUNNING')")
            .IsUnique();
        builder.HasIndex(run => new { run.RequestedAt, run.Id })
            .HasDatabaseName("ix_media_cleanup_runs_latest")
            .IsDescending();
    }

    private static string FailureCodeToString(MediaCleanupFailureCode value) => value switch
    {
        MediaCleanupFailureCode.StorageUnavailable => "STORAGE_UNAVAILABLE",
        MediaCleanupFailureCode.PartialDeleteFailure => "PARTIAL_DELETE_FAILURE",
        MediaCleanupFailureCode.CleanupFailed => "CLEANUP_FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static MediaCleanupFailureCode FailureCodeFromString(string value) => value switch
    {
        "STORAGE_UNAVAILABLE" => MediaCleanupFailureCode.StorageUnavailable,
        "PARTIAL_DELETE_FAILURE" => MediaCleanupFailureCode.PartialDeleteFailure,
        "CLEANUP_FAILED" => MediaCleanupFailureCode.CleanupFailed,
        _ => throw new InvalidOperationException("Unknown media cleanup failure code."),
    };
}
