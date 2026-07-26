using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class FileApiFlowTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task FileFlow_WhenAuthenticated_StreamsListsRangesTrashesAndRestores()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync();
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Documents");

        using var duplicateFolder = await client.PostAsJsonAsync(
            "/api/v1/folders",
            new { parentId = rootId, name = "Documents" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateFolder.StatusCode);
        await AssertErrorAsync(duplicateFolder, "FILE_NAME_CONFLICT");

        var content = Encoding.UTF8.GetBytes("0123456789");
        var idempotencyKey = Guid.NewGuid().ToString();
        var uploaded = await UploadAsync(client, folder.Id, "report.txt", content, idempotencyKey);
        var repeated = await UploadAsync(client, folder.Id, "report.txt", content, idempotencyKey);
        Assert.Equal(uploaded.Id, repeated.Id);

        using var changedPayload = await SendUploadAsync(
            client,
            folder.Id,
            "different.txt",
            content,
            idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
        await AssertErrorAsync(changedPayload, "IDEMPOTENCY_CONFLICT");

        using var details = await client.GetAsync($"/api/v1/files/{uploaded.Id}");
        details.EnsureSuccessStatusCode();
        var detailsText = await details.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ownerUserId", detailsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("relativePath", detailsText, StringComparison.OrdinalIgnoreCase);

        using var full = await client.GetAsync($"/api/v1/files/{uploaded.Id}/content");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal(content, await full.Content.ReadAsByteArrayAsync());
        Assert.Equal("bytes", full.Headers.AcceptRanges.Single());
        Assert.NotNull(full.Content.Headers.ContentDisposition);

        using var rangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/{uploaded.Id}/content");
        rangeRequest.Headers.Range = new RangeHeaderValue(2, 5);
        using var range = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal("2345", await range.Content.ReadAsStringAsync());
        Assert.Equal("bytes 2-5/10", range.Content.Headers.ContentRange!.ToString());

        foreach (var (from, to, expected) in new (long?, long?, string)[]
        {
            (0, 2, "012"),
            (8, null, "89"),
            (null, 2, "89"),
        })
        {
            using var boundaryRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/files/{uploaded.Id}/content");
            boundaryRequest.Headers.Range = new RangeHeaderValue(from, to);
            using var boundaryResponse = await client.SendAsync(boundaryRequest);
            Assert.Equal(HttpStatusCode.PartialContent, boundaryResponse.StatusCode);
            Assert.Equal(expected, await boundaryResponse.Content.ReadAsStringAsync());
        }

        using var invalidRangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/{uploaded.Id}/content");
        invalidRangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=100-200");
        using var invalidRange = await client.SendAsync(invalidRangeRequest);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalidRange.StatusCode);
        await AssertErrorAsync(invalidRange, "RANGE_NOT_SATISFIABLE");

        var other = await fixture.CreateAuthenticatedClientAsync("bob", "bob-password");
        using (other.Client)
        using (var idor = await other.Client.GetAsync($"/api/v1/files/{uploaded.Id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, idor.StatusCode);
            await AssertErrorAsync(idor, "FILE_NOT_FOUND");
        }

        using var trash = await client.DeleteAsync($"/api/v1/files/{uploaded.Id}");
        trash.EnsureSuccessStatusCode();
        using var trashList = await client.GetAsync("/api/v1/trash");
        trashList.EnsureSuccessStatusCode();
        using (var json = await JsonDocument.ParseAsync(await trashList.Content.ReadAsStreamAsync()))
        {
            Assert.Contains(
                json.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("id").GetGuid() == uploaded.Id);
        }

        using var restore = await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null);
        restore.EnsureSuccessStatusCode();
        using var trashAgain = await client.DeleteAsync($"/api/v1/files/{uploaded.Id}");
        trashAgain.EnsureSuccessStatusCode();
        _ = await UploadAsync(client, folder.Id, "report.txt", content, Guid.NewGuid().ToString());
        using var conflict = await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertErrorAsync(conflict, "FILE_RESTORE_CONFLICT");
    }

    [Fact]
    public async Task FolderTrash_WhenItHasChildren_UpdatesDescendantsAsOneCatalogChange()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("folder-user", "folder-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Parent");
        var childFolder = await CreateFolderAsync(client, folder.Id, "Child");
        var file = await UploadAsync(
            client,
            childFolder.Id,
            "nested.bin",
            [1, 2, 3],
            Guid.NewGuid().ToString());

        using var trash = await client.DeleteAsync($"/api/v1/files/{folder.Id}");
        trash.EnsureSuccessStatusCode();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entries = await database.FileEntries
                .Where(entry => entry.Id == folder.Id || entry.Id == childFolder.Id || entry.Id == file.Id)
                .ToListAsync();
            Assert.All(entries, entry => Assert.Equal(FileEntryStatus.Trashed, entry.Status));
        }

        using var restore = await client.PostAsync($"/api/v1/files/{folder.Id}/restore", null);
        restore.EnsureSuccessStatusCode();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entries = await database.FileEntries
                .Where(entry => entry.Id == folder.Id || entry.Id == childFolder.Id || entry.Id == file.Id)
                .ToListAsync();
            Assert.All(entries, entry => Assert.Equal(FileEntryStatus.Active, entry.Status));
        }
    }

    [Fact]
    public async Task FileInputs_WhenPathOrNulLikeNamesAreSent_RejectThemWithoutCreatingEntries()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("path-user", "path-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        foreach (var invalid in new[] { "..", "/absolute", "nested/name", "nested\\name", "bad\0name" })
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/folders",
                new { parentId = rootId, name = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertErrorAsync(response, "VALIDATION_FAILED");
        }
    }

    [Fact]
    public async Task Recovery_WhenFilesystemMoveCompletedBeforeDatabaseUpdate_CompletesTrashIdempotently()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("recovery-user", "recovery-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Recovery");
        var file = await UploadAsync(
            client,
            folder.Id,
            "recover.bin",
            [4, 5, 6],
            Guid.NewGuid().ToString());

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
            var entry = await database.FileEntries.SingleAsync(candidate => candidate.Id == file.Id);
            var source = RelativeStoragePath.Create(entry.RelativePath);
            var target = RelativeStoragePath.Create(
                $"users/{entry.OwnerUserId:N}/trash/{entry.Id:N}/{entry.Name}");
            await store.MoveAsync(source, target, false, CancellationToken.None);
            var operation = new FileOperation(
                Guid.NewGuid(),
                entry.OwnerUserId,
                FileOperationType.Trash,
                entry.Id,
                null,
                source.Value,
                target.Value,
                null,
                null,
                DateTimeOffset.UtcNow);
            operation.MarkFilesystemDone(DateTimeOffset.UtcNow);
            database.FileOperations.Add(operation);
            await database.SaveChangesAsync();
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<FileOperationRecoveryService>()
                .RecoverAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(
                FileEntryStatus.Trashed,
                (await database.FileEntries.SingleAsync(entry => entry.Id == file.Id)).Status);
            Assert.Equal(
                FileOperationStatus.Completed,
                (await database.FileOperations
                    .OrderByDescending(operation => operation.CreatedAt)
                    .FirstAsync(operation => operation.FileEntryId == file.Id)).Status);
        }
    }

    [Fact]
    public async Task Upload_WhenSizeOrChecksumDoesNotMatch_RejectsAndAllowsSafeWholeFileRetry()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("upload-user", "upload-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var key = Guid.NewGuid().ToString();
        var content = new byte[] { 1, 2, 3, 4 };

        using var shortBody = await SendUploadWithMetadataAsync(
            client,
            rootId,
            "size.bin",
            content[..^1],
            declaredSize: content.Length,
            sha256: Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            idempotencyKey: key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, shortBody.StatusCode);
        await AssertErrorAsync(shortBody, "UPLOAD_SIZE_MISMATCH");

        var retried = await UploadAsync(client, rootId, "size.bin", content, key);
        Assert.Equal("size.bin", retried.Name);

        using var checksum = await SendUploadWithMetadataAsync(
            client,
            rootId,
            "checksum.bin",
            content,
            content.Length,
            new string('0', 64),
            Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, checksum.StatusCode);
        await AssertErrorAsync(checksum, "UPLOAD_CHECKSUM_MISMATCH");

        using var tooLong = await SendUploadWithMetadataAsync(
            client,
            rootId,
            "too-long.bin",
            content,
            content.Length - 1,
            null,
            Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
        await AssertErrorAsync(tooLong, "UPLOAD_SIZE_MISMATCH");
    }

    private static async Task<Guid> GetRootIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/files");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("parentId").GetGuid();
    }

    private static async Task<TestFileItem> CreateFolderAsync(HttpClient client, Guid parentId, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/folders", new { parentId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestFileItem>())!;
    }

    private static async Task<TestFileItem> UploadAsync(
        HttpClient client,
        Guid parentId,
        string name,
        byte[] content,
        string idempotencyKey)
    {
        using var response = await SendUploadAsync(client, parentId, name, content, idempotencyKey);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestFileItem>())!;
    }

    private static async Task<HttpResponseMessage> SendUploadAsync(
        HttpClient client,
        Guid parentId,
        string name,
        byte[] content,
        string idempotencyKey)
    {
        return await SendUploadWithMetadataAsync(
            client,
            parentId,
            name,
            content,
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            idempotencyKey);
    }

    private static async Task<HttpResponseMessage> SendUploadWithMetadataAsync(
        HttpClient client,
        Guid parentId,
        string name,
        byte[] content,
        long declaredSize,
        string? sha256,
        string idempotencyKey)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(parentId.ToString()), "destinationFolderId");
        multipart.Add(new StringContent(name), "fileName");
        multipart.Add(new StringContent(declaredSize.ToString()), "size");
        if (sha256 is not null)
        {
            multipart.Add(new StringContent(sha256), "sha256");
        }

        multipart.Add(new ByteArrayContent(content), "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/upload")
        {
            Content = multipart,
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("requestId").GetString()));
    }

    private sealed record TestFileItem(Guid Id, Guid? ParentId, string Name, string EntryType);
}
