using System.Net;
using System.Text.Json;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class SearchApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task Search_ReturnsContractMetadataWithoutInternalPaths()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("search-api-owner", "owner-password");
        using var client = authenticated.Client;
        using (var provision = await client.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid fileId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var user = await database.Users.SingleAsync(user => user.UsernameNormalized == "SEARCH-API-OWNER");
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == user.Id && entry.ParentId == null);
            var name = FileName.Create("API Report.pdf");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(),
                user.Id,
                root.Id,
                name,
                RelativeStoragePath.Create(root.RelativePath).Append(name),
                "application/pdf",
                123,
                DateTimeOffset.UtcNow);
            fileId = file.Id;
            database.FileEntries.Add(file);
            await database.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/v1/search?q=api%20report&page=1&pageSize=50");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(fileId, item.GetProperty("id").GetGuid());
        Assert.Equal("FILE", item.GetProperty("entryType").GetString());
        Assert.Equal("DOCUMENT", item.GetProperty("fileCategory").GetString());
        Assert.Equal("OWNER", item.GetProperty("permission").GetString());
        Assert.Equal("OWNER", item.GetProperty("permissionSource").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain("relativePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shareId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            fixture.LogMessages,
            message => message.Contains("api%20report", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("API Report.pdf", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/v1/search", "INVALID_SEARCH_QUERY")]
    [InlineData("/api/v1/search?q=a&status=TRASHED", "INVALID_SEARCH_FILTER")]
    [InlineData("/api/v1/search?q=a&ownerUserId=not-a-uuid", "INVALID_SEARCH_FILTER")]
    [InlineData("/api/v1/search?q=a&updatedFrom=not-a-date", "INVALID_SEARCH_FILTER")]
    [InlineData("/api/v1/search?q=a&minSize=-1", "INVALID_SEARCH_FILTER")]
    [InlineData("/api/v1/search?q=a&pageSize=101", "INVALID_SEARCH_FILTER")]
    public async Task Search_InvalidQueryUsesStableError(string path, string expectedCode)
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"search-invalid-{Guid.NewGuid():N}",
            "owner-password");
        using var client = authenticated.Client;

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_ApiClientEnforcesActorScopeFiltersAndImmediateRevocation()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"search-flow-owner-{suffix}";
        var recipientName = $"search-flow-recipient-{suffix}";
        var strangerName = $"search-flow-stranger-{suffix}";
        var adminName = $"search-flow-admin-{suffix}";
        var ownerAuthentication = await fixture.CreateAuthenticatedClientAsync(ownerName, "owner-password");
        var recipientAuthentication = await fixture.CreateAuthenticatedClientAsync(recipientName, "recipient-password");
        var strangerAuthentication = await fixture.CreateAuthenticatedClientAsync(strangerName, "stranger-password");
        var adminAuthentication = await fixture.CreateAuthenticatedClientAsync(adminName, "admin-password");
        using var ownerClient = ownerAuthentication.Client;
        using var recipientClient = recipientAuthentication.Client;
        using var strangerClient = strangerAuthentication.Client;
        using var adminClient = adminAuthentication.Client;
        foreach (var client in new[] { ownerClient, recipientClient, strangerClient, adminClient })
        {
            using var provision = await client.GetAsync("/api/v1/files");
            provision.EnsureSuccessStatusCode();
        }

        Guid ownerId;
        Guid sharedFolderId;
        Guid sharedFileId;
        Guid shareId;
        var now = DateTimeOffset.UtcNow;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var recipient = await database.Users.SingleAsync(user => user.UsernameNormalized == recipientName.ToUpperInvariant());
            var admin = await database.Users.SingleAsync(user => user.UsernameNormalized == adminName.ToUpperInvariant());
            ownerId = owner.Id;
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET role = 'ADMIN' WHERE id = {admin.Id}");
            var ownerRoot = await database.FileEntries.SingleAsync(
                entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var folderName = FileName.Create("api-flow-folder");
            var sharedFolder = FileEntry.CreateFolder(
                Guid.NewGuid(),
                owner.Id,
                ownerRoot.Id,
                folderName,
                RelativeStoragePath.Create(ownerRoot.RelativePath).Append(folderName),
                now);
            var sharedName = FileName.Create("api-flow-shared.pdf");
            var sharedFile = FileEntry.CreateFile(
                Guid.NewGuid(),
                owner.Id,
                sharedFolder.Id,
                sharedName,
                RelativeStoragePath.Create(sharedFolder.RelativePath).Append(sharedName),
                "application/pdf",
                42,
                now);
            var privateName = FileName.Create("api-flow-private.txt");
            var privateFile = FileEntry.CreateFile(
                Guid.NewGuid(),
                owner.Id,
                ownerRoot.Id,
                privateName,
                RelativeStoragePath.Create(ownerRoot.RelativePath).Append(privateName),
                "text/plain",
                7,
                now);
            var share = new Share(Guid.NewGuid(), sharedFolder.Id, owner.Id, now);
            share.AddMember(recipient.Id, SharePermission.Viewer, now);
            sharedFolderId = sharedFolder.Id;
            sharedFileId = sharedFile.Id;
            shareId = share.Id;
            database.AddRange(sharedFolder, sharedFile, privateFile, share);
            await database.SaveChangesAsync();
        }

        using (var ownerResponse = await ownerClient.GetAsync("/api/v1/search?q=api-flow&pageSize=100"))
        using (var recipientResponse = await recipientClient.GetAsync("/api/v1/search?q=api-flow&pageSize=100"))
        using (var strangerResponse = await strangerClient.GetAsync("/api/v1/search?q=api-flow&pageSize=100"))
        using (var adminResponse = await adminClient.GetAsync("/api/v1/search?q=api-flow&pageSize=100"))
        {
            ownerResponse.EnsureSuccessStatusCode();
            recipientResponse.EnsureSuccessStatusCode();
            strangerResponse.EnsureSuccessStatusCode();
            adminResponse.EnsureSuccessStatusCode();
            using var ownerPage = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
            using var recipientPage = JsonDocument.Parse(await recipientResponse.Content.ReadAsStringAsync());
            using var strangerPage = JsonDocument.Parse(await strangerResponse.Content.ReadAsStringAsync());
            using var adminPage = JsonDocument.Parse(await adminResponse.Content.ReadAsStringAsync());
            Assert.Equal(3, ownerPage.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, recipientPage.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, strangerPage.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, adminPage.RootElement.GetProperty("totalCount").GetInt32());
            var recipientItems = recipientPage.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal("DIRECT", recipientItems.Single(item => item.GetProperty("id").GetGuid() == sharedFolderId).GetProperty("permissionSource").GetString());
            Assert.Equal("INHERITED", recipientItems.Single(item => item.GetProperty("id").GetGuid() == sharedFileId).GetProperty("permissionSource").GetString());
        }

        var from = Uri.EscapeDataString(now.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(now.AddMinutes(1).ToString("O"));
        var combinedPath = $"/api/v1/search?q=api-flow&entryType=FILE&category=DOCUMENT&status=ACTIVE&updatedFrom={from}&updatedTo={to}&minSize=42&maxSize=42&ownerUserId={ownerId}&shareTargetId={sharedFolderId}&page=1&pageSize=100";
        using (var filteredResponse = await recipientClient.GetAsync(combinedPath))
        {
            filteredResponse.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
            Assert.Equal(sharedFileId, Assert.Single(page.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shares WHERE id = {shareId}");
        }

        using var revokedResponse = await recipientClient.GetAsync("/api/v1/search?q=api-flow&pageSize=100");
        revokedResponse.EnsureSuccessStatusCode();
        using var revokedPage = JsonDocument.Parse(await revokedResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, revokedPage.RootElement.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(
            fixture.LogMessages,
            message => message.Contains("api-flow", StringComparison.OrdinalIgnoreCase) ||
                message.Contains(ownerName, StringComparison.OrdinalIgnoreCase) ||
                message.Contains(recipientName, StringComparison.OrdinalIgnoreCase));
    }
}
