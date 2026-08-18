using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Identity;
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
            await store.CreateDirectoryAsync(
                RelativeStoragePath.Create($"users/{entry.OwnerUserId:N}/trash/{entry.Id:N}"),
                CancellationToken.None);
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

    [Fact]
    public async Task RenameAndMove_WhenValid_PreserveIdentityVersionContentAndDescendantPaths()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("relocate-user", "relocate-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var source = await CreateFolderAsync(client, rootId, "Source");
        var destination = await CreateFolderAsync(client, rootId, "Destination");
        var folder = await CreateFolderAsync(client, source.Id, "Folder");
        var child = await CreateFolderAsync(client, folder.Id, "Child");
        var bytes = Encoding.UTF8.GetBytes("content-is-not-renamed");
        var file = await UploadAsync(
            client,
            child.Id,
            "before.txt",
            bytes,
            Guid.NewGuid().ToString());

        using var renameFile = await client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { name = "after.txt" });
        renameFile.EnsureSuccessStatusCode();
        var renamedFile = (await renameFile.Content.ReadFromJsonAsync<TestFileItem>())!;
        Assert.Equal(file.Id, renamedFile.Id);
        Assert.Equal(file.FileVersion, renamedFile.FileVersion);
        Assert.Equal(file.Size, renamedFile.Size);

        using var repeatRename = await client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { name = "after.txt" });
        repeatRename.EnsureSuccessStatusCode();
        using var repeatMove = await client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { parentId = child.Id });
        repeatMove.EnsureSuccessStatusCode();

        using var renameFolder = await client.PatchAsJsonAsync(
            $"/api/v1/files/{folder.Id}",
            new { name = "RenamedFolder" });
        renameFolder.EnsureSuccessStatusCode();
        using var moveFolder = await client.PatchAsJsonAsync(
            $"/api/v1/files/{folder.Id}",
            new { parentId = destination.Id });
        moveFolder.EnsureSuccessStatusCode();

        using var download = await client.GetAsync($"/api/v1/files/{file.Id}/content");
        download.EnsureSuccessStatusCode();
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("after.txt", download.Content.Headers.ContentDisposition!.FileName!);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var entries = await database.FileEntries
            .Where(entry => entry.Id == folder.Id || entry.Id == child.Id || entry.Id == file.Id)
            .ToDictionaryAsync(entry => entry.Id);
        Assert.Equal(destination.Id, entries[folder.Id].ParentId);
        Assert.Contains("/Destination/RenamedFolder", entries[folder.Id].RelativePath, StringComparison.Ordinal);
        Assert.Contains("/Destination/RenamedFolder/Child", entries[child.Id].RelativePath, StringComparison.Ordinal);
        Assert.EndsWith("/Destination/RenamedFolder/Child/after.txt", entries[file.Id].RelativePath, StringComparison.Ordinal);
        Assert.Equal(file.FileVersion, entries[file.Id].FileVersion);
        Assert.Equal(bytes.Length, entries[file.Id].Size);

        var audits = await database.AuditLogs
            .Where(log => log.TargetId == file.Id.ToString() || log.TargetId == folder.Id.ToString())
            .ToListAsync();
        Assert.Contains(audits, audit => audit.Action == "FILE_RENAME" && audit.ResultCode == "SUCCESS");
        Assert.Contains(audits, audit => audit.Action == "FILE_MOVE" && audit.ResultCode == "SUCCESS");
        Assert.All(audits, audit =>
        {
            Assert.DoesNotContain("Destination", audit.ResultCode, StringComparison.Ordinal);
            Assert.NotNull(audit.ActorUserId);
            Assert.NotNull(audit.ActorDeviceId);
            Assert.False(string.IsNullOrWhiteSpace(audit.RequestId));
        });
    }

    [Fact]
    public async Task RenameAndMove_WhenInvalid_RejectWithoutOverwriteOrOwnershipDisclosure()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("reject-user", "reject-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var source = await CreateFolderAsync(client, rootId, "Source");
        var destination = await CreateFolderAsync(client, rootId, "Destination");
        var nested = await CreateFolderAsync(client, source.Id, "Nested");
        _ = await CreateFolderAsync(client, destination.Id, "Nested");
        var nonFolderTarget = await UploadAsync(
            client,
            source.Id,
            "not-a-folder.bin",
            [1],
            Guid.NewGuid().ToString());

        using var both = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { name = "Both", parentId = destination.Id });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        await AssertErrorAsync(both, "VALIDATION_FAILED");

        using var neither = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        await AssertErrorAsync(neither, "VALIDATION_FAILED");

        using var unknownProperty = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { name = "Known", path = "/not-accepted" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownProperty.StatusCode);
        await AssertErrorAsync(unknownProperty, "VALIDATION_FAILED");

        using var emptyParent = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { parentId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, emptyParent.StatusCode);
        await AssertErrorAsync(emptyParent, "VALIDATION_FAILED");

        using var nonFolder = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { parentId = nonFolderTarget.Id });
        Assert.Equal(HttpStatusCode.NotFound, nonFolder.StatusCode);
        await AssertErrorAsync(nonFolder, "FILE_NOT_FOUND");

        using var invalidName = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { name = "../escape" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);
        await AssertErrorAsync(invalidName, "VALIDATION_FAILED");

        using var cycle = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { parentId = nested.Id });
        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        await AssertErrorAsync(cycle, "FILE_MOVE_CYCLE");

        using var conflict = await client.PatchAsJsonAsync(
            $"/api/v1/files/{nested.Id}",
            new { parentId = destination.Id });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertErrorAsync(conflict, "FILE_NAME_CONFLICT");

        using var rootRename = await client.PatchAsJsonAsync(
            $"/api/v1/files/{rootId}",
            new { name = "Root" });
        Assert.Equal(HttpStatusCode.Conflict, rootRename.StatusCode);
        await AssertErrorAsync(rootRename, "FILE_OPERATION_NOT_ALLOWED");

        var other = await fixture.CreateAuthenticatedClientAsync("reject-other", "reject-other-password");
        using (other.Client)
        {
            var otherRoot = await GetRootIdAsync(other.Client);
            var otherFolder = await CreateFolderAsync(other.Client, otherRoot, "OtherDestination");
            using var foreignDestination = await client.PatchAsJsonAsync(
                $"/api/v1/files/{source.Id}",
                new { parentId = otherFolder.Id });
            Assert.Equal(HttpStatusCode.NotFound, foreignDestination.StatusCode);
            await AssertErrorAsync(foreignDestination, "FILE_NOT_FOUND");

            using var idor = await other.Client.PatchAsJsonAsync(
                $"/api/v1/files/{source.Id}",
                new { parentId = destination.Id });
            Assert.Equal(HttpStatusCode.NotFound, idor.StatusCode);
            await AssertErrorAsync(idor, "FILE_NOT_FOUND");
        }

        using var trash = await client.DeleteAsync($"/api/v1/files/{nested.Id}");
        trash.EnsureSuccessStatusCode();
        using var trashedRename = await client.PatchAsJsonAsync(
            $"/api/v1/files/{nested.Id}",
            new { name = "Nope" });
        Assert.Equal(HttpStatusCode.NotFound, trashedRename.StatusCode);
        await AssertErrorAsync(trashedRename, "FILE_NOT_FOUND");

        using var anonymous = fixture.Factory.CreateClient();
        using var unauthorized = await anonymous.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { name = "Denied" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await AssertErrorAsync(unauthorized, "AUTHENTICATION_REQUIRED");

        var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(authenticated.AccessToken);
        var userId = Guid.Parse(accessToken.Claims.Single(claim => claim.Type == "sub").Value);
        var deviceId = Guid.Parse(accessToken.Claims.Single(claim => claim.Type == "device_id").Value);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
            Assert.True(
                await identity.RevokeDeviceAsync(
                    userId,
                    deviceId,
                    "file-patch-integration-test",
                    CancellationToken.None));
        }

        using var revokedDevice = await client.PatchAsJsonAsync(
            $"/api/v1/files/{source.Id}",
            new { name = "DeniedAfterRevocation" });
        Assert.Equal(HttpStatusCode.Unauthorized, revokedDevice.StatusCode);
        await AssertErrorAsync(revokedDevice, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task IncompleteRename_IsQuarantinedAndFilesystemDoneRecoveryConverges()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("rename-recovery", "recovery-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Recovery");
        var file = await UploadAsync(
            client,
            folder.Id,
            "before.bin",
            [4, 5, 6],
            Guid.NewGuid().ToString());

        Guid operationId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var entry = await database.FileEntries.SingleAsync(candidate => candidate.Id == file.Id);
            var operation = new FileOperation(
                Guid.NewGuid(),
                entry.OwnerUserId,
                FileOperationType.Rename,
                entry.Id,
                null,
                entry.RelativePath,
                entry.RelativePath.Replace("before.bin", "after.bin", StringComparison.Ordinal),
                null,
                null,
                DateTimeOffset.UtcNow);
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, DateTimeOffset.UtcNow);
            operationId = operation.Id;
            database.FileOperations.Add(operation);
            await database.SaveChangesAsync();
        }

        using var details = await client.GetAsync($"/api/v1/files/{file.Id}");
        Assert.Equal(HttpStatusCode.Conflict, details.StatusCode);
        await AssertErrorAsync(details, "RECOVERY_REQUIRED");
        using var content = await client.GetAsync($"/api/v1/files/{file.Id}/content");
        Assert.Equal(HttpStatusCode.Conflict, content.StatusCode);
        await AssertErrorAsync(content, "RECOVERY_REQUIRED");
        using var listing = await client.GetAsync($"/api/v1/files?parentId={folder.Id}");
        listing.EnsureSuccessStatusCode();
        using (var json = await JsonDocument.ParseAsync(await listing.Content.ReadAsStreamAsync()))
        {
            Assert.DoesNotContain(
                json.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("id").GetGuid() == file.Id);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var operation = await database.FileOperations.SingleAsync(item => item.Id == operationId);
            operation.Retry(DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<FileOperationRecoveryService>()
                .RecoverAsync(CancellationToken.None);
        }

        using var recovered = await client.GetAsync($"/api/v1/files/{file.Id}");
        recovered.EnsureSuccessStatusCode();
        var recoveredItem = (await recovered.Content.ReadFromJsonAsync<TestFileItem>())!;
        Assert.Equal("after.bin", recoveredItem.Name);
        Assert.Equal(file.FileVersion, recoveredItem.FileVersion);
    }

    [Fact]
    public async Task ConcurrentRenames_AreSerializedWithoutDeadlockOrDuplicateFilesystemEntries()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("parallel-rename", "parallel-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Parallel");
        var file = await UploadAsync(
            client,
            folder.Id,
            "initial.txt",
            [1, 2, 3],
            Guid.NewGuid().ToString());

        var firstTask = client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { name = "first.txt" });
        var secondTask = client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { name = "second.txt" });
        var responses = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(10));
        using var first = responses[0];
        using var second = responses[1];
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        using var details = await client.GetAsync($"/api/v1/files/{file.Id}");
        details.EnsureSuccessStatusCode();
        var current = (await details.Content.ReadFromJsonAsync<TestFileItem>())!;
        Assert.Contains(current.Name, new[] { "first.txt", "second.txt" });
        Assert.Equal(file.FileVersion, current.FileVersion);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var storedEntry = await database.FileEntries.SingleAsync(entry => entry.Id == file.Id);
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        Assert.True(
            await store.ExistsAsync(
                RelativeStoragePath.Create(storedEntry.RelativePath),
                false,
                CancellationToken.None));
        var staleName = current.Name == "first.txt" ? "second.txt" : "first.txt";
        var stalePath = RelativeStoragePath.Create(storedEntry.RelativePath[..storedEntry.RelativePath.LastIndexOf('/')])
            .Append(FileName.Create(staleName));
        Assert.False(await store.ExistsAsync(stalePath, false, CancellationToken.None));
        Assert.Equal(
            2,
            await database.AuditLogs.CountAsync(
                audit =>
                    audit.TargetId == file.Id.ToString() &&
                    audit.Action == "FILE_RENAME" &&
                audit.ResultCode == "SUCCESS"));
    }

    [Fact]
    public async Task RenameRecovery_ConvergesFilesystemDoneAndQuarantinesAmbiguousBothExist()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("recovery-matrix", "matrix-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Matrix");
        var moved = await UploadAsync(
            client,
            folder.Id,
            "moved-before.bin",
            [7, 8, 9],
            Guid.NewGuid().ToString());
        var ambiguous = await UploadAsync(
            client,
            folder.Id,
            "ambiguous-before.bin",
            [1, 3, 5],
            Guid.NewGuid().ToString());
        Guid movedOperationId;
        Guid ambiguousOperationId;

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
            var movedEntry = await database.FileEntries.SingleAsync(entry => entry.Id == moved.Id);
            var movedSource = RelativeStoragePath.Create(movedEntry.RelativePath);
            var movedTarget = RelativeStoragePath.Create(
                movedEntry.RelativePath.Replace("moved-before.bin", "moved-after.bin", StringComparison.Ordinal));
            await store.MoveAsync(movedSource, movedTarget, false, CancellationToken.None);
            var movedOperation = new FileOperation(
                Guid.NewGuid(),
                movedEntry.OwnerUserId,
                FileOperationType.Rename,
                movedEntry.Id,
                null,
                movedSource.Value,
                movedTarget.Value,
                null,
                null,
                DateTimeOffset.UtcNow);
            movedOperation.MarkFilesystemDone(DateTimeOffset.UtcNow);
            movedOperationId = movedOperation.Id;
            database.FileOperations.Add(movedOperation);

            var ambiguousEntry = await database.FileEntries.SingleAsync(entry => entry.Id == ambiguous.Id);
            var ambiguousSource = RelativeStoragePath.Create(ambiguousEntry.RelativePath);
            var ambiguousTarget = RelativeStoragePath.Create(
                ambiguousEntry.RelativePath.Replace("ambiguous-before.bin", "ambiguous-after.bin", StringComparison.Ordinal));
            var temporary = await store.WriteUploadTempAsync(
                ambiguousEntry.OwnerUserId,
                Guid.NewGuid(),
                new MemoryStream([1, 3, 5]),
                3,
                CancellationToken.None);
            await store.MoveAsync(temporary.Path, ambiguousTarget, false, CancellationToken.None);
            var ambiguousOperation = new FileOperation(
                Guid.NewGuid(),
                ambiguousEntry.OwnerUserId,
                FileOperationType.Rename,
                ambiguousEntry.Id,
                null,
                ambiguousSource.Value,
                ambiguousTarget.Value,
                null,
                null,
                DateTimeOffset.UtcNow);
            ambiguousOperationId = ambiguousOperation.Id;
            database.FileOperations.Add(ambiguousOperation);
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
                FileOperationStatus.Completed,
                (await database.FileOperations.SingleAsync(operation => operation.Id == movedOperationId)).Status);
            Assert.Equal(
                "moved-after.bin",
                (await database.FileEntries.SingleAsync(entry => entry.Id == moved.Id)).Name);
            Assert.Equal(
                FileOperationStatus.RecoveryRequired,
                (await database.FileOperations.SingleAsync(operation => operation.Id == ambiguousOperationId)).Status);
            Assert.Equal(
                "ambiguous-before.bin",
                (await database.FileEntries.SingleAsync(entry => entry.Id == ambiguous.Id)).Name);
        }

        using var ambiguousDetails = await client.GetAsync($"/api/v1/files/{ambiguous.Id}");
        Assert.Equal(HttpStatusCode.Conflict, ambiguousDetails.StatusCode);
        await AssertErrorAsync(ambiguousDetails, "RECOVERY_REQUIRED");
    }

    [Fact]
    public async Task Rename_WhenOnlyFilesystemTargetExists_RejectsWithoutOverwrite()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("hdd-conflict", "conflict-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var folder = await CreateFolderAsync(client, rootId, "Conflict");
        var file = await UploadAsync(
            client,
            folder.Id,
            "source.bin",
            [1, 2, 3],
            Guid.NewGuid().ToString());

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
            var entry = await database.FileEntries.SingleAsync(candidate => candidate.Id == file.Id);
            var temporary = await store.WriteUploadTempAsync(
                entry.OwnerUserId,
                Guid.NewGuid(),
                new MemoryStream([9, 9, 9]),
                3,
                CancellationToken.None);
            var target = RelativeStoragePath.Create(
                entry.RelativePath.Replace("source.bin", "occupied.bin", StringComparison.Ordinal));
            await store.MoveAsync(temporary.Path, target, false, CancellationToken.None);
        }

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { name = "occupied.bin" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorAsync(response, "FILE_NAME_CONFLICT");
        using var download = await client.GetAsync($"/api/v1/files/{file.Id}/content");
        download.EnsureSuccessStatusCode();
        Assert.Equal(new byte[] { 1, 2, 3 }, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ConcurrentMoveTrashAndRestoreRename_AreSerializedWithoutDeadlock()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("parallel-mutations", "mutations-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var source = await CreateFolderAsync(client, rootId, "Source");
        var destination = await CreateFolderAsync(client, rootId, "Destination");
        var file = await UploadAsync(
            client,
            source.Id,
            "item.bin",
            [2, 4, 6],
            Guid.NewGuid().ToString());

        var moveTask = client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { parentId = destination.Id });
        var trashTask = client.DeleteAsync($"/api/v1/files/{file.Id}");
        var firstPair = await Task.WhenAll(moveTask, trashTask).WaitAsync(TimeSpan.FromSeconds(10));
        using var move = firstPair[0];
        using var trash = firstPair[1];
        Assert.Contains(move.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.OK, trash.StatusCode);

        var restoreTask = client.PostAsync($"/api/v1/files/{file.Id}/restore", null);
        var renameTask = client.PatchAsJsonAsync(
            $"/api/v1/files/{file.Id}",
            new { name = "renamed.bin" });
        var secondPair = await Task.WhenAll(restoreTask, renameTask).WaitAsync(TimeSpan.FromSeconds(10));
        using var restore = secondPair[0];
        using var rename = secondPair[1];
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Contains(rename.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });

        using var details = await client.GetAsync($"/api/v1/files/{file.Id}");
        details.EnsureSuccessStatusCode();
        var current = (await details.Content.ReadFromJsonAsync<TestFileItem>())!;
        Assert.Equal(file.FileVersion, current.FileVersion);
        using var download = await client.GetAsync($"/api/v1/files/{file.Id}/content");
        download.EnsureSuccessStatusCode();
        Assert.Equal(new byte[] { 2, 4, 6 }, await download.Content.ReadAsByteArrayAsync());
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

    private sealed record TestFileItem(
        Guid Id,
        Guid? ParentId,
        string Name,
        string EntryType,
        string? MimeType,
        long Size,
        string Status,
        long FileVersion);
}
