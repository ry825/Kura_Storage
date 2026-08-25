using System.Net;
using System.Net.Http.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KuraStorage.IntegrationTests;

public sealed class RecentFileApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task RecentFileRecord_AfterLockWaitObservesShareRevocation()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"recent-race-owner-{suffix}";
        var memberName = $"recent-race-member-{suffix}";
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(ownerName, "owner-password");
        var memberAuth = await fixture.CreateAuthenticatedClientAsync(memberName, "member-password");
        using var ownerClient = ownerAuth.Client;
        using var memberClient = memberAuth.Client;
        using (var provision = await ownerClient.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        Guid fileId;
        Guid shareId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var member = await database.Users.SingleAsync(user => user.UsernameNormalized == memberName.ToUpperInvariant());
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var now = DateTimeOffset.UtcNow;
            var name = FileName.Create("race.txt");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), owner.Id, root.Id, name,
                RelativeStoragePath.Create(root.RelativePath).Append(name), "text/plain", 1, now);
            var share = new Share(Guid.NewGuid(), file.Id, owner.Id, now);
            share.AddMember(member.Id, SharePermission.Viewer, now);
            fileId = file.Id;
            shareId = share.Id;
            database.AddRange(file, share);
            await database.SaveChangesAsync();
        }

        await using var lockScope = fixture.Factory.Services.CreateAsyncScope();
        var lockDatabase = lockScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        await lockDatabase.Database.OpenConnectionAsync();
        var key = ToAdvisoryLockKey(fileId);
        await lockDatabase.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({key})");
        try
        {
            var recordTask = memberClient.PutAsync($"/api/v1/recent-files/{fileId}", null);
            await WaitForAdvisoryWaiterAsync();
            await using (var revokeScope = fixture.Factory.Services.CreateAsyncScope())
            {
                var database = revokeScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
                await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shares WHERE id = {shareId}");
            }

            await lockDatabase.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({key})");
            using var response = await recordTask;
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await lockDatabase.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({key})");
            await lockDatabase.Database.CloseConnectionAsync();
        }

        await using var assertScope = fixture.Factory.Services.CreateAsyncScope();
        var assertDatabase = assertScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Equal(0, await assertDatabase.RecentFiles.CountAsync(recent => recent.FileId == fileId));

        async Task WaitForAdvisoryWaiterAsync()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await using var scope = fixture.Factory.Services.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
                var waiters = await database.Database.SqlQuery<long>(
                    $"SELECT count(*) AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted")
                    .SingleAsync();
                if (waiters > 0)
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("The recent-file request did not wait for the mutation lock.");
        }
    }

    [Fact]
    public async Task RecentFiles_PageOfOneHundredUsesStableOpenedTimeAndIdOrder()
    {
        var username = $"recent-page-{Guid.NewGuid():N}";
        var authenticated = await fixture.CreateAuthenticatedClientAsync(username, "page-password");
        using var client = authenticated.Client;
        using (var provision = await client.GetAsync("/api/v1/files"))
        {
            provision.EnsureSuccessStatusCode();
        }

        var expectedIds = new List<Guid>();
        Guid pageOwnerId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == username.ToUpperInvariant());
            pageOwnerId = owner.Id;
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var openedAt = DateTimeOffset.Parse("2026-08-25T03:00:00Z");
            for (var index = 0; index < 101; index++)
            {
                var id = Guid.NewGuid();
                var name = FileName.Create($"page-{index:D3}.txt");
                database.FileEntries.Add(
                    FileEntry.CreateFile(
                        id, owner.Id, root.Id, name,
                        RelativeStoragePath.Create(root.RelativePath).Append(name),
                        "text/plain", index, openedAt));
                database.RecentFiles.Add(RecentFile.Create(owner.Id, id, openedAt));
                expectedIds.Add(id);
            }

            await database.SaveChangesAsync();
        }

        var orderedExpectedIds = expectedIds.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        using var firstResponse = await client.GetAsync("/api/v1/recent-files?page=1&pageSize=100");
        using var secondResponse = await client.GetAsync("/api/v1/recent-files?page=2&pageSize=100");
        using var emptyResponse = await client.GetAsync("/api/v1/recent-files?page=3&pageSize=100");
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        emptyResponse.EnsureSuccessStatusCode();
        using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        using var empty = JsonDocument.Parse(await emptyResponse.Content.ReadAsStringAsync());
        var actualIds = first.RootElement.GetProperty("items").EnumerateArray()
            .Concat(second.RootElement.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();

        Assert.Equal(101, first.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(101, second.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(101, empty.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(orderedExpectedIds, actualIds);
        Assert.Equal(101, actualIds.Distinct().Count());
        await using var planScope = fixture.Factory.Services.CreateAsyncScope();
        var planDatabase = planScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var connection = (NpgsqlConnection)planDatabase.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var plan = new NpgsqlCommand(
            """
            SET enable_seqscan = off;
            EXPLAIN (COSTS OFF)
            SELECT file_id
            FROM recent_files
            WHERE user_id = @user_id
            ORDER BY opened_at DESC, file_id
            LIMIT 100;
            """,
            connection);
        plan.Parameters.AddWithValue("user_id", pageOwnerId);
        var lines = new List<string>();
        await using var reader = await plan.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        Assert.Contains(lines, line => line.Contains("ix_recent_files_user_opened_at_file_id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecentFiles_ReevaluatesPermissionStateAndKeepsUserHistoryIsolated()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerName = $"recent-owner-{suffix}";
        var memberName = $"recent-member-{suffix}";
        var strangerName = $"recent-stranger-{suffix}";
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync(ownerName, "owner-password");
        var memberAuth = await fixture.CreateAuthenticatedClientAsync(memberName, "member-password");
        var strangerAuth = await fixture.CreateAuthenticatedClientAsync(strangerName, "stranger-password");
        using var ownerClient = ownerAuth.Client;
        using var memberClient = memberAuth.Client;
        using var strangerClient = strangerAuth.Client;
        foreach (var client in new[] { ownerClient, memberClient, strangerClient })
        {
            using var provision = await client.GetAsync("/api/v1/files");
            provision.EnsureSuccessStatusCode();
        }

        Guid ownerId;
        Guid memberId;
        Guid folderId;
        Guid privateFolderId;
        Guid fileId;
        Guid folderShareId;
        Guid directShareId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var owner = await database.Users.SingleAsync(user => user.UsernameNormalized == ownerName.ToUpperInvariant());
            var member = await database.Users.SingleAsync(user => user.UsernameNormalized == memberName.ToUpperInvariant());
            ownerId = owner.Id;
            memberId = member.Id;
            var root = await database.FileEntries.SingleAsync(entry => entry.OwnerUserId == owner.Id && entry.ParentId == null);
            var now = DateTimeOffset.UtcNow;
            var folderName = FileName.Create("recent-shared");
            var folder = FileEntry.CreateFolder(
                Guid.NewGuid(), owner.Id, root.Id, folderName,
                RelativeStoragePath.Create(root.RelativePath).Append(folderName), now);
            var privateFolderName = FileName.Create("recent-private");
            var privateFolder = FileEntry.CreateFolder(
                Guid.NewGuid(), owner.Id, root.Id, privateFolderName,
                RelativeStoragePath.Create(root.RelativePath).Append(privateFolderName), now);
            var fileName = FileName.Create("recent-report.pdf");
            var file = FileEntry.CreateFile(
                Guid.NewGuid(), owner.Id, folder.Id, fileName,
                RelativeStoragePath.Create(folder.RelativePath).Append(fileName),
                "application/pdf", 42, now);
            var folderShare = new Share(Guid.NewGuid(), folder.Id, owner.Id, now);
            folderShare.AddMember(member.Id, SharePermission.Viewer, now);
            var directShare = new Share(Guid.NewGuid(), file.Id, owner.Id, now);
            directShare.AddMember(member.Id, SharePermission.Editor, now);
            folderId = folder.Id;
            privateFolderId = privateFolder.Id;
            fileId = file.Id;
            folderShareId = folderShare.Id;
            directShareId = directShare.Id;
            database.AddRange(folder, privateFolder, file, folderShare, directShare);
            await database.SaveChangesAsync();
        }

        using (var ownerRecord = await ownerClient.PutAsync($"/api/v1/recent-files/{fileId}", null))
        using (var memberRecord = await memberClient.PutAsync($"/api/v1/recent-files/{fileId}", null))
        using (var strangerRecord = await strangerClient.PutAsync($"/api/v1/recent-files/{fileId}", null))
        using (var folderRecord = await ownerClient.PutAsync($"/api/v1/recent-files/{folderId}", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, ownerRecord.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, memberRecord.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, strangerRecord.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, folderRecord.StatusCode);
        }

        var simultaneous = Enumerable.Range(0, 6)
            .Select(_ => memberClient.PutAsync($"/api/v1/recent-files/{fileId}", null))
            .ToArray();
        var simultaneousResponses = await Task.WhenAll(simultaneous);
        foreach (var response in simultaneousResponses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
        }

        var memberOpenedAt = await AssertSingleAsync(
            memberClient,
            fileId,
            expectedPermission: "EDITOR",
            expectedSource: "DIRECT",
            expectedStatus: "ACTIVE");
        _ = await AssertSingleAsync(ownerClient, fileId, "OWNER", "OWNER", "ACTIVE");
        await AssertEmptyAsync(strangerClient);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(2, await database.RecentFiles.CountAsync(recent => recent.FileId == fileId));
            Assert.Equal(1, await database.RecentFiles.CountAsync(
                recent => recent.UserId == memberId && recent.FileId == fileId));
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM shares WHERE id = {directShareId}");
        }

        _ = await AssertSingleAsync(memberClient, fileId, "VIEWER", "INHERITED", "ACTIVE");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            var privateFolder = await database.FileEntries.SingleAsync(entry => entry.Id == privateFolderId);
            file.MoveTo(
                privateFolder.Id,
                RelativeStoragePath.Create(privateFolder.RelativePath).Append(FileName.Create(file.Name)),
                DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        await AssertEmptyAsync(memberClient);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(1, await database.RecentFiles.CountAsync(
                recent => recent.UserId == memberId && recent.FileId == fileId));
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            var folder = await database.FileEntries.SingleAsync(entry => entry.Id == folderId);
            file.MoveTo(
                folder.Id,
                RelativeStoragePath.Create(folder.RelativePath).Append(FileName.Create(file.Name)),
                DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        var restoredOpenedAt = await AssertSingleAsync(memberClient, fileId, "VIEWER", "INHERITED", "ACTIVE");
        Assert.Equal(memberOpenedAt, restoredOpenedAt);

        Guid operationId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            var operation = new FileOperation(
                Guid.NewGuid(), ownerId, FileOperationType.Rename, file.Id, null,
                file.RelativePath, file.RelativePath, null, null, DateTimeOffset.UtcNow);
            operationId = operation.Id;
            database.FileOperations.Add(operation);
            await database.SaveChangesAsync();
        }
        await AssertEmptyAsync(memberClient);
        using (var blockedRecord = await memberClient.PutAsync($"/api/v1/recent-files/{fileId}", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, blockedRecord.StatusCode);
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var operation = await database.FileOperations.SingleAsync(item => item.Id == operationId);
            operation.Complete(DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }
        _ = await AssertSingleAsync(memberClient, fileId, "VIEWER", "INHERITED", "ACTIVE");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM shares WHERE id = {folderShareId}");
        }
        await AssertEmptyAsync(memberClient);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var now = DateTimeOffset.UtcNow;
            var share = new Share(Guid.NewGuid(), folderId, ownerId, now);
            share.AddMember(memberId, SharePermission.Viewer, now);
            database.Shares.Add(share);
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            file.MarkMissingCandidate(Guid.NewGuid(), now);
            await database.SaveChangesAsync();
        }

        _ = await AssertSingleAsync(memberClient, fileId, "VIEWER", "INHERITED", "MISSING_CANDIDATE");
        using (var missingRecord = await memberClient.PutAsync($"/api/v1/recent-files/{fileId}", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, missingRecord.StatusCode);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            var confirmedAt = file.MissingDetectedAt!.Value.AddMinutes(6);
            file.ConfirmMissing(Guid.NewGuid(), confirmedAt, TimeSpan.FromMinutes(5));
            await database.SaveChangesAsync();
        }
        _ = await AssertSingleAsync(memberClient, fileId, "VIEWER", "INHERITED", "MISSING");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            var now = DateTimeOffset.UtcNow.AddMinutes(10);
            file.ApplySourceObservation(file.Size, file.MimeType, now, null, now, false);
            file.Trash(
                RelativeStoragePath.Create($"users/{ownerId:N}/trash/{file.Id:N}/{file.Name}"),
                now.AddSeconds(1));
            await database.SaveChangesAsync();
        }
        await AssertEmptyAsync(ownerClient);
        await AssertEmptyAsync(memberClient);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var file = await database.FileEntries.SingleAsync(entry => entry.Id == fileId);
            var folder = await database.FileEntries.SingleAsync(entry => entry.Id == folderId);
            file.Restore(
                folder.Id,
                RelativeStoragePath.Create(folder.RelativePath).Append(FileName.Create(file.Name)),
                DateTimeOffset.UtcNow.AddMinutes(11));
            await database.SaveChangesAsync();
        }
        _ = await AssertSingleAsync(ownerClient, fileId, "OWNER", "OWNER", "ACTIVE");
        var restoredAfterTrashOpenedAt = await AssertSingleAsync(
            memberClient, fileId, "VIEWER", "INHERITED", "ACTIVE");
        Assert.Equal(memberOpenedAt, restoredAfterTrashOpenedAt);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM file_entries WHERE id = {fileId}");
            Assert.Equal(0, await database.RecentFiles.CountAsync(recent => recent.FileId == fileId));
        }
        await AssertEmptyAsync(ownerClient);
        await AssertEmptyAsync(memberClient);
        Assert.DoesNotContain(
            fixture.LogMessages,
            message => message.Contains("recent-report.pdf", StringComparison.OrdinalIgnoreCase) ||
                message.Contains(ownerName, StringComparison.OrdinalIgnoreCase) ||
                message.Contains(memberName, StringComparison.OrdinalIgnoreCase) ||
                message.Contains($"users/{ownerId:N}", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/api/v1/recent-files?page=0")]
    [InlineData("/api/v1/recent-files?pageSize=101")]
    [InlineData("/api/v1/recent-files?page=not-a-number")]
    public async Task RecentFiles_InvalidPagingUsesStableError(string path)
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"recent-invalid-{Guid.NewGuid():N}",
            "password");
        using var client = authenticated.Client;

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_RECENT_FILES_REQUEST", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RecordRecentFile_RejectsClientSuppliedState()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync(
            $"recent-body-{Guid.NewGuid():N}",
            "password");
        using var client = authenticated.Client;
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/recent-files/{Guid.NewGuid()}",
            new { userId = Guid.NewGuid(), openedAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_RECENT_FILES_REQUEST", json.RootElement.GetProperty("code").GetString());
    }

    private static async Task<DateTimeOffset> AssertSingleAsync(
        HttpClient client,
        Guid fileId,
        string expectedPermission,
        string expectedSource,
        string expectedStatus)
    {
        using var response = await client.GetAsync("/api/v1/recent-files?page=1&pageSize=100");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(fileId, item.GetProperty("id").GetGuid());
        Assert.Equal(expectedPermission, item.GetProperty("permission").GetString());
        Assert.Equal(expectedSource, item.GetProperty("permissionSource").GetString());
        Assert.Equal(expectedStatus, item.GetProperty("status").GetString());
        Assert.False(item.TryGetProperty("relativePath", out _));
        return item.GetProperty("openedAt").GetDateTimeOffset();
    }

    private static async Task AssertEmptyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/recent-files?page=1&pageSize=100");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
    }

    private static long ToAdvisoryLockKey(Guid id)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(id.ToByteArray(), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
