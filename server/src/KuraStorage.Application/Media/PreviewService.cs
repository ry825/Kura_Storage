using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Media;

public sealed class PreviewService(
    IFileRepository files,
    IAuthorizationService authorization,
    IMediaRepository media,
    IMediaJobQueue queue,
    IDerivativeStore derivativeStore,
    IStorageGuard storageGuard,
    ISystemClock clock,
    IMediaWaiter waiter,
    MediaRuntimeOptions options)
{
    public async Task<MediaResult<MediaRequestResult>> RequestAsync(
        MediaContentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ActorUserId == Guid.Empty || request.FileId == Guid.Empty ||
            !MediaContractRules.TryParseVariant(request.Variant, out var variant) ||
            variant == MediaVariant.Original ||
            !MediaContractRules.TryParseDisposition(request.Disposition, out var disposition))
        {
            return MediaResult<MediaRequestResult>.Fail(MediaErrorCodes.VariantUnsupported, MediaFailureKind.BadRequest);
        }

        var source = await files.FindByIdAsync(request.FileId, cancellationToken);
        var failure = await ValidateSourceAsync(request.ActorUserId, source, variant, cancellationToken);
        if (failure is not null)
        {
            return MediaResult<MediaRequestResult>.Fail(failure.Code, failure.Kind);
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([source!.Id], cancellationToken);
        if (!await files.ReloadAsync(source, cancellationToken))
        {
            return MediaResult<MediaRequestResult>.Fail(FileErrorCodes.FileNotFound, MediaFailureKind.NotFound);
        }

        failure = await ValidateSourceAsync(request.ActorUserId, source, variant, cancellationToken);
        if (failure is not null)
        {
            return MediaResult<MediaRequestResult>.Fail(failure.Code, failure.Kind);
        }

        var derivativeType = MediaContractRules.ToDerivativeType(source.MimeType, variant);
        var profileVersion = MediaContractRules.ProfileVersion(
            variant, options.ThumbnailProfileVersion, options.ImageProfileVersion, options.VideoProfileVersion);
        var snapshot = await media.GetOrCreateRequestAsync(
            source, derivativeType, profileVersion, request.ActorUserId, clock.UtcNow, cancellationToken);
        var result = await ResolveAsync(request.ActorUserId, variant, disposition, snapshot, cancellationToken);
        if (result.Status != MediaRequestStatus.Generating || snapshot.Job is null ||
            source.MimeType?.StartsWith("video/", StringComparison.Ordinal) == true)
        {
            return MediaResult<MediaRequestResult>.Success(result);
        }

        var elapsed = 0;
        while (elapsed < options.ImageWaitMilliseconds)
        {
            await waiter.DelayAsync(TimeSpan.FromMilliseconds(options.JobPollMilliseconds), cancellationToken);
            elapsed += options.JobPollMilliseconds;
            var refreshed = await media.FindByJobAsync(snapshot.Job.Id, cancellationToken);
            if (refreshed is null)
            {
                break;
            }

            result = await ResolveAsync(request.ActorUserId, variant, disposition, refreshed, cancellationToken);
            if (result.Status != MediaRequestStatus.Generating)
            {
                return MediaResult<MediaRequestResult>.Success(result);
            }
        }

        return MediaResult<MediaRequestResult>.Success(result);
    }

    public async Task<MediaResult<MediaJobView>> GetJobAsync(
        Guid actorUserId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var snapshot = await AuthorizedJobAsync(actorUserId, jobId, cancellationToken);
        if (snapshot is null || snapshot.Job is null)
        {
            return MediaResult<MediaJobView>.Fail(FileErrorCodes.FileNotFound, MediaFailureKind.NotFound);
        }

        return MediaResult<MediaJobView>.Success(await MapJobAsync(snapshot, cancellationToken));
    }

    public async Task<MediaResult<MediaJobView>> RetryJobAsync(
        Guid actorUserId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var snapshot = await AuthorizedJobAsync(actorUserId, jobId, cancellationToken);
        if (snapshot?.Job is null)
        {
            return MediaResult<MediaJobView>.Fail(FileErrorCodes.FileNotFound, MediaFailureKind.NotFound);
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([snapshot.Source.Id], cancellationToken);
        snapshot = await AuthorizedJobAsync(actorUserId, jobId, cancellationToken);
        if (snapshot?.Job is null || snapshot.Job.Status != MediaJobStatus.Failed ||
            !CanRetry(snapshot.Job.ErrorCode) || snapshot.Source.Status != FileEntryStatus.Active ||
            snapshot.Source.FileVersion != snapshot.Derivative.SourceVersion ||
            await files.HasIncompleteOperationAsync(
                snapshot.Source.OwnerUserId,
                snapshot.Source.Id,
                snapshot.Source.RelativePath,
                cancellationToken))
        {
            return MediaResult<MediaJobView>.Fail(MediaErrorCodes.RetryNotAllowed, MediaFailureKind.Conflict);
        }

        var newJobId = await queue.TryRetryFailedAsync(
            jobId, Guid.NewGuid(), actorUserId, clock.UtcNow, cancellationToken);
        if (newJobId is null)
        {
            return MediaResult<MediaJobView>.Fail(MediaErrorCodes.RetryNotAllowed, MediaFailureKind.Conflict);
        }

        var retried = await AuthorizedJobAsync(actorUserId, newJobId.Value, cancellationToken);
        return retried?.Job is null
            ? MediaResult<MediaJobView>.Fail(FileErrorCodes.FileNotFound, MediaFailureKind.NotFound)
            : MediaResult<MediaJobView>.Success(await MapJobAsync(retried, cancellationToken));
    }

    private async Task<MediaRequestResult> ResolveAsync(
        Guid actorUserId,
        MediaVariant variant,
        MediaDisposition disposition,
        MediaRequestSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Derivative.Status == DerivativeStatus.Ready && snapshot.Derivative.RelativePath is not null)
        {
            var ownerToken = Guid.NewGuid();
            var lease = await media.TryAcquireDeliveryAsync(
                snapshot.Derivative.Id,
                ownerToken,
                clock.UtcNow,
                TimeSpan.FromSeconds(options.DeliveryLeaseSeconds),
                cancellationToken);
            if (lease is null || !await authorization.AllowsAsync(
                    actorUserId, snapshot.Source.Id, ShareOperation.View, cancellationToken))
            {
                if (lease is not null)
                {
                    await media.ReleaseLeaseAsync(
                        lease.DerivativeId, DerivativeLeaseType.Delivery, ownerToken, clock.UtcNow, cancellationToken);
                }

                return new MediaRequestResult(MediaRequestStatus.Failed, null, snapshot.Job?.Id, FileErrorCodes.FileNotFound);
            }

            try
            {
                var stream = await derivativeStore.OpenReadAsync(
                    RelativeStoragePath.Create(snapshot.Derivative.RelativePath), cancellationToken);
                if (!stream.CanSeek || stream.Length != snapshot.Derivative.Size)
                {
                    await stream.DisposeAsync();
                    throw new IOException("The derivative size does not match its catalog record.");
                }

                await media.RecordDeliveryAccessAsync(
                    snapshot.Derivative.Id, clock.UtcNow, TimeSpan.FromHours(options.CacheTtlHours), cancellationToken);
                return new MediaRequestResult(
                    MediaRequestStatus.Ready,
                    new MediaContent(
                        snapshot.Derivative.Id,
                        RelativeStoragePath.Create(snapshot.Derivative.RelativePath),
                        snapshot.Derivative.Size,
                        MediaContractRules.ContentType(snapshot.Derivative.DerivativeType),
                        MediaContractRules.DownloadName(snapshot.Source.Name, variant),
                        disposition,
                        ownerToken,
                        stream),
                    snapshot.Job?.Id,
                    null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await media.ReleaseLeaseAsync(
                    snapshot.Derivative.Id, DerivativeLeaseType.Delivery, ownerToken, clock.UtcNow, cancellationToken);
                return new MediaRequestResult(MediaRequestStatus.Failed, null, snapshot.Job?.Id, FileErrorCodes.StorageUnavailable);
            }
        }

        if (snapshot.Derivative.Status is DerivativeStatus.Failed or DerivativeStatus.BlockedSourceMissing)
        {
            return new MediaRequestResult(
                MediaRequestStatus.Failed, null, snapshot.Job?.Id, MediaErrorCodes.GenerationFailed);
        }

        return new MediaRequestResult(MediaRequestStatus.Generating, null, snapshot.Job?.Id, null);
    }

    private async Task<MediaFailure?> ValidateSourceAsync(
        Guid actorUserId,
        FileEntry? source,
        MediaVariant variant,
        CancellationToken cancellationToken)
    {
        if (source is null || source.EntryType != FileEntryType.File ||
            !await authorization.AllowsAsync(actorUserId, source.Id, ShareOperation.View, cancellationToken))
        {
            return new MediaFailure(FileErrorCodes.FileNotFound, MediaFailureKind.NotFound);
        }

        if (source.Status != FileEntryStatus.Active || source.ParentId is null ||
            await files.HasIncompleteOperationAsync(source.OwnerUserId, source.Id, source.RelativePath, cancellationToken))
        {
            return new MediaFailure(MediaErrorCodes.SourceNotActive, MediaFailureKind.Conflict);
        }

        if (!MediaContractRules.Supports(source.MimeType, variant))
        {
            return new MediaFailure(MediaErrorCodes.VariantUnsupported, MediaFailureKind.BadRequest);
        }

        return await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) == StorageStatus.Available
            ? null
            : new MediaFailure(FileErrorCodes.StorageUnavailable, MediaFailureKind.StorageUnavailable);
    }

    private async Task<MediaRequestSnapshot?> AuthorizedJobAsync(
        Guid actorUserId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || jobId == Guid.Empty)
        {
            return null;
        }

        var snapshot = await media.FindByJobAsync(jobId, cancellationToken);
        return snapshot is not null && await authorization.AllowsAsync(
            actorUserId, snapshot.Source.Id, ShareOperation.View, cancellationToken)
            ? snapshot
            : null;
    }

    private async Task<MediaJobView> MapJobAsync(
        MediaRequestSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var job = snapshot.Job!;
        var status = job.Status switch
        {
            MediaJobStatus.Completed when snapshot.Derivative.Status == DerivativeStatus.Ready => "READY",
            MediaJobStatus.Failed => "FAILED",
            MediaJobStatus.Cancelled => "CANCELLED",
            _ => "GENERATING",
        };
        var position = job.Status == MediaJobStatus.Queued
            ? await queue.GetQueuePositionAsync(job.Id, clock.UtcNow, cancellationToken)
            : null;
        return new MediaJobView(
            job.Id,
            status,
            job.ProgressPercent,
            job.ProcessedDurationMs,
            job.TotalDurationMs,
            position,
            status == "READY" ? 0 : 2,
            status == "READY"
                ? $"/api/v1/files/{snapshot.Source.Id}/content?variant={MediaContractRules.PublishedVariant(snapshot.Derivative.DerivativeType)}"
                : null);
    }

    private static bool CanRetry(string? errorCode) => errorCode is
        FileErrorCodes.StorageUnavailable or
        MediaErrorCodes.ToolUnavailable or
        MediaErrorCodes.WorkerUnavailable or
        MediaErrorCodes.CompletionUnknown or
        "MEDIA_WORKER_STOPPED" or
        "MEDIA_WORKER_STALE";
}
