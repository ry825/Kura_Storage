using System.Globalization;
using System.Text.Json;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Media;

public sealed class ExternalMediaGenerator(
    IOptions<StorageOptions> storageOptions,
    IOptions<MediaOptions> mediaOptions,
    IStorageGuard storageGuard,
    IMediaProcessRunner runner) : IMediaGenerator
{
    private const long MaximumGeneratedBytes = 268_435_456;
    private const long MaximumVideoBytes = 53_687_091_200;
    private readonly string storageRoot = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(storageOptions.Value.RootPath));
    private readonly MediaOptions options = mediaOptions.Value;

    public async Task<GeneratedMedia> GenerateAsync(
        MediaGenerationContext context,
        Stream source,
        CancellationToken cancellationToken,
        Func<MediaGenerationProgress, CancellationToken, ValueTask>? progress = null)
    {
        ValidateContext(context);
        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            throw new MediaGenerationException(FileErrorCodes.StorageUnavailable, retryable: true);
        }

        var workspace = ResolveWorkspace(context.JobId, context.Attempt);
        var input = Path.Combine(workspace, "source.input");
        var videoOutput = context.DerivativeType is DerivativeType.VideoLow or DerivativeType.VideoMedium;
        var extension = videoOutput ? "mp4" : "webp";
        var output = Path.Combine(workspace, $"generated.{extension}");
        var handedOff = false;
        try
        {
            EnsureNoLinks(workspace);
            Directory.CreateDirectory(workspace);
            EnsureNoLinks(workspace);
            await CopyExactAsync(source, input, context.SourceSize, cancellationToken);
            switch (context.DerivativeType)
            {
                case DerivativeType.Thumbnail when IsVideo(context.SourceMimeType):
                    await GenerateVideoThumbnailAsync(input, output, workspace, cancellationToken);
                    break;
                case DerivativeType.PdfThumbnail:
                    await GeneratePdfThumbnailAsync(input, output, workspace, cancellationToken);
                    break;
                case DerivativeType.Thumbnail:
                case DerivativeType.ImageLow:
                case DerivativeType.ImageMedium:
                    await GenerateImageAsync(context.DerivativeType, input, output, workspace, cancellationToken);
                    break;
                case DerivativeType.VideoLow:
                case DerivativeType.VideoMedium:
                    await GenerateVideoAsync(
                        context.DerivativeType, input, output, workspace, progress, cancellationToken);
                    break;
                default:
                    throw new MediaGenerationException(MediaErrorCodes.VariantUnsupported, retryable: false);
            }

            if (!videoOutput)
            {
                await ValidateWebpAsync(output, MaximumDimension(context.DerivativeType), workspace, cancellationToken);
            }

            var info = new FileInfo(output);
            var maximumSize = videoOutput ? MaximumVideoBytes : MaximumGeneratedBytes;
            if (!info.Exists || info.Length <= 0 || info.Length > maximumSize)
            {
                throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
            }

            Stream content = new FileStream(
                output,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            handedOff = true;
            return new GeneratedMedia(content, info.Length, extension, () => CleanupWorkspaceAsync(workspace));
        }
        catch (MediaGenerationException)
        {
            throw;
        }
        catch (MediaProcessTimeoutException exception)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: true, exception);
        }
        catch (IOException exception)
        {
            throw new MediaGenerationException(MediaErrorCodes.ToolUnavailable, retryable: true, exception);
        }
        finally
        {
            if (!handedOff)
            {
                await CleanupWorkspaceAsync(workspace);
            }
        }
    }

    private async Task GenerateVideoAsync(
        DerivativeType type,
        string input,
        string output,
        string workspace,
        Func<MediaGenerationProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        var source = await ProbeVideoAsync(input, workspace, cancellationToken);
        var profile = type == DerivativeType.VideoLow
            ? new VideoProfile(1280, 720, 1500, 96)
            : new VideoProfile(1920, 1080, 4000, 128);
        var parser = new FfmpegProgressParser(source.DurationMilliseconds, progress);
        var scale =
            $"scale=w='if(gte(iw,ih),min({profile.LandscapeWidth},iw),min({profile.LandscapeHeight},iw))':" +
            $"h='if(gte(iw,ih),min({profile.LandscapeHeight},ih),min({profile.LandscapeWidth},ih))':" +
            "force_original_aspect_ratio=decrease:force_divisible_by=2";
        var result = await runner.RunAsync(
            new MediaProcessRequest(
                options.FfmpegPath,
                ["-nostdin", "-v", "error", "-i", input,
                    "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1",
                    "-vf", scale,
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-b:v", $"{profile.VideoKbps}k",
                    "-maxrate", $"{profile.VideoKbps}k", "-bufsize", $"{profile.VideoKbps * 2}k",
                    "-preset", "medium", "-fpsmax", "30",
                    "-c:a", "aac", "-b:a", $"{profile.AudioKbps}k",
                    "-movflags", "+faststart", "-max_muxing_queue_size", "1024",
                    "-progress", "pipe:1", "-stats_period", "1", "-nostats",
                    "-f", "mp4", output],
                workspace,
                TimeSpan.FromHours(2),
                StandardOutputLineHandler: parser.AcceptAsync),
            cancellationToken);
        EnsureSuccess(result);
        var generated = await ProbeVideoAsync(output, workspace, cancellationToken);
        ValidateTranscodedVideo(source, generated, profile, new FileInfo(output).Length);
    }

    private async Task<VideoProbe> ProbeVideoAsync(
        string path,
        string workspace,
        CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new MediaProcessRequest(
                options.FfprobePath,
                ["-v", "error", "-show_entries",
                    "format=format_name,duration,size:stream=codec_type,codec_name,width,height,avg_frame_rate",
                    "-of", "json", path],
                workspace,
                TimeSpan.FromMinutes(2)),
            cancellationToken);
        EnsureSuccess(result);
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var format = document.RootElement.GetProperty("format");
            var formatName = format.GetProperty("format_name").GetString() ?? string.Empty;
            var durationText = format.GetProperty("duration").GetString();
            var sizeText = format.TryGetProperty("size", out var sizeValue) ? sizeValue.GetString() : null;
            if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
                !double.IsFinite(duration) || duration <= 0)
            {
                throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
            }

            var streams = format.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("streams", out var streamValues)
                ? streamValues.EnumerateArray().Select(ParseStream).ToArray()
                : [];
            if (streams.Count(stream => stream.Type == "video") != 1 ||
                streams.Any(stream => stream.Width is > 65_535 || stream.Height is > 65_535))
            {
                throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
            }

            _ = long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var reportedSize);
            return new VideoProbe(
                formatName,
                checked((long)Math.Round(duration * 1000, MidpointRounding.AwayFromZero)),
                reportedSize,
                streams);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FormatException)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false, exception);
        }
    }

    private static VideoStreamProbe ParseStream(JsonElement stream)
    {
        var type = stream.GetProperty("codec_type").GetString() ?? string.Empty;
        var codec = stream.GetProperty("codec_name").GetString() ?? string.Empty;
        var width = stream.TryGetProperty("width", out var widthValue) ? widthValue.GetInt32() : 0;
        var height = stream.TryGetProperty("height", out var heightValue) ? heightValue.GetInt32() : 0;
        var rate = stream.TryGetProperty("avg_frame_rate", out var rateValue)
            ? ParseFrameRate(rateValue.GetString())
            : 0;
        return new VideoStreamProbe(type, codec, width, height, rate);
    }

    private static double ParseFrameRate(string? value)
    {
        var parts = value?.Split('/', 2);
        if (parts is not { Length: 2 } ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator <= 0)
        {
            return 0;
        }

        return numerator / denominator;
    }

    private static void ValidateTranscodedVideo(
        VideoProbe source,
        VideoProbe generated,
        VideoProfile profile,
        long physicalSize)
    {
        var sourceVideo = source.Streams.Single(stream => stream.Type == "video");
        var video = generated.Streams.Single(stream => stream.Type == "video");
        var landscape = video.Width >= video.Height;
        var maximumWidth = landscape ? profile.LandscapeWidth : profile.LandscapeHeight;
        var maximumHeight = landscape ? profile.LandscapeHeight : profile.LandscapeWidth;
        var durationTolerance = Math.Max(2000, source.DurationMilliseconds / 50);
        if (!generated.FormatName.Split(',').Any(value => value is "mp4" or "mov") ||
            video.Codec != "h264" || video.Width <= 0 || video.Height <= 0 ||
            video.Width > maximumWidth || video.Height > maximumHeight || video.FrameRate is <= 0 or > 30.01 ||
            video.Width > sourceVideo.Width || video.Height > sourceVideo.Height ||
            generated.Streams.Count(stream => stream.Type == "audio") > 1 ||
            generated.Streams.Any(stream => stream.Type == "audio" && stream.Codec != "aac") ||
            generated.Streams.Any(stream => stream.Type is not ("video" or "audio")) ||
            Math.Abs(generated.DurationMilliseconds - source.DurationMilliseconds) > durationTolerance ||
            generated.ReportedSize != physicalSize || physicalSize is <= 0 or > MaximumVideoBytes)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
        }
    }

    private async Task GenerateImageAsync(
        DerivativeType type,
        string input,
        string output,
        string workspace,
        CancellationToken cancellationToken)
    {
        var dimension = MaximumDimension(type);
        var quality = type switch
        {
            DerivativeType.ImageLow => 70,
            DerivativeType.ImageMedium => 82,
            _ => options.ThumbnailWebpQuality,
        };
        var result = await runner.RunAsync(
            new MediaProcessRequest(
                options.VipsPath,
                ["thumbnail", input,
                    $"{output}[Q={quality.ToString(CultureInfo.InvariantCulture)},strip]",
                    dimension.ToString(CultureInfo.InvariantCulture), "--size", "down"],
                workspace,
                TimeSpan.FromMinutes(5)),
            cancellationToken);
        EnsureSuccess(result);
    }

    private async Task GeneratePdfThumbnailAsync(
        string input,
        string output,
        string workspace,
        CancellationToken cancellationToken)
    {
        var prefix = Path.Combine(workspace, "page");
        var raster = await runner.RunAsync(
            new MediaProcessRequest(
                options.PdftoppmPath,
                ["-f", "1", "-l", "1", "-singlefile", "-png", "-scale-to", "4096", input, prefix],
                workspace,
                TimeSpan.FromMinutes(2)),
            cancellationToken);
        EnsureSuccess(raster);
        await GenerateImageAsync(DerivativeType.Thumbnail, prefix + ".png", output, workspace, cancellationToken);
    }

    private async Task GenerateVideoThumbnailAsync(
        string input,
        string output,
        string workspace,
        CancellationToken cancellationToken)
    {
        var probe = await runner.RunAsync(
            new MediaProcessRequest(
                options.FfprobePath,
                ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", input],
                workspace,
                TimeSpan.FromMinutes(1)),
            cancellationToken);
        EnsureSuccess(probe);
        if (!double.TryParse(probe.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
            !double.IsFinite(duration) || duration <= 0)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
        }

        var timestamp = Math.Min(duration * 0.1, 10d);
        var result = await ExtractFrameAsync(input, output, workspace, timestamp, cancellationToken);
        if (result.ExitCode != 0)
        {
            result = await ExtractFrameAsync(input, output, workspace, 0, cancellationToken);
        }

        EnsureSuccess(result);
    }

    private Task<MediaProcessResult> ExtractFrameAsync(
        string input,
        string output,
        string workspace,
        double timestamp,
        CancellationToken cancellationToken)
    {
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        return runner.RunAsync(
            new MediaProcessRequest(
                options.FfmpegPath,
                ["-v", "error", "-ss", timestamp.ToString("0.###", CultureInfo.InvariantCulture), "-i", input,
                    "-frames:v", "1", "-vf",
                    $"scale=w='min({options.ThumbnailMaxDimension},iw)':h='min({options.ThumbnailMaxDimension},ih)':force_original_aspect_ratio=decrease",
                    "-c:v", "libwebp", "-quality", options.ThumbnailWebpQuality.ToString(CultureInfo.InvariantCulture), output],
                workspace,
                TimeSpan.FromMinutes(2)),
            cancellationToken);
    }

    private async Task ValidateWebpAsync(
        string output,
        int maximumDimension,
        string workspace,
        CancellationToken cancellationToken)
    {
        var probe = await runner.RunAsync(
            new MediaProcessRequest(
                options.FfprobePath,
                ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height",
                    "-of", "json", output],
                workspace,
                TimeSpan.FromMinutes(1)),
            cancellationToken);
        EnsureSuccess(probe);
        try
        {
            using var document = JsonDocument.Parse(probe.StandardOutput);
            var stream = document.RootElement.GetProperty("streams")[0];
            var codec = stream.GetProperty("codec_name").GetString();
            var width = stream.GetProperty("width").GetInt32();
            var height = stream.GetProperty("height").GetInt32();
            if (codec != "webp" || width <= 0 || height <= 0 || width > maximumDimension || height > maximumDimension)
            {
                throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false, exception);
        }
    }

    private string ResolveWorkspace(Guid jobId, int attempt)
    {
        var relative = RelativeStoragePath.Create(
            $"{options.TemporaryRoot}/{jobId:N}/{attempt}-work").Value.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(storageRoot, relative));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new MediaGenerationException(FileErrorCodes.StorageUnavailable, retryable: false);
        }

        return path;
    }

    private void EnsureNoLinks(string path)
    {
        var current = new DirectoryInfo(path);
        while (current.FullName.StartsWith(storageRoot, StringComparison.Ordinal))
        {
            if (current.Exists && current.LinkTarget is not null)
            {
                throw new MediaGenerationException(FileErrorCodes.StorageUnavailable, retryable: false);
            }

            if (current.FullName == storageRoot || current.Parent is null)
            {
                break;
            }

            current = current.Parent;
        }
    }

    private static async Task CopyExactAsync(
        Stream source,
        string path,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (total < expectedSize)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, expectedSize - total)), cancellationToken);
            if (read == 0)
            {
                throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        if (await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
        }

        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private void ValidateContext(MediaGenerationContext context)
    {
        var valid = context.DerivativeType switch
        {
            DerivativeType.Thumbnail => MediaContractRules.Supports(context.SourceMimeType, MediaVariant.Thumbnail),
            DerivativeType.PdfThumbnail => context.SourceMimeType == "application/pdf",
            DerivativeType.ImageLow => MediaContractRules.Supports(context.SourceMimeType, MediaVariant.ImageLow),
            DerivativeType.ImageMedium => MediaContractRules.Supports(context.SourceMimeType, MediaVariant.ImageMedium),
            DerivativeType.VideoLow => MediaContractRules.Supports(context.SourceMimeType, MediaVariant.VideoLow),
            DerivativeType.VideoMedium => MediaContractRules.Supports(context.SourceMimeType, MediaVariant.VideoMedium),
            _ => false,
        };
        if (!valid || context.SourceSize <= 0 || context.Attempt < 1)
        {
            throw new MediaGenerationException(MediaErrorCodes.VariantUnsupported, retryable: false);
        }
    }

    private int MaximumDimension(DerivativeType type) => type switch
    {
        DerivativeType.ImageLow => 1280,
        DerivativeType.ImageMedium => 2560,
        _ => options.ThumbnailMaxDimension,
    };

    private static bool IsVideo(string? mimeType) => mimeType is
        "video/mp4" or "video/quicktime" or "video/webm" or "video/x-matroska" or "video/3gpp";

    private static ValueTask CleanupWorkspaceAsync(string workspace)
    {
        try
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }

            var jobDirectory = Directory.GetParent(workspace)?.FullName;
            if (jobDirectory is not null && Directory.Exists(jobDirectory) &&
                !Directory.EnumerateFileSystemEntries(jobDirectory).Any())
            {
                Directory.Delete(jobDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later recovery pass can remove an abandoned bounded job workspace.
        }

        return ValueTask.CompletedTask;
    }

    private static void EnsureSuccess(MediaProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
        }
    }

    private sealed record VideoProfile(int LandscapeWidth, int LandscapeHeight, int VideoKbps, int AudioKbps);

    private sealed record VideoProbe(
        string FormatName,
        long DurationMilliseconds,
        long ReportedSize,
        IReadOnlyList<VideoStreamProbe> Streams);

    private sealed record VideoStreamProbe(
        string Type,
        string Codec,
        int Width,
        int Height,
        double FrameRate);

    private sealed class FfmpegProgressParser(
        long totalDurationMilliseconds,
        Func<MediaGenerationProgress, CancellationToken, ValueTask>? progress)
    {
        private long? processedDurationMilliseconds;

        public async ValueTask AcceptAsync(string line, CancellationToken cancellationToken)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            if (key is "out_time_us" or "out_time_ms" &&
                long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var microseconds) &&
                microseconds >= 0)
            {
                processedDurationMilliseconds = microseconds / 1000;
            }
            else if (key == "out_time" && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var processed) &&
                processed >= TimeSpan.Zero)
            {
                processedDurationMilliseconds = checked((long)processed.TotalMilliseconds);
            }

            if (key != "progress" || progress is null)
            {
                return;
            }

            long? boundedProcessed = processedDurationMilliseconds is null
                ? null
                : Math.Min(processedDurationMilliseconds.Value, totalDurationMilliseconds);
            int? percent = boundedProcessed is null
                ? null
                : Math.Min(99, (int)(boundedProcessed.Value * 100 / totalDurationMilliseconds));
            await progress(
                new MediaGenerationProgress(percent, boundedProcessed, totalDurationMilliseconds),
                cancellationToken);
        }
    }
}
