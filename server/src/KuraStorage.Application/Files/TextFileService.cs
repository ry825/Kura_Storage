using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Files;

public sealed class TextFileService(
    IFileRepository files,
    IFileVersionRepository versions,
    IFileVersionStore versionStore,
    IFileStore fileStore,
    IAuthorizationService authorization,
    FileVersionService fileVersions,
    IStorageGuard storageGuard,
    ISystemClock clock,
    UserActivityFactory? activities = null)
{
    private const int BufferSize = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<TextFileResult<TextDocument>> GetAsync(
        Guid actorUserId,
        Guid fileEntryId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || fileEntryId == Guid.Empty)
        {
            return NotFound<TextDocument>();
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([fileEntryId], cancellationToken);
        var entry = await files.FindByIdAsync(fileEntryId, cancellationToken);
        if (entry is null ||
            !(await authorization.ResolveAsync(actorUserId, fileEntryId, cancellationToken))
                .Allows(ShareOperation.View))
        {
            return NotFound<TextDocument>();
        }

        var eligibility = ValidateReadableEntry<TextDocument>(entry);
        if (eligibility is not null)
        {
            return eligibility;
        }

        if (await files.HasIncompleteOperationAsync(
                entry.OwnerUserId,
                entry.Id,
                entry.RelativePath,
                cancellationToken))
        {
            return Fail<TextDocument>(TextFileErrorCodes.FileStateConflict, TextFileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
        {
            return Fail<TextDocument>(TextFileErrorCodes.StorageUnavailable, TextFileFailureKind.StorageUnavailable);
        }

        FileVersionRecord? current;
        try
        {
            current = await fileVersions.EnsureCurrentAsync(
                entry,
                FileVersionChangeKind.Upload,
                Guid.NewGuid(),
                actorUserId: null,
                actorDeviceId: null,
                cancellationToken);
        }
        catch (FileVersionStorageUnavailableException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.StorageUnavailable, TextFileFailureKind.StorageUnavailable);
        }
        catch (FileVersionConsistencyException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
        }

        if (current is null)
        {
            return Fail<TextDocument>(TextFileErrorCodes.TextEncodingInvalid, TextFileFailureKind.Unprocessable);
        }

        await files.SaveChangesAsync(cancellationToken);

        try
        {
            await using var stream = await fileStore.OpenReadAsync(
                RelativeStoragePath.Create(entry.RelativePath),
                cancellationToken);
            var bytes = await ReadBoundedAsync(stream, entry.Size, cancellationToken);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (current.Size != bytes.LongLength ||
                !FixedTimeHexEquals(current.Sha256, sha256))
            {
                return Fail<TextDocument>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
            }

            var content = StrictUtf8.GetString(bytes);
            if (content.Length > 0 && content[0] == '\uFEFF')
            {
                content = content[1..];
            }

            return TextFileResult<TextDocument>.Success(
                new TextDocument(content, "UTF-8", entry.FileVersion, bytes.LongLength, sha256));
        }
        catch (DecoderFallbackException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.TextEncodingInvalid, TextFileFailureKind.Unprocessable);
        }
        catch (TextContentSizeException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.TextSizeLimitExceeded, TextFileFailureKind.PayloadTooLarge);
        }
        catch (IOException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.StorageUnavailable, TextFileFailureKind.StorageUnavailable);
        }
    }

    public async Task<TextFileResult<TextMutationResult>> SaveAsync(
        SaveTextFileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorUserId == Guid.Empty ||
            command.ActorDeviceId == Guid.Empty ||
            command.FileEntryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RequestId) ||
            !TextFileRules.ValidMutation(command.ExpectedVersion, command.OperationId))
        {
            return Fail<TextMutationResult>(TextFileErrorCodes.ValidationFailed, TextFileFailureKind.BadRequest);
        }

        if (!TextFileRules.TryEncode(command.Content, out var content, out var encodingFailure))
        {
            return encodingFailure switch
            {
                TextEncodingFailure.SizeLimitExceeded => Fail<TextMutationResult>(
                    TextFileErrorCodes.TextSizeLimitExceeded,
                    TextFileFailureKind.PayloadTooLarge),
                TextEncodingFailure.InvalidEncoding => Fail<TextMutationResult>(
                    TextFileErrorCodes.TextEncodingInvalid,
                    TextFileFailureKind.Unprocessable),
                _ => Fail<TextMutationResult>(
                    TextFileErrorCodes.ValidationFailed,
                    TextFileFailureKind.BadRequest),
            };
        }

        var contentSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await using var mutationLock = await files.AcquireMutationLocksAsync(
            [command.FileEntryId],
            cancellationToken);
        var entry = await files.FindByIdAsync(command.FileEntryId, cancellationToken);
        if (entry is null ||
            !(await authorization.ResolveAsync(
                    command.ActorUserId,
                    command.FileEntryId,
                    cancellationToken))
                .Allows(ShareOperation.Edit))
        {
            return NotFound<TextMutationResult>();
        }

        var eligibility = ValidateReadableEntry<TextMutationResult>(entry);
        if (eligibility is not null)
        {
            return eligibility;
        }

        var idempotencyKey = command.OperationId.ToString("D");
        var operation = await files.FindOperationAsync(
            entry.OwnerUserId,
            idempotencyKey,
            cancellationToken);
        if (operation is not null)
        {
            if (operation.OperationType != FileOperationType.TextEdit ||
                operation.FileEntryId != entry.Id ||
                operation.ExpectedSize != content.LongLength ||
                !string.Equals(operation.ExpectedSha256, contentSha256, StringComparison.Ordinal))
            {
                return Fail<TextMutationResult>(
                    TextFileErrorCodes.IdempotencyConflict,
                    TextFileFailureKind.Conflict);
            }

            if (operation.Status == FileOperationStatus.Completed &&
                operation.ResultFileVersion is long completedVersion)
            {
                var completed = await versions.FindAsync(entry.Id, completedVersion, cancellationToken);
                return completed is null
                    ? Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict)
                    : TextFileResult<TextMutationResult>.Success(MapMutation(completed));
            }

            if (operation.Status == FileOperationStatus.RecoveryRequired)
            {
                return Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict);
            }
        }

        if (await files.HasIncompleteOperationAsync(
                entry.OwnerUserId,
                entry.Id,
                entry.RelativePath,
                cancellationToken) &&
            operation is null)
        {
            return Fail<TextMutationResult>(TextFileErrorCodes.FileStateConflict, TextFileFailureKind.Conflict);
        }

        if (entry.FileVersion != command.ExpectedVersion)
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.FileVersionConflict,
                TextFileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.StorageUnavailable,
                TextFileFailureKind.StorageUnavailable);
        }

        if (!await fileStore.HasCapacityAsync(content.LongLength, cancellationToken))
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.StorageCapacityInsufficient,
                TextFileFailureKind.CapacityInsufficient);
        }

        var now = clock.UtcNow;
        try
        {
            var baseline = await fileVersions.EnsureCurrentAsync(
                entry,
                FileVersionChangeKind.Upload,
                command.OperationId,
                actorUserId: null,
                actorDeviceId: null,
                cancellationToken);
            if (baseline is null)
            {
                return Fail<TextMutationResult>(
                    TextFileErrorCodes.TextEncodingInvalid,
                    TextFileFailureKind.Unprocessable);
            }

            var createdOperation = operation is null;
            operation ??= new FileOperation(
                command.OperationId,
                entry.OwnerUserId,
                FileOperationType.TextEdit,
                entry.Id,
                idempotencyKey,
                $"upload-temp/{entry.OwnerUserId:N}/{command.OperationId:N}.upload",
                entry.RelativePath,
                content.LongLength,
                contentSha256,
                now,
                command.ActorDeviceId,
                command.RequestId,
                actorUserId: command.ActorUserId);
            if (createdOperation)
            {
                files.Add(operation);
            }

            await files.SaveChangesAsync(cancellationToken);

            var nextVersion = checked(entry.FileVersion + 1);
            var existingVersion = await versions.FindAsync(entry.Id, nextVersion, cancellationToken);
            FileVersionRecord resultVersion;
            if (existingVersion is not null)
            {
                if (existingVersion.Size != content.LongLength ||
                    !FixedTimeHexEquals(existingVersion.Sha256, contentSha256) ||
                    existingVersion.ChangeKind != FileVersionChangeKind.TextEdit)
                {
                    throw new FileVersionConsistencyException();
                }

                resultVersion = existingVersion;
                if (operation.ResultFileVersion is null)
                {
                    operation.RecordPublishedVersion(
                        entry.FileVersion,
                        nextVersion,
                        VersionTemporaryPath(entry, nextVersion, command.OperationId),
                        existingVersion.ContentRelativePath,
                        existingVersion.Sha256,
                        now);
                    await files.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                await using var versionSource = new MemoryStream(content, writable: false);
                var published = await versionStore.TryPublishAsync(
                    entry.OwnerUserId,
                    entry.Id,
                    nextVersion,
                    command.OperationId,
                    versionSource,
                    content.LongLength,
                    cancellationToken);
                if (published is null)
                {
                    throw new FileVersionConsistencyException();
                }

                resultVersion = new FileVersionRecord(
                    Guid.NewGuid(),
                    entry.Id,
                    nextVersion,
                    published.Size,
                    published.Sha256,
                    published.Path.Value,
                    FileVersionChangeKind.TextEdit,
                    command.ActorUserId,
                    command.ActorDeviceId,
                    now);
                versions.Add(resultVersion);
                operation.RecordPublishedVersion(
                    entry.FileVersion,
                    nextVersion,
                    published.TemporaryPath.Value,
                    published.Path.Value,
                    published.Sha256,
                    now);
                await files.SaveChangesAsync(cancellationToken);
            }

            await using var replacementSource = new MemoryStream(content, writable: false);
            var replacement = await fileStore.WriteUploadTempAsync(
                entry.OwnerUserId,
                command.OperationId,
                replacementSource,
                content.LongLength,
                cancellationToken);
            if (replacement.Size != content.LongLength ||
                !FixedTimeHexEquals(replacement.Sha256, contentSha256))
            {
                throw new FileVersionConsistencyException();
            }

            await fileStore.ReplaceAsync(
                replacement.Path,
                RelativeStoragePath.Create(entry.RelativePath),
                cancellationToken);
            if (operation.Status == FileOperationStatus.Pending)
            {
                operation.MarkFilesystemDone(now);
                await files.SaveChangesAsync(cancellationToken);
            }

            await using var transaction = await files.BeginTransactionAsync(cancellationToken);
            entry.ApplyManagedContentChange(content.LongLength, command.ExpectedVersion, now);
            if (activities is not null)
            {
                await activities.AddEditAsync(
                    operation.Id,
                    command.ActorUserId,
                    command.ActorDeviceId,
                    entry,
                    resultVersion.Version,
                    ActivityEditKind.TextSave,
                    cancellationToken);
            }

            files.Add(new AuditLog(
                Guid.NewGuid(),
                command.ActorUserId,
                command.ActorDeviceId,
                null,
                "FILE_TEXT_EDIT",
                "FILE_ENTRY",
                entry.Id.ToString(),
                "SUCCESS",
                command.RequestId,
                now));
            operation.Complete(now);
            await files.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TextFileResult<TextMutationResult>.Success(MapMutation(resultVersion));
        }
        catch (FileVersionStorageUnavailableException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return Fail<TextMutationResult>(
                TextFileErrorCodes.StorageUnavailable,
                TextFileFailureKind.StorageUnavailable);
        }
        catch (IOException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict);
        }
        catch (FilePersistenceConflictException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict);
        }
    }

    public async Task<TextFileResult<TextMutationResult>> RestoreAsync(
        RestoreTextVersionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorUserId == Guid.Empty ||
            command.ActorDeviceId == Guid.Empty ||
            command.FileEntryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RequestId) ||
            !TextFileRules.ValidVersion(command.Version) ||
            !TextFileRules.ValidMutation(command.ExpectedVersion, command.OperationId))
        {
            return Fail<TextMutationResult>(TextFileErrorCodes.ValidationFailed, TextFileFailureKind.BadRequest);
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync(
            [command.FileEntryId],
            cancellationToken);
        var entry = await files.FindByIdAsync(command.FileEntryId, cancellationToken);
        if (entry is null ||
            !(await authorization.ResolveAsync(
                    command.ActorUserId,
                    command.FileEntryId,
                    cancellationToken))
                .Allows(ShareOperation.Edit))
        {
            return NotFound<TextMutationResult>();
        }

        var eligibility = ValidateReadableEntry<TextMutationResult>(entry);
        if (eligibility is not null)
        {
            return eligibility;
        }

        var idempotencyKey = command.OperationId.ToString("D");
        var targetToken = command.Version.ToString(CultureInfo.InvariantCulture);
        var operation = await files.FindOperationAsync(
            entry.OwnerUserId,
            idempotencyKey,
            cancellationToken);
        if (operation is not null)
        {
            if (operation.OperationType != FileOperationType.VersionRestore ||
                operation.FileEntryId != entry.Id ||
                !string.Equals(operation.Trigger, targetToken, StringComparison.Ordinal))
            {
                return Fail<TextMutationResult>(
                    TextFileErrorCodes.IdempotencyConflict,
                    TextFileFailureKind.Conflict);
            }

            if (operation.Status == FileOperationStatus.Completed &&
                operation.ResultFileVersion is long completedVersion)
            {
                var completed = await versions.FindAsync(entry.Id, completedVersion, cancellationToken);
                return completed is null
                    ? Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict)
                    : TextFileResult<TextMutationResult>.Success(MapMutation(completed));
            }

            if (operation.Status == FileOperationStatus.RecoveryRequired)
            {
                return Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict);
            }
        }

        if (await files.HasIncompleteOperationAsync(
                entry.OwnerUserId,
                entry.Id,
                entry.RelativePath,
                cancellationToken) &&
            operation is null)
        {
            return Fail<TextMutationResult>(TextFileErrorCodes.FileStateConflict, TextFileFailureKind.Conflict);
        }

        if (entry.FileVersion != command.ExpectedVersion)
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.FileVersionConflict,
                TextFileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.StorageUnavailable,
                TextFileFailureKind.StorageUnavailable);
        }

        var baselineResult = await EnsureBaselineAsync<TextMutationResult>(entry, cancellationToken);
        if (baselineResult is not null)
        {
            return baselineResult;
        }

        var target = command.Version <= entry.FileVersion
            ? await versions.FindAsync(entry.Id, command.Version, cancellationToken)
            : null;
        if (target is null)
        {
            return Fail<TextMutationResult>(TextFileErrorCodes.FileVersionNotFound, TextFileFailureKind.NotFound);
        }

        byte[] content;
        try
        {
            await using var targetSource = await versionStore.OpenReadAsync(
                RelativeStoragePath.Create(target.ContentRelativePath),
                target.Size,
                target.Sha256,
                cancellationToken);
            content = await ReadBoundedAsync(targetSource, target.Size, cancellationToken);
            _ = StrictUtf8.GetString(content);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (!FixedTimeHexEquals(target.Sha256, actualSha256))
            {
                return Fail<TextMutationResult>(
                    TextFileErrorCodes.FileVersionCorrupt,
                    TextFileFailureKind.Conflict);
            }
        }
        catch (Exception exception) when (
            exception is FileVersionConsistencyException or DecoderFallbackException or TextContentSizeException)
        {
            return Fail<TextMutationResult>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
        }
        catch (IOException)
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.StorageUnavailable,
                TextFileFailureKind.StorageUnavailable);
        }

        if (!await fileStore.HasCapacityAsync(content.LongLength, cancellationToken))
        {
            return Fail<TextMutationResult>(
                TextFileErrorCodes.StorageCapacityInsufficient,
                TextFileFailureKind.CapacityInsufficient);
        }

        var now = clock.UtcNow;
        try
        {
            var createdOperation = operation is null;
            operation ??= new FileOperation(
                command.OperationId,
                entry.OwnerUserId,
                FileOperationType.VersionRestore,
                entry.Id,
                idempotencyKey,
                $"upload-temp/{entry.OwnerUserId:N}/{command.OperationId:N}.upload",
                entry.RelativePath,
                target.Size,
                target.Sha256,
                now,
                command.ActorDeviceId,
                command.RequestId,
                targetToken,
                command.ActorUserId);
            if (createdOperation)
            {
                files.Add(operation);
                await files.SaveChangesAsync(cancellationToken);
            }

            var nextVersion = checked(entry.FileVersion + 1);
            var existingVersion = await versions.FindAsync(entry.Id, nextVersion, cancellationToken);
            FileVersionRecord resultVersion;
            if (existingVersion is not null)
            {
                if (existingVersion.Size != content.LongLength ||
                    !FixedTimeHexEquals(existingVersion.Sha256, target.Sha256) ||
                    existingVersion.ChangeKind != FileVersionChangeKind.Restore)
                {
                    throw new FileVersionConsistencyException();
                }

                resultVersion = existingVersion;
                if (operation.ResultFileVersion is null)
                {
                    operation.RecordPublishedVersion(
                        entry.FileVersion,
                        nextVersion,
                        VersionTemporaryPath(entry, nextVersion, command.OperationId),
                        existingVersion.ContentRelativePath,
                        existingVersion.Sha256,
                        now);
                    await files.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                await using var versionSource = new MemoryStream(content, writable: false);
                var published = await versionStore.TryPublishAsync(
                    entry.OwnerUserId,
                    entry.Id,
                    nextVersion,
                    command.OperationId,
                    versionSource,
                    content.LongLength,
                    cancellationToken);
                if (published is null)
                {
                    throw new FileVersionConsistencyException();
                }

                resultVersion = new FileVersionRecord(
                    Guid.NewGuid(),
                    entry.Id,
                    nextVersion,
                    published.Size,
                    published.Sha256,
                    published.Path.Value,
                    FileVersionChangeKind.Restore,
                    command.ActorUserId,
                    command.ActorDeviceId,
                    now);
                versions.Add(resultVersion);
                operation.RecordPublishedVersion(
                    entry.FileVersion,
                    nextVersion,
                    published.TemporaryPath.Value,
                    published.Path.Value,
                    published.Sha256,
                    now);
                await files.SaveChangesAsync(cancellationToken);
            }

            await using var replacementSource = new MemoryStream(content, writable: false);
            var replacement = await fileStore.WriteUploadTempAsync(
                entry.OwnerUserId,
                command.OperationId,
                replacementSource,
                content.LongLength,
                cancellationToken);
            if (replacement.Size != content.LongLength ||
                !FixedTimeHexEquals(replacement.Sha256, target.Sha256))
            {
                throw new FileVersionConsistencyException();
            }

            await fileStore.ReplaceAsync(
                replacement.Path,
                RelativeStoragePath.Create(entry.RelativePath),
                cancellationToken);
            if (operation.Status == FileOperationStatus.Pending)
            {
                operation.MarkFilesystemDone(now);
                await files.SaveChangesAsync(cancellationToken);
            }

            await using var transaction = await files.BeginTransactionAsync(cancellationToken);
            entry.ApplyManagedContentChange(content.LongLength, command.ExpectedVersion, now);
            if (activities is not null)
            {
                await activities.AddEditAsync(
                    operation.Id,
                    command.ActorUserId,
                    command.ActorDeviceId,
                    entry,
                    resultVersion.Version,
                    ActivityEditKind.VersionRestore,
                    cancellationToken);
            }

            files.Add(new AuditLog(
                Guid.NewGuid(),
                command.ActorUserId,
                command.ActorDeviceId,
                null,
                "FILE_VERSION_RESTORE",
                "FILE_ENTRY",
                entry.Id.ToString(),
                "SUCCESS",
                command.RequestId,
                now));
            operation.Complete(now);
            await files.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TextFileResult<TextMutationResult>.Success(MapMutation(resultVersion));
        }
        catch (IOException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict);
        }
        catch (FilePersistenceConflictException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return Fail<TextMutationResult>(TextFileErrorCodes.RecoveryRequired, TextFileFailureKind.Conflict);
        }
    }

    public async Task<TextFileResult<FileVersionPage>> ListVersionsAsync(
        Guid actorUserId,
        Guid fileEntryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty ||
            fileEntryId == Guid.Empty ||
            !TextFileRules.ValidPage(page, pageSize))
        {
            return Fail<FileVersionPage>(
                TextFileErrorCodes.ValidationFailed,
                TextFileFailureKind.BadRequest);
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([fileEntryId], cancellationToken);
        var entry = await files.FindByIdAsync(fileEntryId, cancellationToken);
        if (entry is null ||
            !(await authorization.ResolveAsync(actorUserId, fileEntryId, cancellationToken))
                .Allows(ShareOperation.View))
        {
            return NotFound<FileVersionPage>();
        }

        var eligibility = ValidateReadableEntry<FileVersionPage>(entry);
        if (eligibility is not null)
        {
            return eligibility;
        }

        if (await files.HasIncompleteOperationAsync(
                entry.OwnerUserId,
                entry.Id,
                entry.RelativePath,
                cancellationToken))
        {
            return Fail<FileVersionPage>(TextFileErrorCodes.FileStateConflict, TextFileFailureKind.Conflict);
        }

        var baselineResult = await EnsureBaselineAsync<FileVersionPage>(entry, cancellationToken);
        if (baselineResult is not null)
        {
            return baselineResult;
        }

        var skip = checked((page - 1) * pageSize);
        var items = await versions.ListAsync(
            entry.Id,
            entry.FileVersion,
            skip,
            pageSize,
            cancellationToken);
        var total = await versions.CountAsync(entry.Id, entry.FileVersion, cancellationToken);
        return TextFileResult<FileVersionPage>.Success(
            new FileVersionPage(
                items.Select(MapHistory).ToArray(),
                page,
                pageSize,
                total));
    }

    public async Task<TextFileResult<TextDocument>> GetVersionTextAsync(
        Guid actorUserId,
        Guid fileEntryId,
        long version,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty ||
            fileEntryId == Guid.Empty ||
            !TextFileRules.ValidVersion(version))
        {
            return Fail<TextDocument>(TextFileErrorCodes.ValidationFailed, TextFileFailureKind.BadRequest);
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([fileEntryId], cancellationToken);
        var entry = await files.FindByIdAsync(fileEntryId, cancellationToken);
        if (entry is null ||
            !(await authorization.ResolveAsync(actorUserId, fileEntryId, cancellationToken))
                .Allows(ShareOperation.View))
        {
            return NotFound<TextDocument>();
        }

        var eligibility = ValidateReadableEntry<TextDocument>(entry);
        if (eligibility is not null)
        {
            return eligibility;
        }

        if (await files.HasIncompleteOperationAsync(
                entry.OwnerUserId,
                entry.Id,
                entry.RelativePath,
                cancellationToken))
        {
            return Fail<TextDocument>(TextFileErrorCodes.FileStateConflict, TextFileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
        {
            return Fail<TextDocument>(TextFileErrorCodes.StorageUnavailable, TextFileFailureKind.StorageUnavailable);
        }

        var baselineResult = await EnsureBaselineAsync<TextDocument>(entry, cancellationToken);
        if (baselineResult is not null)
        {
            return baselineResult;
        }

        var record = version <= entry.FileVersion
            ? await versions.FindAsync(entry.Id, version, cancellationToken)
            : null;
        if (record is null)
        {
            return Fail<TextDocument>(TextFileErrorCodes.FileVersionNotFound, TextFileFailureKind.NotFound);
        }

        try
        {
            await using var stream = await versionStore.OpenReadAsync(
                RelativeStoragePath.Create(record.ContentRelativePath),
                record.Size,
                record.Sha256,
                cancellationToken);
            var bytes = await ReadBoundedAsync(stream, record.Size, cancellationToken);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!FixedTimeHexEquals(record.Sha256, sha256))
            {
                return Fail<TextDocument>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
            }

            var content = StrictUtf8.GetString(bytes);
            if (content.Length > 0 && content[0] == '\uFEFF')
            {
                content = content[1..];
            }

            return TextFileResult<TextDocument>.Success(
                new TextDocument(content, "UTF-8", record.Version, record.Size, record.Sha256));
        }
        catch (FileVersionConsistencyException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
        }
        catch (DecoderFallbackException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
        }
        catch (IOException)
        {
            return Fail<TextDocument>(TextFileErrorCodes.StorageUnavailable, TextFileFailureKind.StorageUnavailable);
        }
    }

    private static TextFileResult<T>? ValidateReadableEntry<T>(FileEntry entry)
    {
        if (entry.EntryType != FileEntryType.File || entry.Status != FileEntryStatus.Active)
        {
            return NotFound<T>();
        }

        if (!TextFileRules.IsSupportedMimeType(entry.MimeType))
        {
            return Fail<T>(TextFileErrorCodes.UnsupportedTextType, TextFileFailureKind.UnsupportedMediaType);
        }

        return entry.Size > FileVersionRecord.MaximumContentBytes
            ? Fail<T>(TextFileErrorCodes.TextSizeLimitExceeded, TextFileFailureKind.PayloadTooLarge)
            : null;
    }

    private async Task<TextFileResult<T>?> EnsureBaselineAsync<T>(
        FileEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await fileVersions.EnsureCurrentAsync(
                entry,
                FileVersionChangeKind.Upload,
                Guid.NewGuid(),
                actorUserId: null,
                actorDeviceId: null,
                cancellationToken);
            if (current is null)
            {
                return Fail<T>(TextFileErrorCodes.TextEncodingInvalid, TextFileFailureKind.Unprocessable);
            }

            await files.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (FileVersionStorageUnavailableException)
        {
            return Fail<T>(TextFileErrorCodes.StorageUnavailable, TextFileFailureKind.StorageUnavailable);
        }
        catch (FileVersionConsistencyException)
        {
            return Fail<T>(TextFileErrorCodes.FileVersionCorrupt, TextFileFailureKind.Conflict);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (expectedSize is < 0 or > FileVersionRecord.MaximumContentBytes)
        {
            throw new TextContentSizeException();
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var destination = new MemoryStream(checked((int)expectedSize));
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > expectedSize || total > FileVersionRecord.MaximumContentBytes)
                {
                    throw new TextContentSizeException();
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total != expectedSize)
            {
                throw new TextContentSizeException();
            }

            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string VersionTemporaryPath(FileEntry entry, long version, Guid operationId) =>
        $"version-temp/{entry.OwnerUserId:N}/{entry.Id:N}/{version}/{operationId:N}.part";

    private async Task RequireRecoveryAsync(FileOperation? operation, CancellationToken cancellationToken)
    {
        if (operation is null || operation.Status == FileOperationStatus.RecoveryRequired)
        {
            return;
        }

        operation.RequireRecovery(TextFileErrorCodes.RecoveryRequired, clock.UtcNow);
        await files.SaveChangesAsync(cancellationToken.IsCancellationRequested
            ? CancellationToken.None
            : cancellationToken);
    }

    private static TextMutationResult MapMutation(FileVersionRecord record) =>
        new(
            record.Version,
            record.Size,
            record.Sha256,
            TextFileRules.ToContractChangeKind(record.ChangeKind),
            record.CreatedAt);

    private static FileVersionItem MapHistory(FileVersionHistoryRow row) =>
        new(
            row.Version,
            row.Size,
            row.Sha256,
            TextFileRules.ToContractChangeKind(row.ChangeKind),
            row.ChangeKind == FileVersionChangeKind.ExternalChange
                ? "External change"
                : row.ActorDisplayName ?? "Deleted user",
            row.CreatedAt);

    private static TextFileResult<T> NotFound<T>() =>
        Fail<T>(TextFileErrorCodes.FileNotFound, TextFileFailureKind.NotFound);

    private static TextFileResult<T> Fail<T>(string code, TextFileFailureKind kind) =>
        TextFileResult<T>.Fail(code, kind);
}

public sealed class TextContentSizeException : IOException;
