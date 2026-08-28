using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class FavoriteEntryConfiguration : IEntityTypeConfiguration<FavoriteEntry>
{
    public void Configure(EntityTypeBuilder<FavoriteEntry> builder)
    {
        builder.ToTable("favorite_entries");
        builder.HasKey(favorite => new { favorite.UserId, favorite.EntryId });
        builder.Property(favorite => favorite.UserId).HasColumnName("user_id");
        builder.Property(favorite => favorite.EntryId).HasColumnName("entry_id");
        builder.Property(favorite => favorite.FavoritedAt).HasColumnName("favorited_at");
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(favorite => favorite.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(favorite => favorite.EntryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(favorite => new { favorite.UserId, favorite.FavoritedAt, favorite.EntryId })
            .IsDescending(false, true, false)
            .HasDatabaseName("ix_favorite_entries_user_favorited_at_entry_id");
        builder.HasIndex(favorite => favorite.EntryId)
            .HasDatabaseName("ix_favorite_entries_entry_id");
    }
}
