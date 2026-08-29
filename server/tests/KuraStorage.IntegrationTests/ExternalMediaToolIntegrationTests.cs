using System.Globalization;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Configuration;
using KuraStorage.Infrastructure.Media;
using Microsoft.Extensions.Options;

namespace KuraStorage.IntegrationTests;

public sealed class ExternalMediaToolIntegrationTests
{
    [Fact]
    public async Task RealTools_GenerateImagePdfAndVideoThumbnailsAndRejectCorruptInput()
    {
        if (Environment.GetEnvironmentVariable("KURASTORAGE_RUN_MEDIA_TOOL_TESTS") != "1")
        {
            return;
        }

        await using var fixture = new RealToolFixture();
        var image = Encoding.ASCII.GetBytes(
            "P3\n2 2\n255\n255 0 0  0 255 0\n0 0 255  255 255 255\n");
        await AssertWebpAsync(await fixture.GenerateAsync(image, "image/png", DerivativeType.Thumbnail));
        await AssertWebpAsync(await fixture.GenerateAsync(BuildPdf(), "application/pdf", DerivativeType.PdfThumbnail));

        var videoPath = Path.Combine(fixture.Root, "source.mp4");
        var video = await fixture.Runner.RunAsync(
            new MediaProcessRequest(
                fixture.FfmpegPath,
                ["-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=1", "-t", "2",
                    "-pix_fmt", "yuv420p", "-c:v", "libx264", videoPath],
                fixture.Root,
                TimeSpan.FromMinutes(1)),
            CancellationToken.None);
        Assert.Equal(0, video.ExitCode);
        await AssertWebpAsync(await fixture.GenerateAsync(
            await File.ReadAllBytesAsync(videoPath), "video/mp4", DerivativeType.Thumbnail));

        var failure = await Assert.ThrowsAsync<MediaGenerationException>(() => fixture.GenerateAsync(
            "not-an-image"u8.ToArray(), "image/jpeg", DerivativeType.Thumbnail));
        Assert.Equal(MediaErrorCodes.GenerationFailed, failure.ErrorCode);
        Assert.False(failure.Retryable);
    }

    [Theory]
    [InlineData(DerivativeType.VideoLow, 320, 180, 3, false)]
    [InlineData(DerivativeType.VideoMedium, 180, 320, 12, true)]
    public async Task RealTools_TranscodeFixedMp4ProfilesWithoutUpscaling(
        DerivativeType type,
        int width,
        int height,
        int durationSeconds,
        bool multipleAudioStreams)
    {
        if (Environment.GetEnvironmentVariable("KURASTORAGE_RUN_MEDIA_TOOL_TESTS") != "1")
        {
            return;
        }

        await using var fixture = new RealToolFixture();
        var sourcePath = Path.Combine(fixture.Root, $"source-{type}.mkv");
        var arguments = new List<string>
        {
            "-nostdin", "-v", "error", "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r=60",
        };
        if (multipleAudioStreams)
        {
            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440", "-f", "lavfi", "-i", "sine=frequency=880"]);
        }

        arguments.AddRange(["-t", durationSeconds.ToString(CultureInfo.InvariantCulture), "-map", "0:v:0"]);
        if (multipleAudioStreams)
        {
            arguments.AddRange(["-map", "1:a:0", "-map", "2:a:0"]);
        }

        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", sourcePath]);
        var sourceResult = await fixture.Runner.RunAsync(
            new MediaProcessRequest(fixture.FfmpegPath, arguments, fixture.Root, TimeSpan.FromMinutes(1)),
            CancellationToken.None);
        Assert.Equal(0, sourceResult.ExitCode);

        var progress = new List<MediaGenerationProgress>();
        await using var generated = await fixture.GenerateAsync(
            await File.ReadAllBytesAsync(sourcePath),
            "video/x-matroska",
            type,
            (value, _) =>
            {
                progress.Add(value);
                return ValueTask.CompletedTask;
            });

        Assert.Equal("mp4", generated.Extension);
        var header = new byte[12];
        Assert.Equal(header.Length, await generated.Content.ReadAsync(header));
        Assert.Equal("ftyp", Encoding.ASCII.GetString(header, 4, 4));
        Assert.NotEmpty(progress);
        Assert.All(progress.Where(value => value.Percent is not null), value => Assert.InRange(value.Percent!.Value, 0, 99));
    }

    private static async Task AssertWebpAsync(GeneratedMedia generated)
    {
        await using (generated)
        {
            Assert.Equal("webp", generated.Extension);
            Assert.InRange(generated.Size, 1, 268_435_456);
            var header = new byte[12];
            Assert.Equal(header.Length, await generated.Content.ReadAsync(header));
            Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
            Assert.Equal("WEBP", Encoding.ASCII.GetString(header, 8, 4));
        }
    }

    private static byte[] BuildPdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> /Contents 4 0 R >>",
            "<< /Length 29 >>\nstream\n0 0 1 rg 0 0 200 200 re f\nendstream",
        };
        using var output = new MemoryStream();
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = output.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();

        void Write(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            output.Write(bytes);
        }
    }

    private sealed class RealToolFixture : IAsyncDisposable
    {
        private readonly ExternalMediaGenerator generator;

        public RealToolFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"kurastorage-real-media-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Runner = new MediaProcessRunner();
            var mediaOptions = new MediaOptions
            {
                FfmpegPath = Environment.GetEnvironmentVariable("KURASTORAGE_TEST_FFMPEG_PATH") ?? "/usr/bin/ffmpeg",
                FfprobePath = Environment.GetEnvironmentVariable("KURASTORAGE_TEST_FFPROBE_PATH") ?? "/usr/bin/ffprobe",
            };
            FfmpegPath = mediaOptions.FfmpegPath;
            generator = new ExternalMediaGenerator(
                Options.Create(new StorageOptions { RootPath = Root, StorageId = "real-media-test", MinimumFreeBytes = 1 }),
                Options.Create(mediaOptions),
                new AvailableStorageGuard(),
                Runner);
        }

        public string Root { get; }

        public string FfmpegPath { get; }

        public MediaProcessRunner Runner { get; }

        public Task<GeneratedMedia> GenerateAsync(
            byte[] source,
            string mimeType,
            DerivativeType type,
            Func<MediaGenerationProgress, CancellationToken, ValueTask>? progress = null) =>
            generator.GenerateAsync(
                new MediaGenerationContext(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                    RelativeStoragePath.Create("users/source"), source.LongLength, mimeType, type, 1, 1, Guid.NewGuid()),
                new MemoryStream(source, writable: false),
                CancellationToken.None,
                progress);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AvailableStorageGuard : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(StorageStatus.Available);
    }
}
