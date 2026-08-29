using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class MediaApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task ReadyThumbnail_AuthorizesStreamsRangesAndUsesCurrentUnicodeName()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(
            $"media-owner-{Guid.NewGuid():N}", "media-owner-password");
        var strangerAuth = await fixture.CreateAuthenticatedClientAsync(
            $"media-stranger-{Guid.NewGuid():N}", "media-stranger-password");
        using var owner = ownerAuth.Client;
        using var stranger = strangerAuth.Client;
        var (fileId, derivativeId) = await SeedReadyAsync(owner, "写真 renamed.jpg", "0123456789"u8.ToArray());

        using (var full = await owner.GetAsync($"/api/v1/files/{fileId}/content?variant=thumbnail"))
        {
            Assert.Equal(HttpStatusCode.OK, full.StatusCode);
            Assert.Equal("image/webp", full.Content.Headers.ContentType!.MediaType);
            Assert.Equal("bytes", full.Headers.AcceptRanges.Single());
            Assert.Equal("0123456789", await full.Content.ReadAsStringAsync());
            Assert.Contains("filename*=UTF-8''", full.Content.Headers.ContentDisposition!.ToString());
            Assert.Contains("%E5%86%99%E7%9C%9F", full.Content.Headers.ContentDisposition.ToString());
        }

        using var rangeRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/files/{fileId}/content?variant=thumbnail&disposition=attachment");
        rangeRequest.Headers.Range = new RangeHeaderValue(2, 5);
        using (var range = await owner.SendAsync(rangeRequest))
        {
            Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
            Assert.Equal("bytes 2-5/10", range.Content.Headers.ContentRange!.ToString());
            Assert.Equal("2345", await range.Content.ReadAsStringAsync());
            Assert.Equal("attachment", range.Content.Headers.ContentDisposition!.DispositionType);
        }

        using var invalidRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/files/{fileId}/content?variant=thumbnail");
        invalidRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-1,4-5");
        using (var invalid = await owner.SendAsync(invalidRequest))
        {
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalid.StatusCode);
            Assert.Equal("bytes */10", invalid.Content.Headers.ContentRange!.ToString());
        }

        using (var hidden = await stranger.GetAsync($"/api/v1/files/{fileId}/content?variant=thumbnail"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        using (var unsupported = await owner.GetAsync($"/api/v1/files/{fileId}/content?variant=video-low"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        }

        using (var invalidDisposition = await owner.GetAsync(
                   $"/api/v1/files/{fileId}/content?variant=thumbnail&disposition=unknown"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidDisposition.StatusCode);
        }

        await using (var corruptScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var corruptDatabase = corruptScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var relative = await corruptDatabase.FileDerivatives
                .Where(item => item.Id == derivativeId)
                .Select(item => item.RelativePath)
                .SingleAsync();
            await File.WriteAllBytesAsync(
                Path.Combine(fixture.StorageRootPath, relative!.Replace('/', Path.DirectorySeparatorChar)), [1]);
        }

        using (var sizeMismatch = await owner.GetAsync($"/api/v1/files/{fileId}/content?variant=thumbnail"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, sizeMismatch.StatusCode);
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Empty(await database.DerivativeLeases.Where(item => item.DerivativeId == derivativeId).ToListAsync());
    }

    [Fact]
    public async Task PendingThumbnail_ConcurrentRequestsShareJobAndRetryOnlyTransientFailure()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"media-pending-{Guid.NewGuid():N}", "media-pending-password");
        using var client = authenticated.Client;
        var fileId = await SeedSourceAsync(client, "pending.jpg");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.GetAsync($"/api/v1/files/{fileId}/content?variant=thumbnail")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var jobIds = new List<Guid>();
        foreach (var response in responses)
        {
            Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter!.Delta);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var jobId = json.RootElement.GetProperty("jobId").GetGuid();
            jobIds.Add(jobId);
            Assert.Equal($"/api/v1/media-jobs/{jobId}", json.RootElement.GetProperty("jobStatusUrl").GetString());
            response.Dispose();
        }

        var originalJobId = Assert.Single(jobIds.Distinct());
        var strangerAuth = await fixture.CreateAuthenticatedClientAsync(
            $"media-job-stranger-{Guid.NewGuid():N}", "media-job-stranger-password");
        using (var stranger = strangerAuth.Client)
        using (var hiddenJob = await stranger.GetAsync($"/api/v1/media-jobs/{originalJobId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenJob.StatusCode);
        }

        using (var emptyJob = await client.GetAsync($"/api/v1/media-jobs/{Guid.Empty}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, emptyJob.StatusCode);
        }

        using (var status = await client.GetAsync($"/api/v1/media-jobs/{originalJobId}"))
        {
            status.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await status.Content.ReadAsStreamAsync());
            Assert.Equal("GENERATING", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("progressPercent").ValueKind);
        }

        await FailJobAsync(originalJobId, MediaErrorCodes.GenerationFailed);
        using (var failedContent = await client.GetAsync(
                   $"/api/v1/files/{fileId}/content?variant=thumbnail"))
        {
            Assert.Equal(HttpStatusCode.Conflict, failedContent.StatusCode);
        }

        using (var permanent = await client.PostAsync($"/api/v1/media-jobs/{originalJobId}/retry", null))
        {
            Assert.Equal(HttpStatusCode.Conflict, permanent.StatusCode);
        }

        var transientFileId = await SeedSourceAsync(client, "transient.jpg");
        using var accepted = await client.GetAsync($"/api/v1/files/{transientFileId}/content?variant=thumbnail");
        using var acceptedJson = await JsonDocument.ParseAsync(await accepted.Content.ReadAsStreamAsync());
        var transientJobId = acceptedJson.RootElement.GetProperty("jobId").GetGuid();
        await FailJobAsync(transientJobId, MediaErrorCodes.ToolUnavailable);

        var retries = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            client.PostAsync($"/api/v1/media-jobs/{transientJobId}/retry", null)));
        Assert.All(retries, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var retriedIds = new List<Guid>();
        foreach (var response in retries)
        {
            var view = await response.Content.ReadFromJsonAsync<MediaJobView>();
            retriedIds.Add(view!.JobId);
            response.Dispose();
        }

        Assert.Single(retriedIds.Distinct());
    }

    [Fact]
    public async Task VideoVariants_ReturnImmediatelyAndReadyMp4SupportsAuthorizedRanges()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(
            $"media-video-{Guid.NewGuid():N}", "media-video-password");
        var strangerAuth = await fixture.CreateAuthenticatedClientAsync(
            $"media-video-stranger-{Guid.NewGuid():N}", "media-video-stranger-password");
        using var owner = ownerAuth.Client;
        using var stranger = strangerAuth.Client;
        var pendingFileId = await SeedSourceAsync(owner, "pending-video.mkv", "video/x-matroska");
        var elapsed = Stopwatch.StartNew();

        using var accepted = await owner.GetAsync(
            $"/api/v1/files/{pendingFileId}/content?variant=video-medium");

        elapsed.Stop();
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        using var acceptedJson = await JsonDocument.ParseAsync(await accepted.Content.ReadAsStreamAsync());
        var jobId = acceptedJson.RootElement.GetProperty("jobId").GetGuid();
        using var status = await owner.GetAsync($"/api/v1/media-jobs/{jobId}");
        var view = await status.Content.ReadFromJsonAsync<MediaJobView>();
        Assert.Equal("GENERATING", view!.Status);
        Assert.True(view.QueuePosition >= 1);

        var bytes = "0123456789"u8.ToArray();
        var (readyFileId, _) = await SeedReadyAsync(
            owner, "ready-video.mov", bytes, DerivativeType.VideoLow, "video/quicktime");
        using var rangeRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/files/{readyFileId}/content?variant=video-low");
        rangeRequest.Headers.Range = new RangeHeaderValue(3, 6);
        using var range = await owner.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal("video/mp4", range.Content.Headers.ContentType!.MediaType);
        Assert.Equal("bytes 3-6/10", range.Content.Headers.ContentRange!.ToString());
        Assert.Equal("3456", await range.Content.ReadAsStringAsync());
        Assert.Contains("ready-video_low.mp4", range.Content.Headers.ContentDisposition!.ToString());

        using var hidden = await stranger.GetAsync(
            $"/api/v1/files/{readyFileId}/content?variant=video-low");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task PendingThumbnail_ClientCancellationAndApiRestartDoNotCancelPersistentJob()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"media-cancel-{Guid.NewGuid():N}", "media-cancel-password");
        using var client = authenticated.Client;
        var fileId = await SeedSourceAsync(client, "cancelled-request.jpg");
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync(
            $"/api/v1/files/{fileId}/content?variant=thumbnail", cancellation.Token);

        Guid jobId = Guid.Empty;
        for (var attempt = 0; attempt < 100 && jobId == Guid.Empty; attempt++)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            jobId = await (
                    from job in database.MediaJobs
                    join derivative in database.FileDerivatives on job.DerivativeId equals derivative.Id
                    where derivative.SourceFileId == fileId
                    select job.Id)
                .SingleOrDefaultAsync();
            if (jobId == Guid.Empty)
            {
                await Task.Delay(20);
            }
        }

        Assert.NotEqual(Guid.Empty, jobId);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        await using (var verifyScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var verify = verifyScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var persistent = await verify.MediaJobs.SingleAsync(job => job.Id == jobId);
            Assert.Equal(MediaJobStatus.Queued, persistent.Status);
        }

        await fixture.RestartApiAsync();
        using var restarted = fixture.Factory.CreateClient();
        restarted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", authenticated.AccessToken);
        using var status = await restarted.GetAsync($"/api/v1/media-jobs/{jobId}");
        status.EnsureSuccessStatusCode();
        var view = await status.Content.ReadFromJsonAsync<MediaJobView>();
        Assert.Equal("GENERATING", view!.Status);
    }

    [Fact]
    public async Task MediaJobs_ReportTerminalStatesAndSourceValidationFailsClosed()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"media-states-{Guid.NewGuid():N}", "media-states-password");
        using var client = authenticated.Client;
        var userId = ReadSubject(authenticated.AccessToken);
        var (readyFileId, derivativeId) = await SeedReadyAsync(client, "ready-state.jpg", [1, 2, 3]);
        var unsupportedFileId = await SeedSourceAsync(client, "unsupported.txt");
        var inactiveFileId = await SeedSourceAsync(client, "inactive.jpg");
        Guid completedJobId;
        Guid failedJobId;
        Guid cancelledJobId;

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var unsupported = await database.FileEntries.SingleAsync(item => item.Id == unsupportedFileId);
            unsupported.ApplySourceObservation(
                unsupported.Size, "text/plain", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, contentChanged: false);
            var inactive = await database.FileEntries.SingleAsync(item => item.Id == inactiveFileId);
            inactive.MarkMissingCandidate(Guid.NewGuid(), DateTimeOffset.UtcNow);

            var worker = Guid.NewGuid();
            var completed = new MediaJob(
                Guid.NewGuid(), derivativeId, DerivativeType.Thumbnail, userId, DateTimeOffset.UtcNow);
            completed.Start(worker, DateTimeOffset.UtcNow);
            completed.Complete(worker, DateTimeOffset.UtcNow);
            completedJobId = completed.Id;

            worker = Guid.NewGuid();
            var failed = new MediaJob(
                Guid.NewGuid(), derivativeId, DerivativeType.Thumbnail, userId, DateTimeOffset.UtcNow);
            failed.Start(worker, DateTimeOffset.UtcNow);
            failed.Fail(worker, MediaErrorCodes.GenerationFailed, retryable: false, DateTimeOffset.UtcNow);
            failedJobId = failed.Id;

            var cancelled = new MediaJob(
                Guid.NewGuid(), derivativeId, DerivativeType.Thumbnail, userId, DateTimeOffset.UtcNow);
            cancelled.Cancel("MEDIA_SOURCE_DELETED", DateTimeOffset.UtcNow);
            cancelledJobId = cancelled.Id;
            database.MediaJobs.AddRange(completed, failed, cancelled);
            await database.SaveChangesAsync();
        }

        await AssertJobStatusAsync(client, completedJobId, "READY");
        await AssertJobStatusAsync(client, failedJobId, "FAILED");
        await AssertJobStatusAsync(client, cancelledJobId, "CANCELLED");

        using (var unsupported = await client.GetAsync(
                   $"/api/v1/files/{unsupportedFileId}/content?variant=thumbnail"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        }

        using (var inactive = await client.GetAsync($"/api/v1/files/{inactiveFileId}/content?variant=thumbnail"))
        {
            Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        }

        using (var missingRetry = await client.PostAsync($"/api/v1/media-jobs/{Guid.Empty}/retry", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, missingRetry.StatusCode);
        }

        using var ready = await client.GetAsync($"/api/v1/files/{readyFileId}/content?variant=thumbnail");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task PendingImage_WhenWorkerCompletesDuringBoundedWait_ReturnsReadyContent()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"media-wait-{Guid.NewGuid():N}", "media-wait-password");
        using var client = authenticated.Client;
        var userId = ReadSubject(authenticated.AccessToken);
        var fileId = await SeedSourceAsync(client, "wait.jpg");
        var request = client.GetAsync($"/api/v1/files/{fileId}/content?variant=image-low");

        FileDerivative? derivative = null;
        MediaJob? job = null;
        for (var attempt = 0; attempt < 100 && job is null; attempt++)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            derivative = await database.FileDerivatives.SingleOrDefaultAsync(item => item.SourceFileId == fileId);
            if (derivative is not null)
            {
                job = await database.MediaJobs.SingleOrDefaultAsync(item => item.DerivativeId == derivative.Id);
            }

            if (job is null)
            {
                await Task.Delay(20);
            }
        }

        Assert.NotNull(derivative);
        Assert.NotNull(job);
        var worker = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        derivative.Start(now);
        var relative = $"derivatives/{userId:N}/{fileId:N}/1/1/image-low.webp";
        var physical = Path.Combine(fixture.StorageRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllBytesAsync(physical, [4, 3, 2, 1]);
        derivative.MarkReady(relative, 4, now, now.AddHours(24));
        job.Start(worker, now);
        job.Complete(worker, now);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            database.Update(derivative);
            database.Update(job);
            await database.SaveChangesAsync();
        }

        using var response = await request;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([4, 3, 2, 1], await response.Content.ReadAsByteArrayAsync());
    }

    private async Task<(Guid FileId, Guid DerivativeId)> SeedReadyAsync(
        HttpClient client,
        string name,
        byte[] derivativeBytes,
        DerivativeType derivativeType = DerivativeType.Thumbnail,
        string sourceMimeType = "image/jpeg")
    {
        var fileId = await SeedSourceAsync(client, name, sourceMimeType);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var file = await database.FileEntries.SingleAsync(item => item.Id == fileId);
        var derivative = new FileDerivative(
            Guid.NewGuid(), file.Id, file.FileVersion, derivativeType, 1, DateTimeOffset.UtcNow);
        derivative.Start(DateTimeOffset.UtcNow);
        var segment = derivativeType switch
        {
            DerivativeType.VideoLow => "video-low.mp4",
            DerivativeType.VideoMedium => "video-medium.mp4",
            _ => "thumbnail.webp",
        };
        var relative = $"derivatives/{file.OwnerUserId:N}/{file.Id:N}/1/1/{segment}";
        derivative.MarkReady(
            relative,
            derivativeBytes.Length,
            DateTimeOffset.UtcNow,
            derivativeType is DerivativeType.VideoLow or DerivativeType.VideoMedium
                ? DateTimeOffset.UtcNow.AddHours(24)
                : null);
        database.FileDerivatives.Add(derivative);
        await database.SaveChangesAsync();

        var physical = Path.Combine(fixture.StorageRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllBytesAsync(physical, derivativeBytes);
        return (fileId, derivative.Id);
    }

    private async Task<Guid> SeedSourceAsync(
        HttpClient client,
        string name,
        string mimeType = "image/jpeg")
    {
        using (var provision = await client.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        var username = ReadSubject(client.DefaultRequestHeaders.Authorization!.Parameter!);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var userId = await database.Users
            .Where(user => user.Id == username)
            .Select(user => user.Id)
            .SingleAsync();
        var root = await database.FileEntries.SingleAsync(item => item.OwnerUserId == userId && item.ParentId == null);
        var fileName = FileName.Create(name);
        var file = FileEntry.CreateFile(
            Guid.NewGuid(), userId, root.Id, fileName, RelativeStoragePath.Create(root.RelativePath).Append(fileName),
            mimeType, 4, DateTimeOffset.UtcNow);
        database.FileEntries.Add(file);
        await database.SaveChangesAsync();
        return file.Id;
    }

    private async Task FailJobAsync(Guid jobId, string errorCode)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var queue = new PostgreSqlMediaJobQueue(database);
        var worker = Guid.NewGuid();
        var job = await queue.TryAcquireNextAsync(worker, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(jobId, job.Id);
        Assert.True(await queue.TryFailAsync(
            job.Id, worker, errorCode, retryable: false, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    private static async Task AssertJobStatusAsync(HttpClient client, Guid jobId, string expected)
    {
        using var response = await client.GetAsync($"/api/v1/media-jobs/{jobId}");
        response.EnsureSuccessStatusCode();
        var view = await response.Content.ReadFromJsonAsync<MediaJobView>();
        Assert.Equal(expected, view!.Status);
    }

    private static Guid ReadSubject(string token)
    {
        var payload = token.Split('.')[1];
        payload = payload.Replace('-', '+').Replace('_', '/').PadRight((payload.Length + 3) / 4 * 4, '=');
        using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(json.RootElement.GetProperty("sub").GetString()!);
    }
}
