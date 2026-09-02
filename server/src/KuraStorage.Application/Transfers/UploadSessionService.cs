using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Sharing;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using KuraStorage.Domain.Transfers;

namespace KuraStorage.Application.Transfers;

public sealed class UploadSessionService(
    IUploadSessionRepository sessions,
    IFileRepository files,
    IUploadSessionStore store,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    ISystemClock clock,
    UploadSessionOptions options,
    UploadChunkLimiter limiter,
    IAuthorizationService? authorizationService = null,
    FileVersionService? fileVersions = null,
    UserActivityFactory? activities = null)
{
    private static readonly Meter Meter = new("KuraStorage.Transfers");
    private static readonly Counter<long> SessionCounter = Meter.CreateCounter<long>("kurastorage.upload.sessions");
    private static readonly UpDownCounter<long> ActiveSessions = Meter.CreateUpDownCounter<long>("kurastorage.upload.active_sessions");
    private static readonly Counter<long> ChunkCounter = Meter.CreateCounter<long>("kurastorage.upload.chunks");
    private static readonly Counter<long> ChunkBytes = Meter.CreateCounter<long>("kurastorage.upload.chunk.bytes");
    private static readonly Histogram<double> ChunkDuration = Meter.CreateHistogram<double>("kurastorage.upload.chunk.duration", "ms");
    private static readonly Counter<long> FailureCounter = Meter.CreateCounter<long>("kurastorage.upload.failures");

    public async Task<FileResult<CreatedUploadSession>> CreateAsync(
        CreateUploadSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(command.FileName, out var fileName) ||
            command.Size < 0 || command.Size > options.MaximumFileBytes ||
            !Guid.TryParse(command.IdempotencyKey, out _) || !ValidSha256(command.Sha256) ||
            (command.ContentType?.Length ?? 0) > 255)
        {
            var code = command.Size > options.MaximumFileBytes
                ? FileErrorCodes.FileSizeLimitExceeded
                : FileErrorCodes.ValidationFailed;
            var kind = command.Size > options.MaximumFileBytes
                ? FileFailureKind.PayloadTooLarge
                : FileFailureKind.BadRequest;
            return FileResult<CreatedUploadSession>.Fail(code, kind);
        }

        var normalizedSha = command.Sha256?.ToLowerInvariant();
        var normalizedContentType = NormalizeContentType(command.ContentType);
        var existing = await sessions.FindByActorAndKeyAsync(
            command.ActorUserId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.SameMetadata(
                    command.DeviceId,
                    command.DestinationFolderId,
                    fileName!.Value,
                    normalizedContentType,
                    command.Size,
                    normalizedSha))
            {
                return FileResult<CreatedUploadSession>.Fail(
                    FileErrorCodes.IdempotencyConflict,
                    FileFailureKind.Conflict);
            }

            return FileResult<CreatedUploadSession>.Success(
                new CreatedUploadSession(await MapAsync(existing, cancellationToken), false));
        }

        if (!await sessions.IsDeviceActiveAsync(command.ActorUserId, command.DeviceId, cancellationToken))
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.UploadSessionNotFound,
                FileFailureKind.NotFound);
        }

        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.StorageUnavailable,
                FileFailureKind.StorageUnavailable);
        }

        if (!await fileStore.HasCapacityAsync(command.Size, cancellationToken))
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.StorageCapacityInsufficient,
                FileFailureKind.CapacityInsufficient);
        }

        var parent = authorizationService is null
            ? await files.FindOwnedAsync(command.ActorUserId, command.DestinationFolderId, cancellationToken)
            : await files.FindByIdAsync(command.DestinationFolderId, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<CreatedUploadSession>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var targetOwnerUserId = parent!.OwnerUserId;
        var permission = await ResolvePermissionAsync(command.ActorUserId, parent, cancellationToken);
        if (!permission.Allows(ShareOperation.Contribute))
        {
            return FileResult<CreatedUploadSession>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        await using var destinationLock = await files.AcquireMutationLocksAsync(
            new[] { parent.Id }.Concat(OptionalId(permission.ShareTargetId)),
            cancellationToken);
        var parentExists = await files.ReloadAsync(parent, cancellationToken);
        var lockedPermission = parentExists
            ? await ResolvePermissionAsync(command.ActorUserId, parent, cancellationToken)
            : null;
        if (!parentExists || !IsActiveFolder(parent) || parent.OwnerUserId != targetOwnerUserId ||
            lockedPermission is null || !SamePermissionLockScope(permission, lockedPermission) ||
            !lockedPermission.Allows(ShareOperation.Contribute))
        {
            return FileResult<CreatedUploadSession>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await files.FindOperationAsync(targetOwnerUserId, command.IdempotencyKey, cancellationToken) is not null)
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.IdempotencyConflict,
                FileFailureKind.Conflict);
        }

        if (await files.HasIncompleteOperationAsync(
                targetOwnerUserId,
                parent!.Id,
                parent.RelativePath,
                cancellationToken))
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.RecoveryRequired,
                FileFailureKind.Conflict);
        }

        if (await files.FindActiveChildAsync(
                targetOwnerUserId,
                command.DestinationFolderId,
                fileName!.Value,
                cancellationToken) is not null)
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.FileNameConflict,
                FileFailureKind.Conflict);
        }

        if (await sessions.CountActiveForActorAsync(command.ActorUserId, cancellationToken) >=
                options.MaximumActiveSessionsPerUser ||
            await sessions.CountActiveForDeviceAsync(command.DeviceId, cancellationToken) >=
                options.MaximumActiveSessionsPerDevice)
        {
            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.UploadLimitReached,
                FileFailureKind.TooManyRequests);
        }

        var now = clock.UtcNow;
        var sessionId = Guid.NewGuid();
        var session = new UploadSession(
            sessionId,
            command.ActorUserId,
            targetOwnerUserId,
            command.DeviceId,
            command.DestinationFolderId,
            Guid.NewGuid(),
            command.IdempotencyKey,
            fileName.Value,
            normalizedContentType,
            command.Size,
            normalizedSha,
            $"upload-sessions/{command.ActorUserId:N}/{sessionId:N}.upload",
            now,
            now.AddHours(options.IdleExpirationHours),
            now.AddHours(options.AbsoluteExpirationHours));
        sessions.Add(session);
        files.Add(CreateAudit(command.ActorUserId, command.DeviceId, session.Id, "UPLOAD_SESSION_CREATE", "SUCCESS", command.RequestId, now));
        try
        {
            await sessions.SaveChangesAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            var raced = await sessions.FindByActorAndKeyAsync(
                command.ActorUserId,
                command.IdempotencyKey,
                cancellationToken);
            if (raced is not null && raced.SameMetadata(
                    command.DeviceId,
                    command.DestinationFolderId,
                    fileName.Value,
                    normalizedContentType,
                    command.Size,
                    normalizedSha))
            {
                return FileResult<CreatedUploadSession>.Success(
                    new CreatedUploadSession(await MapAsync(raced, cancellationToken), false));
            }

            return FileResult<CreatedUploadSession>.Fail(
                FileErrorCodes.IdempotencyConflict,
                FileFailureKind.Conflict);
        }

        SessionCounter.Add(1, new KeyValuePair<string, object?>("result", "created"));
        ActiveSessions.Add(1);
        return FileResult<CreatedUploadSession>.Success(
            new CreatedUploadSession(await MapAsync(session, cancellationToken), true));
    }

    public async Task<FileResult<UploadSessionItem>> GetAsync(
        Guid actorUserId,
        Guid deviceId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var mutationLock = await files.AcquireMutationLocksAsync([sessionId], cancellationToken);
        var session = await FindAccessibleAsync(actorUserId, deviceId, sessionId, cancellationToken);
        if (session is null)
        {
            return FileResult<UploadSessionItem>.Fail(
                FileErrorCodes.UploadSessionNotFound,
                FileFailureKind.NotFound);
        }

        if (session.IsExpiredAt(clock.UtcNow))
        {
            await ExpireAndCleanAsync(session, cancellationToken);
            return FileResult<UploadSessionItem>.Fail(
                FileErrorCodes.UploadSessionExpired,
                FileFailureKind.Conflict);
        }

        return FileResult<UploadSessionItem>.Success(await MapAsync(session, cancellationToken));
    }

    public async Task<FileResult<UploadChunkItem>> UploadChunkAsync(
        UploadChunkCommand command,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await UploadChunkCoreAsync(command, cancellationToken);
        }
        finally
        {
            ChunkDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task<FileResult<UploadChunkItem>> UploadChunkCoreAsync(
        UploadChunkCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Offset < 0 || command.Length <= 0 || command.Length > options.MaximumChunkBytes ||
            !ValidRequiredSha256(command.Sha256))
        {
            var tooLarge = command.Length > options.MaximumChunkBytes;
            return FileResult<UploadChunkItem>.Fail(
                tooLarge ? FileErrorCodes.ChunkSizeLimitExceeded : FileErrorCodes.ValidationFailed,
                tooLarge ? FileFailureKind.PayloadTooLarge : FileFailureKind.BadRequest);
        }

        await using var lease = await limiter.TryEnterAsync(cancellationToken);
        if (lease is null)
        {
            FailureCounter.Add(1, new KeyValuePair<string, object?>("reason", "concurrency_limit"));
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.UploadLimitReached,
                FileFailureKind.TooManyRequests);
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([command.SessionId], cancellationToken);
        var session = await FindAccessibleAsync(
            command.ActorUserId,
            command.DeviceId,
            command.SessionId,
            cancellationToken);
        if (session is null)
        {
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.UploadSessionNotFound,
                FileFailureKind.NotFound);
        }

        var stateFailure = await EnsureActiveAsync(session, cancellationToken);
        if (stateFailure is not null)
        {
            return FileResult<UploadChunkItem>.Fail(stateFailure.Code, stateFailure.Kind);
        }

        if (command.Offset == session.LastChunkOffset && session.IsLastChunk(
                command.Offset,
                command.Length,
                command.Sha256))
        {
            try
            {
                var replay = await store.ReadAndHashAsync(command.Content, command.Length, cancellationToken);
                if (!FixedEquals(replay.Sha256, command.Sha256))
                {
                    return FileResult<UploadChunkItem>.Fail(
                        FileErrorCodes.ChunkChecksumMismatch,
                        FileFailureKind.Unprocessable);
                }
            }
            catch (UploadChunkSizeMismatchException)
            {
                return FileResult<UploadChunkItem>.Fail(
                    FileErrorCodes.ValidationFailed,
                    FileFailureKind.BadRequest);
            }

            ChunkCounter.Add(1, new KeyValuePair<string, object?>("result", "replayed"));
            SessionCounter.Add(1, new KeyValuePair<string, object?>("result", "resumed"));
            return FileResult<UploadChunkItem>.Success(MapChunk(session, command.Offset, command.Length, command.Sha256, true));
        }

        var remaining = session.ExpectedSize - session.ReceivedBytes;
        var finalChunk = command.Length == remaining;
        if (command.Offset != session.ReceivedBytes || command.Length > remaining ||
            (!finalChunk && command.Length < UploadSessionOptions.MinimumChunkBytes))
        {
            FailureCounter.Add(1, new KeyValuePair<string, object?>("reason", "offset"));
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.UploadOffsetMismatch,
                FileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.StorageUnavailable,
                FileFailureKind.StorageUnavailable);
        }

        StoredChunk stored;
        var path = RelativeStoragePath.Create(session.TemporaryRelativePath);
        try
        {
            stored = await store.WriteChunkAsync(
                path,
                command.Offset,
                command.Content,
                command.Length,
                cancellationToken);
        }
        catch (UploadTemporaryFileTooShortException)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(CancellationToken.None);
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.RecoveryRequired,
                FileFailureKind.Conflict);
        }
        catch (UploadChunkSizeMismatchException)
        {
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.ValidationFailed,
                FileFailureKind.BadRequest);
        }
        catch (IOException)
        {
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.StorageUnavailable,
                FileFailureKind.StorageUnavailable);
        }

        if (!FixedEquals(stored.Sha256, command.Sha256))
        {
            await store.TruncateAsync(path, command.Offset, CancellationToken.None);
            FailureCounter.Add(1, new KeyValuePair<string, object?>("reason", "chunk_checksum"));
            return FileResult<UploadChunkItem>.Fail(
                FileErrorCodes.ChunkChecksumMismatch,
                FileFailureKind.Unprocessable);
        }

        session.AcceptChunk(
            command.Offset,
            stored.Length,
            stored.Sha256,
            clock.UtcNow,
            TimeSpan.FromHours(options.IdleExpirationHours));
        await sessions.SaveChangesAsync(cancellationToken);
        ChunkCounter.Add(1, new KeyValuePair<string, object?>("result", "accepted"));
        ChunkBytes.Add(stored.Length);
        return FileResult<UploadChunkItem>.Success(MapChunk(session, command.Offset, stored.Length, stored.Sha256, false));
    }

    public async Task<FileResult<FileItem>> CompleteAsync(
        Guid actorUserId,
        Guid deviceId,
        Guid sessionId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var initialSession = await FindAccessibleAsync(actorUserId, deviceId, sessionId, cancellationToken);
        if (initialSession is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.UploadSessionNotFound, FileFailureKind.NotFound);
        }

        FileEntry? initialParent = null;
        EffectivePermission? initialPermission = null;
        if (initialSession.DestinationFolderId is Guid initialDestinationFolderId)
        {
            initialParent = authorizationService is null
                ? await files.FindOwnedAsync(initialSession.TargetOwnerUserId, initialDestinationFolderId, cancellationToken)
                : await files.FindByIdAsync(initialDestinationFolderId, cancellationToken);
            if (IsActiveFolder(initialParent))
            {
                initialPermission = await ResolvePermissionAsync(actorUserId, initialParent!, cancellationToken);
            }
        }

        var lockIds = new List<Guid> { sessionId };
        if (initialSession.DestinationFolderId is Guid lockedDestinationFolderId)
        {
            lockIds.Add(lockedDestinationFolderId);
        }

        lockIds.AddRange(OptionalId(initialPermission?.ShareTargetId));
        await using var mutationLock = await files.AcquireMutationLocksAsync(lockIds, cancellationToken);
        await sessions.ReloadAsync(initialSession, cancellationToken);
        var session = await FindAccessibleAsync(actorUserId, deviceId, sessionId, cancellationToken);
        if (session is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.UploadSessionNotFound, FileFailureKind.NotFound);
        }

        if (session.Status == UploadSessionStatus.Completed)
        {
            var completed = await files.FindOwnedAsync(session.TargetOwnerUserId, session.FileEntryId, cancellationToken);
            return completed is null
                ? FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict)
                : FileResult<FileItem>.Success(await MapFileAsync(actorUserId, completed, cancellationToken));
        }

        var stateFailure = await EnsureActiveAsync(session, cancellationToken);
        if (stateFailure is not null)
        {
            return FileResult<FileItem>.Fail(stateFailure.Code, stateFailure.Kind);
        }

        if (session.ReceivedBytes != session.ExpectedSize)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.UploadIncomplete, FileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var source = RelativeStoragePath.Create(session.TemporaryRelativePath);
        var temporary = await store.InspectAsync(source, cancellationToken);
        if (!temporary.Exists && session.ExpectedSize == 0)
        {
            await store.TruncateAsync(source, 0, cancellationToken);
            temporary = await store.InspectAsync(source, cancellationToken);
        }

        if (!temporary.Exists || temporary.Length != session.ExpectedSize)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (session.ExpectedSha256 is not null)
        {
            var actual = await store.ComputeSha256Async(source, cancellationToken);
            if (!FixedEquals(actual, session.ExpectedSha256))
            {
                await store.TruncateAsync(source, 0, CancellationToken.None);
                session.ResetAfterChecksumFailure(
                    clock.UtcNow,
                    TimeSpan.FromHours(options.IdleExpirationHours));
                await sessions.SaveChangesAsync(CancellationToken.None);
                return FileResult<FileItem>.Fail(
                    FileErrorCodes.UploadChecksumMismatch,
                    FileFailureKind.Unprocessable);
            }
        }

        if (session.DestinationFolderId is not Guid destinationFolderId)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var parent = initialParent?.Id == destinationFolderId
            ? initialParent
            : authorizationService is null
                ? await files.FindOwnedAsync(session.TargetOwnerUserId, destinationFolderId, cancellationToken)
                : await files.FindByIdAsync(destinationFolderId, cancellationToken);
        if (parent is null || !await files.ReloadAsync(parent, cancellationToken) || !IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var lockedPermission = await ResolvePermissionAsync(actorUserId, parent, cancellationToken);
        if (parent.OwnerUserId != session.TargetOwnerUserId || initialPermission is null ||
            !SamePermissionLockScope(initialPermission, lockedPermission) ||
            !lockedPermission.Allows(ShareOperation.Contribute))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await files.HasIncompleteOperationAsync(
                session.TargetOwnerUserId,
                parent.Id,
                parent.RelativePath,
                cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var fileName = FileName.Create(session.FileName);
        if (await files.FindActiveChildAsync(session.TargetOwnerUserId, parent.Id, fileName.Value, cancellationToken) is not null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        var target = RelativeStoragePath.Create(parent.RelativePath).Append(fileName);
        var operation = new FileOperation(
            Guid.NewGuid(),
            session.TargetOwnerUserId,
            FileOperationType.Upload,
            session.FileEntryId,
            session.IdempotencyKey,
            source.Value,
            target.Value,
            session.ExpectedSize,
            session.ExpectedSha256,
            clock.UtcNow,
            deviceId,
            requestId,
            "UPLOAD_SESSION",
            actorUserId);
        session.BeginCompletion(operation.Id, clock.UtcNow);
        files.Add(operation);
        await sessions.SaveChangesAsync(cancellationToken);

        try
        {
            await fileStore.MoveAsync(source, target, false, cancellationToken);
        }
        catch (IOException)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        operation.MarkFilesystemDone(clock.UtcNow);
        await sessions.SaveChangesAsync(cancellationToken);
        var now = clock.UtcNow;
        var entry = FileEntry.CreateFile(
            session.FileEntryId,
            session.TargetOwnerUserId,
            parent.Id,
            fileName,
            target,
            session.ContentType,
            session.ExpectedSize,
            now);
        try
        {
            if (fileVersions is not null)
            {
                _ = await fileVersions.EnsureCurrentAsync(
                    entry,
                    FileVersionChangeKind.Upload,
                    operation.Id,
                    actorUserId,
                    deviceId,
                    cancellationToken,
                    operation);
            }
        }
        catch (IOException)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        await using var transaction = await files.BeginTransactionAsync(cancellationToken);
        files.Add(entry);
        if (activities is not null)
        {
            await activities.AddUploadAsync(
                operation.Id,
                actorUserId,
                deviceId,
                entry,
                entry.FileVersion,
                cancellationToken);
        }

        session.Complete(now);
        operation.Complete(now);
        files.Add(CreateAudit(actorUserId, deviceId, session.Id, "UPLOAD_SESSION_COMPLETE", "SUCCESS", requestId, now));
        try
        {
            await sessions.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        SessionCounter.Add(1, new KeyValuePair<string, object?>("result", "completed"));
        ActiveSessions.Add(-1);
        return FileResult<FileItem>.Success(await MapFileAsync(actorUserId, entry, cancellationToken));
    }

    public async Task<FileResult<bool>> CancelAsync(
        Guid actorUserId,
        Guid deviceId,
        Guid sessionId,
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var mutationLock = await files.AcquireMutationLocksAsync([sessionId], cancellationToken);
        var session = await FindAccessibleAsync(actorUserId, deviceId, sessionId, cancellationToken);
        if (session is null)
        {
            return FileResult<bool>.Fail(FileErrorCodes.UploadSessionNotFound, FileFailureKind.NotFound);
        }

        if (session.Status == UploadSessionStatus.Cancelled)
        {
            return FileResult<bool>.Success(true);
        }

        if (session.Status == UploadSessionStatus.Completed)
        {
            return FileResult<bool>.Fail(FileErrorCodes.UploadSessionCompleted, FileFailureKind.Conflict);
        }

        if (session.Status != UploadSessionStatus.Active)
        {
            return FileResult<bool>.Fail(StateCode(session), FileFailureKind.Conflict);
        }

        var now = clock.UtcNow;
        session.Cancel(null, now);
        files.Add(CreateAudit(actorUserId, deviceId, session.Id, "UPLOAD_SESSION_CANCEL", "SUCCESS", requestId, now));
        await sessions.SaveChangesAsync(cancellationToken);
        try
        {
            await store.DeleteIfExistsAsync(RelativeStoragePath.Create(session.TemporaryRelativePath), cancellationToken);
            session.MarkCleaned(clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
        }
        catch (IOException)
        {
            return FileResult<bool>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        SessionCounter.Add(1, new KeyValuePair<string, object?>("result", "cancelled"));
        ActiveSessions.Add(-1);
        return FileResult<bool>.Success(true);
    }

    internal async Task<FileResult<FileItem>> RecoverCompletingAsync(
        UploadSession session,
        CancellationToken cancellationToken)
    {
        if (session.Status != UploadSessionStatus.Completing || session.FileOperationId is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var operation = await files.FindOperationAsync(
            session.TargetOwnerUserId,
            session.IdempotencyKey,
            cancellationToken);
        if (session.DestinationFolderId is not Guid destinationFolderId)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var parent = await files.FindOwnedAsync(
            session.TargetOwnerUserId,
            destinationFolderId,
            cancellationToken);
        if (operation is null || !IsActiveFolder(parent) || operation.TargetRelativePath is null)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var source = RelativeStoragePath.Create(session.TemporaryRelativePath);
        var target = RelativeStoragePath.Create(operation.TargetRelativePath);
        var sourceExists = await fileStore.ExistsAsync(source, false, cancellationToken);
        var targetExists = await fileStore.ExistsAsync(target, false, cancellationToken);
        if (sourceExists == targetExists)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (sourceExists)
        {
            await fileStore.MoveAsync(source, target, false, cancellationToken);
        }

        if (operation.Status == FileOperationStatus.Pending)
        {
            operation.MarkFilesystemDone(clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
        }

        var existing = await files.FindOwnedAsync(session.TargetOwnerUserId, session.FileEntryId, cancellationToken);
        if (existing is null)
        {
            existing = FileEntry.CreateFile(
                session.FileEntryId,
                session.TargetOwnerUserId,
                destinationFolderId,
                FileName.Create(session.FileName),
                target,
                session.ContentType,
                session.ExpectedSize,
                clock.UtcNow);
            files.Add(existing);
        }

        try
        {
            if (fileVersions is not null)
            {
                _ = await fileVersions.EnsureCurrentAsync(
                    existing,
                    FileVersionChangeKind.Upload,
                    operation.Id,
                    session.ActorUserId,
                    session.DeviceId,
                    cancellationToken,
                    operation);
            }
        }
        catch (IOException)
        {
            session.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await sessions.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var now = clock.UtcNow;
        await using var transaction = await files.BeginTransactionAsync(cancellationToken);
        if (activities is not null)
        {
            await activities.AddUploadAsync(
                operation.Id,
                session.ActorUserId,
                session.DeviceId,
                existing,
                existing.FileVersion,
                cancellationToken);
        }

        session.Complete(now);
        operation.Complete(now);
        files.Add(CreateAudit(session.ActorUserId, session.DeviceId, session.Id, "UPLOAD_SESSION_RECOVER", "SUCCESS", operation.RequestId ?? session.Id.ToString(), now));
        await sessions.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return FileResult<FileItem>.Success(FileService.Map(existing));
    }

    private async Task<UploadSession?> FindAccessibleAsync(
        Guid actorUserId,
        Guid deviceId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        return session is not null && session.ActorUserId == actorUserId && session.DeviceId == deviceId &&
               await sessions.IsDeviceActiveAsync(actorUserId, deviceId, cancellationToken)
            ? session
            : null;
    }

    private async Task<FileFailure?> EnsureActiveAsync(
        UploadSession session,
        CancellationToken cancellationToken)
    {
        if (session.IsExpiredAt(clock.UtcNow))
        {
            await ExpireAndCleanAsync(session, cancellationToken);
            return new FileFailure(FileErrorCodes.UploadSessionExpired, FileFailureKind.Conflict);
        }

        return session.Status == UploadSessionStatus.Active
            ? null
            : new FileFailure(StateCode(session), FileFailureKind.Conflict);
    }

    private async Task ExpireAndCleanAsync(UploadSession session, CancellationToken cancellationToken)
    {
        session.Expire(clock.UtcNow);
        SessionCounter.Add(1, new KeyValuePair<string, object?>("result", "expired"));
        ActiveSessions.Add(-1);
        await sessions.SaveChangesAsync(cancellationToken);
        try
        {
            await store.DeleteIfExistsAsync(RelativeStoragePath.Create(session.TemporaryRelativePath), cancellationToken);
            session.MarkCleaned(clock.UtcNow);
            await sessions.SaveChangesAsync(cancellationToken);
        }
        catch (IOException)
        {
        }
    }

    private async Task<UploadSessionItem> MapAsync(UploadSession session, CancellationToken cancellationToken)
    {
        FileItem? file = null;
        if (session.Status == UploadSessionStatus.Completed)
        {
            var entry = await files.FindOwnedAsync(session.TargetOwnerUserId, session.FileEntryId, cancellationToken);
            file = entry is null ? null : FileService.Map(entry);
        }

        return new UploadSessionItem(
            session.Id,
            session.Status.ToString().ToUpperInvariant(),
            session.ExpectedSize,
            session.ReceivedBytes,
            session.ReceivedBytes,
            options.PreferredChunkBytes,
            options.MaximumChunkBytes,
            session.ExpiresAt,
            session.AbsoluteExpiresAt,
            session.Status == UploadSessionStatus.Active && !session.IsExpiredAt(clock.UtcNow),
            file);
    }

    private static UploadChunkItem MapChunk(
        UploadSession session,
        long offset,
        long length,
        string sha256,
        bool replayed) =>
        new(offset, length, sha256.ToLowerInvariant(), session.ReceivedBytes, session.ReceivedBytes, session.ExpiresAt, replayed);

    private static bool IsActiveFolder(FileEntry? entry) =>
        entry is { Status: FileEntryStatus.Active, EntryType: FileEntryType.Folder };

    private async Task<EffectivePermission> ResolvePermissionAsync(
        Guid actorUserId,
        FileEntry entry,
        CancellationToken cancellationToken) =>
        actorUserId == entry.OwnerUserId || authorizationService is null
            ? new EffectivePermission(
                entry.Id,
                EffectivePermissionLevel.Owner,
                PermissionSource.Owner,
                null,
                null)
            : await authorizationService.ResolveAsync(actorUserId, entry.Id, cancellationToken);

    private async Task<FileItem> MapFileAsync(
        Guid actorUserId,
        FileEntry entry,
        CancellationToken cancellationToken)
    {
        var owner = await files.FindOwnerAsync(entry.OwnerUserId, cancellationToken) ??
            new FileOwnerItem(entry.OwnerUserId, string.Empty);
        return FileService.Map(
            entry,
            owner,
            await ResolvePermissionAsync(actorUserId, entry, cancellationToken));
    }

    private static IEnumerable<Guid> OptionalId(Guid? value)
    {
        if (value is Guid id)
        {
            yield return id;
        }
    }

    private static bool SamePermissionLockScope(
        EffectivePermission initial,
        EffectivePermission current) =>
        initial.ShareTargetId == current.ShareTargetId;

    private static string StateCode(UploadSession session) => session.Status switch
    {
        UploadSessionStatus.Expired => FileErrorCodes.UploadSessionExpired,
        UploadSessionStatus.Cancelled => FileErrorCodes.UploadSessionCancelled,
        UploadSessionStatus.Completed => FileErrorCodes.UploadSessionCompleted,
        _ => FileErrorCodes.RecoveryRequired,
    };

    private static bool ValidSha256(string? value) => value is null || ValidRequiredSha256(value);

    private static bool ValidRequiredSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool FixedEquals(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));

    private static string? NormalizeContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditLog CreateAudit(
        Guid actorUserId,
        Guid deviceId,
        Guid sessionId,
        string action,
        string result,
        string requestId,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            actorUserId,
            deviceId,
            null,
            action,
            "UPLOAD_SESSION",
            sessionId.ToString(),
            result,
            requestId,
            now,
            AuditActorType.UserDevice);
}
