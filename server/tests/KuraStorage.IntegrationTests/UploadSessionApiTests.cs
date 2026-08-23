using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Transfers;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Transfers;
using KuraStorage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuraStorage.IntegrationTests;

public sealed class UploadSessionApiTests(PostgreSqlAuthFlowFixture fixture)
    : IClassFixture<PostgreSqlAuthFlowFixture>
{
    [Fact]
    public async Task ResumableUpload_ValidatesChunksResumesAndPublishesExactlyOnce()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-flow", "resumable-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var content = RandomNumberGenerator.GetBytes(UploadSessionOptions.MinimumChunkBytes + 37);
        var wholeSha = Sha(content);
        var key = Guid.NewGuid().ToString();

        using var created = await CreateSessionAsync(client, rootId, "large.bin", content.Length, wholeSha.ToUpperInvariant(), key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("0", created.Headers.GetValues("Upload-Offset").Single());
        var sessionId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var retry = await CreateSessionAsync(client, rootId, "large.bin", content.Length, wholeSha, key))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal(sessionId, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        using (var conflict = await CreateSessionAsync(client, rootId, "other.bin", content.Length, wholeSha, key))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            await AssertErrorAsync(conflict, "IDEMPOTENCY_CONFLICT");
        }

        var first = content[..UploadSessionOptions.MinimumChunkBytes];
        using (var future = await SendChunkAsync(client, sessionId, 1, first, Sha(first)))
        {
            Assert.Equal(HttpStatusCode.Conflict, future.StatusCode);
            Assert.Equal("0", future.Headers.GetValues("Upload-Offset").Single());
            await AssertErrorAsync(future, "UPLOAD_OFFSET_MISMATCH");
        }

        using (var badHash = await SendChunkAsync(client, sessionId, 0, first, new string('0', 64)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badHash.StatusCode);
            await AssertErrorAsync(badHash, "CHUNK_CHECKSUM_MISMATCH");
        }

        using (var accepted = await SendChunkAsync(client, sessionId, 0, first, Sha(first).ToUpperInvariant()))
        {
            accepted.EnsureSuccessStatusCode();
            var result = await accepted.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(result.GetProperty("replayed").GetBoolean());
            Assert.Equal(first.Length, result.GetProperty("nextOffset").GetInt64());
        }

        using (var replay = await SendChunkAsync(client, sessionId, 0, first, Sha(first)))
        {
            replay.EnsureSuccessStatusCode();
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        }

        using (var overlap = await SendChunkAsync(client, sessionId, 1, content[^37..], Sha(content[^37..])))
        {
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
            await AssertErrorAsync(overlap, "UPLOAD_OFFSET_MISMATCH");
        }

        using (var final = await SendChunkAsync(
                   client,
                   sessionId,
                   UploadSessionOptions.MinimumChunkBytes,
                   content[^37..],
                   Sha(content[^37..])))
        {
            final.EnsureSuccessStatusCode();
            Assert.Equal(content.Length.ToString(), final.Headers.GetValues("Upload-Offset").Single());
        }

        using (var listBeforeComplete = await client.GetAsync("/api/v1/files"))
        {
            var list = await listBeforeComplete.Content.ReadFromJsonAsync<JsonElement>();
            Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), item => item.GetProperty("name").GetString() == "large.bin");
        }

        Guid fileId;
        using (var complete = await client.PostAsync($"/api/v1/upload-sessions/{sessionId}/complete", null))
        {
            complete.EnsureSuccessStatusCode();
            fileId = (await complete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        using (var repeatedComplete = await client.PostAsync($"/api/v1/upload-sessions/{sessionId}/complete", null))
        {
            repeatedComplete.EnsureSuccessStatusCode();
            Assert.Equal(fileId, (await repeatedComplete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        using var download = await client.GetAsync($"/api/v1/files/{fileId}/content");
        download.EnsureSuccessStatusCode();
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        Assert.Single(await database.FileEntries.Where(entry => entry.Id == fileId).ToListAsync());
        Assert.Single(await database.AuditLogs.Where(audit =>
            audit.TargetId == sessionId.ToString() && audit.Action == "UPLOAD_SESSION_COMPLETE").ToListAsync());
        var auditText = string.Join('|', await database.AuditLogs
            .Where(audit => audit.TargetId == sessionId.ToString())
            .Select(audit => audit.Action + audit.TargetId + audit.ResultCode + audit.RequestId)
            .ToListAsync());
        Assert.DoesNotContain("large.bin", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(wholeSha, auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key, auditText, StringComparison.OrdinalIgnoreCase);
        var logs = string.Join('\n', fixture.LogMessages);
        Assert.DoesNotContain("large.bin", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(wholeSha, logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAndDeviceRevocation_CleanTemporaryFilesIdempotently()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-cancel", "cancel-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var chunk = RandomNumberGenerator.GetBytes(UploadSessionOptions.MinimumChunkBytes);
        var firstId = await CreateSessionIdAsync(client, rootId, "cancel.bin", chunk.Length, Sha(chunk));
        using (var accepted = await SendChunkAsync(client, firstId, 0, chunk, Sha(chunk)))
        {
            accepted.EnsureSuccessStatusCode();
        }

        using (var cancelled = await client.DeleteAsync($"/api/v1/upload-sessions/{firstId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }
        using (var repeated = await client.DeleteAsync($"/api/v1/upload-sessions/{firstId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }

        var secondId = await CreateSessionIdAsync(client, rootId, "revoked.bin", chunk.Length, Sha(chunk));
        using (var accepted = await SendChunkAsync(client, secondId, 0, chunk, Sha(chunk)))
        {
            accepted.EnsureSuccessStatusCode();
        }

        var token = new JwtSecurityTokenHandler().ReadJwtToken(authenticated.AccessToken);
        var deviceId = Guid.Parse(token.Claims.Single(claim => claim.Type == "device_id").Value);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            (await database.Devices.SingleAsync(device => device.Id == deviceId)).Revoke(DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<UploadSessionCleanupService>().RunAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var cancelled = await database.UploadSessions.SingleAsync(session => session.Id == firstId);
            var revoked = await database.UploadSessions.SingleAsync(session => session.Id == secondId);
            Assert.Equal(UploadSessionStatus.Cancelled, cancelled.Status);
            Assert.NotNull(cancelled.CleanedAt);
            Assert.Equal(UploadSessionStatus.Cancelled, revoked.Status);
            Assert.Equal("DEVICE_REVOKED", revoked.ErrorCode);
            Assert.NotNull(revoked.CleanedAt);
            Assert.DoesNotContain(fixture.StorageRootPath, cancelled.TemporaryRelativePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ZeroByteUpload_CompletesWithoutChunk()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-empty", "empty-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var sessionId = await CreateSessionIdAsync(client, rootId, "empty.bin", 0, Sha([]));

        using var complete = await client.PostAsync($"/api/v1/upload-sessions/{sessionId}/complete", null);
        complete.EnsureSuccessStatusCode();
        Assert.Equal(0, (await complete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("size").GetInt64());
    }

    [Fact]
    public async Task RecoveryAndExpiry_ReconcileTemporaryLengthAndNeverPublishIncompleteFiles()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-recovery", "recovery-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var chunk = RandomNumberGenerator.GetBytes(UploadSessionOptions.MinimumChunkBytes);
        var expectedSize = chunk.Length + 1;
        var longId = await CreateSessionIdAsync(client, rootId, "long.bin", expectedSize, Sha(new byte[expectedSize]));
        var shortId = await CreateSessionIdAsync(client, rootId, "short.bin", expectedSize, Sha(new byte[expectedSize]));
        var expiredId = await CreateSessionIdAsync(client, rootId, "expired.bin", expectedSize, Sha(new byte[expectedSize]));
        foreach (var id in new[] { longId, shortId, expiredId })
        {
            using var accepted = await SendChunkAsync(client, id, 0, chunk, Sha(chunk));
            accepted.EnsureSuccessStatusCode();
        }

        string longPath;
        string shortPath;
        string expiredPath;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            longPath = PhysicalPath((await database.UploadSessions.SingleAsync(session => session.Id == longId)).TemporaryRelativePath);
            shortPath = PhysicalPath((await database.UploadSessions.SingleAsync(session => session.Id == shortId)).TemporaryRelativePath);
            expiredPath = PhysicalPath((await database.UploadSessions.SingleAsync(session => session.Id == expiredId)).TemporaryRelativePath);
            await using (var file = new FileStream(longPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                file.SetLength(chunk.Length + 17);
            }
            await using (var file = new FileStream(shortPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                file.SetLength(chunk.Length - 1);
            }

            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE upload_sessions SET expires_at = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE id = {expiredId}");
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadSessionRecoveryService>().RecoverAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadSessionCleanupService>().RunAsync(CancellationToken.None);
        }

        Assert.Equal(chunk.Length, new FileInfo(longPath).Length);
        Assert.False(File.Exists(expiredPath));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(UploadSessionStatus.Active, (await database.UploadSessions.SingleAsync(session => session.Id == longId)).Status);
            Assert.Equal(UploadSessionStatus.RecoveryRequired, (await database.UploadSessions.SingleAsync(session => session.Id == shortId)).Status);
            var expired = await database.UploadSessions.SingleAsync(session => session.Id == expiredId);
            Assert.Equal(UploadSessionStatus.Expired, expired.Status);
            Assert.NotNull(expired.CleanedAt);
        }

        using var list = await client.GetAsync("/api/v1/files");
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(items, item => item.GetProperty("name").GetString() is "long.bin" or "short.bin" or "expired.bin");

        string PhysicalPath(string relativePath) =>
            Path.Combine(fixture.StorageRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task Completion_RejectsIncompleteChecksumAndExistingMultipartNameWithoutDuplicateFile()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-conflict", "conflict-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var content = RandomNumberGenerator.GetBytes(UploadSessionOptions.MinimumChunkBytes);
        var wrongWholeHash = new string(Sha(content)[0] == '0' ? '1' : '0', 64);
        var checksumId = await CreateSessionIdAsync(client, rootId, "checksum.bin", content.Length, wrongWholeHash);

        using (var incomplete = await client.PostAsync($"/api/v1/upload-sessions/{checksumId}/complete", null))
        {
            Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
            await AssertErrorAsync(incomplete, "UPLOAD_INCOMPLETE");
        }
        using (var accepted = await SendChunkAsync(client, checksumId, 0, content, Sha(content)))
        {
            accepted.EnsureSuccessStatusCode();
        }
        using (var mismatch = await client.PostAsync($"/api/v1/upload-sessions/{checksumId}/complete", null))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
            await AssertErrorAsync(mismatch, "UPLOAD_CHECKSUM_MISMATCH");
        }
        using (var state = await client.GetAsync($"/api/v1/upload-sessions/{checksumId}"))
        {
            state.EnsureSuccessStatusCode();
            Assert.Equal(0, (await state.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextOffset").GetInt64());
        }

        var raceId = await CreateSessionIdAsync(client, rootId, "race.bin", content.Length, Sha(content));
        using (var accepted = await SendChunkAsync(client, raceId, 0, content, Sha(content)))
        {
            accepted.EnsureSuccessStatusCode();
        }
        using (var multipart = await SendMultipartAsync(client, rootId, "race.bin", content))
        {
            multipart.EnsureSuccessStatusCode();
        }
        using (var conflict = await client.PostAsync($"/api/v1/upload-sessions/{raceId}/complete", null))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            await AssertErrorAsync(conflict, "FILE_NAME_CONFLICT");
        }

        using var list = await client.GetAsync("/api/v1/files");
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray();
        Assert.Single(items, item => item.GetProperty("name").GetString() == "race.bin");
        Assert.DoesNotContain(items, item => item.GetProperty("name").GetString() == "checksum.bin");
    }

    [Fact]
    public async Task StartupRecovery_AfterAtomicMoveBeforeDatabaseCommit_PublishesExactlyOneFile()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-publish-recovery", "publish-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var content = RandomNumberGenerator.GetBytes(UploadSessionOptions.MinimumChunkBytes);
        var sessionId = await CreateSessionIdAsync(client, rootId, "recovered.bin", content.Length, Sha(content));
        using (var accepted = await SendChunkAsync(client, sessionId, 0, content, Sha(content)))
        {
            accepted.EnsureSuccessStatusCode();
        }

        Guid fileId;
        Guid operationId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var session = await database.UploadSessions.SingleAsync(item => item.Id == sessionId);
            var parent = await database.FileEntries.SingleAsync(item => item.Id == rootId);
            fileId = session.FileEntryId;
            operationId = Guid.NewGuid();
            var source = RelativeStoragePath.Create(session.TemporaryRelativePath);
            var target = RelativeStoragePath.Create(parent.RelativePath).Append(FileName.Create(session.FileName));
            var operation = new FileOperation(
                operationId,
                session.TargetOwnerUserId,
                FileOperationType.Upload,
                fileId,
                session.IdempotencyKey,
                source.Value,
                target.Value,
                session.ExpectedSize,
                session.ExpectedSha256,
                DateTimeOffset.UtcNow,
                session.DeviceId,
                "recovery-test",
                "UPLOAD_SESSION");
            session.BeginCompletion(operationId, DateTimeOffset.UtcNow);
            database.FileOperations.Add(operation);
            await database.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IFileStore>()
                .MoveAsync(source, target, false, CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadSessionRecoveryService>()
                .RecoverAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            Assert.Equal(UploadSessionStatus.Completed, (await database.UploadSessions.SingleAsync(item => item.Id == sessionId)).Status);
            Assert.Equal(FileOperationStatus.Completed, (await database.FileOperations.SingleAsync(item => item.Id == operationId)).Status);
            Assert.Single(await database.FileEntries.Where(item => item.Id == fileId).ToListAsync());
            Assert.Single(await database.AuditLogs.Where(audit =>
                audit.TargetId == sessionId.ToString() && audit.Action == "UPLOAD_SESSION_RECOVER").ToListAsync());
        }

        using var download = await client.GetAsync($"/api/v1/files/{fileId}/content");
        download.EnsureSuccessStatusCode();
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ConcurrentCompleteAndCancel_SerializeToOneTerminalOutcome()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-concurrent", "concurrent-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var content = RandomNumberGenerator.GetBytes(UploadSessionOptions.MinimumChunkBytes);
        var sessionId = await CreateSessionIdAsync(client, rootId, "concurrent.bin", content.Length, Sha(content));
        using (var accepted = await SendChunkAsync(client, sessionId, 0, content, Sha(content)))
        {
            accepted.EnsureSuccessStatusCode();
        }

        var completeTask = client.PostAsync($"/api/v1/upload-sessions/{sessionId}/complete", null);
        var cancelTask = client.DeleteAsync($"/api/v1/upload-sessions/{sessionId}");
        using var complete = await completeTask;
        using var cancel = await cancelTask;

        Assert.True(
            (complete.StatusCode == HttpStatusCode.OK && cancel.StatusCode == HttpStatusCode.Conflict) ||
            (complete.StatusCode == HttpStatusCode.Conflict && cancel.StatusCode == HttpStatusCode.NoContent));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var session = await database.UploadSessions.SingleAsync(item => item.Id == sessionId);
        var files = await database.FileEntries.CountAsync(item => item.Id == session.FileEntryId);
        Assert.True(
            (session.Status == UploadSessionStatus.Completed && files == 1) ||
            (session.Status == UploadSessionStatus.Cancelled && files == 0));
    }

    [Fact]
    public async Task SessionAccess_HidesResourceFromOtherUserDeviceAndInvalidToken()
    {
        var owner = await fixture.CreateAuthenticatedClientAsync("resumable-owner", "owner-password");
        using var ownerClient = owner.Client;
        var rootId = await GetRootIdAsync(ownerClient);
        var sessionId = await CreateSessionIdAsync(ownerClient, rootId, "private.bin", 1, Sha([1]));
        var otherDevice = await fixture.CreateAuthenticatedClientAsync("resumable-owner", "owner-password");
        using var otherDeviceClient = otherDevice.Client;
        var otherUser = await fixture.CreateAuthenticatedClientAsync("resumable-other", "other-password");
        using var otherUserClient = otherUser.Client;

        foreach (var client in new[] { otherDeviceClient, otherUserClient })
        {
            using var hidden = await client.GetAsync($"/api/v1/upload-sessions/{sessionId}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            await AssertErrorAsync(hidden, "UPLOAD_SESSION_NOT_FOUND");
        }

        using var invalidClient = fixture.Factory.CreateClient();
        invalidClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var unauthorized = await invalidClient.GetAsync($"/api/v1/upload-sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task SessionCreation_EnforcesFileCapacityAndDeviceSessionLimits()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-limits", "limits-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);

        using (var tooLarge = await CreateSessionAsync(
                   client,
                   rootId,
                   "too-large.bin",
                   1024L * 1024 * 1024 * 1024 + 1,
                   new string('0', 64),
                   Guid.NewGuid().ToString()))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
            await AssertErrorAsync(tooLarge, "FILE_SIZE_LIMIT_EXCEEDED");
        }

        using (var insufficient = await CreateSessionAsync(
                   client,
                   rootId,
                   "capacity.bin",
                   1024L * 1024 * 1024 * 1024,
                   new string('0', 64),
                   Guid.NewGuid().ToString()))
        {
            Assert.Equal(HttpStatusCode.InsufficientStorage, insufficient.StatusCode);
            await AssertErrorAsync(insufficient, "STORAGE_CAPACITY_INSUFFICIENT");
        }

        for (var index = 0; index < 5; index++)
        {
            using var created = await CreateSessionAsync(
                client,
                rootId,
                $"limit-{index}.bin",
                1,
                Sha([(byte)index]),
                Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var limited = await CreateSessionAsync(
            client,
            rootId,
            "limit-rejected.bin",
            1,
            Sha([9]),
            Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("5", limited.Headers.GetValues("Retry-After").Single());
        await AssertErrorAsync(limited, "UPLOAD_LIMIT_REACHED");
    }

    [Fact]
    public async Task TransferMetrics_UseOnlyLowCardinalityNonSensitiveDimensions()
    {
        var measurements = new ConcurrentBag<(string Name, string Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == "KuraStorage.Transfers")
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, string.Join('|', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}")))));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, string.Join('|', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}")))));
        listener.Start();

        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-metrics", "metrics-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var content = new byte[] { 42 };
        var sessionId = await CreateSessionIdAsync(client, rootId, "must-not-be-a-tag.bin", 1, Sha(content));
        using (var chunk = await SendChunkAsync(client, sessionId, 0, content, Sha(content)))
        {
            chunk.EnsureSuccessStatusCode();
        }
        using (var cancel = await client.DeleteAsync($"/api/v1/upload-sessions/{sessionId}"))
        {
            cancel.EnsureSuccessStatusCode();
        }
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, item => item.Name == "kurastorage.upload.sessions");
        Assert.Contains(measurements, item => item.Name == "kurastorage.upload.chunks");
        Assert.Contains(measurements, item => item.Name == "kurastorage.upload.chunk.bytes");
        Assert.Contains(measurements, item => item.Name == "kurastorage.upload.chunk.duration");
        Assert.Contains(measurements, item => item.Name == "kurastorage.upload.concurrent_chunk_writes");
        foreach (var measurement in measurements)
        {
            Assert.DoesNotContain("user", measurement.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("device", measurement.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("session", measurement.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("path", measurement.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("checksum", measurement.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("must-not-be-a-tag.bin", measurement.Tags, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RecoveryAndCleanupCandidateQueries_DoNotTrackStateBeforeSessionLockReload()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-candidate-tracking", "tracking-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var sessionId = await CreateSessionIdAsync(client, rootId, "tracking.bin", 1, Sha([1]));

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUploadSessionRepository>();
        var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();

        var recoveryCandidate = (await repository.ListRecoveryCandidatesAsync(100, CancellationToken.None))
            .Single(session => session.Id == sessionId);
        Assert.Equal(EntityState.Detached, database.Entry(recoveryCandidate).State);

        await database.UploadSessions
            .Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                session => session.ExpiresAt,
                DateTimeOffset.UtcNow.AddMinutes(-1)));
        var cleanupCandidate = (await repository.ListCleanupCandidatesAsync(
            DateTimeOffset.UtcNow,
            100,
            CancellationToken.None)).Single(session => session.Id == sessionId);
        Assert.Equal(EntityState.Detached, database.Entry(cleanupCandidate).State);
    }

    [Fact]
    public async Task Cleanup_WhenCandidatesExceedBatch_ProcessesEveryExpiredSessionInBoundedBatches()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-cleanup-batch", "cleanup-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(authenticated.AccessToken);
        var ownerId = Guid.Parse(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        var deviceId = Guid.Parse(token.Claims.Single(claim => claim.Type == "device_id").Value);
        var ids = new List<Guid>();

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IUploadSessionStore>();
            var createdAt = DateTimeOffset.UtcNow.AddDays(-2);
            for (var index = 0; index < 17; index++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                var relativePath = $"upload-sessions/{ownerId:N}/{id:N}.upload";
                database.UploadSessions.Add(
                    new UploadSession(
                        id,
                        ownerId,
                        ownerId,
                        deviceId,
                        rootId,
                        Guid.NewGuid(),
                        Guid.NewGuid().ToString(),
                        $"expired-{index}.bin",
                        null,
                        0,
                        null,
                        relativePath,
                        createdAt,
                        createdAt.AddHours(1),
                        createdAt.AddDays(7)));
                await store.TruncateAsync(RelativeStoragePath.Create(relativePath), 0, CancellationToken.None);
            }
            await database.SaveChangesAsync();
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var cleanup = new UploadSessionCleanupService(
                scope.ServiceProvider.GetRequiredService<IUploadSessionRepository>(),
                scope.ServiceProvider.GetRequiredService<IFileRepository>(),
                scope.ServiceProvider.GetRequiredService<IUploadSessionStore>(),
                scope.ServiceProvider.GetRequiredService<IStorageGuard>(),
                scope.ServiceProvider.GetRequiredService<ISystemClock>(),
                new UploadSessionOptions { CleanupBatchSize = 5 });
            await cleanup.RunAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var sessions = await database.UploadSessions.Where(session => ids.Contains(session.Id)).ToListAsync();
            Assert.Equal(17, sessions.Count);
            Assert.All(sessions, session =>
            {
                Assert.Equal(UploadSessionStatus.Expired, session.Status);
                Assert.NotNull(session.CleanedAt);
            });
            Assert.All(sessions, session => Assert.False(File.Exists(
                Path.Combine(fixture.StorageRootPath, session.TemporaryRelativePath.Replace('/', Path.DirectorySeparatorChar)))));
        }
    }

    [Fact]
    public async Task DestinationFolderPurge_DoesNotCascadeSessionAndCleanupStillRemovesTemporaryFile()
    {
        var authenticated = await fixture.CreateAuthenticatedClientAsync("resumable-purge", "purge-password");
        using var client = authenticated.Client;
        var rootId = await GetRootIdAsync(client);
        using var createFolder = await client.PostAsJsonAsync("/api/v1/folders", new { parentId = rootId, name = "UploadTarget" });
        createFolder.EnsureSuccessStatusCode();
        var folderId = (await createFolder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var content = RandomNumberGenerator.GetBytes(1);
        var sessionId = await CreateSessionIdAsync(client, folderId, "orphan-safe.bin", 1, Sha(content));
        using (var chunk = await SendChunkAsync(client, sessionId, 0, content, Sha(content)))
        {
            chunk.EnsureSuccessStatusCode();
        }

        using (var trash = await client.DeleteAsync($"/api/v1/files/{folderId}"))
        {
            trash.EnsureSuccessStatusCode();
        }
        using (var purgeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/trash/{folderId}"))
        {
            purgeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var purge = await client.SendAsync(purgeRequest);
            Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);
        }

        string temporaryPath;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            var session = await database.UploadSessions.SingleAsync(item => item.Id == sessionId);
            Assert.Null(session.DestinationFolderId);
            temporaryPath = Path.Combine(
                fixture.StorageRootPath,
                session.TemporaryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(temporaryPath));
        }

        using (var complete = await client.PostAsync($"/api/v1/upload-sessions/{sessionId}/complete", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, complete.StatusCode);
            await AssertErrorAsync(complete, "FILE_NOT_FOUND");
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE upload_sessions SET expires_at = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE id = {sessionId}");
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadSessionCleanupService>().RunAsync(CancellationToken.None);
        }
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task SharedDestination_UsesTargetOwnerAndRejectsCompletionAfterPermissionDowngrade()
    {
        var ownerAuth = await fixture.CreateAuthenticatedClientAsync("shared-session-owner", "owner-password");
        var contributorAuth = await fixture.CreateAuthenticatedClientAsync("shared-session-contributor", "contributor-password");
        using var owner = ownerAuth.Client;
        using var contributor = contributorAuth.Client;
        Guid ownerId;
        Guid contributorId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
            ownerId = await database.Users.Where(user => user.UsernameNormalized == "SHARED-SESSION-OWNER")
                .Select(user => user.Id).SingleAsync();
            contributorId = await database.Users.Where(user => user.UsernameNormalized == "SHARED-SESSION-CONTRIBUTOR")
                .Select(user => user.Id).SingleAsync();
        }

        var rootId = await GetRootIdAsync(owner);
        using var createFolder = await owner.PostAsJsonAsync(
            "/api/v1/folders", new { parentId = rootId, name = "SharedSessionTarget" });
        createFolder.EnsureSuccessStatusCode();
        var folderId = (await createFolder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid shareId;
        using (var createShare = await owner.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                targetEntryId = folderId,
                members = new[] { new { userId = contributorId, permission = "CONTRIBUTOR" } },
            }))
        {
            createShare.EnsureSuccessStatusCode();
            shareId = (await createShare.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var content = new byte[] { 41 };
        var successfulSessionId = await CreateSessionIdAsync(
            contributor, folderId, "shared-complete.bin", content.Length, Sha(content));
        using (var chunk = await SendChunkAsync(contributor, successfulSessionId, 0, content, Sha(content)))
        {
            chunk.EnsureSuccessStatusCode();
        }
        Guid fileId;
        using (var complete = await contributor.PostAsync(
            $"/api/v1/upload-sessions/{successfulSessionId}/complete", null))
        {
            complete.EnsureSuccessStatusCode();
            var item = await complete.Content.ReadFromJsonAsync<JsonElement>();
            fileId = item.GetProperty("id").GetGuid();
            Assert.Equal(ownerId, item.GetProperty("owner").GetProperty("id").GetGuid());
            Assert.Equal("CONTRIBUTOR", item.GetProperty("permission").GetString());
        }
        using (var repeated = await contributor.PostAsync(
            $"/api/v1/upload-sessions/{successfulSessionId}/complete", null))
        {
            repeated.EnsureSuccessStatusCode();
            var item = await repeated.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(fileId, item.GetProperty("id").GetGuid());
            Assert.Equal(ownerId, item.GetProperty("owner").GetProperty("id").GetGuid());
            Assert.Equal("CONTRIBUTOR", item.GetProperty("permission").GetString());
        }

        var revokedSessionId = await CreateSessionIdAsync(
            contributor, folderId, "shared-revoked.bin", content.Length, Sha(content));
        using (var chunk = await SendChunkAsync(contributor, revokedSessionId, 0, content, Sha(content)))
        {
            chunk.EnsureSuccessStatusCode();
        }
        using (var downgrade = await owner.PutAsJsonAsync(
            $"/api/v1/shares/{shareId}/members/{contributorId}", new { permission = "VIEWER" }))
        {
            downgrade.EnsureSuccessStatusCode();
        }
        using (var rejected = await contributor.PostAsync(
            $"/api/v1/upload-sessions/{revokedSessionId}/complete", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
            await AssertErrorAsync(rejected, "FILE_NOT_FOUND");
        }
        using (var cancel = await contributor.DeleteAsync($"/api/v1/upload-sessions/{revokedSessionId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        }

        await using var verifyScope = fixture.Factory.Services.CreateAsyncScope();
        var verifyDatabase = verifyScope.ServiceProvider.GetRequiredService<KuraStorageDbContext>();
        var successfulSession = await verifyDatabase.UploadSessions.SingleAsync(session => session.Id == successfulSessionId);
        Assert.Equal(contributorId, successfulSession.ActorUserId);
        Assert.Equal(ownerId, successfulSession.TargetOwnerUserId);
        Assert.Equal(ownerId, (await verifyDatabase.FileEntries.SingleAsync(entry => entry.Id == fileId)).OwnerUserId);
        Assert.Single(await verifyDatabase.FileEntries.Where(entry => entry.Id == fileId).ToListAsync());
        Assert.Contains(await verifyDatabase.AuditLogs.ToListAsync(), audit =>
            audit.Action == "UPLOAD_SESSION_COMPLETE" && audit.ActorUserId == contributorId &&
            audit.TargetId == successfulSessionId.ToString());
        Assert.False(await verifyDatabase.FileEntries.AnyAsync(entry => entry.Name == "shared-revoked.bin"));
    }

    private static async Task<Guid> GetRootIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/files");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parentId").GetGuid();
    }

    private static async Task<Guid> CreateSessionIdAsync(
        HttpClient client,
        Guid destinationFolderId,
        string fileName,
        long size,
        string sha256)
    {
        using var response = await CreateSessionAsync(
            client,
            destinationFolderId,
            fileName,
            size,
            sha256,
            Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> CreateSessionAsync(
        HttpClient client,
        Guid destinationFolderId,
        string fileName,
        long size,
        string sha256,
        string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/upload-sessions")
        {
            Content = JsonContent.Create(new { destinationFolderId, fileName, size, contentType = "application/octet-stream", sha256 }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendChunkAsync(
        HttpClient client,
        Guid sessionId,
        long offset,
        byte[] content,
        string sha256)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/upload-sessions/{sessionId}/chunks")
        {
            Content = new ByteArrayContent(content),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add("Upload-Offset", offset.ToString());
        request.Headers.Add("X-Chunk-Sha256", sha256);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendMultipartAsync(
        HttpClient client,
        Guid destinationFolderId,
        string fileName,
        byte[] content)
    {
        var multipart = new MultipartFormDataContent
        {
            { new StringContent(destinationFolderId.ToString()), "destinationFolderId" },
            { new StringContent(fileName), "fileName" },
            { new StringContent(content.Length.ToString()), "size" },
            { new StringContent(Sha(content)), "sha256" },
            { new ByteArrayContent(content), "file", fileName },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/upload") { Content = multipart };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static string Sha(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("requestId").GetString()));
    }
}
