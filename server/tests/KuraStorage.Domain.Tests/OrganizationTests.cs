using KuraStorage.Domain.Organization;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class OrganizationTests
{
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-08-28T11:00:00Z");

    [Fact]
    public void FavoriteEntry_CreateStoresActorEntryAndFirstUtcTime()
    {
        var userId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var favorite = FavoriteEntry.Create(userId, entryId, UtcNow);

        favorite.Register(UtcNow.AddMinutes(1));

        Assert.Equal(userId, favorite.UserId);
        Assert.Equal(entryId, favorite.EntryId);
        Assert.Equal(UtcNow, favorite.FavoritedAt);
    }

    [Fact]
    public void FavoriteEntry_RejectsEmptyIdsAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => FavoriteEntry.Create(Guid.Empty, Guid.NewGuid(), UtcNow));
        Assert.Throws<ArgumentException>(() => FavoriteEntry.Create(Guid.NewGuid(), Guid.Empty, UtcNow));
        Assert.Throws<ArgumentException>(() => FavoriteEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.FromHours(10))));
    }

    [Fact]
    public void EntryTag_CreateStoresIdsAndFirstUtcTime()
    {
        var tagId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var attached = EntryTag.Create(tagId, entryId, UtcNow);

        attached.Attach(UtcNow.AddMinutes(1));

        Assert.Equal(tagId, attached.TagId);
        Assert.Equal(entryId, attached.EntryId);
        Assert.Equal(UtcNow, attached.AttachedAt);
    }

    [Fact]
    public void EntryTag_RejectsEmptyIdsAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => EntryTag.Create(Guid.Empty, Guid.NewGuid(), UtcNow));
        Assert.Throws<ArgumentException>(() => EntryTag.Create(Guid.NewGuid(), Guid.Empty, UtcNow));
        Assert.Throws<ArgumentException>(() => EntryTag.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.FromHours(10))));
    }

    [Fact]
    public void Tag_CreateNormalizesNameAndStoresServerState()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tag = Tag.Create(id, userId, "Café", "CAFÉ", UtcNow);

        Assert.Equal(id, tag.Id);
        Assert.Equal(userId, tag.UserId);
        Assert.Equal("Café", tag.Name);
        Assert.Equal("CAFÉ", tag.NameKey);
        Assert.Equal(UtcNow, tag.CreatedAt);
        Assert.Equal(UtcNow, tag.UpdatedAt);
    }

    [Fact]
    public void Tag_RenameTreatsSameNameKeyAsIdempotentAndUpdatesDisplayWhenChanged()
    {
        var tag = Tag.Create(Guid.NewGuid(), Guid.NewGuid(), "Work", "WORK", UtcNow);

        Assert.False(tag.Rename("Work", "WORK", UtcNow.AddMinutes(1)));
        Assert.True(tag.Rename("work", "WORK", UtcNow.AddMinutes(2)));
        Assert.Equal("work", tag.Name);
        Assert.Equal(UtcNow.AddMinutes(2), tag.UpdatedAt);

        Assert.True(tag.Rename("Project", "PROJECT", UtcNow.AddMinutes(3)));
        Assert.Equal("Project", tag.Name);
        Assert.Equal("PROJECT", tag.NameKey);
        Assert.Equal(UtcNow.AddMinutes(3), tag.UpdatedAt);
    }

    [Fact]
    public void Tag_RejectsEmptyIdsAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => Tag.Create(Guid.Empty, Guid.NewGuid(), "Work", "WORK", UtcNow));
        Assert.Throws<ArgumentException>(() => Tag.Create(Guid.NewGuid(), Guid.Empty, "Work", "WORK", UtcNow));
        Assert.Throws<ArgumentException>(() => Tag.Create(Guid.NewGuid(), Guid.NewGuid(), "", "WORK", UtcNow));
        Assert.Throws<ArgumentException>(() => Tag.Create(Guid.NewGuid(), Guid.NewGuid(), "Work", "", UtcNow));
        Assert.Throws<ArgumentException>(() => Tag.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Work",
            "WORK",
            new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.FromHours(10))));
    }
}
