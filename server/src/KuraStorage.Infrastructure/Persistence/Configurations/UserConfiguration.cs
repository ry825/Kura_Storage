using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.UsernameNormalized).HasColumnName("username_normalized").HasMaxLength(128);
        builder.HasIndex(user => user.UsernameNormalized).IsUnique();
        builder.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(1024);
        builder.Property(user => user.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(16);
        builder.Property(user => user.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(user => user.FailedLoginCount).HasColumnName("failed_login_count");
        builder.Property(user => user.FailedLoginWindowStartedAt).HasColumnName("failed_login_window_started_at");
        builder.Property(user => user.LockType).HasColumnName("lock_type").HasConversion<string>().HasMaxLength(16);
        builder.Property(user => user.CreatedAt).HasColumnName("created_at");
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at");
    }
}
