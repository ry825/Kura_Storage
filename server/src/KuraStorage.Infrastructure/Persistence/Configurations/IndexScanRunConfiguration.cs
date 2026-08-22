using KuraStorage.Domain.Indexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class IndexScanRunConfiguration : IEntityTypeConfiguration<IndexScanRun>
{
    public void Configure(EntityTypeBuilder<IndexScanRun> builder)
    {
        builder.ToTable(
            "index_scan_runs",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_index_scan_runs_completion",
                    "(status = 'RUNNING' AND completed_at IS NULL) OR (status <> 'RUNNING' AND completed_at IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_index_scan_runs_counts",
                    "enumerated_count >= 0 AND added_count >= 0 AND updated_count >= 0 AND moved_count >= 0 " +
                    "AND candidate_count >= 0 AND missing_count >= 0 AND revived_count >= 0 " +
                    "AND isolated_count >= 0 AND error_count >= 0");
                table.HasCheckConstraint(
                    "ck_index_scan_runs_error_code",
                    "(status = 'FAILED' AND error_code IS NOT NULL) OR (status <> 'FAILED' AND error_code IS NULL)");
            });
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasColumnName("id");
        builder.Property(run => run.Trigger)
            .HasColumnName("trigger")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<IndexScanTrigger>(value, true))
            .HasMaxLength(16);
        builder.Property(run => run.Mode)
            .HasColumnName("mode")
            .HasConversion(value => value == IndexScanMode.DryRun ? "DRY_RUN" : "APPLY", value => value == "DRY_RUN" ? IndexScanMode.DryRun : IndexScanMode.Apply)
            .HasMaxLength(16);
        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasConversion(value => value == IndexScanStatus.CompletedWithWarnings ? "COMPLETED_WITH_WARNINGS" : value.ToString().ToUpperInvariant(), value => value == "COMPLETED_WITH_WARNINGS" ? IndexScanStatus.CompletedWithWarnings : Enum.Parse<IndexScanStatus>(value, true))
            .HasMaxLength(32);
        builder.Property(run => run.StartedAt).HasColumnName("started_at");
        builder.Property(run => run.CompletedAt).HasColumnName("completed_at");
        builder.Property(run => run.EnumeratedCount).HasColumnName("enumerated_count");
        builder.Property(run => run.AddedCount).HasColumnName("added_count");
        builder.Property(run => run.UpdatedCount).HasColumnName("updated_count");
        builder.Property(run => run.MovedCount).HasColumnName("moved_count");
        builder.Property(run => run.CandidateCount).HasColumnName("candidate_count");
        builder.Property(run => run.MissingCount).HasColumnName("missing_count");
        builder.Property(run => run.RevivedCount).HasColumnName("revived_count");
        builder.Property(run => run.IsolatedCount).HasColumnName("isolated_count");
        builder.Property(run => run.ErrorCount).HasColumnName("error_count");
        builder.Property(run => run.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.HasIndex(run => run.StartedAt).IsDescending().HasDatabaseName("ix_index_scan_runs_started_at");
        builder.HasIndex(run => new { run.Status, run.StartedAt }).HasDatabaseName("ix_index_scan_runs_status_started_at");
    }
}
