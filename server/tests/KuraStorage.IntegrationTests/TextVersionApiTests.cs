using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace KuraStorage.IntegrationTests;

public sealed class TextVersionApiTests(PostgreSqlAuthFlowFixture fixture, ITestOutputHelper output)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task TextVersionEndpoints_SaveConflictHistoryPastTextRestoreAndRetry()
    {
        var username = $"text-owner-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "text-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var original = Encoding.UTF8.GetBytes("original sentinel α");
        var fileId = await UploadAsync(client, rootId, "text-api-note.txt", original, "text/plain");

        using (var currentResponse = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            currentResponse.EnsureSuccessStatusCode();
            using var current = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync());
            Assert.Equal("original sentinel α", current.RootElement.GetProperty("content").GetString());
            Assert.Equal(1, current.RootElement.GetProperty("fileVersion").GetInt64());
            Assert.Equal(Sha256(original), current.RootElement.GetProperty("sha256").GetString());
        }

        var saveOperation = Guid.NewGuid();
        using (var savedResponse = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = "\uFEFFedited sentinel β", expectedVersion = 1, operationId = saveOperation }))
        {
            savedResponse.EnsureSuccessStatusCode();
            using var saved = JsonDocument.Parse(await savedResponse.Content.ReadAsStringAsync());
            Assert.Equal(2, saved.RootElement.GetProperty("fileVersion").GetInt64());
            Assert.Equal("TEXT_EDIT", saved.RootElement.GetProperty("changeKind").GetString());
        }

        using (var retrySaveResponse = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = "\uFEFFedited sentinel β", expectedVersion = 1, operationId = saveOperation }))
        {
            retrySaveResponse.EnsureSuccessStatusCode();
            using var retry = JsonDocument.Parse(await retrySaveResponse.Content.ReadAsStringAsync());
            Assert.Equal(2, retry.RootElement.GetProperty("fileVersion").GetInt64());
        }

        using (var conflictResponse = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = "must not win", expectedVersion = 1, operationId = Guid.NewGuid() }))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
            await AssertErrorAsync(conflictResponse, "FILE_VERSION_CONFLICT");
        }

        using (var historyResponse = await client.GetAsync(
                   $"/api/v1/files/{fileId}/versions?page=1&pageSize=50"))
        {
            historyResponse.EnsureSuccessStatusCode();
            using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
            Assert.Equal(2, history.RootElement.GetProperty("totalCount").GetInt32());
            var items = history.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal([2L, 1L], items.Select(item => item.GetProperty("version").GetInt64()));
            Assert.All(items, item => Assert.False(item.TryGetProperty("contentRelativePath", out _)));
        }

        using (var pastResponse = await client.GetAsync(
                   $"/api/v1/files/{fileId}/versions/1/text"))
        {
            pastResponse.EnsureSuccessStatusCode();
            using var past = JsonDocument.Parse(await pastResponse.Content.ReadAsStringAsync());
            Assert.Equal("original sentinel α", past.RootElement.GetProperty("content").GetString());
            Assert.Equal(1, past.RootElement.GetProperty("fileVersion").GetInt64());
        }

        var restoreOperation = Guid.NewGuid();
        using (var restoredResponse = await client.PostAsJsonAsync(
                   $"/api/v1/files/{fileId}/versions/1/restore",
                   new { expectedVersion = 2, operationId = restoreOperation }))
        {
            restoredResponse.EnsureSuccessStatusCode();
            using var restored = JsonDocument.Parse(await restoredResponse.Content.ReadAsStringAsync());
            Assert.Equal(3, restored.RootElement.GetProperty("fileVersion").GetInt64());
            Assert.Equal("RESTORE", restored.RootElement.GetProperty("changeKind").GetString());
        }

        using (var retryRestoreResponse = await client.PostAsJsonAsync(
                   $"/api/v1/files/{fileId}/versions/1/restore",
                   new { expectedVersion = 2, operationId = restoreOperation }))
        {
            retryRestoreResponse.EnsureSuccessStatusCode();
            using var retry = JsonDocument.Parse(await retryRestoreResponse.Content.ReadAsStringAsync());
            Assert.Equal(3, retry.RootElement.GetProperty("fileVersion").GetInt64());
        }

        using (var currentResponse = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            currentResponse.EnsureSuccessStatusCode();
            using var current = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync());
            Assert.Equal("original sentinel α", current.RootElement.GetProperty("content").GetString());
            Assert.Equal(3, current.RootElement.GetProperty("fileVersion").GetInt64());
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(3, await database.FileVersionRecords.CountAsync(record => record.FileEntryId == fileId));
        Assert.Equal(1, await database.AuditLogs.CountAsync(audit =>
            audit.TargetId == fileId.ToString() && audit.Action == "FILE_TEXT_EDIT"));
        Assert.Equal(1, await database.AuditLogs.CountAsync(audit =>
            audit.TargetId == fileId.ToString() && audit.Action == "FILE_VERSION_RESTORE"));
        Assert.DoesNotContain(fixture.LogMessages, message => message.Contains("original sentinel", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.LogMessages, message => message.Contains("text-api-note.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.LogMessages, message => message.Contains(fixture.StorageRootPath, StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.LogMessages, message => message.Contains(authenticated.AccessToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TextVersionEndpoints_ValidateJsonUnknownFieldsMimeSizeAndAuthentication()
    {
        var username = $"text-validation-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "text-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var fileId = await UploadAsync(
            client,
            rootId,
            "text-validation.txt",
            Encoding.UTF8.GetBytes("valid"),
            "text/plain");

        using (var unknownResponse = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = "x", expectedVersion = 1, operationId = Guid.NewGuid(), unknown = true }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
            await AssertErrorAsync(unknownResponse, "VALIDATION_FAILED");
        }

        using (var wrongMediaRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/files/{fileId}/text")
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain"),
        })
        using (var wrongMediaResponse = await client.SendAsync(wrongMediaRequest))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongMediaResponse.StatusCode);
        }

        var oversized = new string('a', checked((int)KuraStorage.Domain.Files.FileVersionRecord.MaximumContentBytes + 1));
        using (var oversizedResponse = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = oversized, expectedVersion = 1, operationId = Guid.NewGuid() }))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
            await AssertErrorAsync(oversizedResponse, "TEXT_SIZE_LIMIT_EXCEEDED");
        }

        using var anonymous = fixture.Factory.CreateClient();
        using var unauthorizedResponse = await anonymous.GetAsync($"/api/v1/files/{fileId}/versions");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        await AssertErrorAsync(unauthorizedResponse, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task TextVersionEndpoints_ReevaluateShareAndSerializeTwoUserSaveConflictAcrossRoutes()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"text-share-owner-{suffix}";
        var memberName = $"text-share-member-{suffix}";
        var adminName = $"text-share-admin-{suffix}";
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(ownerName, "owner-password");
        var memberAuth = await fixture.CreateAuthenticatedClientAsync(memberName, "member-password");
        var adminAuth = await fixture.CreateAuthenticatedClientAsync(
            adminName,
            "admin-password",
            KuraStorage.Domain.Identity.UserRole.Admin);
        using var owner = ownerAuth.Client;
        using var member = memberAuth.Client;
        using var admin = adminAuth.Client;
        owner.DefaultRequestHeaders.Add("X-KuraStorage-Route", "LOCAL_DIRECT");
        member.DefaultRequestHeaders.Add("X-KuraStorage-Route", "REMOTE_SECURE");
        var rootId = await GetRootIdAsync(owner);
        _ = await GetRootIdAsync(member);
        _ = await GetRootIdAsync(admin);
        var fileId = await UploadAsync(
            owner,
            rootId,
            "shared-text.txt",
            Encoding.UTF8.GetBytes("base"),
            "text/plain");

        Guid memberId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            memberId = await database.Users
                .Where(user => user.UsernameNormalized == memberName.ToUpperInvariant())
                .Select(user => user.Id)
                .SingleAsync();
        }

        Guid shareId;
        using (var shareResponse = await owner.PostAsJsonAsync(
                   "/api/v1/shares",
                   new
                   {
                       targetEntryId = fileId,
                       members = new[] { new { userId = memberId, permission = "VIEWER" } },
                   }))
        {
            shareResponse.EnsureSuccessStatusCode();
            using var share = JsonDocument.Parse(await shareResponse.Content.ReadAsStringAsync());
            shareId = share.RootElement.GetProperty("id").GetGuid();
        }

        using (var viewerRead = await member.GetAsync($"/api/v1/files/{fileId}/text"))
        using (var viewerWrite = await member.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = "denied", expectedVersion = 1, operationId = Guid.NewGuid() }))
        using (var adminRead = await admin.GetAsync($"/api/v1/files/{fileId}/versions"))
        {
            viewerRead.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, viewerWrite.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, adminRead.StatusCode);
        }

        using (var promote = await owner.PutAsJsonAsync(
                   $"/api/v1/shares/{shareId}/members/{memberId}",
                   new { permission = "EDITOR" }))
        {
            promote.EnsureSuccessStatusCode();
        }

        var ownerSave = owner.PutAsJsonAsync(
            $"/api/v1/files/{fileId}/text",
            new { content = "owner value", expectedVersion = 1, operationId = Guid.NewGuid() });
        var memberSave = member.PutAsJsonAsync(
            $"/api/v1/files/{fileId}/text",
            new { content = "member value", expectedVersion = 1, operationId = Guid.NewGuid() });
        var responses = await Task.WhenAll(ownerSave, memberSave);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertErrorAsync(
                responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                "FILE_VERSION_CONFLICT");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using (var revoke = await owner.DeleteAsync($"/api/v1/shares/{shareId}/members/{memberId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        using (var revokedText = await member.GetAsync($"/api/v1/files/{fileId}/text"))
        using (var revokedHistory = await member.GetAsync($"/api/v1/files/{fileId}/versions"))
        using (var revokedPast = await member.GetAsync($"/api/v1/files/{fileId}/versions/1/text"))
        {
            Assert.Equal(HttpStatusCode.NotFound, revokedText.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, revokedHistory.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, revokedPast.StatusCode);
        }

        await using (var revokeDeviceScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = revokeDeviceScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var device = await database.Devices.SingleAsync(candidate => candidate.UserId == memberId);
            device.Revoke(DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        using (var deviceRevoked = await member.GetAsync($"/api/v1/files/{fileId}/versions"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, deviceRevoked.StatusCode);
            await AssertErrorAsync(deviceRevoked, "AUTHENTICATION_REQUIRED");
        }

        await using var assertScope = fixture.Factory.Services.CreateAsyncScope();
        var assertDatabase = assertScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(2, await assertDatabase.FileVersionRecords.CountAsync(record => record.FileEntryId == fileId));
    }

    [Fact]
    public async Task TextEditRecovery_RollsForwardFilesystemDoneVersionAndIsIdempotent()
    {
        var username = $"text-recovery-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "recovery-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var fileId = await UploadAsync(
            client,
            rootId,
            "recovery-note.txt",
            Encoding.UTF8.GetBytes("before recovery"),
            "text/plain");
        var after = Encoding.UTF8.GetBytes("after recovery");
        var operationId = Guid.NewGuid();

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entry = await database.FileEntries.SingleAsync(candidate => candidate.Id == fileId);
            var user = await database.Users.SingleAsync(candidate =>
                candidate.UsernameNormalized == username.ToUpperInvariant());
            var deviceId = await database.Devices
                .Where(device => device.UserId == user.Id)
                .Select(device => device.Id)
                .SingleAsync();
            var versionStore = scope.ServiceProvider.GetRequiredService<IFileVersionStore>();
            await using var versionSource = new MemoryStream(after, writable: false);
            var published = await versionStore.TryPublishAsync(
                entry.OwnerUserId,
                entry.Id,
                2,
                operationId,
                versionSource,
                after.LongLength,
                default);
            var operation = new FileOperation(
                operationId,
                entry.OwnerUserId,
                FileOperationType.TextEdit,
                entry.Id,
                operationId.ToString("D"),
                $"upload-temp/{entry.OwnerUserId:N}/{operationId:N}.upload",
                entry.RelativePath,
                after.LongLength,
                published!.Sha256,
                DateTimeOffset.UtcNow,
                deviceId,
                "recovery-request");
            operation.RecordPublishedVersion(
                1,
                2,
                published.TemporaryPath.Value,
                published.Path.Value,
                published.Sha256,
                DateTimeOffset.UtcNow);
            operation.MarkFilesystemDone(DateTimeOffset.UtcNow);
            database.FileVersionRecords.Add(new FileVersionRecord(
                Guid.NewGuid(),
                entry.Id,
                2,
                published.Size,
                published.Sha256,
                published.Path.Value,
                FileVersionChangeKind.TextEdit,
                user.Id,
                deviceId,
                DateTimeOffset.UtcNow));
            database.FileOperations.Add(operation);
            await database.SaveChangesAsync();

            var fileStore = scope.ServiceProvider.GetRequiredService<IFileStore>();
            await using var replacementSource = new MemoryStream(after, writable: false);
            var replacement = await fileStore.WriteUploadTempAsync(
                entry.OwnerUserId,
                operationId,
                replacementSource,
                after.LongLength,
                default);
            await fileStore.ReplaceAsync(
                replacement.Path,
                RelativeStoragePath.Create(entry.RelativePath),
                default);
        }

        await using (var recoveryScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var recovery = recoveryScope.ServiceProvider.GetRequiredService<FileOperationRecoveryService>();
            await recovery.RecoverAsync(default);
            await recovery.RecoverAsync(default);
        }

        using (var currentResponse = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            currentResponse.EnsureSuccessStatusCode();
            using var current = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync());
            Assert.Equal("after recovery", current.RootElement.GetProperty("content").GetString());
            Assert.Equal(2, current.RootElement.GetProperty("fileVersion").GetInt64());
        }

        await using var assertScope = fixture.Factory.Services.CreateAsyncScope();
        var assertDatabase = assertScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(
            FileOperationStatus.Completed,
            (await assertDatabase.FileOperations.SingleAsync(operation => operation.Id == operationId)).Status);
        Assert.Equal(1, await assertDatabase.AuditLogs.CountAsync(audit =>
            audit.RequestId == "recovery-request" && audit.Action == "FILE_TEXT_EDIT"));
        Assert.Equal(2, await assertDatabase.FileVersionRecords.CountAsync(record => record.FileEntryId == fileId));
    }

    [Fact]
    public async Task TextSave_SerializesWithTrashAndPurgeRemovesOnlyTargetHistory()
    {
        var username = $"text-trash-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "trash-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var fileId = await UploadAsync(
            client,
            rootId,
            "trash-race.txt",
            Encoding.UTF8.GetBytes("before trash race"),
            "text/plain");
        var survivorId = await UploadAsync(
            client,
            rootId,
            "survivor.txt",
            Encoding.UTF8.GetBytes("survivor"),
            "text/plain");

        var saveTask = client.PutAsJsonAsync(
            $"/api/v1/files/{fileId}/text",
            new { content = "after trash race", expectedVersion = 1, operationId = Guid.NewGuid() });
        var trashTask = client.DeleteAsync($"/api/v1/files/{fileId}");
        var results = await Task.WhenAll(saveTask, trashTask);
        try
        {
            Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
            Assert.Contains(results[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        }
        finally
        {
            foreach (var response in results)
            {
                response.Dispose();
            }
        }

        using (var trashedText = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            Assert.Equal(HttpStatusCode.NotFound, trashedText.StatusCode);
        }

        using (var restore = await client.PostAsync($"/api/v1/files/{fileId}/restore", null))
        {
            restore.EnsureSuccessStatusCode();
        }

        using (var restoredText = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            restoredText.EnsureSuccessStatusCode();
            using var restored = JsonDocument.Parse(await restoredText.Content.ReadAsStringAsync());
            Assert.Contains(
                restored.RootElement.GetProperty("content").GetString(),
                new[] { "before trash race", "after trash race" });
            Assert.Contains(restored.RootElement.GetProperty("fileVersion").GetInt64(), new[] { 1L, 2L });
        }

        using (var trashAgain = await client.DeleteAsync($"/api/v1/files/{fileId}"))
        {
            trashAgain.EnsureSuccessStatusCode();
        }

        using (var purgeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/trash/{fileId}"))
        {
            purgeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var purge = await client.SendAsync(purgeRequest);
            Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(0, await database.FileVersionRecords.CountAsync(record => record.FileEntryId == fileId));
        Assert.Equal(1, await database.FileVersionRecords.CountAsync(record => record.FileEntryId == survivorId));
    }

    [Fact]
    public async Task TextSave_SerializesWithMoveWithoutLosingVersionOrContent()
    {
        var username = $"text-move-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "move-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        Guid folderId;
        using (var folderResponse = await client.PostAsJsonAsync(
                   "/api/v1/folders",
                   new { parentId = rootId, name = "text-move-target" }))
        {
            folderResponse.EnsureSuccessStatusCode();
            using var folder = JsonDocument.Parse(await folderResponse.Content.ReadAsStringAsync());
            folderId = folder.RootElement.GetProperty("id").GetGuid();
        }

        var fileId = await UploadAsync(
            client,
            rootId,
            "move-race.txt",
            Encoding.UTF8.GetBytes("before move"),
            "text/plain");
        var saveTask = client.PutAsJsonAsync(
            $"/api/v1/files/{fileId}/text",
            new { content = "after move", expectedVersion = 1, operationId = Guid.NewGuid() });
        var moveRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/files/{fileId}")
        {
            Content = JsonContent.Create(new { parentId = folderId }),
        };
        var moveTask = client.SendAsync(moveRequest);
        var responses = await Task.WhenAll(saveTask, moveTask);
        try
        {
            Assert.All(responses, response => response.EnsureSuccessStatusCode());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            moveRequest.Dispose();
        }

        using (var textResponse = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            textResponse.EnsureSuccessStatusCode();
            using var text = JsonDocument.Parse(await textResponse.Content.ReadAsStringAsync());
            Assert.Equal("after move", text.RootElement.GetProperty("content").GetString());
            Assert.Equal(2, text.RootElement.GetProperty("fileVersion").GetInt64());
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var entry = await database.FileEntries.SingleAsync(candidate => candidate.Id == fileId);
        Assert.Equal(folderId, entry.ParentId);
        Assert.Equal(2, await database.FileVersionRecords.CountAsync(record => record.FileEntryId == fileId));
    }

    [Fact]
    public async Task RestoreAndSave_FromSameExpectedVersionAllowExactlyOneNewVersion()
    {
        var username = $"text-restore-race-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "restore-race-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var fileId = await UploadAsync(
            client,
            rootId,
            "restore-race.txt",
            Encoding.UTF8.GetBytes("one"),
            "text/plain");
        using (var versionTwo = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = "two", expectedVersion = 1, operationId = Guid.NewGuid() }))
        {
            versionTwo.EnsureSuccessStatusCode();
        }

        var restoreTask = client.PostAsJsonAsync(
            $"/api/v1/files/{fileId}/versions/1/restore",
            new { expectedVersion = 2, operationId = Guid.NewGuid() });
        var saveTask = client.PutAsJsonAsync(
            $"/api/v1/files/{fileId}/text",
            new { content = "three", expectedVersion = 2, operationId = Guid.NewGuid() });
        var responses = await Task.WhenAll(restoreTask, saveTask);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertErrorAsync(
                responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                "FILE_VERSION_CONFLICT");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using (var currentResponse = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            currentResponse.EnsureSuccessStatusCode();
            using var current = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync());
            Assert.Equal(3, current.RootElement.GetProperty("fileVersion").GetInt64());
            Assert.Contains(current.RootElement.GetProperty("content").GetString(), new[] { "one", "three" });
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(3, await database.FileVersionRecords.CountAsync(record => record.FileEntryId == fileId));
    }

    [Fact]
    public async Task TextVersionEndpoints_ApplyPerUserRateLimitWithErrorEnvelope()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"text-rate-{Guid.NewGuid():N}",
            "rate-password");
        using var client = authenticated.Client;
        var missingId = Guid.NewGuid();
        for (var requestNumber = 0; requestNumber < 120; requestNumber++)
        {
            using var response = await client.GetAsync($"/api/v1/files/{missingId}/versions");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var limited = await client.GetAsync($"/api/v1/files/{missingId}/versions");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AssertErrorAsync(limited, "RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task TextVersionEndpoints_OneMiBBoundaryCompletesWithinTwoSecondsPerOperation()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"text-performance-{Guid.NewGuid():N}",
            "performance-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var originalText = new string('a', checked((int)FileVersionRecord.MaximumContentBytes));
        var original = Encoding.UTF8.GetBytes(originalText);
        var fileId = await UploadAsync(client, rootId, "one-mib.txt", original, "text/plain");
        var editedText = new string('b', checked((int)FileVersionRecord.MaximumContentBytes));

        var getCurrent = Stopwatch.StartNew();
        using (var response = await client.GetAsync($"/api/v1/files/{fileId}/text"))
        {
            response.EnsureSuccessStatusCode();
        }
        getCurrent.Stop();

        var save = Stopwatch.StartNew();
        using (var response = await client.PutAsJsonAsync(
                   $"/api/v1/files/{fileId}/text",
                   new { content = editedText, expectedVersion = 1, operationId = Guid.NewGuid() }))
        {
            response.EnsureSuccessStatusCode();
        }
        save.Stop();

        var getPast = Stopwatch.StartNew();
        using (var response = await client.GetAsync($"/api/v1/files/{fileId}/versions/1/text"))
        {
            response.EnsureSuccessStatusCode();
        }
        getPast.Stop();

        var restore = Stopwatch.StartNew();
        using (var response = await client.PostAsJsonAsync(
                   $"/api/v1/files/{fileId}/versions/1/restore",
                   new { expectedVersion = 2, operationId = Guid.NewGuid() }))
        {
            response.EnsureSuccessStatusCode();
        }
        restore.Stop();

        output.WriteLine(
            "Redacted 1 MiB API timings: current={0:F1} ms, save={1:F1} ms, past={2:F1} ms, restore={3:F1} ms",
            getCurrent.Elapsed.TotalMilliseconds,
            save.Elapsed.TotalMilliseconds,
            getPast.Elapsed.TotalMilliseconds,
            restore.Elapsed.TotalMilliseconds);

        Assert.True(getCurrent.Elapsed < TimeSpan.FromSeconds(2), $"Current text took {getCurrent.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.True(save.Elapsed < TimeSpan.FromSeconds(2), $"Save took {save.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.True(getPast.Elapsed < TimeSpan.FromSeconds(2), $"Past text took {getPast.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.True(restore.Elapsed < TimeSpan.FromSeconds(2), $"Restore took {restore.Elapsed.TotalMilliseconds:F1} ms.");
    }

    private static async Task<Guid> GetRootIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/files");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("parentId").GetGuid();
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client,
        Guid parentId,
        string name,
        byte[] content,
        string contentType)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(parentId.ToString()), "destinationFolderId");
        multipart.Add(new StringContent(name), "fileName");
        multipart.Add(new StringContent(content.LongLength.ToString()), "size");
        multipart.Add(new StringContent(Sha256(content)), "sha256");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(fileContent, "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/upload")
        {
            Content = multipart,
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("requestId").GetString()));
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("details").ValueKind);
    }
}
