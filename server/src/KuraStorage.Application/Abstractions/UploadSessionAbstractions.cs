using KuraStorage.Application.Transfers;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Transfers;

namespace KuraStorage.Application.Abstractions;

public interface IUploadSessionRepository
{
    Task<UploadSession?> FindByOwnerAndKeyAsync(
        Guid ownerUserId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<UploadSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<bool> IsDeviceActiveAsync(Guid ownerUserId, Guid deviceId, CancellationToken cancellationToken);

    Task<int> CountActiveForUserAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task<int> CountActiveForDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadSession>> ListCleanupCandidatesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadSession>> ListRecoveryCandidatesAsync(
        int take,
        CancellationToken cancellationToken);

    void Add(UploadSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUploadSessionStore
{
    Task<TemporaryUploadState> InspectAsync(
        RelativeStoragePath path,
        CancellationToken cancellationToken);

    Task<StoredChunk> WriteChunkAsync(
        RelativeStoragePath path,
        long offset,
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken);

    Task<StoredChunk> ReadAndHashAsync(
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken);

    Task TruncateAsync(
        RelativeStoragePath path,
        long length,
        CancellationToken cancellationToken);

    Task<string> ComputeSha256Async(
        RelativeStoragePath path,
        CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(
        RelativeStoragePath path,
        CancellationToken cancellationToken);
}

public sealed class UploadChunkLimiter(UploadSessionOptions options)
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("KuraStorage.Transfers");
    private static long concurrentWrites;
    private readonly SemaphoreSlim semaphore = new(options.MaximumConcurrentChunkWrites);

    static UploadChunkLimiter()
    {
        Meter.CreateObservableGauge(
            "kurastorage.upload.concurrent_chunk_writes",
            () => Interlocked.Read(ref concurrentWrites));
    }

    public async Task<IAsyncDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await semaphore.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        Interlocked.Increment(ref concurrentWrites);
        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Interlocked.Decrement(ref concurrentWrites);
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
