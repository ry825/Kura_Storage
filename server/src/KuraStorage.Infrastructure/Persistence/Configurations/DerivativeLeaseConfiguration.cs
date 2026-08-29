using KuraStorage.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class DerivativeLeaseConfiguration : IEntityTypeConfiguration<DerivativeLease>
{
    public void Configure(EntityTypeBuilder<DerivativeLease> builder)
    {
        builder.ToTable(
            "derivative_leases",
            table => table.HasCheckConstraint("ck_derivative_leases_type", "lease_type IN ('GENERATION', 'DELIVERY')"));
        builder.HasKey(lease => lease.Id);
        builder.Property(lease => lease.Id).HasColumnName("id");
        builder.Property(lease => lease.DerivativeId).HasColumnName("derivative_id");
        builder.Property(lease => lease.LeaseType)
            .HasColumnName("lease_type")
            .HasConversion(value => value.ToString().ToUpperInvariant(), value => Enum.Parse<DerivativeLeaseType>(value, true))
            .HasMaxLength(16);
        builder.Property(lease => lease.OwnerToken).HasColumnName("owner_token");
        builder.Property(lease => lease.ExpiresAt).HasColumnName("expires_at");
        builder.Property(lease => lease.CreatedAt).HasColumnName("created_at");
        builder.Property(lease => lease.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(lease => lease.IsReleased);
        builder.HasOne<FileDerivative>()
            .WithMany()
            .HasForeignKey(lease => lease.DerivativeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(lease => new { lease.DerivativeId, lease.LeaseType, lease.OwnerToken })
            .IsUnique()
            .HasDatabaseName("ux_derivative_leases_owner");
        builder.HasIndex(lease => new { lease.DerivativeId, lease.ExpiresAt })
            .HasDatabaseName("ix_derivative_leases_active");
        builder.HasIndex(lease => new { lease.ExpiresAt, lease.Id })
            .HasDatabaseName("ix_derivative_leases_expiry");
    }
}
