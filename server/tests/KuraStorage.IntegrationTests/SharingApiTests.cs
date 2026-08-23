using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using KuraStorage.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class SharingApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task ShareValidation_RejectsRootSelfDuplicateAndUnauthorizedManagement_AndKeepsAdminNonImplicit()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("boundary-owner", "owner-password");
        var viewerAuth = await fixture.CreateAuthenticatedClientAsync("boundary-viewer", "viewer-password");
        var otherAuth = await fixture.CreateAuthenticatedClientAsync("boundary-other", "other-password");
        var adminAuth = await fixture.CreateAuthenticatedClientAsync(
            "boundary-admin", "admin-password", UserRole.Admin);
        using var owner = ownerAuth.Client;
        using var viewer = viewerAuth.Client;
        using var other = otherAuth.Client;
        using var admin = adminAuth.Client;
        var ownerId = await UserIdAsync("BOUNDARY-OWNER");
        var viewerId = await UserIdAsync("BOUNDARY-VIEWER");
        var otherId = await UserIdAsync("BOUNDARY-OTHER");
        var rootId = await RootIdAsync(owner);
        var folderId = await CreateFolderAsync(owner, rootId, "BoundaryFolder");

        using (var rootShare = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = rootId,
                members = new[] { new { userId = viewerId, permission = "VIEWER" } },
            }))
        {
            Assert.Equal(HttpStatusCode.NotFound, rootShare.StatusCode);
            await AssertErrorAsync(rootShare, "SHARE_NOT_FOUND");
        }

        using (var selfShare = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new[] { new { userId = ownerId, permission = "VIEWER" } },
            }))
        {
            Assert.Equal(HttpStatusCode.Conflict, selfShare.StatusCode);
            await AssertErrorAsync(selfShare, "SHARE_OPERATION_NOT_ALLOWED");
        }

        using (var duplicateMembers = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new object[]
                {
                    new { userId = viewerId, permission = "VIEWER" },
                    new { userId = viewerId, permission = "EDITOR" },
                },
            }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, duplicateMembers.StatusCode);
            await AssertErrorAsync(duplicateMembers, "VALIDATION_FAILED");
        }

        Guid shareId;
        using (var created = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new[] { new { userId = viewerId, permission = "VIEWER" } },
            }))
        {
            created.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
            shareId = json.RootElement.GetProperty("id").GetGuid();
        }

        using (var duplicateShare = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new[] { new { userId = otherId, permission = "VIEWER" } },
            }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicateShare.StatusCode);
            await AssertErrorAsync(duplicateShare, "SHARE_CONFLICT");
        }

        using (var viewerManagement = await viewer.PutAsJsonAsync(
            $"/api/v1/shares/{shareId}/members/{otherId}",
            new { permission = "EDITOR" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, viewerManagement.StatusCode);
            await AssertErrorAsync(viewerManagement, "SHARE_NOT_FOUND");
        }

        using (var missingMember = await owner.DeleteAsync($"/api/v1/shares/{shareId}/members/{otherId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missingMember.StatusCode);
            await AssertErrorAsync(missingMember, "SHARE_MEMBER_NOT_FOUND");
        }

        using var adminFile = await admin.GetAsync($"/api/v1/files/{folderId}");
        Assert.Equal(HttpStatusCode.NotFound, adminFile.StatusCode);
    }

    [Fact]
    public async Task Candidates_ExcludeDisabledButIncludeSecurityLockedActiveUsers()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("candidate-owner", "owner-password");
        _ = await fixture.CreateAuthenticatedClientAsync("candidate-disabled", "disabled-password");
        _ = await fixture.CreateAuthenticatedClientAsync("candidate-locked", "locked-password");
        using var owner = ownerAuth.Client;
        var disabledId = await UserIdAsync("CANDIDATE-DISABLED");
        var lockedId = await UserIdAsync("CANDIDATE-LOCKED");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Users.Where(user => user.Id == disabledId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Status, UserStatus.Disabled));
            var locked = await database.Users.SingleAsync(user => user.Id == lockedId);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                locked.RecordFailedLogin(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15), 10);
            }

            await database.SaveChangesAsync();
        }

        using var response = await owner.GetAsync("/api/v1/shares/candidates");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), item => item.GetProperty("userId").GetGuid() == disabledId);
        Assert.Contains(json.RootElement.EnumerateArray(), item => item.GetProperty("userId").GetGuid() == lockedId);
    }

    [Fact]
    public async Task SharedFolderPage_WithOneHundredEntries_ReturnsPermissionForEveryItem()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("batch-owner", "owner-password");
        var recipientAuth = await fixture.CreateAuthenticatedClientAsync("batch-recipient", "recipient-password");
        using var owner = ownerAuth.Client;
        using var recipient = recipientAuth.Client;
        var recipientId = await UserIdAsync("BATCH-RECIPIENT");
        var rootId = await RootIdAsync(owner);
        var folderId = await CreateFolderAsync(owner, rootId, "BatchFolder");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var ownerId = await database.FileEntries
                .Where(entry => entry.Id == folderId)
                .Select(entry => entry.OwnerUserId)
                .SingleAsync();
            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < 100; index++)
            {
                database.FileEntries.Add(FileEntry.CreateFile(
                    Guid.NewGuid(),
                    ownerId,
                    folderId,
                    FileName.Create($"item-{index:D3}.txt"),
                    RelativeStoragePath.Create($"users/{ownerId:N}/files/BatchFolder/item-{index:D3}.txt"),
                    "text/plain",
                    index,
                    now));
            }

            await database.SaveChangesAsync();
        }

        using (var created = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new[] { new { userId = recipientId, permission = "VIEWER" } },
            }))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await recipient.GetAsync($"/api/v1/files?parentId={folderId}&pageSize=100");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(100, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.All(
            json.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("VIEWER", item.GetProperty("permission").GetString()));
    }

    [Fact]
    public async Task ShareFlow_ManagesMembersAndGrantsReadAccessWithoutSiblingLeakage()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("share-owner", "owner-password");
        var recipientAuth = await fixture.CreateAuthenticatedClientAsync("share-recipient", "recipient-password");
        var managerAuth = await fixture.CreateAuthenticatedClientAsync("share-manager", "manager-password");
        using var owner = ownerAuth.Client;
        using var recipient = recipientAuth.Client;
        using var manager = managerAuth.Client;
        var recipientId = await UserIdAsync("SHARE-RECIPIENT");
        var managerId = await UserIdAsync("SHARE-MANAGER");

        var rootId = await RootIdAsync(owner);
        var folderId = await CreateFolderAsync(owner, rootId, "SharedFolder");
        var childId = await UploadAsync(owner, folderId, "inside.txt", [1, 2, 3]);
        var siblingId = await UploadAsync(owner, rootId, "private.txt", [4, 5, 6]);

        using (var candidates = await owner.GetAsync("/api/v1/shares/candidates"))
        {
            candidates.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await candidates.Content.ReadAsStreamAsync());
            Assert.Contains(json.RootElement.EnumerateArray(), item => item.GetProperty("userId").GetGuid() == recipientId);
            Assert.DoesNotContain(json.RootElement.EnumerateArray(), item =>
                string.Equals(item.GetProperty("displayName").GetString(), "share-owner", StringComparison.Ordinal));
        }

        Guid shareId;
        using (var created = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new object[]
                {
                    new { userId = recipientId, permission = "VIEWER" },
                    new { userId = managerId, permission = "MANAGER" },
                },
            }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
            shareId = json.RootElement.GetProperty("id").GetGuid();
        }

        using (var received = await recipient.GetAsync("/api/v1/shares?scope=received&targetType=FOLDER"))
        {
            received.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await received.Content.ReadAsStreamAsync());
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(folderId, item.GetProperty("targetEntryId").GetGuid());
            Assert.Equal("VIEWER", item.GetProperty("permission").GetString());
        }

        using (var children = await recipient.GetAsync($"/api/v1/files?parentId={folderId}"))
        {
            children.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await children.Content.ReadAsStreamAsync());
            var child = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(childId, child.GetProperty("id").GetGuid());
            Assert.Equal("VIEWER", child.GetProperty("permission").GetString());
            Assert.Equal("INHERITED", child.GetProperty("permissionSource").GetString());
            Assert.Equal(folderId, child.GetProperty("shareTargetId").GetGuid());
        }

        Guid directShareId;
        using (var directShare = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = childId,
                members = new[] { new { userId = recipientId, permission = "EDITOR" } },
            }))
        {
            directShare.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await directShare.Content.ReadAsStreamAsync());
            directShareId = json.RootElement.GetProperty("id").GetGuid();
        }

        using (var strongest = await recipient.GetAsync($"/api/v1/files/{childId}"))
        {
            strongest.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await strongest.Content.ReadAsStreamAsync());
            Assert.Equal("EDITOR", json.RootElement.GetProperty("permission").GetString());
            Assert.Equal("DIRECT", json.RootElement.GetProperty("permissionSource").GetString());
        }

        using (var removeDirect = await owner.DeleteAsync($"/api/v1/shares/{directShareId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, removeDirect.StatusCode);
        }

        using (var inheritedRemains = await recipient.GetAsync($"/api/v1/files/{childId}"))
        {
            inheritedRemains.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await inheritedRemains.Content.ReadAsStreamAsync());
            Assert.Equal("VIEWER", json.RootElement.GetProperty("permission").GetString());
            Assert.Equal("INHERITED", json.RootElement.GetProperty("permissionSource").GetString());
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{childId}/content"))
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
            using var download = await recipient.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, download.StatusCode);
            Assert.Equal([2, 3], await download.Content.ReadAsByteArrayAsync());
        }

        using (var hiddenSibling = await recipient.GetAsync($"/api/v1/files/{siblingId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenSibling.StatusCode);
        }

        using (var update = await manager.PutAsJsonAsync(
            $"/api/v1/shares/{shareId}/members/{recipientId}",
            new { permission = "EDITOR" }))
        {
            update.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await update.Content.ReadAsStreamAsync());
            Assert.Contains(
                json.RootElement.GetProperty("members").EnumerateArray(),
                member => member.GetProperty("userId").GetGuid() == recipientId &&
                    member.GetProperty("permission").GetString() == "EDITOR");
        }

        using (var remove = await manager.DeleteAsync($"/api/v1/shares/{shareId}/members/{recipientId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        }

        using (var revoked = await recipient.GetAsync($"/api/v1/files/{childId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        }

        using (var delete = await owner.DeleteAsync($"/api/v1/shares/{shareId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.False(await database.Shares.AnyAsync(share => share.Id == shareId));
        Assert.Contains(await database.AuditLogs.ToListAsync(), audit =>
            audit.Action == "SHARE_DELETE" && audit.TargetId == shareId.ToString());
    }

    [Fact]
    public async Task ConcurrentMemberUpdates_TranslateStaleRowVersionToShareConflict()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("concurrent-share-owner", "owner-password");
        _ = await fixture.CreateAuthenticatedClientAsync("concurrent-share-member", "member-password");
        using var owner = ownerAuth.Client;
        var memberId = await UserIdAsync("CONCURRENT-SHARE-MEMBER");
        var rootId = await RootIdAsync(owner);
        var folderId = await CreateFolderAsync(owner, rootId, "ConcurrentShare");
        Guid shareId;
        using (var created = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new[] { new { userId = memberId, permission = "VIEWER" } },
            }))
        {
            created.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
            shareId = json.RootElement.GetProperty("id").GetGuid();
        }

        await using var firstScope = fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = fixture.Factory.Services.CreateAsyncScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IShareRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IShareRepository>();
        var first = (await firstRepository.FindByIdAsync(shareId, CancellationToken.None))!;
        var second = (await secondRepository.FindByIdAsync(shareId, CancellationToken.None))!;
        first.SetMemberPermission(memberId, SharePermission.Editor, DateTimeOffset.UtcNow);
        second.SetMemberPermission(memberId, SharePermission.Manager, DateTimeOffset.UtcNow.AddSeconds(1));

        await using (var transaction = await firstRepository.BeginTransactionAsync(CancellationToken.None))
        {
            await firstRepository.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using (var transaction = await secondRepository.BeginTransactionAsync(CancellationToken.None))
        {
            await Assert.ThrowsAsync<SharePersistenceConflictException>(
                () => secondRepository.SaveChangesAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task CreateFileShare_ContributorAndUnrelatedAccessAreRejectedWithoutDisclosure()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("file-share-owner", "owner-password");
        var recipientAuth = await fixture.CreateAuthenticatedClientAsync("file-share-recipient", "recipient-password");
        var unrelatedAuth = await fixture.CreateAuthenticatedClientAsync("file-share-unrelated", "unrelated-password");
        using var owner = ownerAuth.Client;
        using var recipient = recipientAuth.Client;
        using var unrelated = unrelatedAuth.Client;
        var recipientId = await UserIdAsync("FILE-SHARE-RECIPIENT");
        var rootId = await RootIdAsync(owner);
        var fileId = await UploadAsync(owner, rootId, "direct.txt", [7, 8]);

        using (var invalid = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = fileId,
                members = new[] { new { userId = recipientId, permission = "CONTRIBUTOR" } },
            }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            await AssertErrorAsync(invalid, "INVALID_SHARE_PERMISSION");
        }

        Guid shareId;
        using (var created = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = fileId,
                members = new[] { new { userId = recipientId, permission = "VIEWER" } },
            }))
        {
            created.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
            shareId = json.RootElement.GetProperty("id").GetGuid();
        }

        using var allowed = await recipient.GetAsync($"/api/v1/files/{fileId}");
        allowed.EnsureSuccessStatusCode();
        using var hiddenShare = await unrelated.GetAsync($"/api/v1/shares/{shareId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenShare.StatusCode);
        await AssertErrorAsync(hiddenShare, "SHARE_NOT_FOUND");
    }

    private async Task<Guid> UserIdAsync(string normalizedUsername)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>().Users
            .Where(user => user.UsernameNormalized == normalizedUsername)
            .Select(user => user.Id)
            .SingleAsync();
    }

    private static async Task<Guid> RootIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/files");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("parentId").GetGuid();
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, Guid parentId, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/folders", new { parentId, name });
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid parentId, string name, byte[] content)
    {
        using var multipart = new MultipartFormDataContent
        {
            { new StringContent(parentId.ToString()), "destinationFolderId" },
            { new StringContent(name), "fileName" },
            { new StringContent(content.Length.ToString()), "size" },
            { new StringContent(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()), "sha256" },
            { new ByteArrayContent(content), "file", name },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/upload") { Content = multipart };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("requestId").GetString()));
    }
}
