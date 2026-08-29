using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class MediaJobConfiguration : IEntityTypeConfiguration<MediaJob>
{
    public void Configure(EntityTypeBuilder<MediaJob> builder)
    {
        builder.ToTable(
            "media_jobs",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_media_jobs_status",
                    "status IN ('QUEUED', 'RUNNING', 'COMPLETED', 'FAILED', 'CANCELLED')");
                table.HasCheckConstraint(
                    "ck_media_jobs_attempts",
                    $"attempt_count >= 0 AND attempt_count <= {MediaJob.MaximumAttempts}");
                table.HasCheckConstraint(
                    "ck_media_jobs_progress",
                    "progress_percent IS NULL OR (progress_percent >= 0 AND progress_percent <= 100)");
                table.HasCheckConstraint(
                    "ck_media_jobs_duration",
                    "(processed_duration_ms IS NULL OR processed_duration_ms >= 0) AND " +
                    "(total_duration_ms IS NULL OR total_duration_ms >= 0) AND " +
                    "(processed_duration_ms IS NULL OR total_duration_ms IS NULL OR processed_duration_ms <= total_duration_ms)");
                table.HasCheckConstraint(
                    "ck_media_jobs_owner",
                    "(status = 'RUNNING' AND worker_token IS NOT NULL AND heartbeat_at IS NOT NULL) OR " +
                    "(status <> 'RUNNING' AND worker_token IS NULL AND heartbeat_at IS NULL)");
                table.HasCheckConstraint(
                    "ck_media_jobs_completion",
                    "(status IN ('COMPLETED', 'FAILED', 'CANCELLED') AND completed_at IS NOT NULL) OR " +
                    "(status IN ('QUEUED', 'RUNNING') AND completed_at IS NULL)");
                table.HasCheckConstraint(
                    "ck_media_jobs_error",
                    "status NOT IN ('FAILED', 'CANCELLED') OR error_code IS NOT NULL");
            });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnName("id");
        builder.Property(job => job.DerivativeId).HasColumnName("derivative_id");
        builder.Property(job => job.JobType)
            .HasColumnName("job_type")
            .HasConversion(
                value => FileDerivativeConfiguration.ToDatabase(value),
                value => FileDerivativeConfiguration.FromDerivativeType(value))
            .HasMaxLength(32);
        builder.Property(job => job.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<MediaJobStatus>(value, true))
            .HasMaxLength(16);
        builder.Property(job => job.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(job => job.AttemptCount).HasColumnName("attempt_count");
        builder.Property(job => job.AvailableAt).HasColumnName("available_at");
        builder.Property(job => job.WorkerToken).HasColumnName("worker_token");
        builder.Property(job => job.HeartbeatAt).HasColumnName("heartbeat_at");
        builder.Property(job => job.ProgressPercent).HasColumnName("progress_percent");
        builder.Property(job => job.ProcessedDurationMs).HasColumnName("processed_duration_ms");
        builder.Property(job => job.TotalDurationMs).HasColumnName("total_duration_ms");
        builder.Property(job => job.StartedAt).HasColumnName("started_at");
        builder.Property(job => job.CompletedAt).HasColumnName("completed_at");
        builder.Property(job => job.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.Property(job => job.CreatedAt).HasColumnName("created_at");
        builder.Property(job => job.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<FileDerivative>()
            .WithMany()
            .HasForeignKey(job => job.DerivativeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(job => job.RequestedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(job => new { job.Status, job.AvailableAt, job.CreatedAt, job.Id })
            .HasDatabaseName("ix_media_jobs_queue");
        builder.HasIndex(job => job.DerivativeId)
            .HasDatabaseName("ix_media_jobs_derivative");
        builder.HasIndex(job => new { job.DerivativeId, job.Status })
            .IsUnique()
            .HasFilter("status IN ('QUEUED', 'RUNNING')")
            .HasDatabaseName("ux_media_jobs_active_derivative");
        builder.HasIndex(job => new { job.Status, job.HeartbeatAt, job.Id })
            .HasDatabaseName("ix_media_jobs_stale");
        builder.HasIndex(job => new { job.Status, job.CompletedAt, job.Id })
            .HasDatabaseName("ix_media_jobs_history_cleanup");
    }
}
