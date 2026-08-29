using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Media;

public sealed class MediaJobRunner(
    IMediaJobQueue queue,
    IMediaRepository media,
    IMediaGenerator generator,
    IMediaHeartbeat heartbeat,
    IFileStore fileStore,
    IDerivativeStore derivativeStore,
    ISystemClock clock,
    MediaRuntimeOptions options)
{
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var workerToken = Guid.NewGuid();
        var job = await queue.TryAcquireNextAsync(workerToken, clock.UtcNow, cancellationToken);
        if (job is null)
        {
            return false;
        }

        var leaseOwnerToken = Guid.NewGuid();
        var context = await media.TryAcquireGenerationAsync(
            job.Id,
            workerToken,
            leaseOwnerToken,
            clock.UtcNow,
            TimeSpan.FromSeconds(options.GenerationLeaseSeconds),
            cancellationToken);
        if (context is null)
        {
            await queue.TryFailAsync(
                job.Id, workerToken, MediaErrorCodes.SourceNotActive, retryable: false, clock.UtcNow, cancellationToken);
            return true;
        }

        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatAsync(context, workerToken, heartbeatStop.Token);
        PublishedDerivative? published = null;
        var completed = false;
        try
        {
            await using var source = await fileStore.OpenReadAsync(context.SourcePath, cancellationToken);
            await using var generated = await generator.GenerateAsync(context, source, cancellationToken);
            var temporary = await derivativeStore.WriteTemporaryAsync(
                context.JobId, context.Attempt, generated.Content, generated.Size, cancellationToken);
            published = await derivativeStore.PublishAsync(
                temporary,
                context.OwnerUserId,
                context.SourceFileId,
                context.SourceVersion,
                context.ProfileVersion,
                context.DerivativeType,
                generated.Extension,
                generated.Size,
                cancellationToken);
            DateTimeOffset? expiresAt = context.DerivativeType is DerivativeType.Thumbnail or DerivativeType.PdfThumbnail
                ? null
                : clock.UtcNow.AddHours(options.CacheTtlHours);
            completed = await media.CompleteGenerationAsync(
                context.JobId, workerToken, leaseOwnerToken, published, clock.UtcNow, expiresAt, cancellationToken);
            if (!completed)
            {
                await derivativeStore.DeleteIfExistsAsync(published.Path, cancellationToken);
                published = null;
                await ReleaseAndFailAsync(
                    context,
                    workerToken,
                    MediaErrorCodes.SourceNotActive,
                    retryable: false,
                    CancellationToken.None);
            }
        }
        catch (MediaGenerationException exception)
        {
            await ReleaseAndFailAsync(context, workerToken, exception.ErrorCode, exception.Retryable, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ReleaseAndFailAsync(context, workerToken, FileErrorCodes.StorageUnavailable, retryable: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAndFailAsync(
                context, workerToken, "MEDIA_WORKER_STOPPED", retryable: true, CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            await ReleaseAndFailAsync(
                context, workerToken, MediaErrorCodes.GenerationFailed, retryable: false, CancellationToken.None);
        }
        finally
        {
            if (!completed && published is not null)
            {
                await derivativeStore.DeleteIfExistsAsync(published.Path, CancellationToken.None);
            }

            heartbeatStop.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        return true;
    }

    private async Task RunHeartbeatAsync(
        MediaGenerationContext context,
        Guid workerToken,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.JobHeartbeatSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!await heartbeat.PulseAsync(
                    context.JobId,
                    workerToken,
                    context.DerivativeId,
                    context.LeaseOwnerToken,
                    clock.UtcNow,
                    TimeSpan.FromSeconds(options.GenerationLeaseSeconds),
                    cancellationToken))
            {
                return;
            }
        }
    }

    private async Task ReleaseAndFailAsync(
        MediaGenerationContext context,
        Guid workerToken,
        string errorCode,
        bool retryable,
        CancellationToken cancellationToken)
    {
        await media.ReleaseLeaseAsync(
            context.DerivativeId,
            DerivativeLeaseType.Generation,
            context.LeaseOwnerToken,
            clock.UtcNow,
            cancellationToken);
        await queue.TryFailAsync(
            context.JobId, workerToken, errorCode, retryable, clock.UtcNow, cancellationToken);
    }
}
