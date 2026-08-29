using KuraStorage.Api;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class LeasedMediaResultTests
{
    [Theory]
    [InlineData(null, 200, "0123456789", null)]
    [InlineData("bytes=2-5", 206, "2345", "bytes 2-5/10")]
    [InlineData("bytes=-3", 206, "789", "bytes 7-9/10")]
    [InlineData("bytes=0-9", 206, "0123456789", "bytes 0-9/10")]
    public async Task ExecuteAsync_StreamsFullOrSingleRangeAndReleasesLease(
        string? range,
        int expectedStatus,
        string expectedBody,
        string? expectedContentRange)
    {
        var repository = new RecordingMediaRepository();
        var context = CreateContext(repository, new IncrementingClock());
        if (range is not null)
        {
            context.Request.Headers.Range = range;
        }

        await new LeasedMediaResult(CreateContent()).ExecuteAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedBody, System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.Equal(expectedContentRange, context.Response.Headers.ContentRange.ToString().NullIfEmpty());
        Assert.Equal("bytes", context.Response.Headers.AcceptRanges);
        Assert.Contains("inline", context.Response.Headers.ContentDisposition.ToString());
        Assert.Contains("filename*=UTF-8''", context.Response.Headers.ContentDisposition.ToString());
        Assert.True(repository.ReleaseCalled);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidOrMultipleRange_Returns416AndReleasesLease()
    {
        var repository = new RecordingMediaRepository();
        var context = CreateContext(repository, new IncrementingClock());
        context.Request.Headers.Range = "bytes=0-1,4-5";

        await new LeasedMediaResult(CreateContent()).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status416RangeNotSatisfiable, context.Response.StatusCode);
        Assert.Equal("bytes */10", context.Response.Headers.ContentRange);
        Assert.True(repository.ReleaseCalled);
    }

    [Fact]
    public async Task ExecuteAsync_LongTransferRenewsDeliveryLease()
    {
        var repository = new RecordingMediaRepository();
        var context = CreateContext(repository, new IncrementingClock());
        var bytes = new byte[200_000];
        var content = CreateContent(new MemoryStream(bytes, writable: false), bytes.Length);

        await new LeasedMediaResult(content).ExecuteAsync(context);

        Assert.True(repository.RenewCount >= 2);
        Assert.True(repository.ReleaseCalled);
    }

    [Fact]
    public async Task ExecuteAsync_ClientCancellationStillReleasesDeliveryLease()
    {
        var repository = new RecordingMediaRepository();
        var context = CreateContext(repository, new IncrementingClock());
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;
        var stream = new CancellingStream(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LeasedMediaResult(CreateContent(stream, 100_000)).ExecuteAsync(context));

        Assert.True(repository.ReleaseCalled);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStreamDisposalFails_StillReleasesDeliveryLease()
    {
        var repository = new RecordingMediaRepository();
        var context = CreateContext(repository, new IncrementingClock());

        await Assert.ThrowsAsync<IOException>(() =>
            new LeasedMediaResult(CreateContent(new ThrowingDisposeStream(), 10)).ExecuteAsync(context));

        Assert.True(repository.ReleaseCalled);
    }

    private static DefaultHttpContext CreateContext(IMediaRepository repository, ISystemClock clock)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(repository)
            .AddSingleton(clock)
            .AddSingleton(new MediaRuntimeOptions())
            .BuildServiceProvider();
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };
    }

    private static MediaContent CreateContent(Stream? stream = null, long size = 10) => new(
        Guid.NewGuid(),
        RelativeStoragePath.Create("derivatives/test.webp"),
        size,
        "image/webp",
        "写真_thumbnail.webp",
        MediaDisposition.Inline,
        Guid.NewGuid(),
        stream ?? new MemoryStream("0123456789"u8.ToArray(), writable: false));

    private sealed class IncrementingClock : ISystemClock
    {
        private DateTimeOffset current = DateTimeOffset.Parse("2026-08-29T00:00:00Z");

        public DateTimeOffset UtcNow
        {
            get
            {
                current = current.AddSeconds(31);
                return current;
            }
        }
    }

    private sealed class RecordingMediaRepository : IMediaRepository
    {
        public bool ReleaseCalled { get; private set; }

        public int RenewCount { get; private set; }

        public Task<bool> RenewLeaseAsync(
            Guid derivativeId,
            DerivativeLeaseType leaseType,
            Guid ownerToken,
            DateTimeOffset now,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            RenewCount++;
            return Task.FromResult(true);
        }

        public Task<bool> ReleaseLeaseAsync(
            Guid derivativeId,
            DerivativeLeaseType leaseType,
            Guid ownerToken,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ReleaseCalled = true;
            return Task.FromResult(true);
        }

        public Task<MediaRequestSnapshot> GetOrCreateRequestAsync(
            FileEntry source,
            DerivativeType derivativeType,
            int profileVersion,
            Guid requestedByUserId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MediaRequestSnapshot?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaGenerationContext?> TryAcquireGenerationAsync(
            Guid jobId,
            Guid workerToken,
            Guid leaseOwnerToken,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CompleteGenerationAsync(
            Guid jobId,
            Guid workerToken,
            Guid leaseOwnerToken,
            PublishedDerivative published,
            DateTimeOffset now,
            DateTimeOffset? expiresAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DerivativeLeaseHandle?> TryAcquireDeliveryAsync(
            Guid derivativeId,
            Guid ownerToken,
            DateTimeOffset now,
            TimeSpan duration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RecordDeliveryAccessAsync(
            Guid derivativeId,
            DateTimeOffset now,
            TimeSpan cacheTtl,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream(new byte[100_000])
    {
        private bool cancelled;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!cancelled)
            {
                cancelled = true;
                cancellation.Cancel();
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class ThrowingDisposeStream() : MemoryStream("0123456789"u8.ToArray(), writable: false)
    {
        public override ValueTask DisposeAsync() => ValueTask.FromException(new IOException("Injected dispose failure."));
    }
}

internal static class HeaderTestExtensions
{
    public static string? NullIfEmpty(this string value) => string.IsNullOrEmpty(value) ? null : value;
}
