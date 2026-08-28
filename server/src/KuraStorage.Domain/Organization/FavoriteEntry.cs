using System.Diagnostics.CodeAnalysis;

namespace KuraStorage.Domain.Organization;

public sealed class FavoriteEntry
{
    [ExcludeFromCodeCoverage]
    private FavoriteEntry()
    {
    }

    private FavoriteEntry(Guid userId, Guid entryId, DateTimeOffset favoritedAt)
    {
        EnsureId(userId, nameof(userId));
        EnsureId(entryId, nameof(entryId));
        EnsureUtc(favoritedAt, nameof(favoritedAt));
        UserId = userId;
        EntryId = entryId;
        FavoritedAt = favoritedAt;
    }

    public Guid UserId { get; private set; }

    public Guid EntryId { get; private set; }

    public DateTimeOffset FavoritedAt { get; private set; }

    public static FavoriteEntry Create(Guid userId, Guid entryId, DateTimeOffset favoritedAt) =>
        new(userId, entryId, favoritedAt);

    public void Register(DateTimeOffset favoritedAt) => EnsureUtc(favoritedAt, nameof(favoritedAt));

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID is required.", parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The time must be UTC.", parameterName);
        }
    }
}
