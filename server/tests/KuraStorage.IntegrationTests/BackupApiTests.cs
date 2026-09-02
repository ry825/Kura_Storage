using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using KuraStorage.Domain.Transfers;
using KuraStorage.Domain.Files;
using KuraStorage.Application.Transfers;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class BackupApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task CompareAndUpload_NewThenChanged_ConvergesOnOneFileVersionAndReceipt()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("backup-flow", "backup-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var documentKey = Guid.NewGuid().ToString("D");
        var original = new byte[] { 1, 2, 3 };
        var originalModified = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

        var initial = await CompareAsync(client, rootId, documentKey, original, originalModified);
        Assert.Equal("NEW", initial.GetProperty("decision").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("remoteFileId").ValueKind);

        var newSession = await CreateBackupSessionAsync(
            client, rootId, "photo.jpg", documentKey, "Photos/photo.jpg", original,
            originalModified, "NEW", null, null);
        await UploadAndCompleteAsync(client, newSession, original);

        var unchanged = await CompareAsync(client, rootId, documentKey, original, originalModified);
        Assert.Equal("ALREADY_UPLOADED", unchanged.GetProperty("decision").GetString());
        var fileId = unchanged.GetProperty("remoteFileId").GetGuid();
        Assert.Equal(1, unchanged.GetProperty("expectedRemoteFileVersion").GetInt64());

        using (var favorite = await client.PutAsync($"/api/v1/favorites/{fileId}", null))
        {
            favorite.EnsureSuccessStatusCode();
        }
        using (var recent = await client.PutAsync($"/api/v1/recent-files/{fileId}", null))
        {
            recent.EnsureSuccessStatusCode();
        }
        Guid tagId;
        using (var tag = await client.PostAsJsonAsync("/api/v1/tags", new { name = "BackupKeep" }))
        {
            tag.EnsureSuccessStatusCode();
            tagId = (await tag.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        using (var attach = await client.PutAsync($"/api/v1/files/{fileId}/tags/{tagId}", null))
        {
            attach.EnsureSuccessStatusCode();
        }
        var viewerAuth = await fixture.CreateAuthenticatedClientAsync("backup-flow-viewer", "backup-password");
        using var viewer = viewerAuth.Client;
        Guid viewerId;
        await using (var viewerScope = fixture.Factory.Services.CreateAsyncScope())
        {
            viewerId = await viewerScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>().Users
                .Where(user => user.UsernameNormalized == "BACKUP-FLOW-VIEWER")
                .Select(user => user.Id)
                .SingleAsync();
        }
        Guid shareId;
        using (var share = await client.PostAsJsonAsync(
                   "/api/v1/shares",
                   new { targetEntryId = fileId, members = new[] { new { userId = viewerId, permission = "VIEWER" } } }))
        {
            share.EnsureSuccessStatusCode();
            shareId = (await share.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var changedContent = new byte[] { 9, 8, 7, 6 };
        var changedModified = originalModified.AddMinutes(1);
        var changed = await CompareAsync(client, rootId, documentKey, changedContent, changedModified);
        Assert.Equal("CHANGED", changed.GetProperty("decision").GetString());
        Assert.Equal(fileId, changed.GetProperty("remoteFileId").GetGuid());

        var changedSession = await CreateBackupSessionAsync(
            client, rootId, "ignored-new-name.jpg", documentKey, "Photos/renamed-locally.jpg", changedContent,
            changedModified, "CHANGED", fileId, 1);
        var completed = await UploadAndCompleteAsync(client, changedSession, changedContent);
        Assert.Equal(fileId, completed.GetProperty("id").GetGuid());
        Assert.Equal("photo.jpg", completed.GetProperty("name").GetString());
        Assert.Equal(2, completed.GetProperty("fileVersion").GetInt64());

        using (var repeated = await client.PostAsync($"/api/v1/upload-sessions/{changedSession}/complete", null))
        {
            repeated.EnsureSuccessStatusCode();
            Assert.Equal(2, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileVersion").GetInt64());
        }
        using (var download = await client.GetAsync($"/api/v1/files/{fileId}/content"))
        {
            download.EnsureSuccessStatusCode();
            Assert.Equal(changedContent, await download.Content.ReadAsByteArrayAsync());
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Single(await database.BackupReceipts.Where(receipt => receipt.LocalDocumentKey == documentKey).ToListAsync());
        var receipt = await database.BackupReceipts.SingleAsync(receipt => receipt.LocalDocumentKey == documentKey);
        Assert.Equal(fileId, receipt.RemoteFileId);
        Assert.Equal(2, receipt.RemoteFileVersion);
        Assert.Equal("Photos/renamed-locally.jpg", receipt.RelativePath);
        Assert.Single(await database.FileEntries.Where(entry => entry.Id == fileId).ToListAsync());
        Assert.True(await database.FavoriteEntries.AnyAsync(item => item.EntryId == fileId));
        Assert.True(await database.RecentFiles.AnyAsync(item => item.FileId == fileId));
        Assert.True(await database.EntryTags.AnyAsync(item => item.EntryId == fileId && item.TagId == tagId));
        Assert.True(await database.Shares.AnyAsync(item => item.Id == shareId && item.TargetEntryId == fileId));
        Assert.Contains(await database.UserActivities.Where(activity => activity.TargetEntryId == fileId).ToListAsync(),
            activity => activity.ActivityType == KuraStorage.Domain.Activity.UserActivityType.Edit &&
                        activity.EditKind == KuraStorage.Domain.Activity.ActivityEditKind.BackupUpload &&
                        activity.ResultingFileVersion == 2);
    }

    [Fact]
    public async Task Compare_RejectsDuplicateKeysAndDoesNotEchoSensitiveMetadata()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("backup-invalid", "backup-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var key = Guid.NewGuid().ToString("D");
        var item = new
        {
            localDocumentKey = key,
            relativePath = "Private/secret-name.jpg",
            size = 1,
            modifiedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            checksum = Sha([1]),
        };

        using var response = await client.PostAsJsonAsync(
            "/api/v1/backup/compare",
            new { destinationFolderId = rootId, items = new[] { item, item } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("BACKUP_INVALID_REQUEST", body, StringComparison.Ordinal);
        Assert.DoesNotContain(key, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recovery_AfterChangedFilesystemPublish_CompletesVersionAndReceiptOnce()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("backup-recovery", "backup-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var key = Guid.NewGuid().ToString("D");
        var initial = new byte[] { 1 };
        var initialTime = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);
        var firstSession = await CreateBackupSessionAsync(
            client, rootId, "recover.jpg", key, "Photos/recover.jpg", initial,
            initialTime, "NEW", null, null);
        var first = await UploadAndCompleteAsync(client, firstSession, initial);
        var fileId = first.GetProperty("id").GetGuid();

        var changed = new byte[] { 4, 5, 6 };
        var changedTime = initialTime.AddMinutes(1);
        var changedSessionId = await CreateBackupSessionAsync(
            client, rootId, "recover.jpg", key, "Photos/recover.jpg", changed,
            changedTime, "CHANGED", fileId, 1);
        await UploadOnlyAsync(client, changedSessionId, changed);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var session = await database.UploadSessions.SingleAsync(item => item.Id == changedSessionId);
            var entry = await database.FileEntries.SingleAsync(item => item.Id == fileId);
            var operation = new FileOperation(
                Guid.NewGuid(),
                entry.OwnerUserId,
                FileOperationType.BackupUpdate,
                entry.Id,
                session.IdempotencyKey,
                session.TemporaryRelativePath,
                entry.RelativePath,
                changed.LongLength,
                Sha(changed),
                DateTimeOffset.UtcNow,
                session.DeviceId,
                "recovery-test",
                "BACKUP_UPLOAD",
                session.ActorUserId);
            session.BeginCompletion(operation.Id, DateTimeOffset.UtcNow);
            operation.MarkFilesystemDone(DateTimeOffset.UtcNow);
            database.FileOperations.Add(operation);
            await database.SaveChangesAsync();

            var source = Path.Combine(fixture.StorageRootPath, session.TemporaryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(fixture.StorageRootPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Move(source, target, true);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadSessionRecoveryService>()
                .RecoverAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(UploadSessionStatus.Completed,
                (await database.UploadSessions.SingleAsync(item => item.Id == changedSessionId)).Status);
            Assert.Equal(2, (await database.FileEntries.SingleAsync(item => item.Id == fileId)).FileVersion);
            Assert.Equal(2, (await database.BackupReceipts.SingleAsync(item => item.LocalDocumentKey == key)).RemoteFileVersion);
        }
    }

    [Fact]
    public async Task PendingDocumentIsUniqueAndStaleVersionNeverPublishesOrAdvancesReceipt()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("backup-conflict", "backup-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var key = Guid.NewGuid().ToString("D");
        var initial = new byte[] { 3, 3 };
        var initialTime = new DateTimeOffset(2026, 9, 2, 2, 0, 0, TimeSpan.Zero);
        var pending = await CreateBackupSessionAsync(
            client, rootId, "conflict.jpg", key, "Photos/conflict.jpg", initial,
            initialTime, "NEW", null, null);

        using (var duplicateRequest = CreateBackupSessionRequest(
                   rootId, "conflict-copy.jpg", key, "Photos/conflict.jpg", initial,
                   initialTime, "NEW", null, null))
        using (var duplicate = await client.SendAsync(duplicateRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }
        using (var cancel = await client.DeleteAsync($"/api/v1/upload-sessions/{pending}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        }

        var replacement = await CreateBackupSessionAsync(
            client, rootId, "conflict.jpg", key, "Photos/conflict.jpg", initial,
            initialTime, "NEW", null, null);
        var completed = await UploadAndCompleteAsync(client, replacement, initial);
        var fileId = completed.GetProperty("id").GetGuid();
        var changed = new byte[] { 7, 7, 7 };
        var changedSession = await CreateBackupSessionAsync(
            client, rootId, "conflict.jpg", key, "Photos/conflict.jpg", changed,
            initialTime.AddMinutes(1), "CHANGED", fileId, 1);
        await UploadOnlyAsync(client, changedSession, changed);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entry = await database.FileEntries.SingleAsync(item => item.Id == fileId);
            entry.ApplyManagedContentChange(initial.LongLength, 1, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        using (var stale = await client.PostAsync($"/api/v1/upload-sessions/{changedSession}/complete", null))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var body = await stale.Content.ReadAsStringAsync();
            Assert.Contains("BACKUP_VERSION_CONFLICT", body, StringComparison.Ordinal);
        }
        using (var download = await client.GetAsync($"/api/v1/files/{fileId}/content"))
        {
            download.EnsureSuccessStatusCode();
            Assert.Equal(initial, await download.Content.ReadAsByteArrayAsync());
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(1, (await database.BackupReceipts.SingleAsync(item => item.LocalDocumentKey == key)).RemoteFileVersion);
        }
    }

    [Fact]
    public async Task SharedBackup_UsesFolderOwnerAndRejectsChangedCompletionAfterPermissionDowngrade()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("backup-owner", "backup-password");
        var contributorAuth = await fixture.CreateAuthenticatedClientAsync("backup-contributor", "backup-password");
        using var owner = ownerAuth.Client;
        using var contributor = contributorAuth.Client;
        Guid ownerId;
        Guid contributorId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            ownerId = await database.Users.Where(user => user.UsernameNormalized == "BACKUP-OWNER").Select(user => user.Id).SingleAsync();
            contributorId = await database.Users.Where(user => user.UsernameNormalized == "BACKUP-CONTRIBUTOR").Select(user => user.Id).SingleAsync();
        }

        var rootId = await GetRootIdAsync(owner);
        using var folderResponse = await owner.PostAsJsonAsync("/api/v1/folders", new { parentId = rootId, name = "BackupShared" });
        folderResponse.EnsureSuccessStatusCode();
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid shareId;
        using (var share = await owner.PostAsJsonAsync(
                   "/api/v1/shares",
                   new
                   {
                       targetEntryId = folderId,
                       members = new[] { new { userId = contributorId, permission = "EDITOR" } },
                   }))
        {
            share.EnsureSuccessStatusCode();
            shareId = (await share.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var content = new byte[] { 5 };
        var key = Guid.NewGuid().ToString("D");
        var modified = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal("NEW", (await CompareAsync(contributor, folderId, key, content, modified)).GetProperty("decision").GetString());
        var session = await CreateBackupSessionAsync(
            contributor, folderId, "shared-photo.jpg", key, "Photos/shared-photo.jpg", content,
            modified, "NEW", null, null);
        var completed = await UploadAndCompleteAsync(contributor, session, content);
        var fileId = completed.GetProperty("id").GetGuid();
        Assert.Equal(ownerId, completed.GetProperty("owner").GetProperty("id").GetGuid());

        var changedContent = new byte[] { 8, 9 };
        var changedSession = await CreateBackupSessionAsync(
            contributor, folderId, "shared-photo.jpg", key, "Photos/shared-photo.jpg", changedContent,
            modified.AddMinutes(1), "CHANGED", fileId, 1);
        await UploadOnlyAsync(contributor, changedSession, changedContent);
        using (var downgrade = await owner.PutAsJsonAsync(
                   $"/api/v1/shares/{shareId}/members/{contributorId}", new { permission = "VIEWER" }))
        {
            downgrade.EnsureSuccessStatusCode();
        }
        using (var rejected = await contributor.PostAsync($"/api/v1/upload-sessions/{changedSession}/complete", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        }
        using (var download = await owner.GetAsync($"/api/v1/files/{fileId}/content"))
        {
            download.EnsureSuccessStatusCode();
            Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        }

        await using var verifyScope = fixture.Factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(ownerId, (await verify.FileEntries.SingleAsync(entry => entry.Id == fileId)).OwnerUserId);
        var receipt = await verify.BackupReceipts.SingleAsync(item => item.LocalDocumentKey == key);
        Assert.Equal(contributorId, receipt.UserId);
        Assert.Equal(fileId, receipt.RemoteFileId);
        Assert.Equal(1, receipt.RemoteFileVersion);
    }

    [Fact]
    public async Task ChangedTextBackup_PreservesImmutableVersionHistory()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("backup-text", "backup-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var key = Guid.NewGuid().ToString("D");
        var firstContent = "first"u8.ToArray();
        var modified = new DateTimeOffset(2026, 9, 2, 4, 0, 0, TimeSpan.Zero);
        var firstSession = await CreateBackupSessionAsync(
            client, rootId, "notes.txt", key, "Documents/notes.txt", firstContent,
            modified, "NEW", null, null, "text/plain");
        var fileId = (await UploadAndCompleteAsync(client, firstSession, firstContent)).GetProperty("id").GetGuid();

        var secondContent = "second"u8.ToArray();
        var secondSession = await CreateBackupSessionAsync(
            client, rootId, "notes.txt", key, "Documents/notes.txt", secondContent,
            modified.AddMinutes(1), "CHANGED", fileId, 1, "text/plain");
        await UploadAndCompleteAsync(client, secondSession, secondContent);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var versions = await database.FileVersionRecords
            .Where(version => version.FileEntryId == fileId)
            .OrderBy(version => version.Version)
            .ToListAsync();
        Assert.Equal([1L, 2L], versions.Select(version => version.Version));
        Assert.Equal(Sha(firstContent), versions[0].Sha256);
        Assert.Equal(Sha(secondContent), versions[1].Sha256);
    }

    private static async Task<JsonElement> CompareAsync(
        HttpClient client,
        Guid folderId,
        string key,
        byte[] content,
        DateTimeOffset modifiedAt)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/backup/compare",
            new
            {
                destinationFolderId = folderId,
                items = new[]
                {
                    new
                    {
                        localDocumentKey = key,
                        relativePath = "Photos/photo.jpg",
                        size = content.LongLength,
                        modifiedAt,
                        checksum = Sha(content),
                    },
                },
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
    }

    private static async Task<Guid> CreateBackupSessionAsync(
        HttpClient client,
        Guid folderId,
        string fileName,
        string key,
        string relativePath,
        byte[] content,
        DateTimeOffset modifiedAt,
        string decision,
        Guid? expectedRemoteFileId,
        long? expectedRemoteFileVersion,
        string contentType = "image/jpeg")
    {
        using var request = CreateBackupSessionRequest(
            folderId,
            fileName,
            key,
            relativePath,
            content,
            modifiedAt,
            decision,
            expectedRemoteFileId,
            expectedRemoteFileVersion,
            contentType);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static HttpRequestMessage CreateBackupSessionRequest(
        Guid folderId,
        string fileName,
        string key,
        string relativePath,
        byte[] content,
        DateTimeOffset modifiedAt,
        string decision,
        Guid? expectedRemoteFileId,
        long? expectedRemoteFileVersion,
        string contentType = "image/jpeg")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/upload-sessions")
        {
            Content = JsonContent.Create(new
            {
                destinationFolderId = folderId,
                fileName,
                size = content.LongLength,
                contentType,
                sha256 = Sha(content),
                backup = new
                {
                    localDocumentKey = key,
                    relativePath,
                    modifiedAt,
                    decision,
                    expectedRemoteFileId,
                    expectedRemoteFileVersion,
                },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return request;
    }

    private static async Task<JsonElement> UploadAndCompleteAsync(HttpClient client, Guid sessionId, byte[] content)
    {
        await UploadOnlyAsync(client, sessionId, content);

        using var complete = await client.PostAsync($"/api/v1/upload-sessions/{sessionId}/complete", null);
        complete.EnsureSuccessStatusCode();
        return await complete.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task UploadOnlyAsync(HttpClient client, Guid sessionId, byte[] content)
    {
        var chunk = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/upload-sessions/{sessionId}/chunks")
        {
            Content = new ByteArrayContent(content),
        };
        chunk.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        chunk.Headers.Add("Upload-Offset", "0");
        chunk.Headers.Add("X-Chunk-Sha256", Sha(content));
        using (var response = await client.SendAsync(chunk))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<Guid> GetRootIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/files");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parentId").GetGuid();
    }

    private static string Sha(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
