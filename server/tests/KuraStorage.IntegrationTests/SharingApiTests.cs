using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Application.Maintenance;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using KuraStorage.Domain.Activity;
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

        using (var noOpUpdate = await manager.PutAsJsonAsync(
            $"/api/v1/shares/{shareId}/members/{recipientId}",
            new { permission = "EDITOR" }))
        {
            noOpUpdate.EnsureSuccessStatusCode();
        }

        using (var remove = await manager.DeleteAsync($"/api/v1/shares/{shareId}/members/{recipientId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        }

        using (var repeatedRemove = await manager.DeleteAsync($"/api/v1/shares/{shareId}/members/{recipientId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, repeatedRemove.StatusCode);
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
        var folderActivities = await database.UserActivities
            .Where(activity => activity.TargetEntryId == folderId && activity.ActivityType == UserActivityType.Share)
            .ToListAsync();
        Assert.Equal(5, folderActivities.Count);
        Assert.Equal(2, folderActivities.Count(activity => activity.ShareAction == ActivityShareAction.Created));
        Assert.Single(folderActivities, activity =>
            activity.ShareAction == ActivityShareAction.Updated && activity.RecipientUserId == recipientId);
        Assert.Equal(2, folderActivities.Count(activity => activity.ShareAction == ActivityShareAction.Revoked));
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

        var reloaded = await secondRepository.ReloadAsync(second, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.NotSame(second, reloaded);
        Assert.Equal(
            SharePermission.Editor,
            Assert.Single(reloaded.Members, member => member.UserId == memberId).Permission);
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

    [Fact]
    public async Task SharedFolderMutations_EnforcePermissionMatrixAndSeparateActorFromOwner()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("mutation-owner", "owner-password");
        var viewerAuth = await fixture.CreateAuthenticatedClientAsync("mutation-viewer", "viewer-password");
        var contributorAuth = await fixture.CreateAuthenticatedClientAsync("mutation-contributor", "contributor-password");
        var editorAuth = await fixture.CreateAuthenticatedClientAsync("mutation-editor", "editor-password");
        var managerAuth = await fixture.CreateAuthenticatedClientAsync("mutation-manager", "manager-password");
        using var owner = ownerAuth.Client;
        using var viewer = viewerAuth.Client;
        using var contributor = contributorAuth.Client;
        using var editor = editorAuth.Client;
        using var manager = managerAuth.Client;
        var ownerId = await UserIdAsync("MUTATION-OWNER");
        var viewerId = await UserIdAsync("MUTATION-VIEWER");
        var contributorId = await UserIdAsync("MUTATION-CONTRIBUTOR");
        var editorId = await UserIdAsync("MUTATION-EDITOR");
        var managerId = await UserIdAsync("MUTATION-MANAGER");
        var rootId = await RootIdAsync(owner);
        var sharedFolderId = await CreateFolderAsync(owner, rootId, "MutationShared");

        using (var share = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = sharedFolderId,
                members = new object[]
                {
                    new { userId = viewerId, permission = "VIEWER" },
                    new { userId = contributorId, permission = "CONTRIBUTOR" },
                    new { userId = editorId, permission = "EDITOR" },
                    new { userId = managerId, permission = "MANAGER" },
                },
            }))
        {
            share.EnsureSuccessStatusCode();
        }

        using (var rejectedFolder = await viewer.PostAsJsonAsync(
            "/api/v1/folders", new { parentId = sharedFolderId, name = "ViewerDenied" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, rejectedFolder.StatusCode);
            await AssertErrorAsync(rejectedFolder, "FILE_NOT_FOUND");
        }

        using (var rejectedUpload = await SendUploadAsync(viewer, sharedFolderId, "viewer.bin", [1]))
        {
            Assert.Equal(HttpStatusCode.NotFound, rejectedUpload.StatusCode);
            await AssertErrorAsync(rejectedUpload, "FILE_NOT_FOUND");
        }

        var contributorFolderId = await CreateFolderAsync(contributor, sharedFolderId, "ContributorFolder");
        var contributorFileId = await UploadAsync(contributor, contributorFolderId, "contributor.bin", [2, 3]);
        var directShareId = await CreateShareAsync(owner, contributorFileId, viewerId);
        long initialFileVersion;
        using (var initialDetail = await viewer.GetAsync($"/api/v1/files/{contributorFileId}"))
        {
            initialDetail.EnsureSuccessStatusCode();
            initialFileVersion = (await initialDetail.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("fileVersion").GetInt64();
        }
        using (var rejectedRename = await contributor.PatchAsJsonAsync(
            $"/api/v1/files/{contributorFileId}", new { name = "denied.bin" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, rejectedRename.StatusCode);
            await AssertErrorAsync(rejectedRename, "FILE_NOT_FOUND");
        }

        var destinationId = await CreateFolderAsync(editor, sharedFolderId, "EditorDestination");
        using (var rename = await editor.PatchAsJsonAsync(
            $"/api/v1/files/{contributorFileId}", new { name = "renamed.bin" }))
        {
            rename.EnsureSuccessStatusCode();
        }
        using (var move = await editor.PatchAsJsonAsync(
            $"/api/v1/files/{contributorFileId}", new { parentId = destinationId }))
        {
            move.EnsureSuccessStatusCode();
        }
        using (var trash = await editor.DeleteAsync($"/api/v1/files/{contributorFileId}"))
        {
            Assert.Equal(HttpStatusCode.OK, trash.StatusCode);
        }
        using (var hidden = await viewer.GetAsync($"/api/v1/files/{contributorFileId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        using (var recipientRestore = await manager.PostAsync(
            $"/api/v1/files/{contributorFileId}/restore", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, recipientRestore.StatusCode);
            await AssertErrorAsync(recipientRestore, "FILE_NOT_FOUND");
        }
        using (var restore = await owner.PostAsync($"/api/v1/files/{contributorFileId}/restore", null))
        {
            restore.EnsureSuccessStatusCode();
        }
        using (var managerRename = await manager.PatchAsJsonAsync(
            $"/api/v1/files/{contributorFileId}", new { name = "manager.bin" }))
        {
            managerRename.EnsureSuccessStatusCode();
        }
        using (var directShareRestored = await viewer.GetAsync($"/api/v1/files/{contributorFileId}"))
        {
            directShareRestored.EnsureSuccessStatusCode();
            var item = await directShareRestored.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(contributorFileId, item.GetProperty("id").GetGuid());
            Assert.Equal(ownerId, item.GetProperty("owner").GetProperty("id").GetGuid());
            Assert.Equal(initialFileVersion, item.GetProperty("fileVersion").GetInt64());
            Assert.Equal("DIRECT", item.GetProperty("permissionSource").GetString());
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var createdEntries = await database.FileEntries
            .Where(entry => entry.Id == contributorFolderId || entry.Id == contributorFileId || entry.Id == destinationId)
            .ToListAsync();
        Assert.Equal(3, createdEntries.Count);
        Assert.All(createdEntries, entry => Assert.Equal(ownerId, entry.OwnerUserId));
        var file = Assert.Single(createdEntries, entry => entry.Id == contributorFileId);
        Assert.Equal(destinationId, file.ParentId);
        Assert.Equal("manager.bin", file.Name);
        Assert.True(await database.Shares.AnyAsync(share =>
            share.Id == directShareId && share.TargetEntryId == contributorFileId));
        Assert.Contains(await database.AuditLogs.ToListAsync(), audit =>
            audit.Action == "FOLDER_CREATE" && audit.ActorUserId == contributorId && audit.TargetId == contributorFolderId.ToString());
        Assert.Contains(await database.AuditLogs.ToListAsync(), audit =>
            audit.Action == "FILE_UPLOAD" && audit.ActorUserId == contributorId && audit.TargetId == contributorFileId.ToString());
        Assert.Contains(await database.AuditLogs.ToListAsync(), audit =>
            audit.Action == "FILE_RENAME" && audit.ActorUserId == editorId && audit.TargetId == contributorFileId.ToString());
    }

    [Fact]
    public async Task PurgeAndMissingIndexDeletion_RemoveTargetAndDescendantSharesWithMembers()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("share-delete-owner", "owner-password");
        _ = await fixture.CreateAuthenticatedClientAsync("share-delete-member", "member-password");
        using var owner = ownerAuth.Client;
        var memberId = await UserIdAsync("SHARE-DELETE-MEMBER");
        var rootId = await RootIdAsync(owner);
        var folderId = await CreateFolderAsync(owner, rootId, "ShareDeleteTree");
        var childId = await UploadAsync(owner, folderId, "child.bin", [5]);
        var folderShareId = await CreateShareAsync(owner, folderId, memberId);
        var childShareId = await CreateShareAsync(owner, childId, memberId);

        using (var trash = await owner.DeleteAsync($"/api/v1/files/{folderId}"))
        {
            trash.EnsureSuccessStatusCode();
        }
        using (var purgeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/trash/{folderId}"))
        {
            purgeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var purge = await owner.SendAsync(purgeRequest);
            Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);
        }

        var missingId = await UploadAsync(owner, rootId, "missing-share.bin", [7]);
        var missingShareId = await CreateShareAsync(owner, missingId, memberId);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entry = await database.FileEntries.SingleAsync(item => item.Id == missingId);
            await scope.ServiceProvider.GetRequiredService<IFileStore>().DeleteIfExistsAsync(
                RelativeStoragePath.Create(entry.RelativePath), CancellationToken.None);
            entry.MarkMissingCandidate(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-10));
            entry.ConfirmMissing(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(5));
            await database.SaveChangesAsync();
        }
        using (var deletion = await owner.DeleteAsync($"/api/v1/files/{missingId}/missing-index-entry"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        }

        var retentionFolderId = await CreateFolderAsync(owner, rootId, "RetentionShareDelete");
        var retentionChildId = await UploadAsync(owner, retentionFolderId, "retention-child.bin", [9]);
        var retentionFolderShareId = await CreateShareAsync(owner, retentionFolderId, memberId);
        var retentionChildShareId = await CreateShareAsync(owner, retentionChildId, memberId);
        using (var trash = await owner.DeleteAsync($"/api/v1/files/{retentionFolderId}"))
        {
            trash.EnsureSuccessStatusCode();
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE file_entries SET trashed_at = {DateTimeOffset.UtcNow.AddDays(-31)} WHERE id = {retentionFolderId}");
            await scope.ServiceProvider.GetRequiredService<TrashPurgeRunner>().RunAsync(CancellationToken.None);
        }

        var deletedShareIds = new[]
        {
            folderShareId,
            childShareId,
            missingShareId,
            retentionFolderShareId,
            retentionChildShareId,
        };
        await using var verifyScope = fixture.Factory.Services.CreateAsyncScope();
        var verifyDatabase = verifyScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.False(await verifyDatabase.Shares.AnyAsync(share => deletedShareIds.Contains(share.Id)));
        Assert.False(await verifyDatabase.ShareMembers.AnyAsync(member => deletedShareIds.Contains(member.ShareId)));
    }

    [Fact]
    public async Task Move_ReevaluatesInheritedPathWhileKeepingDirectShare()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("move-share-owner", "owner-password");
        var editorAuth = await fixture.CreateAuthenticatedClientAsync("move-share-editor", "editor-password");
        var observerAuth = await fixture.CreateAuthenticatedClientAsync("move-share-observer", "observer-password");
        using var owner = ownerAuth.Client;
        using var editor = editorAuth.Client;
        using var observer = observerAuth.Client;
        var editorId = await UserIdAsync("MOVE-SHARE-EDITOR");
        var observerId = await UserIdAsync("MOVE-SHARE-OBSERVER");
        var rootId = await RootIdAsync(owner);
        var sourceId = await CreateFolderAsync(owner, rootId, "MoveSharedSource");
        var targetId = await CreateFolderAsync(owner, rootId, "MoveSharedTarget");
        var inheritedOnlyId = await UploadAsync(owner, sourceId, "inherited-only.bin", [1]);
        var directlySharedId = await UploadAsync(owner, sourceId, "direct.bin", [2]);
        using (var sourceShare = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = sourceId,
                members = new object[]
                {
                    new { userId = editorId, permission = "EDITOR" },
                    new { userId = observerId, permission = "VIEWER" },
                },
            }))
        {
            sourceShare.EnsureSuccessStatusCode();
        }
        _ = await CreateShareAsync(owner, targetId, editorId, "EDITOR");
        var directShareId = await CreateShareAsync(owner, directlySharedId, observerId);

        using (var before = await observer.GetAsync($"/api/v1/files/{inheritedOnlyId}"))
        {
            before.EnsureSuccessStatusCode();
        }
        foreach (var fileId in new[] { inheritedOnlyId, directlySharedId })
        {
            using var move = await editor.PatchAsJsonAsync(
                $"/api/v1/files/{fileId}", new { parentId = targetId });
            move.EnsureSuccessStatusCode();
        }

        using (var inheritedRevoked = await observer.GetAsync($"/api/v1/files/{inheritedOnlyId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, inheritedRevoked.StatusCode);
        }
        using (var directRemains = await observer.GetAsync($"/api/v1/files/{directlySharedId}"))
        {
            directRemains.EnsureSuccessStatusCode();
            Assert.Equal("DIRECT", (await directRemains.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("permissionSource").GetString());
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>().Shares
            .AnyAsync(share => share.Id == directShareId && share.TargetEntryId == directlySharedId));
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
        using var response = await SendUploadAsync(client, parentId, name, content);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateShareAsync(
        HttpClient owner,
        Guid targetEntryId,
        Guid memberId,
        string permission = "VIEWER")
    {
        using var response = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId,
                members = new[] { new { userId = memberId, permission } },
            });
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendUploadAsync(
        HttpClient client,
        Guid parentId,
        string name,
        byte[] content)
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
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("requestId").GetString()));
    }
}
