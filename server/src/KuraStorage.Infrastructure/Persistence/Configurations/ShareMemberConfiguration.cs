using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class ShareMemberConfiguration : IEntityTypeConfiguration<ShareMember>
{
    public void Configure(EntityTypeBuilder<ShareMember> builder)
    {
        builder.ToTable(
            "share_members",
            table => table.HasCheckConstraint(
                "ck_share_members_permission",
                "\"permission\" IN ('VIEWER', 'CONTRIBUTOR', 'EDITOR', 'MANAGER')"));
        builder.HasKey(member => new { member.ShareId, member.UserId });
        builder.Property(member => member.ShareId).HasColumnName("share_id");
        builder.Property(member => member.UserId).HasColumnName("user_id");
        builder.Property(member => member.Permission)
            .HasColumnName("permission")
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<SharePermission>(value, true))
            .HasMaxLength(16);
        builder.Property(member => member.CreatedAt).HasColumnName("created_at");
        builder.Property(member => member.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(member => new { member.UserId, member.ShareId })
            .HasDatabaseName("ix_share_members_user_share");
    }
}
