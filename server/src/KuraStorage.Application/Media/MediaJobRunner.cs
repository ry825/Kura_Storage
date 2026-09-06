using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Media;
using System.Diagnostics;

namespace KuraStorage.Application.Media;

public sealed class MediaJobRunner(
    IMediaJobQueue queue,
    IMediaRepository media,
    IMediaGenerator generator,
    IMediaHeartbeat heartbeat,
    IFileStore fileStore,
    IDerivativeStore derivativeStore,
    ISystemClock clock,
    MediaRuntimeOptions options) : IMediaJobRunner
{
    public Task<bool> RunNextAsync(CancellationToken cancellationToken) =>
        RunNextAsync(MediaJobClaimScope.Any, 1, cancellationToken);

    public async Task<bool> RunNextAsync(
        MediaJobClaimScope claimScope,
        int maximumConcurrency,
        CancellationToken cancellationToken)
    {
        var workerToken = Guid.NewGuid();
        var job = await queue.TryAcquireNextAsync(
            workerToken,
            clock.UtcNow,
            claimScope,
            maximumConcurrency,
            cancellationToken);
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

        MediaGenerationMetrics.JobStarted();
        var metricStarted = Stopwatch.GetTimestamp();
        var metricResult = "failed";
        var metricReason = "unexpected";
        using var operationStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ownershipLost = 0;
        var heartbeatFaulted = 0;
        var heartbeatTask = RunHeartbeatAsync(
            context,
            workerToken,
            () =>
            {
                Interlocked.Exchange(ref ownershipLost, 1);
                operationStop.Cancel();
            },
            () =>
            {
                Interlocked.Exchange(ref heartbeatFaulted, 1);
                operationStop.Cancel();
            },
            heartbeatStop.Token);
        using var progressGate = new SemaphoreSlim(1, 1);
        var nextProgressAt = DateTimeOffset.MinValue;
        PublishedDerivative? published = null;
        var completed = false;
        var completionStateUnknown = false;
        try
        {
            var extension = context.DerivativeType is DerivativeType.VideoLow or DerivativeType.VideoMedium
                ? "mp4"
                : "webp";
            published = await derivativeStore.FindPublishedAsync(context, extension, operationStop.Token);
            if (published is null)
            {
                await using var source = await fileStore.OpenReadAsync(context.SourcePath, operationStop.Token);
                await using var generated = await generator.GenerateAsync(
                    context,
                    source,
                    operationStop.Token,
                    async (progress, progressCancellationToken) =>
                    {
                        await progressGate.WaitAsync(progressCancellationToken);
                        try
                        {
                            var now = clock.UtcNow;
                            if (now < nextProgressAt)
                            {
                                return;
                            }

                            if (!await heartbeat.PulseProgressAsync(
                                    context.JobId,
                                    workerToken,
                                    context.DerivativeId,
                                    context.LeaseOwnerToken,
                                    now,
                                    TimeSpan.FromSeconds(options.GenerationLeaseSeconds),
                                    progress,
                                    progressCancellationToken))
                            {
                                throw new MediaGenerationOwnershipLostException();
                            }

                            nextProgressAt = now.AddSeconds(5);
                        }
                        finally
                        {
                            progressGate.Release();
                        }
                    });
                var temporary = await derivativeStore.WriteTemporaryAsync(
                    context.JobId, context.Attempt, generated.Content, generated.Size, operationStop.Token);
                published = await derivativeStore.PublishAsync(
                    temporary,
                    context.OwnerUserId,
                    context.SourceFileId,
                    context.SourceVersion,
                    context.ProfileVersion,
                    context.DerivativeType,
                    generated.Extension,
                    generated.Size,
                    operationStop.Token);
            }

            DateTimeOffset? expiresAt = context.DerivativeType is DerivativeType.Thumbnail or DerivativeType.PdfThumbnail
                ? null
                : clock.UtcNow.AddHours(options.CacheTtlHours);
            try
            {
                completed = await media.CompleteGenerationAsync(
                    context.JobId, workerToken, leaseOwnerToken, published, clock.UtcNow, expiresAt, operationStop.Token);
            }
            catch (Exception exception)
            {
                completionStateUnknown = true;
                throw new MediaCompletionStateUnknownException(exception);
            }

            if (!completed)
            {
                metricReason = "source_changed";
                await derivativeStore.DeleteIfExistsAsync(published.Path, cancellationToken);
                published = null;
                await ReleaseAndFailAsync(
                    context,
                    workerToken,
                    MediaErrorCodes.SourceNotActive,
                    retryable: false,
                    CancellationToken.None);
            }
            else
            {
                metricResult = "succeeded";
                metricReason = "none";
            }
        }
        catch (MediaGenerationException exception)
        {
            metricResult = exception.Retryable ? "retry" : "failed";
            metricReason = MetricReason(exception.ErrorCode);
            await ReleaseAndFailAsync(context, workerToken, exception.ErrorCode, exception.Retryable, cancellationToken);
        }
        catch (MediaCompletionStateUnknownException)
        {
            metricResult = "retry";
            metricReason = "completion_unknown";
            await ReleaseAndFailAsync(
                context, workerToken, MediaErrorCodes.CompletionUnknown, retryable: true, CancellationToken.None);
        }
        catch (MediaGenerationOwnershipLostException)
        {
            metricResult = "ownership_lost";
            metricReason = "ownership_lost";
            // A newer worker or recovery pass owns the durable state. This worker must stop without updating it.
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && Volatile.Read(ref ownershipLost) == 1)
        {
            metricResult = "ownership_lost";
            metricReason = "ownership_lost";
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && Volatile.Read(ref heartbeatFaulted) == 1)
        {
            metricResult = "retry";
            metricReason = "worker_unavailable";
            await ReleaseAndFailAsync(
                context, workerToken, MediaErrorCodes.WorkerUnavailable, retryable: true, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            metricResult = "retry";
            metricReason = "storage_unavailable";
            await ReleaseAndFailAsync(context, workerToken, FileErrorCodes.StorageUnavailable, retryable: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            metricResult = "stopped";
            metricReason = "worker_stopped";
            await ReleaseAndFailAsync(
                context, workerToken, "MEDIA_WORKER_STOPPED", retryable: true, CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            metricResult = "failed";
            metricReason = "unexpected";
            await ReleaseAndFailAsync(
                context, workerToken, MediaErrorCodes.GenerationFailed, retryable: false, CancellationToken.None);
        }
        finally
        {
            if (!completed && !completionStateUnknown && published is not null)
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
            catch when (Volatile.Read(ref heartbeatFaulted) == 1)
            {
                // The operation path already converted this heartbeat failure into a retryable job failure.
            }

            MediaGenerationMetrics.JobFinished(
                metricResult,
                metricReason,
                context.DerivativeType,
                Stopwatch.GetElapsedTime(metricStarted),
                completed ? published?.Size : null);
        }

        return true;
    }

    private static string MetricReason(string errorCode) => errorCode switch
    {
        MediaErrorCodes.ToolUnavailable => "tool_unavailable",
        MediaErrorCodes.GenerationFailed => "generation_failed",
        MediaErrorCodes.VariantUnsupported => "variant_unsupported",
        FileErrorCodes.StorageUnavailable => "storage_unavailable",
        _ => "media_failure",
    };

    private async Task RunHeartbeatAsync(
        MediaGenerationContext context,
        Guid workerToken,
        Action ownershipLost,
        Action heartbeatFaulted,
        CancellationToken cancellationToken)
    {
        try
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
                    ownershipLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            heartbeatFaulted();
            throw;
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
