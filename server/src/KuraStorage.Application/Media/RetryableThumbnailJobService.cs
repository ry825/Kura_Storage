using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Media;

/// <summary>
/// Exposes opaque job identifiers solely to the client-side retry coordinator.
/// Authorization and source validity are deliberately evaluated again by RetryJobAsync.
/// </summary>
public sealed class RetryableThumbnailJobService(
    IFileRepository files,
    IAuthorizationService authorization,
    IMediaRepository media)
{
    public const int MaximumJobs = 32;

    public async Task<IReadOnlyList<RetryableThumbnailJobView>> GetAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor user ID is required.", nameof(actorUserId));
        }

        var result = new List<RetryableThumbnailJobView>(MaximumJobs);
        foreach (var snapshot in await media.FindRetryableThumbnailJobsAsync(cancellationToken))
        {
            var job = snapshot.Job;
            if (job is null || job.Status != MediaJobStatus.Failed ||
                job.JobType is not (DerivativeType.Thumbnail or DerivativeType.PdfThumbnail) ||
                !PreviewService.CanRetry(job.ErrorCode) ||
                snapshot.Source.Status != FileEntryStatus.Active ||
                snapshot.Source.FileVersion != snapshot.Derivative.SourceVersion ||
                !await authorization.AllowsAsync(actorUserId, snapshot.Source.Id, ShareOperation.View, cancellationToken) ||
                await files.HasIncompleteOperationAsync(
                    snapshot.Source.OwnerUserId,
                    snapshot.Source.Id,
                    snapshot.Source.RelativePath,
                    cancellationToken))
            {
                continue;
            }

            result.Add(new RetryableThumbnailJobView(job.Id, RetryAfterSeconds: 2, Retryable: true));
            if (result.Count == MaximumJobs)
            {
                break;
            }
        }

        return result;
    }
}
