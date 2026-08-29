using System.Diagnostics;
using System.Text;
using KuraStorage.Application.Abstractions;

namespace KuraStorage.Infrastructure.Media;

public sealed class MediaProcessRunner : IMediaProcessRunner
{
    public const int MaximumDiagnosticBytes = 1024 * 1024;
    private static readonly HashSet<string> AllowedEnvironment = new(StringComparer.Ordinal)
    {
        "LANG",
        "LC_ALL",
        "TZ",
    };

    public async Task<MediaProcessResult> RunAsync(
        MediaProcessRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var startInfo = new ProcessStartInfo
        {
            FileName = request.BinaryPath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        startInfo.Environment.Clear();
        startInfo.Environment["LANG"] = "C.UTF-8";
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        foreach (var pair in request.Environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new IOException("The media process could not be started.");
            }
        }
        catch (Exception exception) when (exception is global::System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new IOException("The configured media tool is unavailable.", exception);
        }

        process.StandardInput.Close();
        var stdout = ReadBoundedAsync(
            process.StandardOutput.BaseStream, request.StandardOutputLineHandler, cancellationToken);
        var stderr = ReadBoundedAsync(process.StandardError.BaseStream, null, cancellationToken);
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var exit = process.WaitForExitAsync(linked.Token);
            var pending = new List<Task> { exit, stdout, stderr };
            while (!exit.IsCompleted)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }

            await exit;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            await DrainAsync(stdout, stderr);
            throw new MediaProcessTimeoutException();
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await DrainAsync(stdout, stderr);
            throw;
        }
        catch
        {
            Kill(process);
            await DrainAsync(stdout, stderr);
            throw;
        }

        var standardOutput = await stdout;
        var standardError = await stderr;
        return new MediaProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static void Validate(MediaProcessRequest request)
    {
        if (!Path.IsPathFullyQualified(request.BinaryPath) ||
            !Path.IsPathFullyQualified(request.WorkingDirectory) ||
            !Directory.Exists(request.WorkingDirectory) ||
            request.Timeout <= TimeSpan.Zero ||
            request.Timeout > TimeSpan.FromHours(2) ||
            request.Arguments.Any(argument => argument.IndexOf('\0') >= 0) ||
            request.Environment?.Any(pair => !AllowedEnvironment.Contains(pair.Key) || pair.Value.IndexOf('\0') >= 0) == true)
        {
            throw new ArgumentException("The media process request is invalid.", nameof(request));
        }
    }

    private static async Task<string> ReadBoundedAsync(
        Stream stream,
        Func<string, CancellationToken, ValueTask>? lineHandler,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var content = new MemoryStream();
        using var line = new MemoryStream();
        var exceeded = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            var remaining = MaximumDiagnosticBytes - checked((int)content.Length);
            if (remaining > 0)
            {
                await content.WriteAsync(buffer.AsMemory(0, Math.Min(read, remaining)), cancellationToken);
            }

            if (lineHandler is null)
            {
                exceeded |= read > remaining;
            }
            if (lineHandler is not null)
            {
                for (var index = 0; index < read; index++)
                {
                    if (buffer[index] == (byte)'\n')
                    {
                        await EmitLineAsync(line, lineHandler, cancellationToken);
                    }
                    else
                    {
                        if (line.Length >= MaximumDiagnosticBytes)
                        {
                            throw new MediaProcessOutputLimitException();
                        }

                        line.WriteByte(buffer[index]);
                    }
                }
            }
        }

        if (exceeded)
        {
            throw new MediaProcessOutputLimitException();
        }

        if (lineHandler is not null && line.Length > 0)
        {
            await EmitLineAsync(line, lineHandler, cancellationToken);
        }

        return Encoding.UTF8.GetString(content.ToArray());
    }

    private static async ValueTask EmitLineAsync(
        MemoryStream content,
        Func<string, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
    {
        var bytes = content.ToArray();
        var length = bytes.Length > 0 && bytes[^1] == (byte)'\r' ? bytes.Length - 1 : bytes.Length;
        content.SetLength(0);
        await handler(Encoding.UTF8.GetString(bytes, 0, length), cancellationToken);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DrainAsync(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr);
        }
        catch (Exception)
        {
        }
    }
}
