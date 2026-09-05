using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using KuraStorage.Domain.Organization;
using KuraStorage.Domain.Identity;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class OrganizationApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task FavoritesTagsAndTagSearch_AreActorScopedIdempotentAndPermissionAware()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var authentication = await fixture.CreateAuthenticatedClientAsync($"organization-{suffix}", "password");
        using var client = authentication.Client;
        using (var provision = await client.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid entryId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var user = await database.Users.SingleAsync(user => user.UsernameNormalized == $"ORGANIZATION-{suffix}".ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == user.Id && entry.ParentId == null);
            var name = FileName.Create($"organization-{suffix}.txt");
            var entry = FileEntry.CreateFile(
                Guid.NewGuid(),
                user.Id,
                root.Id,
                name,
                RelativeStoragePath.Create(root.RelativePath).Append(name),
                "text/plain",
                12,
                DateTimeOffset.UtcNow);
            entryId = entry.Id;
            database.FileEntries.Add(entry);
            await database.SaveChangesAsync();
        }

        Guid tagId;
        using (var create = await client.PostAsJsonAsync("/api/v1/tags", new { name = "  Cafe\u0301  " }))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            tagId = json.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("Café", json.RootElement.GetProperty("name").GetString());
        }

        using (var duplicate = await client.PostAsJsonAsync("/api/v1/tags", new { name = "café" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using var json = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
            Assert.Equal("TAG_NAME_CONFLICT", json.RootElement.GetProperty("code").GetString());
        }

        var favoriteResponses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => client.PutAsync($"/api/v1/favorites/{entryId}", null)));
        var attachResponses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => client.PutAsync($"/api/v1/files/{entryId}/tags/{tagId}", null)));
        foreach (var response in favoriteResponses.Concat(attachResponses))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            response.Dispose();
        }

        Guid secondTagId;
        using (var createSecond = await client.PostAsJsonAsync("/api/v1/tags", new { name = "Second" }))
        {
            createSecond.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await createSecond.Content.ReadAsStringAsync());
            secondTagId = json.RootElement.GetProperty("id").GetGuid();
        }
        using (var attachSecond = await client.PutAsync($"/api/v1/files/{entryId}/tags/{secondTagId}", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, attachSecond.StatusCode);
        }

        using (var favorites = await client.GetAsync("/api/v1/favorites?page=1&pageSize=50"))
        {
            favorites.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await favorites.Content.ReadAsStringAsync());
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(entryId, item.GetProperty("id").GetGuid());
            Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        }

        using (var state = await client.GetAsync($"/api/v1/files/{entryId}/organization"))
        {
            state.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("isFavorite").GetBoolean());
            var tagIds = json.RootElement.GetProperty("tags").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()).ToArray();
            Assert.Equal(2, tagIds.Length);
            Assert.Contains(tagId, tagIds);
            Assert.Contains(secondTagId, tagIds);
        }

        using (var search = await client.GetAsync($"/api/v1/search?tagId={tagId}"))
        {
            search.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            Assert.Equal(entryId, Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var andSearch = await client.GetAsync($"/api/v1/search?tagId={tagId}&tagId={secondTagId}&entryType=FILE"))
        {
            andSearch.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await andSearch.Content.ReadAsStringAsync());
            Assert.Equal(entryId, Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var duplicateFilter = await client.GetAsync($"/api/v1/search?tagId={tagId}&tagId={tagId}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, duplicateFilter.StatusCode);
        }

        var foreignAuthentication =
            await fixture.CreateAuthenticatedClientAsync($"organization-foreign-{suffix}", "password");
        using var foreignClient = foreignAuthentication.Client;
        Guid foreignTagId;
        using (var foreignTag = await foreignClient.PostAsJsonAsync("/api/v1/tags", new { name = "Foreign" }))
        {
            foreignTag.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await foreignTag.Content.ReadAsStringAsync());
            foreignTagId = json.RootElement.GetProperty("id").GetGuid();
        }
        using (var foreignTagSearch = await client.GetAsync($"/api/v1/search?tagId={foreignTagId}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, foreignTagSearch.StatusCode);
        }

        using (var invalidSearch = await client.GetAsync($"/api/v1/search?tagId={Guid.NewGuid()}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidSearch.StatusCode);
            using var json = JsonDocument.Parse(await invalidSearch.Content.ReadAsStringAsync());
            Assert.Equal("INVALID_SEARCH_FILTER", json.RootElement.GetProperty("code").GetString());
        }

        using (var rename = await client.PatchAsJsonAsync($"/api/v1/tags/{tagId}", new { name = "Projects" }))
        {
            rename.EnsureSuccessStatusCode();
        }

        using (var detach = await client.DeleteAsync($"/api/v1/files/{entryId}/tags/{tagId}"))
        using (var unfavorite = await client.DeleteAsync($"/api/v1/favorites/{entryId}"))
        using (var delete = await client.DeleteAsync($"/api/v1/tags/{tagId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, detach.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, unfavorite.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using (var deleteSecond = await client.DeleteAsync($"/api/v1/tags/{secondTagId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteSecond.StatusCode);
        }
        using (var deletedTagSearch = await client.GetAsync($"/api/v1/search?tagId={tagId}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, deletedTagSearch.StatusCode);
        }

        await using var verificationScope = fixture.Factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(0, await verification.FavoriteEntries.CountAsync(item => item.EntryId == entryId));
        Assert.Equal(0, await verification.EntryTags.CountAsync(item => item.EntryId == entryId));
        Assert.Equal(0, await verification.Tags.CountAsync(item => item.Id == tagId));
        Assert.DoesNotContain(
            fixture.LogMessages,
            message => message.Contains("Café", StringComparison.OrdinalIgnoreCase) ||
                message.Contains($"organization-{suffix}.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrganizationEndpoints_RejectAuthenticationBodiesAndInvalidPaging()
    {
        using var anonymous = fixture.Factory.CreateClient();
        using var unauthenticated = await anonymous.GetAsync("/api/v1/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var authentication = await fixture.CreateAuthenticatedClientAsync($"organization-invalid-{Guid.NewGuid():N}", "password");
        using var client = authentication.Client;
        using var paging = await client.GetAsync("/api/v1/favorites?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, paging.StatusCode);
        using var body = await client.PutAsJsonAsync($"/api/v1/favorites/{Guid.NewGuid()}", new { userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
    }

    [Fact]
    public async Task SharedViewer_ReevaluatesRevocationMissingAndTrashWhileRetainingPrivateRelations()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"organization-owner-{suffix}";
        var viewerName = $"organization-viewer-{suffix}";
        var ownerAuthentication = await fixture.CreateAuthenticatedClientAsync(ownerName, "password");
        var viewerAuthentication = await fixture.CreateAuthenticatedClientAsync(viewerName, "password");
        using var ownerClient = ownerAuthentication.Client;
        using var viewerClient = viewerAuthentication.Client;
        foreach (var client in new[] { ownerClient, viewerClient })
        {
            using var provision = await client.GetAsync("/api/v1/files");
            provision.EnsureSuccessStatusCode();
        }

        Guid entryId;
        Guid shareId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var viewer = await database.Users.SingleAsync(user => user.UsernameNormalized == viewerName.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var name = FileName.Create($"shared-{suffix}.txt");
            var entry = FileEntry.CreateFile(Guid.NewGuid(), owner.Id, root.Id, name,
                RelativeStoragePath.Create(root.RelativePath).Append(name), "text/plain", 1, DateTimeOffset.UtcNow);
            var share = new Share(Guid.NewGuid(), entry.Id, owner.Id, DateTimeOffset.UtcNow);
            share.AddMember(viewer.Id, SharePermission.Viewer, DateTimeOffset.UtcNow);
            entryId = entry.Id;
            shareId = share.Id;
            database.AddRange(entry, share);
            await database.SaveChangesAsync();
        }

        Guid tagId;
        using (var tag = await viewerClient.PostAsJsonAsync("/api/v1/tags", new { name = "Viewer private" }))
        {
            tag.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await tag.Content.ReadAsStringAsync());
            tagId = json.RootElement.GetProperty("id").GetGuid();
        }

        using (var favorite = await viewerClient.PutAsync($"/api/v1/favorites/{entryId}", null))
        using (var attach = await viewerClient.PutAsync($"/api/v1/files/{entryId}/tags/{tagId}", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, favorite.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);
        }

        Guid raceTagId;
        using (var raceTag = await viewerClient.PostAsJsonAsync("/api/v1/tags", new { name = "Race private" }))
        {
            raceTag.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await raceTag.Content.ReadAsStringAsync());
            raceTagId = json.RootElement.GetProperty("id").GetGuid();
        }

        var revokeTask = ownerClient.DeleteAsync($"/api/v1/shares/{shareId}");
        var attachDuringRevokeTask = viewerClient.PutAsync($"/api/v1/files/{entryId}/tags/{raceTagId}", null);
        using (var revoked = await revokeTask)
        using (var attachDuringRevoke = await attachDuringRevokeTask)
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            Assert.Contains(attachDuringRevoke.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound });
        }

        using (var hidden = await viewerClient.GetAsync("/api/v1/favorites"))
        using (var hiddenState = await viewerClient.GetAsync($"/api/v1/files/{entryId}/organization"))
        using (var denied = await viewerClient.PutAsync($"/api/v1/favorites/{entryId}", null))
        {
            hidden.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
            Assert.Equal(0, page.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, hiddenState.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var viewer = await database.Users.SingleAsync(user => user.UsernameNormalized == viewerName.ToUpperInvariant());
            var share = new Share(Guid.NewGuid(), entryId, owner.Id, DateTimeOffset.UtcNow);
            share.AddMember(viewer.Id, SharePermission.Viewer, DateTimeOffset.UtcNow);
            database.Shares.Add(share);
            await database.SaveChangesAsync();
        }

        using (var visibleAgain = await viewerClient.GetAsync("/api/v1/favorites"))
        {
            visibleAgain.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await visibleAgain.Content.ReadAsStringAsync());
            Assert.Equal(entryId, Assert.Single(page.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        var deleteTagTask = viewerClient.DeleteAsync($"/api/v1/tags/{raceTagId}");
        var attachDuringDeleteTask = viewerClient.PutAsync($"/api/v1/files/{entryId}/tags/{raceTagId}", null);
        using (var deletedTag = await deleteTagTask)
        using (var attachDuringDelete = await attachDuringDeleteTask)
        {
            Assert.Equal(HttpStatusCode.NoContent, deletedTag.StatusCode);
            Assert.Contains(attachDuringDelete.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound });
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(0, await database.Tags.CountAsync(tag => tag.Id == raceTagId));
            Assert.Equal(0, await database.EntryTags.CountAsync(relation => relation.TagId == raceTagId));
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entry = await database.FileEntries.SingleAsync(item => item.Id == entryId);
            var firstObservation = Guid.NewGuid();
            var detectedAt = DateTimeOffset.UtcNow;
            entry.MarkMissingCandidate(firstObservation, detectedAt);
            entry.ConfirmMissing(Guid.NewGuid(), detectedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
            await database.SaveChangesAsync();
        }

        using (var missingState = await viewerClient.GetAsync($"/api/v1/files/{entryId}/organization"))
        using (var cannotAttach = await viewerClient.PutAsync($"/api/v1/files/{entryId}/tags/{tagId}", null))
        using (var canDetach = await viewerClient.DeleteAsync($"/api/v1/files/{entryId}/tags/{tagId}"))
        {
            missingState.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, cannotAttach.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, canDetach.StatusCode);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entry = await database.FileEntries.SingleAsync(item => item.Id == entryId);
            var now = DateTimeOffset.UtcNow;
            entry.ApplySourceObservation(entry.Size, entry.MimeType, now, null, now, contentChanged: false);
            entry.Trash(RelativeStoragePath.Create($"trash/{entry.OwnerUserId:N}/{entry.Id:N}"), now.AddSeconds(1));
            await database.SaveChangesAsync();
        }

        using var trashed = await viewerClient.GetAsync("/api/v1/favorites");
        trashed.EnsureSuccessStatusCode();
        using var trashedPage = JsonDocument.Parse(await trashed.Content.ReadAsStringAsync());
        Assert.Equal(0, trashedPage.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task TagAndEntryLimits_AreEnforcedWithoutPartialRelations()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"organization-limits-{suffix}";
        var authentication = await fixture.CreateAuthenticatedClientAsync(username, "password");
        using var client = authentication.Client;
        using (var provision = await client.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid entryId;
        Guid overflowTagId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var user = await database.Users.SingleAsync(item => item.UsernameNormalized == username.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == user.Id && entry.ParentId == null);
            var name = FileName.Create($"limits-{suffix}.txt");
            var entry = FileEntry.CreateFile(Guid.NewGuid(), user.Id, root.Id, name,
                RelativeStoragePath.Create(root.RelativePath).Append(name), "text/plain", 1, DateTimeOffset.UtcNow);
            entryId = entry.Id;
            database.FileEntries.Add(entry);
            var now = DateTimeOffset.UtcNow;
            var tags = Enumerable.Range(0, 200)
                .Select(index => Tag.Create(Guid.NewGuid(), user.Id, $"Tag {index:D3}", $"TAG {index:D3}", now))
                .ToArray();
            overflowTagId = tags[20].Id;
            database.Tags.AddRange(tags);
            database.EntryTags.AddRange(tags.Take(20).Select(tag => EntryTag.Create(tag.Id, entry.Id, now)));
            await database.SaveChangesAsync();
        }

        using (var userLimit = await client.PostAsJsonAsync("/api/v1/tags", new { name = "Overflow" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, userLimit.StatusCode);
            using var json = JsonDocument.Parse(await userLimit.Content.ReadAsStringAsync());
            Assert.Equal("TAG_LIMIT_EXCEEDED", json.RootElement.GetProperty("code").GetString());
        }

        using (var entryLimit = await client.PutAsync($"/api/v1/files/{entryId}/tags/{overflowTagId}", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, entryLimit.StatusCode);
            using var json = JsonDocument.Parse(await entryLimit.Content.ReadAsStringAsync());
            Assert.Equal("ENTRY_TAG_LIMIT_EXCEEDED", json.RootElement.GetProperty("code").GetString());
        }

        await using var verificationScope = fixture.Factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(200, await verification.Tags.CountAsync(tag => tag.UserId == verification.Users
            .Where(user => user.UsernameNormalized == username.ToUpperInvariant()).Select(user => user.Id).Single()));
        Assert.Equal(20, await verification.EntryTags.CountAsync(item => item.EntryId == entryId));
    }

    [Fact]
    public async Task EveryReadablePermissionCanAttachOnlyItsOwnTagAndAdminHasNoImplicitAccess()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"organization-permission-owner-{suffix}";
        var permissionUsers = new[]
        {
            ($"organization-viewer-{suffix}", SharePermission.Viewer),
            ($"organization-contributor-{suffix}", SharePermission.Contributor),
            ($"organization-editor-{suffix}", SharePermission.Editor),
            ($"organization-manager-{suffix}", SharePermission.Manager),
        };
        var ownerAuthentication = await fixture.CreateAuthenticatedClientAsync(ownerName, "password");
        var memberAuthentications = new List<AuthenticatedTestClient>();
        foreach (var (name, _) in permissionUsers)
        {
            memberAuthentications.Add(await fixture.CreateAuthenticatedClientAsync(name, "password"));
        }

        var adminAuthentication = await fixture.CreateAuthenticatedClientAsync(
            $"organization-admin-{suffix}", "password", UserRole.Admin);
        using var ownerClient = ownerAuthentication.Client;
        using var adminClient = adminAuthentication.Client;
        foreach (var authentication in memberAuthentications.Prepend(ownerAuthentication).Append(adminAuthentication))
        {
            using var provision = await authentication.Client.GetAsync("/api/v1/files");
            provision.EnsureSuccessStatusCode();
        }

        Guid entryId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var folderName = FileName.Create($"permission-folder-{suffix}");
            var folder = FileEntry.CreateFolder(Guid.NewGuid(), owner.Id, root.Id, folderName,
                RelativeStoragePath.Create(root.RelativePath).Append(folderName), DateTimeOffset.UtcNow);
            var fileName = FileName.Create($"permission-file-{suffix}.txt");
            var file = FileEntry.CreateFile(Guid.NewGuid(), owner.Id, folder.Id, fileName,
                RelativeStoragePath.Create(folder.RelativePath).Append(fileName), "text/plain", 1, DateTimeOffset.UtcNow);
            entryId = file.Id;
            var share = new Share(Guid.NewGuid(), folder.Id, owner.Id, DateTimeOffset.UtcNow);
            foreach (var (name, permission) in permissionUsers)
            {
                var member = await database.Users.SingleAsync(user => user.UsernameNormalized == name.ToUpperInvariant());
                share.AddMember(member.Id, permission, DateTimeOffset.UtcNow);
            }

            database.AddRange(folder, file, share);
            await database.SaveChangesAsync();
        }

        var allClients = memberAuthentications.Select(item => item.Client).Prepend(ownerClient).ToArray();
        foreach (var client in allClients)
        {
            Guid tagId;
            using (var create = await client.PostAsJsonAsync("/api/v1/tags", new { name = $"Private {Guid.NewGuid():N}" }))
            {
                create.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
                tagId = json.RootElement.GetProperty("id").GetGuid();
            }

            using var attach = await client.PutAsync($"/api/v1/files/{entryId}/tags/{tagId}", null);
            Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);
        }

        Guid adminTagId;
        using (var create = await adminClient.PostAsJsonAsync("/api/v1/tags", new { name = "Admin private" }))
        {
            create.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            adminTagId = json.RootElement.GetProperty("id").GetGuid();
        }

        using var denied = await adminClient.PutAsync($"/api/v1/files/{entryId}/tags/{adminTagId}", null);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        foreach (var authentication in memberAuthentications)
        {
            authentication.Client.Dispose();
        }
    }
}
