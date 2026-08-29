using System.Net.Http.Headers;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Media;

namespace KuraStorage.Api;

public sealed class LeasedMediaResult(MediaContent content) : IResult
{
    private const int BufferSize = 64 * 1024;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var media = httpContext.RequestServices.GetRequiredService<IMediaRepository>();
        var clock = httpContext.RequestServices.GetRequiredService<ISystemClock>();
        var options = httpContext.RequestServices.GetRequiredService<MediaRuntimeOptions>();
        try
        {
            if (!TryResolveRange(httpContext.Request.Headers.Range, content.Size, out var start, out var length))
            {
                httpContext.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                httpContext.Response.Headers.ContentRange = $"bytes */{content.Size}";
                await httpContext.Response.WriteAsJsonAsync(
                    new ErrorResponse(
                        "RANGE_NOT_SATISFIABLE",
                        "The request could not be completed.",
                        httpContext.TraceIdentifier,
                        new { }),
                    httpContext.RequestAborted);
                return;
            }

            var rangeRequested = !string.IsNullOrWhiteSpace(httpContext.Request.Headers.Range);
            httpContext.Response.StatusCode = rangeRequested
                ? StatusCodes.Status206PartialContent
                : StatusCodes.Status200OK;
            httpContext.Response.ContentType = content.ContentType;
            httpContext.Response.ContentLength = length;
            httpContext.Response.Headers.AcceptRanges = "bytes";
            httpContext.Response.Headers.ContentDisposition = MediaContentDisposition.Format(
                content.Disposition, content.DownloadName);
            if (httpContext.Response.StatusCode == StatusCodes.Status206PartialContent)
            {
                httpContext.Response.Headers.ContentRange = $"bytes {start}-{start + length - 1}/{content.Size}";
            }

            content.Stream.Position = start;
            var buffer = new byte[BufferSize];
            var remaining = length;
            var renewAt = clock.UtcNow.AddSeconds(options.DeliveryLeaseRenewalSeconds);
            while (remaining > 0)
            {
                var read = await content.Stream.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    httpContext.RequestAborted);
                if (read == 0)
                {
                    throw new IOException("The derivative ended before the catalogued size.");
                }

                await httpContext.Response.Body.WriteAsync(
                    buffer.AsMemory(0, read), httpContext.RequestAborted);
                remaining -= read;

                if (clock.UtcNow >= renewAt)
                {
                    if (!await media.RenewLeaseAsync(
                            content.DerivativeId,
                            DerivativeLeaseType.Delivery,
                            content.LeaseOwnerToken,
                            clock.UtcNow,
                            TimeSpan.FromSeconds(options.DeliveryLeaseSeconds),
                            httpContext.RequestAborted))
                    {
                        throw new IOException("The derivative delivery lease was lost.");
                    }

                    renewAt = clock.UtcNow.AddSeconds(options.DeliveryLeaseRenewalSeconds);
                }
            }
        }
        finally
        {
            try
            {
                await content.Stream.DisposeAsync();
            }
            finally
            {
                await media.ReleaseLeaseAsync(
                    content.DerivativeId,
                    DerivativeLeaseType.Delivery,
                    content.LeaseOwnerToken,
                    clock.UtcNow,
                    CancellationToken.None);
            }
        }
    }

    private static bool TryResolveRange(
        string? header,
        long totalLength,
        out long start,
        out long length)
    {
        start = 0;
        length = totalLength;
        if (string.IsNullOrWhiteSpace(header))
        {
            return totalLength >= 0;
        }

        if (totalLength <= 0 || !RangeHeaderValue.TryParse(header, out var parsed) ||
            !string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            parsed.Ranges.Count != 1)
        {
            return false;
        }

        var range = parsed.Ranges.Single();
        if (range.From is null)
        {
            if (range.To is null or <= 0)
            {
                return false;
            }

            length = Math.Min(range.To.Value, totalLength);
            start = totalLength - length;
            return true;
        }

        start = range.From.Value;
        if (start >= totalLength || range.To is not null && range.To.Value < start)
        {
            return false;
        }

        var end = Math.Min(range.To ?? totalLength - 1, totalLength - 1);
        length = end - start + 1;
        return true;
    }
}

internal static class MediaContentDisposition
{
    public static string Format(MediaDisposition disposition, string downloadName)
    {
        var type = disposition == MediaDisposition.Attachment ? "attachment" : "inline";
        var asciiName = new string(downloadName.Select(character =>
            character is >= (char)0x20 and <= (char)0x7e && character is not '"' and not '\\'
                ? character
                : '_').ToArray());
        var encodedName = Uri.EscapeDataString(downloadName);
        return $"{type}; filename=\"{asciiName}\"; filename*=UTF-8''{encodedName}";
    }
}
