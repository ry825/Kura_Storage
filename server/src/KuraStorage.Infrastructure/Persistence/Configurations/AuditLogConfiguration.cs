using KuraStorage.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).HasColumnName("id");
        builder.Property(log => log.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(log => log.ActorDeviceId).HasColumnName("actor_device_id");
        builder.Property(log => log.ActorOsUser).HasColumnName("actor_os_user").HasMaxLength(128);
        builder.Property(log => log.Action).HasColumnName("action").HasMaxLength(128);
        builder.Property(log => log.TargetType).HasColumnName("target_type").HasMaxLength(64);
        builder.Property(log => log.TargetId).HasColumnName("target_id").HasMaxLength(128);
        builder.Property(log => log.ResultCode).HasColumnName("result_code").HasMaxLength(64);
        builder.Property(log => log.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(log => log.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(log => log.CreatedAt);
        builder.HasIndex(log => new { log.ActorUserId, log.CreatedAt });
    }
}
