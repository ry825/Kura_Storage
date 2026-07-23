using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.HasKey(device => device.Id);
        builder.Property(device => device.Id).HasColumnName("id");
        builder.Property(device => device.UserId).HasColumnName("user_id");
        builder.HasOne<User>().WithMany().HasForeignKey(device => device.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(device => new { device.UserId, device.Status });
        builder.Property(device => device.DeviceName).HasColumnName("device_name").HasMaxLength(128);
        builder.Property(device => device.Platform).HasColumnName("platform").HasConversion<string>().HasMaxLength(16);
        builder.Property(device => device.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(device => device.RegisteredAt).HasColumnName("registered_at");
        builder.Property(device => device.RevokedAt).HasColumnName("revoked_at");
    }
}
