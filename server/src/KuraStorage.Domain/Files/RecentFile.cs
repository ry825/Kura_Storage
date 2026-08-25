using System.Diagnostics.CodeAnalysis;

namespace KuraStorage.Domain.Files;

public sealed class RecentFile
{
    [ExcludeFromCodeCoverage]
    private RecentFile()
    {
    }

    private RecentFile(Guid userId, Guid fileId, DateTimeOffset openedAt)
    {
        EnsureValid(userId, fileId, openedAt);
        UserId = userId;
        FileId = fileId;
        OpenedAt = openedAt;
    }

    public Guid UserId { get; private set; }

    public Guid FileId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public static RecentFile Create(Guid userId, Guid fileId, DateTimeOffset openedAt) =>
        new(userId, fileId, openedAt);

    public void Reopen(DateTimeOffset openedAt)
    {
        EnsureUtc(openedAt);
        if (openedAt > OpenedAt)
        {
            OpenedAt = openedAt;
        }
    }

    private static void EnsureValid(Guid userId, Guid fileId, DateTimeOffset openedAt)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user ID is required.", nameof(userId));
        }

        if (fileId == Guid.Empty)
        {
            throw new ArgumentException("The file ID is required.", nameof(fileId));
        }

        EnsureUtc(openedAt);
    }

    private static void EnsureUtc(DateTimeOffset openedAt)
    {
        if (openedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The opened time must be UTC.", nameof(openedAt));
        }
    }
}
