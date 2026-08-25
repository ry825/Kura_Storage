using KuraStorage.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class RecentFileConfiguration : IEntityTypeConfiguration<RecentFile>
{
    public void Configure(EntityTypeBuilder<RecentFile> builder)
    {
        builder.ToTable("recent_files");
        builder.HasKey(recent => new { recent.UserId, recent.FileId });
        builder.Property(recent => recent.UserId).HasColumnName("user_id");
        builder.Property(recent => recent.FileId).HasColumnName("file_id");
        builder.Property(recent => recent.OpenedAt).HasColumnName("opened_at");
        builder.HasOne<KuraStorage.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(recent => recent.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(recent => recent.FileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(recent => new { recent.UserId, recent.OpenedAt, recent.FileId })
            .IsDescending(false, true, false)
            .HasDatabaseName("ix_recent_files_user_opened_at_file_id");
        builder.HasIndex(recent => recent.FileId)
            .HasDatabaseName("ix_recent_files_file_id");
    }
}
