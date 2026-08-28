using System.Diagnostics.CodeAnalysis;

namespace KuraStorage.Domain.Organization;

public sealed class EntryTag
{
    [ExcludeFromCodeCoverage]
    private EntryTag()
    {
    }

    private EntryTag(Guid tagId, Guid entryId, DateTimeOffset attachedAt)
    {
        EnsureId(tagId, nameof(tagId));
        EnsureId(entryId, nameof(entryId));
        EnsureUtc(attachedAt, nameof(attachedAt));
        TagId = tagId;
        EntryId = entryId;
        AttachedAt = attachedAt;
    }

    public Guid TagId { get; private set; }

    public Guid EntryId { get; private set; }

    public DateTimeOffset AttachedAt { get; private set; }

    public static EntryTag Create(Guid tagId, Guid entryId, DateTimeOffset attachedAt) =>
        new(tagId, entryId, attachedAt);

    public void Attach(DateTimeOffset attachedAt) => EnsureUtc(attachedAt, nameof(attachedAt));

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
