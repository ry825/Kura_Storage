using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class ThumbnailJobSummaryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Get_MapsCountsAndServerObservationTime()
    {
        var actorId = Guid.NewGuid();
        var repository = new RecordingRepository(new(12, 3, 1));
        var service = new ThumbnailJobSummaryService(repository, new FixedClock());

        var result = await service.GetAsync(actorId, CancellationToken.None);

        Assert.Equal(new ThumbnailJobSummaryView(12, 3, 1, Now), result);
        Assert.Equal(actorId, repository.ActorUserId);
    }

    [Fact]
    public async Task Get_RejectsInvalidActorAndImpossibleNegativeRepositoryCounts()
    {
        var service = new ThumbnailJobSummaryService(
            new RecordingRepository(new(-1, 0, 0)),
            new FixedClock());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(Guid.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class RecordingRepository(ThumbnailJobSummarySnapshot snapshot)
        : IThumbnailJobSummaryRepository
    {
        public Guid? ActorUserId { get; private set; }

        public Task<ThumbnailJobSummarySnapshot> GetAsync(
            Guid actorUserId,
            CancellationToken cancellationToken)
        {
            ActorUserId = actorUserId;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
