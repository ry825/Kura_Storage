using System.Collections.Concurrent;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Indexing;

namespace KuraStorage.Worker.Workers;

public sealed class IndexRescanSignal : IIndexRescanSignal, IDisposable
{
    private readonly ConcurrentQueue<IndexScanTrigger> triggers = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly object readyLock = new();
    private TaskCompletionSource ready = NewReadySource();

    public void Request(IndexScanTrigger trigger)
    {
        triggers.Enqueue(trigger);
        if (signal.CurrentCount == 0)
        {
            signal.Release();
        }
    }

    public async Task<IndexScanTrigger?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await signal.WaitAsync(timeout, cancellationToken))
        {
            return null;
        }

        var selected = IndexScanTrigger.Scheduled;
        while (triggers.TryDequeue(out var trigger))
        {
            if (trigger == IndexScanTrigger.Overflow)
            {
                selected = trigger;
            }
        }

        return selected;
    }

    public void SetReady(bool isReady)
    {
        lock (readyLock)
        {
            if (isReady)
            {
                ready.TrySetResult();
            }
            else if (ready.Task.IsCompleted)
            {
                ready = NewReadySource();
            }
        }
    }

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        lock (readyLock)
        {
            return ready.Task.WaitAsync(cancellationToken);
        }
    }

    public void Dispose() => signal.Dispose();

    private static TaskCompletionSource NewReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
