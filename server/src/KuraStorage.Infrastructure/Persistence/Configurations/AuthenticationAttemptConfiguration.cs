using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class AuthenticationAttemptConfiguration : IEntityTypeConfiguration<AuthenticationAttempt>
{
    public void Configure(EntityTypeBuilder<AuthenticationAttempt> builder)
    {
        builder.ToTable("authentication_attempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).HasColumnName("id");
        builder.Property(attempt => attempt.UsernameHash).HasColumnName("username_hash");
        builder.Property(attempt => attempt.DeviceId).HasColumnName("device_id");
        builder.Property(attempt => attempt.ResultCode).HasColumnName("result_code").HasMaxLength(64);
        builder.Property(attempt => attempt.RemoteAddress).HasColumnName("remote_address").HasColumnType("inet");
        builder.Property(attempt => attempt.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(attempt => new { attempt.UsernameHash, attempt.CreatedAt });
    }
}
