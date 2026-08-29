using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Media;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class ExternalMediaGeneratorTests
{
    [Theory]
    [InlineData(DerivativeType.Thumbnail, "512", "75")]
    [InlineData(DerivativeType.ImageLow, "1280", "70")]
    [InlineData(DerivativeType.ImageMedium, "2560", "82")]
    public async Task GenerateImage_UsesBoundedVipsProfileAndValidatesWebp(
        DerivativeType type,
        string dimension,
        string quality)
    {
        await using var fixture = new GeneratorFixture();
        fixture.Runner.Handler = async request =>
        {
            if (request.BinaryPath == "/usr/bin/vips")
            {
                Assert.Equal("thumbnail", request.Arguments[0]);
                Assert.EndsWith("source.input", request.Arguments[1]);
                Assert.Equal(dimension, request.Arguments[3]);
                Assert.DoesNotContain("--no-rotate", request.Arguments);
                Assert.EndsWith($"[Q={quality},strip]", request.Arguments[2]);
                await File.WriteAllBytesAsync(request.Arguments[2].Split('[')[0], [1, 2, 3]);
                return new MediaProcessResult(0, string.Empty, string.Empty);
            }

            Assert.Equal("/usr/bin/ffprobe", request.BinaryPath);
            return new MediaProcessResult(
                0, $"{{\"streams\":[{{\"codec_name\":\"webp\",\"width\":{dimension},\"height\":1}}]}}", string.Empty);
        };

        await using var generated = await fixture.GenerateAsync(type, "image/jpeg");

        Assert.Equal(3, generated.Size);
        Assert.Equal("webp", generated.Extension);
        Assert.Equal([1, 2, 3], await ReadAllAsync(generated.Content));
        Assert.Equal(2, fixture.Runner.Requests.Count);
    }

    [Fact]
    public async Task GeneratePdf_RasterizesOnlyFirstPageBeforeVips()
    {
        await using var fixture = new GeneratorFixture();
        fixture.Runner.Handler = async request =>
        {
            if (request.BinaryPath == "/usr/bin/pdftoppm")
            {
                Assert.Equal(["-f", "1", "-l", "1", "-singlefile", "-png", "-scale-to", "4096"],
                    request.Arguments.Take(8));
                await File.WriteAllBytesAsync(request.Arguments[^1] + ".png", [9]);
            }
            else if (request.BinaryPath == "/usr/bin/vips")
            {
                await File.WriteAllBytesAsync(request.Arguments[2].Split('[')[0], [4, 5]);
            }

            return request.BinaryPath == "/usr/bin/ffprobe"
                ? new MediaProcessResult(0, "{\"streams\":[{\"codec_name\":\"webp\",\"width\":512,\"height\":400}]}", string.Empty)
                : new MediaProcessResult(0, string.Empty, string.Empty);
        };

        await using var generated = await fixture.GenerateAsync(DerivativeType.PdfThumbnail, "application/pdf");

        Assert.Equal(2, generated.Size);
        Assert.Equal(["/usr/bin/pdftoppm", "/usr/bin/vips", "/usr/bin/ffprobe"],
            fixture.Runner.Requests.Select(request => request.BinaryPath));
    }

    [Fact]
    public async Task GenerateVideoThumbnail_UsesTenPercentCappedAtTenSecondsAndFallsBackToZero()
    {
        await using var fixture = new GeneratorFixture();
        var ffmpegCalls = 0;
        fixture.Runner.Handler = async request =>
        {
            if (request.BinaryPath == "/usr/bin/ffprobe" && request.Arguments.Contains("format=duration"))
            {
                return new MediaProcessResult(0, "180", string.Empty);
            }

            if (request.BinaryPath == "/usr/bin/ffmpeg")
            {
                ffmpegCalls++;
                Assert.Equal(ffmpegCalls == 1 ? "10" : "0", request.Arguments[3]);
                Assert.Contains("libwebp", request.Arguments);
                if (ffmpegCalls == 2)
                {
                    await File.WriteAllBytesAsync(request.Arguments[^1], [7, 8]);
                }

                return new MediaProcessResult(ffmpegCalls == 1 ? 1 : 0, string.Empty, string.Empty);
            }

            return new MediaProcessResult(
                0, "{\"streams\":[{\"codec_name\":\"webp\",\"width\":512,\"height\":288}]}", string.Empty);
        };

        await using var generated = await fixture.GenerateAsync(DerivativeType.Thumbnail, "video/mp4");

        Assert.Equal(2, ffmpegCalls);
        Assert.Equal(2, generated.Size);
    }

    [Fact]
    public async Task GenerateImage_WhenProbeReportsOversizedOutput_FailsPermanentlyAndCleansWorkspace()
    {
        await using var fixture = new GeneratorFixture();
        fixture.Runner.Handler = async request =>
        {
            if (request.BinaryPath == "/usr/bin/vips")
            {
                await File.WriteAllBytesAsync(request.Arguments[2].Split('[')[0], [1]);
                return new MediaProcessResult(0, string.Empty, string.Empty);
            }

            return new MediaProcessResult(
                0, "{\"streams\":[{\"codec_name\":\"webp\",\"width\":513,\"height\":1}]}", string.Empty);
        };

        var exception = await Assert.ThrowsAsync<MediaGenerationException>(() =>
            fixture.GenerateAsync(DerivativeType.Thumbnail, "image/jpeg"));

        Assert.Equal(MediaErrorCodes.GenerationFailed, exception.ErrorCode);
        Assert.False(exception.Retryable);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.Root, "derivative-temp")));
    }

    [Fact]
    public async Task Generate_WhenToolTimesOut_MapsRetryableFailureAndCleansWorkspace()
    {
        await using var fixture = new GeneratorFixture();
        fixture.Runner.Handler = _ => throw new MediaProcessTimeoutException();

        var exception = await Assert.ThrowsAsync<MediaGenerationException>(() =>
            fixture.GenerateAsync(DerivativeType.Thumbnail, "image/jpeg"));

        Assert.Equal(MediaErrorCodes.GenerationFailed, exception.ErrorCode);
        Assert.True(exception.Retryable);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.Root, "derivative-temp")));
    }

    [Fact]
    public async Task GeneratePdf_WhenRasterizerRejectsInput_FailsPermanentlyAndCleansWorkspace()
    {
        await using var fixture = new GeneratorFixture();
        fixture.Runner.Handler = request => Task.FromResult(
            request.BinaryPath == "/usr/bin/pdftoppm"
                ? new MediaProcessResult(1, string.Empty, "encrypted or malformed PDF")
                : new MediaProcessResult(0, string.Empty, string.Empty));

        var exception = await Assert.ThrowsAsync<MediaGenerationException>(() =>
            fixture.GenerateAsync(DerivativeType.PdfThumbnail, "application/pdf"));

        Assert.Equal(MediaErrorCodes.GenerationFailed, exception.ErrorCode);
        Assert.False(exception.Retryable);
        Assert.Single(fixture.Runner.Requests);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.Root, "derivative-temp")));
    }

    [Fact]
    public async Task Generate_WhenSourceSizeDoesNotMatch_FailsBeforeToolAndCleansWorkspace()
    {
        await using var fixture = new GeneratorFixture();

        var exception = await Assert.ThrowsAsync<MediaGenerationException>(() =>
            fixture.GenerateAsync(DerivativeType.Thumbnail, "image/jpeg", expectedSourceSize: 5));

        Assert.Equal(MediaErrorCodes.GenerationFailed, exception.ErrorCode);
        Assert.False(exception.Retryable);
        Assert.Empty(fixture.Runner.Requests);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.Root, "derivative-temp")));
    }

    [Fact]
    public async Task Generate_WhenCancelled_PropagatesCancellationAndCleansWorkspace()
    {
        await using var fixture = new GeneratorFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Runner.Handler = _ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<MediaProcessResult>(cancellation.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.GenerateAsync(
            DerivativeType.Thumbnail,
            "image/jpeg",
            cancellationToken: cancellation.Token));

        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.Root, "derivative-temp")));
    }

    [Fact]
    public async Task Generate_RejectsUnsupportedMimeBeforeStartingTool()
    {
        await using var fixture = new GeneratorFixture();

        var exception = await Assert.ThrowsAsync<MediaGenerationException>(() =>
            fixture.GenerateAsync(DerivativeType.Thumbnail, "application/octet-stream"));

        Assert.Equal(MediaErrorCodes.VariantUnsupported, exception.ErrorCode);
        Assert.False(exception.Retryable);
        Assert.Empty(fixture.Runner.Requests);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private sealed class GeneratorFixture : IAsyncDisposable
    {
        private readonly byte[] source = [10, 20, 30, 40];

        public GeneratorFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"kurastorage-generator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "derivative-temp"));
            Runner = new FakeRunner();
            Generator = new ExternalMediaGenerator(
                Options.Create(new StorageOptions { RootPath = Root, StorageId = "generator-test", MinimumFreeBytes = 1 }),
                Options.Create(new MediaOptions()),
                new AvailableStorageGuard(),
                Runner);
        }

        public string Root { get; }

        public FakeRunner Runner { get; }

        private ExternalMediaGenerator Generator { get; }

        public Task<GeneratedMedia> GenerateAsync(
            DerivativeType type,
            string mimeType,
            long? expectedSourceSize = null,
            CancellationToken cancellationToken = default)
        {
            var context = new MediaGenerationContext(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                RelativeStoragePath.Create("users/source.input"), expectedSourceSize ?? source.Length,
                mimeType, type, 1, 1, Guid.NewGuid());
            return Generator.GenerateAsync(context, new MemoryStream(source, writable: false), cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRunner : IMediaProcessRunner
    {
        public List<MediaProcessRequest> Requests { get; } = [];

        public Func<MediaProcessRequest, Task<MediaProcessResult>> Handler { get; set; } =
            _ => throw new InvalidOperationException("A test handler is required.");

        public Task<MediaProcessResult> RunAsync(
            MediaProcessRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Handler(request);
        }
    }

    private sealed class AvailableStorageGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }
}

internal static class MediaArgumentExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
