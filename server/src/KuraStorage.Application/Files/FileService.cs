using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class FileService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    IUserStorageProvisioner provisioner,
    ISystemClock clock,
    TrashPurgeOptions? purgeOptions = null)
{
    private readonly int retentionDays = purgeOptions?.RetentionDays ?? 30;
    public async Task<FileResult<FilePage>> ListAsync(
        Guid ownerUserId,
        Guid? parentId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 500)
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (parentId is null)
        {
            if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
            {
                return FileResult<FilePage>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
            }

            await provisioner.ProvisionAsync(ownerUserId, clock.UtcNow, cancellationToken);
        }

        var parent = parentId is null
            ? await repository.FindRootAsync(ownerUserId, cancellationToken)
            : await repository.FindOwnedAsync(ownerUserId, parentId.Value, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, parent!, cancellationToken))
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var skip = checked((page - 1) * pageSize);
        var entries = await repository.ListActiveChildrenAsync(
            ownerUserId,
            parent!.Id,
            skip,
            pageSize,
            cancellationToken);
        var count = await repository.CountActiveChildrenAsync(ownerUserId, parent.Id, cancellationToken);
        return FileResult<FilePage>.Success(
            new FilePage(parent.Id, entries.Select(Map).ToArray(), page, pageSize, count));
    }

    public async Task<FileResult<FileItem>> GetAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var entry = await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken);
        if (entry?.Status != FileEntryStatus.Active)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        return await IsBlockedAsync(ownerUserId, entry, cancellationToken)
            ? FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict)
            : FileResult<FileItem>.Success(Map(entry));
    }

    public async Task<FileResult<FileItem>> RenameAsync(
        RenameFileCommand command,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(command.Name, out var name) ||
            command.OwnerUserId == Guid.Empty ||
            command.ActorDeviceId == Guid.Empty ||
            command.FileEntryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RequestId))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.StorageUnavailable, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var initial = await repository.FindOwnedAsync(command.OwnerUserId, command.FileEntryId, cancellationToken);
        if (initial is null || initial.Status != FileEntryStatus.Active)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (initial.ParentId is not Guid parentId)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileOperationNotAllowed, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileOperationNotAllowed, FileFailureKind.Conflict);
        }

        if (await IsBlockedAsync(command.OwnerUserId, initial, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            [initial.Id, parentId],
            cancellationToken);
        await repository.ReloadAsync(initial, cancellationToken);
        var entry = initial;
        if (entry is null || entry.Status != FileEntryStatus.Active)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(command.OwnerUserId, entry, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (string.Equals(entry.Name, name.Value, StringComparison.Ordinal))
        {
            await AuditSuccessAsync(command, cancellationToken);
            return FileResult<FileItem>.Success(Map(entry));
        }

        if (await repository.FindActiveChildAsync(
                command.OwnerUserId,
                parentId,
                name.Value,
                cancellationToken) is not null)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNameConflict, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        var source = RelativeStoragePath.Create(entry.RelativePath);
        var parentPath = source.Value[..source.Value.LastIndexOf('/')];
        var target = RelativeStoragePath.Create(parentPath).Append(name);
        return await RelocateAsync(
            entry,
            target,
            parentId,
            FileOperationType.Rename,
            command.ActorDeviceId,
            command.RequestId,
            cancellationToken);
    }

    public async Task<FileResult<FileItem>> MoveAsync(
        MoveFileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.OwnerUserId == Guid.Empty ||
            command.ActorDeviceId == Guid.Empty ||
            command.FileEntryId == Guid.Empty ||
            command.TargetParentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RequestId))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.StorageUnavailable, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var initial = await repository.FindOwnedAsync(command.OwnerUserId, command.FileEntryId, cancellationToken);
        var initialTarget = await repository.FindOwnedAsync(
            command.OwnerUserId,
            command.TargetParentId,
            cancellationToken);
        if (initial is null || initial.Status != FileEntryStatus.Active || !IsActiveFolder(initialTarget))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (initial.ParentId is not Guid sourceParentId)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileOperationNotAllowed, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileOperationNotAllowed, FileFailureKind.Conflict);
        }

        if (await IsBlockedAsync(command.OwnerUserId, initial, cancellationToken) ||
            await IsBlockedAsync(command.OwnerUserId, initialTarget!, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            [initial.Id, sourceParentId, command.TargetParentId],
            cancellationToken);
        await repository.ReloadAsync(initial, cancellationToken);
        await repository.ReloadAsync(initialTarget!, cancellationToken);
        var entry = initial;
        var targetParent = initialTarget;
        if (entry is null || entry.Status != FileEntryStatus.Active || !IsActiveFolder(targetParent))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(command.OwnerUserId, entry, cancellationToken) ||
            await IsBlockedAsync(command.OwnerUserId, targetParent!, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (entry.ParentId == command.TargetParentId)
        {
            await AuditSuccessAsync(command, cancellationToken);
            return FileResult<FileItem>.Success(Map(entry));
        }

        if (entry.EntryType == FileEntryType.Folder &&
            (entry.Id == targetParent!.Id ||
             targetParent.RelativePath.StartsWith(entry.RelativePath + "/", StringComparison.Ordinal)))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileMoveCycle, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileMoveCycle, FileFailureKind.Conflict);
        }

        if (await repository.FindActiveChildAsync(
                command.OwnerUserId,
                targetParent!.Id,
                entry.Name,
                cancellationToken) is not null)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNameConflict, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        var target = RelativeStoragePath.Create(targetParent.RelativePath).Append(FileName.Create(entry.Name));
        var descendants = entry.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(command.OwnerUserId, entry.RelativePath, cancellationToken)
            : [];
        if (ExceedsMaximumDepth(target.Value, entry.RelativePath, descendants))
        {
            await AuditFailureAsync(command, FileErrorCodes.ValidationFailed, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        return await RelocateAsync(
            entry,
            target,
            targetParent.Id,
            FileOperationType.Move,
            command.ActorDeviceId,
            command.RequestId,
            cancellationToken,
            descendants);
    }

    public async Task<FileResult<FileItem>> CreateFolderAsync(
        Guid ownerUserId,
        Guid? parentId,
        string name,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(name, out var fileName))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        await provisioner.ProvisionAsync(ownerUserId, clock.UtcNow, cancellationToken);
        var parent = parentId is null
            ? await repository.FindRootAsync(ownerUserId, cancellationToken)
            : await repository.FindOwnedAsync(ownerUserId, parentId.Value, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, parent!, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (await repository.FindActiveChildAsync(ownerUserId, parent!.Id, fileName.Value, cancellationToken) is not null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        var now = clock.UtcNow;
        var entry = FileEntry.CreateFolder(
            Guid.NewGuid(),
            ownerUserId,
            parent.Id,
            fileName,
            RelativeStoragePath.Create(parent.RelativePath).Append(fileName),
            now);
        var operation = new FileOperation(
            Guid.NewGuid(),
            ownerUserId,
            FileOperationType.CreateFolder,
            entry.Id,
            null,
            null,
            entry.RelativePath,
            null,
            null,
            now);
        repository.Add(operation);
        await repository.SaveChangesAsync(cancellationToken);
        try
        {
            await fileStore.CreateDirectoryAsync(RelativeStoragePath.Create(entry.RelativePath), cancellationToken);
        }
        catch (IOException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        operation.MarkFilesystemDone(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        repository.Add(entry);
        operation.Complete(clock.UtcNow);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(Map(entry));
    }

    public async Task<FileResult<FileItem>> UploadAsync(
        Guid ownerUserId,
        UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(command.FileName, out var fileName) ||
            command.Size < 0 ||
            !Guid.TryParse(command.IdempotencyKey, out _) ||
            !ValidSha256(command.Sha256))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        if (!await fileStore.HasCapacityAsync(command.Size, cancellationToken))
        {
            return FileResult<FileItem>.Fail(
                FileErrorCodes.StorageCapacityInsufficient,
                FileFailureKind.CapacityInsufficient);
        }

        var parent = await repository.FindOwnedAsync(ownerUserId, command.DestinationFolderId, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, parent!, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var target = RelativeStoragePath.Create(parent!.RelativePath).Append(fileName);
        var existingOperation = await repository.FindOperationAsync(
            ownerUserId,
            command.IdempotencyKey,
            cancellationToken);
        if (existingOperation is not null)
        {
            if (!SameUpload(existingOperation, target, command))
            {
                return FileResult<FileItem>.Fail(FileErrorCodes.IdempotencyConflict, FileFailureKind.Conflict);
            }

            if (existingOperation.Status == FileOperationStatus.Completed &&
                existingOperation.FileEntryId is Guid completedId)
            {
                var completed = await repository.FindOwnedAsync(ownerUserId, completedId, cancellationToken);
                if (completed is not null)
                {
                    return FileResult<FileItem>.Success(Map(completed));
                }
            }

            if (existingOperation.Status == FileOperationStatus.RecoveryRequired)
            {
                await fileStore.DeleteIfExistsAsync(
                    RelativeStoragePath.Create(existingOperation.SourceRelativePath!),
                    cancellationToken);
                existingOperation.Retry(clock.UtcNow);
                await repository.SaveChangesAsync(cancellationToken);
            }
            else
            {
                return FileResult<FileItem>.Fail(FileErrorCodes.IdempotencyConflict, FileFailureKind.Conflict);
            }
        }

        if (await repository.FindActiveChildAsync(
                ownerUserId,
                command.DestinationFolderId,
                fileName.Value,
                cancellationToken) is not null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        var now = clock.UtcNow;
        var entryId = existingOperation?.FileEntryId ?? Guid.NewGuid();
        var operation = existingOperation ?? new FileOperation(
            Guid.NewGuid(),
            ownerUserId,
            FileOperationType.Upload,
            entryId,
            command.IdempotencyKey,
            $"upload-temp/{ownerUserId:N}/{entryId:N}.upload",
            target.Value,
            command.Size,
            command.Sha256?.ToLowerInvariant(),
            now);
        if (existingOperation is null)
        {
            repository.Add(operation);
            await repository.SaveChangesAsync(cancellationToken);
        }

        StoredUpload stored;
        try
        {
            stored = await fileStore.WriteUploadTempAsync(
                ownerUserId,
                entryId,
                command.Content,
                command.Size,
                cancellationToken);
        }
        catch (UploadSizeMismatchException)
        {
            operation.RequireRecovery(FileErrorCodes.UploadSizeMismatch, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.UploadSizeMismatch, FileFailureKind.Unprocessable);
        }
        catch (OperationCanceledException)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (IOException)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        if (stored.Size != command.Size)
        {
            await fileStore.DeleteIfExistsAsync(stored.Path, CancellationToken.None);
            operation.RequireRecovery(FileErrorCodes.UploadSizeMismatch, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.UploadSizeMismatch, FileFailureKind.Unprocessable);
        }

        if (command.Sha256 is not null &&
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(stored.Sha256),
                Convert.FromHexString(command.Sha256)))
        {
            await fileStore.DeleteIfExistsAsync(stored.Path, CancellationToken.None);
            operation.RequireRecovery(FileErrorCodes.UploadChecksumMismatch, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.UploadChecksumMismatch, FileFailureKind.Unprocessable);
        }

        try
        {
            await fileStore.MoveAsync(stored.Path, target, false, cancellationToken);
        }
        catch (IOException)
        {
            await fileStore.DeleteIfExistsAsync(stored.Path, CancellationToken.None);
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        operation.MarkFilesystemDone(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        var entry = FileEntry.CreateFile(
            entryId,
            ownerUserId,
            command.DestinationFolderId,
            fileName,
            target,
            NormalizeContentType(command.ContentType),
            stored.Size,
            now);
        repository.Add(entry);
        operation.Complete(clock.UtcNow);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(Map(entry));
    }

    public async Task<FileResult<DownloadFile>> DownloadAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var entry = await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken);
        if (entry is null || entry.Status != FileEntryStatus.Active || entry.EntryType != FileEntryType.File)
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, entry, cancellationToken))
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (!await StorageAvailableAsync(StorageIntent.Read, cancellationToken) ||
            !await fileStore.ExistsAsync(RelativeStoragePath.Create(entry.RelativePath), false, cancellationToken))
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        return FileResult<DownloadFile>.Success(
            new DownloadFile(
                Map(entry),
                await fileStore.OpenReadAsync(RelativeStoragePath.Create(entry.RelativePath), cancellationToken)));
    }

    public async Task<FileResult<FilePage>> ListTrashAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 500)
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        var entries = await repository.ListTrashedAsync(
            ownerUserId,
            checked((page - 1) * pageSize),
            pageSize,
            cancellationToken);
        var count = await repository.CountTrashedAsync(ownerUserId, cancellationToken);
        return FileResult<FilePage>.Success(
            new FilePage(null, entries.Select(entry => Map(entry, retentionDays)).ToArray(), page, pageSize, count));
    }

    public async Task<FileResult<FileItem>> TrashAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var entry = await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken);
        if (entry is null || entry.Status != FileEntryStatus.Active || entry.ParentId is not Guid parentId)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, entry, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            [entry.Id, parentId],
            cancellationToken);
        await repository.ReloadAsync(entry, cancellationToken);
        if (entry is null || entry.Status != FileEntryStatus.Active || entry.ParentId is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, entry, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var source = RelativeStoragePath.Create(entry.RelativePath);
        var target = RelativeStoragePath.Create($"users/{ownerUserId:N}/trash/{entry.Id:N}/{FileName.Create(entry.Name).Value}");
        var now = clock.UtcNow;
        var operation = new FileOperation(
            Guid.NewGuid(),
            ownerUserId,
            FileOperationType.Trash,
            entry.Id,
            null,
            source.Value,
            target.Value,
            null,
            null,
            now);
        repository.Add(operation);
        await repository.SaveChangesAsync(cancellationToken);
        var trashContainer = RelativeStoragePath.Create($"users/{ownerUserId:N}/trash/{entry.Id:N}");
        if (!await fileStore.ExistsAsync(trashContainer, true, cancellationToken))
        {
            await fileStore.CreateDirectoryAsync(trashContainer, cancellationToken);
        }
        await fileStore.MoveAsync(source, target, entry.EntryType == FileEntryType.Folder, cancellationToken);
        operation.MarkFilesystemDone(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        var descendants = entry.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(ownerUserId, source.Value, cancellationToken)
            : [];
        entry.Trash(target, clock.UtcNow);
        ApplyDescendantPaths(descendants, source.Value, target.Value, true, clock.UtcNow);
        operation.Complete(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return FileResult<FileItem>.Success(Map(entry, retentionDays));
    }

    public async Task<FileResult<FileItem>> RestoreAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var entry = await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken);
        if (entry is null ||
            entry.Status != FileEntryStatus.Trashed ||
            entry.OriginalParentId is not Guid parentId ||
            entry.OriginalRelativePath is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, entry, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            [entry.Id, parentId],
            cancellationToken);
        await repository.ReloadAsync(entry, cancellationToken);
        if (entry is null ||
            entry.Status != FileEntryStatus.Trashed ||
            entry.OriginalParentId is not Guid lockedParentId ||
            lockedParentId != parentId ||
            entry.OriginalRelativePath is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var parent = await repository.FindOwnedAsync(ownerUserId, parentId, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileRestoreConflict, FileFailureKind.Conflict);
        }

        if (await IsBlockedAsync(ownerUserId, parent!, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (await repository.FindActiveChildAsync(ownerUserId, parentId, entry.Name, cancellationToken) is not null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileRestoreConflict, FileFailureKind.Conflict);
        }

        var source = RelativeStoragePath.Create(entry.RelativePath);
        var target = RelativeStoragePath.Create(entry.OriginalRelativePath);
        var now = clock.UtcNow;
        var operation = new FileOperation(
            Guid.NewGuid(),
            ownerUserId,
            FileOperationType.Restore,
            entry.Id,
            null,
            source.Value,
            target.Value,
            null,
            null,
            now);
        repository.Add(operation);
        await repository.SaveChangesAsync(cancellationToken);
        await fileStore.MoveAsync(source, target, entry.EntryType == FileEntryType.Folder, cancellationToken);
        operation.MarkFilesystemDone(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        var descendants = entry.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(ownerUserId, source.Value, cancellationToken)
            : [];
        entry.Restore(parentId, target, clock.UtcNow);
        ApplyDescendantPaths(descendants, source.Value, target.Value, false, clock.UtcNow);
        operation.Complete(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return FileResult<FileItem>.Success(Map(entry));
    }

    private async Task<FileResult<FileItem>> RelocateAsync(
        FileEntry entry,
        RelativeStoragePath target,
        Guid targetParentId,
        FileOperationType operationType,
        Guid actorDeviceId,
        string requestId,
        CancellationToken cancellationToken,
        IReadOnlyList<FileEntry>? loadedDescendants = null)
    {
        var source = RelativeStoragePath.Create(entry.RelativePath);
        var directory = entry.EntryType == FileEntryType.Folder;
        var targetParentPath = RelativeStoragePath.Create(
            target.Value[..target.Value.LastIndexOf('/')]);
        if (!await fileStore.ExistsAsync(targetParentPath, true, cancellationToken))
        {
            await RecordAuditAsync(
                entry.OwnerUserId,
                actorDeviceId,
                entry.Id,
                operationType,
                FileErrorCodes.StorageUnavailable,
                requestId,
                cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        if (await fileStore.ExistsAsync(target, directory, cancellationToken) ||
            await fileStore.ExistsAsync(target, !directory, cancellationToken))
        {
            await RecordAuditAsync(
                entry.OwnerUserId,
                actorDeviceId,
                entry.Id,
                operationType,
                FileErrorCodes.FileNameConflict,
                requestId,
                cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        if (!await fileStore.ExistsAsync(source, directory, cancellationToken))
        {
            await RecordAuditAsync(
                entry.OwnerUserId,
                actorDeviceId,
                entry.Id,
                operationType,
                FileErrorCodes.StorageUnavailable,
                requestId,
                cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var operation = new FileOperation(
            Guid.NewGuid(),
            entry.OwnerUserId,
            operationType,
            entry.Id,
            null,
            source.Value,
            target.Value,
            null,
            null,
            clock.UtcNow);
        repository.Add(operation);
        await repository.SaveChangesAsync(cancellationToken);
        try
        {
            await fileStore.MoveAsync(source, target, directory, cancellationToken);
        }
        catch (IOException)
        {
            var sourceExists = await fileStore.ExistsAsync(source, directory, CancellationToken.None);
            var targetExists = await fileStore.ExistsAsync(target, directory, CancellationToken.None);
            if (!sourceExists && targetExists)
            {
                operation.MarkFilesystemDone(clock.UtcNow);
                await repository.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
                await repository.SaveChangesAsync(CancellationToken.None);
                var code = sourceExists && targetExists
                    ? FileErrorCodes.FileNameConflict
                    : FileErrorCodes.RecoveryRequired;
                await RecordAuditAsync(
                    entry.OwnerUserId,
                    actorDeviceId,
                    entry.Id,
                    operationType,
                    code,
                    requestId,
                    CancellationToken.None);
                return FileResult<FileItem>.Fail(code, FileFailureKind.Conflict);
            }
        }

        if (operation.Status == FileOperationStatus.Pending)
        {
            operation.MarkFilesystemDone(clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
        }

        var descendants = loadedDescendants ??
            (directory
                ? await repository.ListDescendantsAsync(entry.OwnerUserId, source.Value, cancellationToken)
                : []);
        var now = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        if (operationType == FileOperationType.Rename)
        {
            entry.Rename(
                FileName.Create(target.Value[(target.Value.LastIndexOf('/') + 1)..]),
                target,
                now);
        }
        else
        {
            entry.MoveTo(targetParentId, target, now);
        }

        foreach (var descendant in descendants)
        {
            descendant.RelocateDescendant(
                RelativeStoragePath.Create(
                    target.Value + descendant.RelativePath[source.Value.Length..]),
                now);
        }

        repository.Add(
            CreateAudit(
                entry.OwnerUserId,
                actorDeviceId,
                entry.Id,
                operationType,
                "SUCCESS",
                requestId,
                now));
        operation.Complete(now);
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return FileResult<FileItem>.Success(Map(entry));
    }

    private async Task<bool> IsBlockedAsync(
        Guid ownerUserId,
        FileEntry entry,
        CancellationToken cancellationToken) =>
        await repository.IsRelocationBlockedAsync(
            ownerUserId,
            entry.Id,
            entry.RelativePath,
            cancellationToken);

    private async Task AuditFailureAsync(
        RenameFileCommand command,
        string code,
        CancellationToken cancellationToken) =>
        await RecordAuditAsync(
            command.OwnerUserId,
            command.ActorDeviceId,
            command.FileEntryId,
            FileOperationType.Rename,
            code,
            command.RequestId,
            cancellationToken);

    private async Task AuditFailureAsync(
        MoveFileCommand command,
        string code,
        CancellationToken cancellationToken) =>
        await RecordAuditAsync(
            command.OwnerUserId,
            command.ActorDeviceId,
            command.FileEntryId,
            FileOperationType.Move,
            code,
            command.RequestId,
            cancellationToken);

    private async Task AuditSuccessAsync(
        RenameFileCommand command,
        CancellationToken cancellationToken) =>
        await RecordAuditAsync(
            command.OwnerUserId,
            command.ActorDeviceId,
            command.FileEntryId,
            FileOperationType.Rename,
            "SUCCESS",
            command.RequestId,
            cancellationToken);

    private async Task AuditSuccessAsync(
        MoveFileCommand command,
        CancellationToken cancellationToken) =>
        await RecordAuditAsync(
            command.OwnerUserId,
            command.ActorDeviceId,
            command.FileEntryId,
            FileOperationType.Move,
            "SUCCESS",
            command.RequestId,
            cancellationToken);

    private async Task RecordAuditAsync(
        Guid ownerUserId,
        Guid actorDeviceId,
        Guid entryId,
        FileOperationType operationType,
        string result,
        string requestId,
        CancellationToken cancellationToken)
    {
        repository.Add(
            CreateAudit(
                ownerUserId,
                actorDeviceId,
                entryId,
                operationType,
                result,
                requestId,
                clock.UtcNow));
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static AuditLog CreateAudit(
        Guid ownerUserId,
        Guid actorDeviceId,
        Guid entryId,
        FileOperationType operationType,
        string result,
        string requestId,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            ownerUserId,
            actorDeviceId,
            null,
            operationType == FileOperationType.Rename ? "FILE_RENAME" : "FILE_MOVE",
            "FILE_ENTRY",
            entryId.ToString(),
            result,
            requestId,
            now);

    private static bool ExceedsMaximumDepth(
        string targetPath,
        string sourcePath,
        IReadOnlyList<FileEntry> descendants)
    {
        var targetDepth = Math.Max(0, targetPath.Count(character => character == '/') - 2);
        if (targetDepth > 64)
        {
            return true;
        }

        var sourceSegments = sourcePath.Count(character => character == '/');
        return descendants.Any(
            descendant =>
                targetDepth +
                descendant.RelativePath.Count(character => character == '/') -
                sourceSegments > 64);
    }

    private async Task<bool> StorageAvailableAsync(StorageIntent intent, CancellationToken cancellationToken) =>
        await storageGuard.InspectAsync(intent, cancellationToken) == StorageStatus.Available;

    private static bool IsActiveFolder(FileEntry? entry) =>
        entry is { Status: FileEntryStatus.Active, EntryType: FileEntryType.Folder };

    private static bool ValidSha256(string? value) =>
        value is null || (value.Length == 64 && value.All(Uri.IsHexDigit));

    private static bool SameUpload(
        FileOperation operation,
        RelativeStoragePath target,
        UploadFileCommand command) =>
        operation.OperationType == FileOperationType.Upload &&
        operation.TargetRelativePath == target.Value &&
        operation.ExpectedSize == command.Size &&
        string.Equals(operation.ExpectedSha256, command.Sha256?.ToLowerInvariant(), StringComparison.Ordinal);

    private static string? NormalizeContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 255)];

    private static void ApplyDescendantPaths(
        IReadOnlyList<FileEntry> descendants,
        string sourcePrefix,
        string targetPrefix,
        bool trash,
        DateTimeOffset now)
    {
        foreach (var descendant in descendants)
        {
            var replacement = RelativeStoragePath.Create(
                targetPrefix + descendant.RelativePath[sourcePrefix.Length..]);
            if (trash)
            {
                descendant.TrashDescendant(replacement, now);
            }
            else
            {
                descendant.RestoreDescendant(replacement, now);
            }
        }
    }

    internal static FileItem Map(FileEntry entry, int retentionDays = 30) =>
        new(
            entry.Id,
            entry.ParentId,
            entry.Name,
            entry.EntryType.ToString().ToUpperInvariant(),
            entry.MimeType,
            entry.Size,
            entry.Status.ToString().ToUpperInvariant(),
            entry.FileVersion,
            entry.TrashedAt,
            entry.Status == FileEntryStatus.Trashed && entry.ParentId is null && entry.TrashedAt is not null
                ? entry.TrashedAt.Value.AddDays(retentionDays)
                : null,
            entry.CreatedAt,
            entry.UpdatedAt);
}

public sealed class UploadSizeMismatchException : IOException
{
    public UploadSizeMismatchException()
        : base("The upload exceeded its declared size.")
    {
    }
}
