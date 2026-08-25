using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Domain.Tests;

public sealed class RecentFileTests
{
    [Fact]
    public void Create_StoresUserFileAndUtcOpenedTime()
    {
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-08-25T01:00:00Z");

        var recent = RecentFile.Create(userId, fileId, openedAt);

        Assert.Equal(userId, recent.UserId);
        Assert.Equal(fileId, recent.FileId);
        Assert.Equal(openedAt, recent.OpenedAt);
    }

    [Fact]
    public void Create_RejectsEmptyIdsAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() =>
            RecentFile.Create(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            RecentFile.Create(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            RecentFile.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.FromHours(10))));
    }

    [Fact]
    public void Reopen_OnlyAdvancesToLaterServerTime()
    {
        var openedAt = DateTimeOffset.Parse("2026-08-25T01:00:00Z");
        var recent = RecentFile.Create(Guid.NewGuid(), Guid.NewGuid(), openedAt);

        recent.Reopen(openedAt.AddMinutes(-1));
        Assert.Equal(openedAt, recent.OpenedAt);

        recent.Reopen(openedAt.AddMinutes(1));
        Assert.Equal(openedAt.AddMinutes(1), recent.OpenedAt);
    }
}
