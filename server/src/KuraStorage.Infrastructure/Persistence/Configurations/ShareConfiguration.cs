using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class ShareConfiguration : IEntityTypeConfiguration<Share>
{
    public void Configure(EntityTypeBuilder<Share> builder)
    {
        builder.ToTable("shares");
        builder.HasKey(share => share.Id);
        builder.Property(share => share.Id).HasColumnName("id");
        builder.Property(share => share.TargetEntryId).HasColumnName("target_entry_id");
        builder.Property(share => share.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(share => share.CreatedAt).HasColumnName("created_at");
        builder.Property(share => share.UpdatedAt).HasColumnName("updated_at");
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<FileEntry>()
            .WithOne()
            .HasForeignKey<Share>(share => share.TargetEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(share => share.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(share => share.Members)
            .WithOne()
            .HasForeignKey(member => member.ShareId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(share => share.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(share => share.TargetEntryId)
            .IsUnique()
            .HasDatabaseName("ux_shares_target_entry_id");
        builder.HasIndex(share => new { share.OwnerUserId, share.UpdatedAt, share.Id })
            .HasDatabaseName("ix_shares_owner_updated_id");
    }
}
