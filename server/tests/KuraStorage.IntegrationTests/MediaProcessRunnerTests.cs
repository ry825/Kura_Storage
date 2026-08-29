using KuraStorage.Application.Abstractions;
using KuraStorage.Infrastructure.Media;

namespace KuraStorage.IntegrationTests;

public sealed class MediaProcessRunnerTests
{
    [Fact]
    public async Task Run_UsesArgumentListAndClearedAllowListedEnvironment()
    {
        var runner = new MediaProcessRunner();
        var result = await runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/env",
                [],
                Path.GetTempPath(),
                TimeSpan.FromSeconds(5),
                new Dictionary<string, string> { ["TZ"] = "UTC" }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("LANG=C.UTF-8", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("LC_ALL=C.UTF-8", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("TZ=UTC", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("HOME=", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_DoesNotInterpretShellMetacharacters()
    {
        var runner = new MediaProcessRunner();
        var marker = Path.Combine(Path.GetTempPath(), $"kurastorage-process-{Guid.NewGuid():N}");
        var payload = $"value;touch {marker}";

        var result = await runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/printf",
                ["%s", payload],
                Path.GetTempPath(),
                TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        Assert.Equal(payload, result.StandardOutput);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Run_KillsTimedOutProcessTree()
    {
        var runner = new MediaProcessRunner();

        await Assert.ThrowsAsync<MediaProcessTimeoutException>(() => runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/sleep",
                ["30"],
                Path.GetTempPath(),
                TimeSpan.FromMilliseconds(100)),
            CancellationToken.None));
    }

    [Fact]
    public async Task Run_WhenCancelled_KillsProcessTreeAndPropagatesCancellation()
    {
        var runner = new MediaProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/sleep",
                ["30"],
                Path.GetTempPath(),
                TimeSpan.FromMinutes(1)),
            cancellation.Token));
    }

    [Fact]
    public async Task Run_RejectsExcessDiagnosticOutput()
    {
        var runner = new MediaProcessRunner();

        await Assert.ThrowsAsync<MediaProcessOutputLimitException>(() => runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/head",
                ["-c", (MediaProcessRunner.MaximumDiagnosticBytes + 1).ToString(), "/dev/zero"],
                Path.GetTempPath(),
                TimeSpan.FromSeconds(5)),
            CancellationToken.None));
    }

    [Fact]
    public async Task Run_RejectsRelativeBinaryAndUnapprovedEnvironment()
    {
        var runner = new MediaProcessRunner();

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(
            new MediaProcessRequest("vips", [], Path.GetTempPath(), TimeSpan.FromSeconds(1)),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/env",
                [],
                Path.GetTempPath(),
                TimeSpan.FromSeconds(1),
                new Dictionary<string, string> { ["PATH"] = "/tmp" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task Run_StreamsBoundedStandardOutputLinesWithoutShellParsing()
    {
        var runner = new MediaProcessRunner();
        var lines = new List<string>();

        var result = await runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/printf",
                ["%s", "out_time_us=5000000\r\nprogress=continue\n"],
                Path.GetTempPath(),
                TimeSpan.FromSeconds(5),
                StandardOutputLineHandler: (line, _) =>
                {
                    lines.Add(line);
                    return ValueTask.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["out_time_us=5000000", "progress=continue"], lines);
        Assert.Contains("progress=continue", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_LongStreamingProgressRemainsMemoryBoundedWithoutRejectingCompletedProcess()
    {
        var runner = new MediaProcessRunner();
        var lineCount = 0;

        var result = await runner.RunAsync(
            new MediaProcessRequest(
                "/usr/bin/seq",
                ["1", "300000"],
                Path.GetTempPath(),
                TimeSpan.FromSeconds(10),
                StandardOutputLineHandler: (_, _) =>
                {
                    lineCount++;
                    return ValueTask.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(300000, lineCount);
        Assert.InRange(result.StandardOutput.Length, 1, MediaProcessRunner.MaximumDiagnosticBytes);
    }
}
