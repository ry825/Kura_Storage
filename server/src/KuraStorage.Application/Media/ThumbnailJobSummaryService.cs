using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Media;

public sealed class ThumbnailJobSummaryService(
    IThumbnailJobSummaryRepository repository,
    ISystemClock clock)
{
    public async Task<ThumbnailJobSummaryView> GetAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor user ID is required.", nameof(actorUserId));
        }

        var snapshot = await repository.GetAsync(actorUserId, cancellationToken);
        if (snapshot.QueuedCount < 0 || snapshot.RunningCount < 0 || snapshot.FailedCount < 0)
        {
            throw new InvalidOperationException("The thumbnail job summary contains a negative count.");
        }

        return new ThumbnailJobSummaryView(
            snapshot.QueuedCount,
            snapshot.RunningCount,
            snapshot.FailedCount,
            clock.UtcNow);
    }
}
