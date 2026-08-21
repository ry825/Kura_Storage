using KuraStorage.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class TrashPurgeRunConfiguration : IEntityTypeConfiguration<TrashPurgeRun>
{
    public void Configure(EntityTypeBuilder<TrashPurgeRun> builder)
    {
        builder.ToTable(
            "trash_purge_runs",
            table =>
            {
                table.HasCheckConstraint("ck_trash_purge_runs_examined", "examined_root_count >= 0");
                table.HasCheckConstraint("ck_trash_purge_runs_deleted", "deleted_root_count >= 0 AND deleted_root_count <= examined_root_count");
                table.HasCheckConstraint("ck_trash_purge_runs_released", "released_bytes >= 0");
                table.HasCheckConstraint("ck_trash_purge_runs_errors", "error_count >= 0");
                table.HasCheckConstraint(
                    "ck_trash_purge_runs_completion",
                    "(status = 'RUNNING' AND completed_at IS NULL) OR (status <> 'RUNNING' AND completed_at IS NOT NULL)");
            });
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasColumnName("id");
        builder.Property(run => run.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(run => run.CompletedAt).HasColumnName("completed_at");
        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status == TrashPurgeRunStatus.CompletedWithErrors
                    ? "COMPLETED_WITH_ERRORS"
                    : status.ToString().ToUpperInvariant(),
                value => value == "COMPLETED_WITH_ERRORS"
                    ? TrashPurgeRunStatus.CompletedWithErrors
                    : Enum.Parse<TrashPurgeRunStatus>(value, true))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(run => run.ExaminedRootCount).HasColumnName("examined_root_count").IsRequired();
        builder.Property(run => run.DeletedRootCount).HasColumnName("deleted_root_count").IsRequired();
        builder.Property(run => run.ReleasedBytes).HasColumnName("released_bytes").IsRequired();
        builder.Property(run => run.ErrorCount).HasColumnName("error_count").IsRequired();
        builder.HasIndex(run => run.StartedAt)
            .HasDatabaseName("ix_trash_purge_runs_started_at")
            .IsDescending();
    }
}
