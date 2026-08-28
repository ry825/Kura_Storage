using System.Diagnostics.CodeAnalysis;

namespace KuraStorage.Domain.Organization;

public sealed class Tag
{
    [ExcludeFromCodeCoverage]
    private Tag()
    {
    }

    private Tag(
        Guid id,
        Guid userId,
        string name,
        string nameKey,
        DateTimeOffset now)
    {
        EnsureId(id, nameof(id));
        EnsureId(userId, nameof(userId));
        EnsureText(name, nameof(name));
        EnsureText(nameKey, nameof(nameKey));
        EnsureUtc(now, nameof(now));
        Id = id;
        UserId = userId;
        Name = name;
        NameKey = nameKey;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NameKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Tag Create(
        Guid id,
        Guid userId,
        string name,
        string nameKey,
        DateTimeOffset now) =>
        new(id, userId, name, nameKey, now);

    public bool Rename(string name, string nameKey, DateTimeOffset now)
    {
        EnsureText(name, nameof(name));
        EnsureText(nameKey, nameof(nameKey));
        EnsureUtc(now, nameof(now));
        if (string.Equals(Name, name, StringComparison.Ordinal) &&
            string.Equals(NameKey, nameKey, StringComparison.Ordinal))
        {
            return false;
        }

        Name = name;
        NameKey = nameKey;
        UpdatedAt = now;
        return true;
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID is required.", parameterName);
        }
    }

    private static void EnsureText(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
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
