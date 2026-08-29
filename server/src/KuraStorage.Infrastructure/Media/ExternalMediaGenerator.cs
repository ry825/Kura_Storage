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
    private readonly string storageRoot = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(storageOptions.Value.RootPath));
    private readonly MediaOptions options = mediaOptions.Value;

    public async Task<GeneratedMedia> GenerateAsync(
        MediaGenerationContext context,
        Stream source,
        CancellationToken cancellationToken)
    {
        ValidateContext(context);
        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            throw new MediaGenerationException(FileErrorCodes.StorageUnavailable, retryable: true);
        }

        var workspace = ResolveWorkspace(context.JobId, context.Attempt);
        var input = Path.Combine(workspace, "source.input");
        var output = Path.Combine(workspace, "generated.webp");
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
                default:
                    throw new MediaGenerationException(MediaErrorCodes.VariantUnsupported, retryable: false);
            }

            await ValidateWebpAsync(output, MaximumDimension(context.DerivativeType), workspace, cancellationToken);
            var info = new FileInfo(output);
            if (!info.Exists || info.Length is <= 0 or > MaximumGeneratedBytes)
            {
                throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
            }

            var bytes = await File.ReadAllBytesAsync(output, cancellationToken);
            return new GeneratedMedia(new MemoryStream(bytes, writable: false), bytes.LongLength, "webp");
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
                // A later maintenance pass can remove an abandoned bounded job workspace.
            }
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
        "video/mp4" or "video/quicktime" or "video/webm" or "video/x-matroska";

    private static void EnsureSuccess(MediaProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new MediaGenerationException(MediaErrorCodes.GenerationFailed, retryable: false);
        }
    }
}
