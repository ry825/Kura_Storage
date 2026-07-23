using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("refresh_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasColumnName("id");
        builder.Property(session => session.FamilyId).HasColumnName("family_id");
        builder.Property(session => session.UserId).HasColumnName("user_id");
        builder.Property(session => session.DeviceId).HasColumnName("device_id");
        builder.Property(session => session.TokenHash).HasColumnName("token_hash");
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => session.FamilyId);
        builder.HasIndex(session => session.DeviceId)
            .IsUnique()
            .HasFilter("revoked_at IS NULL AND used_at IS NULL AND replaced_by_session_id IS NULL");
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>().WithMany().HasForeignKey(session => session.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at");
        builder.Property(session => session.UsedAt).HasColumnName("used_at");
        builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");
        builder.Property(session => session.ReplacedBySessionId).HasColumnName("replaced_by_session_id");
        builder.HasOne<RefreshSession>().WithMany().HasForeignKey(session => session.ReplacedBySessionId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(session => session.CreatedAt).HasColumnName("created_at");
    }
}
