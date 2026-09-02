using System.Net;
using System.Text.Json;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class UserActivityQueryTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task Api_UsesCurrentPermissionKeysetAndDoesNotGrantAdminImplicitAccess()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"activity-owner-{suffix}";
        var viewerName = $"activity-viewer-{suffix}";
        var adminName = $"activity-admin-{suffix}";
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(ownerName, "password");
        var viewerAuth = await fixture.CreateAuthenticatedClientAsync(viewerName, "password");
        var adminAuth = await fixture.CreateAuthenticatedClientAsync(adminName, "password", UserRole.Admin);
        using var ownerClient = ownerAuth.Client;
        using var viewerClient = viewerAuth.Client;
        using var adminClient = adminAuth.Client;
        using (var provision = await ownerClient.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid shareId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var viewer = await database.Users.SingleAsync(user => user.UsernameNormalized == viewerName.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var now = DateTimeOffset.UtcNow;
            var name = FileName.Create("visible.txt");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), owner.Id, root.Id, name,
                RelativeStoragePath.Create(root.RelativePath).Append(name), "text/plain", 1, now);
            var share = new Share(Guid.NewGuid(), file.Id, owner.Id, now);
            share.AddMember(viewer.Id, SharePermission.Viewer, now);
            shareId = share.Id;
            database.AddRange(file, share);
            database.UserActivities.Add(
                UserActivity.CreateUpload(
                    Context(owner.Id, owner.DisplayName, file, owner.DisplayName, now),
                    1));
            database.UserActivities.Add(
                UserActivity.CreateEdit(
                    Context(owner.Id, owner.DisplayName, file, owner.DisplayName, now.AddSeconds(1)),
                    2,
                    ActivityEditKind.TextSave));
            database.UserActivities.Add(
                UserActivity.CreateUpload(
                    Context(viewer.Id, viewer.DisplayName, file, owner.DisplayName, now.AddSeconds(2)),
                    2));
            await database.SaveChangesAsync();
        }

        using var first = await viewerClient.GetAsync("/api/v1/activities?pageSize=1&type=EDIT");
        first.EnsureSuccessStatusCode();
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstItem = Assert.Single(firstJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("EDIT", firstItem.GetProperty("type").GetString());
        Assert.Equal("TEXT_SAVE", firstItem.GetProperty("editKind").GetString());
        Assert.False(firstItem.TryGetProperty("id", out _));
        Assert.False(firstItem.TryGetProperty("operationId", out _));
        Assert.False(firstItem.TryGetProperty("actorDeviceId", out _));
        Assert.False(firstItem.TryGetProperty("requestId", out _));
        Assert.False(firstItem.TryGetProperty("relativePath", out _));

        using var ownerResponse = await ownerClient.GetAsync("/api/v1/activities?pageSize=1");
        ownerResponse.EnsureSuccessStatusCode();
        using var ownerPage = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
        var cursor = ownerPage.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        using var next = await ownerClient.GetAsync($"/api/v1/activities?pageSize=1&cursor={Uri.EscapeDataString(cursor!)}");
        next.EnsureSuccessStatusCode();
        using var nextPage = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        Assert.Single(nextPage.RootElement.GetProperty("items").EnumerateArray());

        using var adminResponse = await adminClient.GetAsync("/api/v1/activities");
        adminResponse.EnsureSuccessStatusCode();
        using var adminPage = JsonDocument.Parse(await adminResponse.Content.ReadAsStringAsync());
        Assert.Empty(adminPage.RootElement.GetProperty("items").EnumerateArray());

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shares WHERE id = {shareId}");
        }

        using var revoked = await viewerClient.GetAsync("/api/v1/activities");
        revoked.EnsureSuccessStatusCode();
        using var revokedPage = JsonDocument.Parse(await revoked.Content.ReadAsStringAsync());
        var actorOnly = Assert.Single(revokedPage.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("UPLOAD", actorOnly.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, actorOnly.GetProperty("targetEntryId").ValueKind);
    }

    [Theory]
    [InlineData("/api/v1/activities?type=UNKNOWN")]
    [InlineData("/api/v1/activities?pageSize=0")]
    [InlineData("/api/v1/activities?pageSize=101")]
    [InlineData("/api/v1/activities?cursor=broken")]
    public async Task Api_RejectsInvalidQuery(string path)
    {
        var auth = await fixture.CreateAuthenticatedClientAsync($"activity-invalid-{Guid.NewGuid():N}", "password");
        using var client = auth.Client;
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ActivityQueryErrorCodes.InvalidRequest, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Api_RequiresAuthentication()
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/activities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_PurgedSnapshotIsVisibleOnlyToActorAndSnapshotOwner()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"purged-owner-{suffix}";
        var actorName = $"purged-actor-{suffix}";
        var strangerName = $"purged-stranger-{suffix}";
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(ownerName, "password");
        var actorAuth = await fixture.CreateAuthenticatedClientAsync(actorName, "password");
        var strangerAuth = await fixture.CreateAuthenticatedClientAsync(strangerName, "password");
        using var ownerClient = ownerAuth.Client;
        using var actorClient = actorAuth.Client;
        using var strangerClient = strangerAuth.Client;
        using (var provision = await ownerClient.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var actor = await database.Users.SingleAsync(user => user.UsernameNormalized == actorName.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var name = FileName.Create("purged-snapshot.txt");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), owner.Id, root.Id, name,
                RelativeStoragePath.Create(root.RelativePath).Append(name), "text/plain", 1, DateTimeOffset.UtcNow);
            database.Add(file);
            database.UserActivities.Add(
                UserActivity.CreateDelete(
                    Context(actor.Id, actor.DisplayName, file, owner.DisplayName, DateTimeOffset.UtcNow),
                    ActivityDeleteKind.Purged));
            await database.SaveChangesAsync();
            database.FileEntries.Remove(file);
            await database.SaveChangesAsync();
        }

        foreach (var client in new[] { ownerClient, actorClient })
        {
            using var response = await client.GetAsync("/api/v1/activities?type=DELETE");
            response.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("purged-snapshot.txt", item.GetProperty("targetName").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("targetEntryId").ValueKind);
        }

        using var hidden = await strangerClient.GetAsync("/api/v1/activities?type=DELETE");
        hidden.EnsureSuccessStatusCode();
        using var hiddenPage = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
        Assert.Empty(hiddenPage.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Api_ReevaluatesInheritedMultiplePathMoveTrashRestoreAndRevocation()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"visibility-owner-{suffix}";
        var viewerName = $"visibility-viewer-{suffix}";
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(ownerName, "password");
        var viewerAuth = await fixture.CreateAuthenticatedClientAsync(viewerName, "password");
        using var ownerClient = ownerAuth.Client;
        using var viewerClient = viewerAuth.Client;
        using (var provision = await ownerClient.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid fileId;
        Guid sharedFolderId;
        Guid hiddenFolderId;
        Guid rootShareId;
        Guid nestedShareId;
        string sharedPath;
        string hiddenPath;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var viewer = await database.Users.SingleAsync(user => user.UsernameNormalized == viewerName.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var now = DateTimeOffset.UtcNow;
            var sharedName = FileName.Create("Shared");
            var hiddenName = FileName.Create("Hidden");
            var sharedFolder = FileEntry.CreateFolder(
                Guid.NewGuid(), owner.Id, root.Id, sharedName,
                RelativeStoragePath.Create(root.RelativePath).Append(sharedName), now);
            var hiddenFolder = FileEntry.CreateFolder(
                Guid.NewGuid(), owner.Id, root.Id, hiddenName,
                RelativeStoragePath.Create(root.RelativePath).Append(hiddenName), now);
            var fileName = FileName.Create("inherited.txt");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), owner.Id, sharedFolder.Id, fileName,
                RelativeStoragePath.Create(sharedFolder.RelativePath).Append(fileName), "text/plain", 1, now);
            var rootShare = new Share(Guid.NewGuid(), root.Id, owner.Id, now);
            rootShare.AddMember(viewer.Id, SharePermission.Viewer, now);
            var nestedShare = new Share(Guid.NewGuid(), sharedFolder.Id, owner.Id, now);
            nestedShare.AddMember(viewer.Id, SharePermission.Manager, now);
            fileId = file.Id;
            sharedFolderId = sharedFolder.Id;
            hiddenFolderId = hiddenFolder.Id;
            rootShareId = rootShare.Id;
            nestedShareId = nestedShare.Id;
            sharedPath = file.RelativePath;
            hiddenPath = RelativeStoragePath.Create(hiddenFolder.RelativePath).Append(fileName).Value;
            database.AddRange(sharedFolder, hiddenFolder, file, rootShare, nestedShare);
            database.UserActivities.Add(
                UserActivity.CreateUpload(Context(owner.Id, owner.DisplayName, file, owner.DisplayName, now), 1));
            await database.SaveChangesAsync();
        }

        await AssertVisibleAsync(true);
        await ExecuteAsync($"DELETE FROM shares WHERE id = {nestedShareId}");
        await AssertVisibleAsync(true);

        await ExecuteAsync($"UPDATE file_entries SET parent_id = {hiddenFolderId}, relative_path = {hiddenPath} WHERE id = {fileId}");
        await AssertVisibleAsync(true); // root share still grants access after the move.

        await ExecuteAsync($"DELETE FROM shares WHERE id = {rootShareId}");
        await AssertVisibleAsync(false);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var viewer = await database.Users.SingleAsync(user => user.UsernameNormalized == viewerName.ToUpperInvariant());
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var share = new Share(Guid.NewGuid(), sharedFolderId, owner.Id, DateTimeOffset.UtcNow);
            share.AddMember(viewer.Id, SharePermission.Editor, DateTimeOffset.UtcNow);
            database.Add(share);
            await database.SaveChangesAsync();
        }

        await ExecuteAsync($"UPDATE file_entries SET parent_id = {sharedFolderId}, relative_path = {sharedPath}, status = 'ACTIVE' WHERE id = {fileId}");
        await AssertVisibleAsync(true);
        await ExecuteAsync($"UPDATE file_entries SET status = 'TRASHED' WHERE id = {fileId}");
        await AssertVisibleAsync(false);
        await ExecuteAsync($"UPDATE file_entries SET status = 'ACTIVE' WHERE id = {fileId}");
        await AssertVisibleAsync(true);

        async Task ExecuteAsync(FormattableString sql)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(sql);
        }

        async Task AssertVisibleAsync(bool expected)
        {
            using var response = await viewerClient.GetAsync("/api/v1/activities?type=UPLOAD");
            response.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected ? 1 : 0, page.RootElement.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task AdminSearch_CombinesFiltersAndAuditsOnlyClassificationAndCount()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"activity-cli-{suffix}";
        var auth = await fixture.CreateAuthenticatedClientAsync(username, "password");
        using var client = auth.Client;
        using (var provision = await client.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid fileId;
        Guid userId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var user = await database.Users.SingleAsync(item => item.UsernameNormalized == username.ToUpperInvariant());
            userId = user.Id;
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == user.Id && entry.ParentId == null);
            var name = FileName.Create("admin-secret-name.txt");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), user.Id, root.Id, name,
                RelativeStoragePath.Create(root.RelativePath).Append(name), "text/plain", 1, DateTimeOffset.UtcNow);
            fileId = file.Id;
            database.Add(file);
            database.UserActivities.Add(
                UserActivity.CreateUpload(
                    Context(user.Id, user.DisplayName, file, user.DisplayName, DateTimeOffset.UtcNow), 1));
            await database.SaveChangesAsync();
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AdminActivityService>();
            var result = await service.SearchAsync(
                new AdminActivitySearchRequest(
                    ActorUser: username,
                    OwnerUser: username,
                    Type: "UPLOAD",
                    From: DateTimeOffset.UtcNow.AddHours(-1),
                    To: DateTimeOffset.UtcNow.AddHours(1),
                    FileId: fileId),
                "integration-admin",
                CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Single(result.Value!.Items);

            var byId = await service.SearchAsync(
                new AdminActivitySearchRequest(ActorUser: userId.ToString()),
                "integration-admin",
                CancellationToken.None);
            Assert.True(byId.IsSuccess);
            Assert.NotEmpty(byId.Value!.Items);

            var unfiltered = await service.SearchAsync(
                new AdminActivitySearchRequest(),
                "integration-admin",
                CancellationToken.None);
            Assert.True(unfiltered.IsSuccess);

            var unknown = await service.SearchAsync(
                new AdminActivitySearchRequest(ActorUser: Guid.NewGuid().ToString()),
                "integration-admin",
                CancellationToken.None);
            Assert.False(unknown.IsSuccess);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var audit = await database.AuditLogs
                .FirstAsync(item => item.Action == "ACTIVITY_SEARCH" && item.TargetId == "AOTDF-:1");
            Assert.Equal("ADMIN_CLI", audit.ActorType.ToString().Replace("AdminCli", "ADMIN_CLI", StringComparison.Ordinal));
            Assert.Equal("AOTDF-:1", audit.TargetId);
            Assert.DoesNotContain(username, audit.TargetId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("admin-secret-name", audit.TargetId, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            fixture.LogMessages,
            message => message.Contains("admin-secret-name", StringComparison.OrdinalIgnoreCase) ||
                message.Contains(fileId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static UserActivityContext Context(
        Guid actorId,
        string actorName,
        FileEntry file,
        string ownerName,
        DateTimeOffset occurredAt) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ActivityActorSnapshot(actorId, actorName, "test-phone"),
            new ActivityTargetSnapshot(file.Id, file.EntryType, file.Name, file.OwnerUserId, ownerName, file.ParentId),
            occurredAt.ToUniversalTime());
}
