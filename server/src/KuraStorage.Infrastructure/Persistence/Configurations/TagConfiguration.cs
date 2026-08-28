using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable(
            "tags",
            table => table.HasCheckConstraint(
                "ck_tags_name",
                "char_length(name) BETWEEN 1 AND 50 AND name = btrim(name)"));
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).HasColumnName("id");
        builder.Property(tag => tag.UserId).HasColumnName("user_id");
        builder.Property(tag => tag.Name).HasColumnName("name").HasColumnType("text");
        builder.Property(tag => tag.NameKey).HasColumnName("name_key").HasColumnType("text");
        builder.Property(tag => tag.CreatedAt).HasColumnName("created_at");
        builder.Property(tag => tag.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(tag => tag.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(tag => new { tag.UserId, tag.NameKey })
            .IsUnique()
            .HasDatabaseName("ux_tags_user_name_key");
        builder.HasIndex(tag => new { tag.UserId, tag.NameKey, tag.Id })
            .HasDatabaseName("ix_tags_user_name_key_id");
    }
}
