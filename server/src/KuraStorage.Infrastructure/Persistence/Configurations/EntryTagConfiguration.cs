using KuraStorage.Domain.Files;
using KuraStorage.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KuraStorage.Infrastructure.Persistence.Configurations;

public sealed class EntryTagConfiguration : IEntityTypeConfiguration<EntryTag>
{
    public void Configure(EntityTypeBuilder<EntryTag> builder)
    {
        builder.ToTable("entry_tags");
        builder.HasKey(entryTag => new { entryTag.TagId, entryTag.EntryId });
        builder.Property(entryTag => entryTag.TagId).HasColumnName("tag_id");
        builder.Property(entryTag => entryTag.EntryId).HasColumnName("entry_id");
        builder.Property(entryTag => entryTag.AttachedAt).HasColumnName("attached_at");
        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(entryTag => entryTag.TagId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileEntry>()
            .WithMany()
            .HasForeignKey(entryTag => entryTag.EntryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entryTag => new { entryTag.EntryId, entryTag.TagId })
            .HasDatabaseName("ix_entry_tags_entry_id_tag_id");
    }
}
