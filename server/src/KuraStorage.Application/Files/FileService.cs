using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Application.Indexing;
using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Files;

public sealed class FileService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    IManagedFileSystemSnapshotReader snapshotReader,
    IUserStorageProvisioner provisioner,
    ISystemClock clock,
    TrashPurgeOptions? purgeOptions = null,
    IAuthorizationService? authorizationService = null,
    FileVersionService? fileVersions = null)
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
            : authorizationService is null
                ? await repository.FindOwnedAsync(ownerUserId, parentId.Value, cancellationToken)
                : await repository.FindByIdAsync(parentId.Value, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (!await CanViewAsync(ownerUserId, parent!, cancellationToken))
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(parent!.OwnerUserId, parent, cancellationToken))
        {
            return FileResult<FilePage>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var skip = checked((page - 1) * pageSize);
        var entries = await repository.ListActiveChildrenAsync(
            parent.OwnerUserId,
            parent!.Id,
            skip,
            pageSize,
            cancellationToken);
        var count = await repository.CountActiveChildrenAsync(parent.OwnerUserId, parent.Id, cancellationToken);
        var permissions = await ResolvePermissionsAsync(ownerUserId, entries, cancellationToken);
        var owner = await ResolveOwnerAsync(parent.OwnerUserId, cancellationToken);
        return FileResult<FilePage>.Success(
            new FilePage(
                parent.Id,
                entries.Select(entry => Map(entry, owner, permissions[entry.Id])).ToArray(),
                page,
                pageSize,
                count));
    }

    public async Task<FileResult<FileItem>> GetAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var entry = authorizationService is null
            ? await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken)
            : await repository.FindByIdAsync(entryId, cancellationToken);
        if (entry is null || entry.Status == FileEntryStatus.Trashed)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (!await CanViewAsync(ownerUserId, entry, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(entry.OwnerUserId, entry, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var permission = await ResolvePermissionAsync(ownerUserId, entry, cancellationToken);
        return FileResult<FileItem>.Success(
            Map(entry, await ResolveOwnerAsync(entry.OwnerUserId, cancellationToken), permission));
    }

    public async Task<FileResult<FileItem>> RenameAsync(
        RenameFileCommand command,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(command.Name, out var name) ||
            command.ActorUserId == Guid.Empty ||
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

        var initial = await repository.FindByIdAsync(command.FileEntryId, cancellationToken);
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

        var initialOwnerUserId = initial.OwnerUserId;
        var initialPermission = await ResolvePermissionAsync(command.ActorUserId, initial, cancellationToken);
        if (!initialPermission.Allows(ShareOperation.Edit))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            new[] { initial.Id, parentId }
                .Concat(OptionalId(initialPermission.ShareTargetId)),
            cancellationToken);
        var entryExists = await repository.ReloadAsync(initial, cancellationToken);
        var entry = initial;
        if (!entryExists || entry.Status != FileEntryStatus.Active ||
            entry.OwnerUserId != initialOwnerUserId || entry.ParentId != parentId)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var lockedPermission = await ResolvePermissionAsync(command.ActorUserId, entry, cancellationToken);
        if (!SamePermissionLockScope(initialPermission, lockedPermission) ||
            !lockedPermission.Allows(ShareOperation.Edit))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(entry.OwnerUserId, entry, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (string.Equals(entry.Name, name.Value, StringComparison.Ordinal))
        {
            await AuditSuccessAsync(command, cancellationToken);
            return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken));
        }

        if (await repository.FindActiveChildAsync(
                entry.OwnerUserId,
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
            command.ActorUserId,
            command.ActorDeviceId,
            command.RequestId,
            cancellationToken);
    }

    public async Task<FileResult<FileItem>> MoveAsync(
        MoveFileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorUserId == Guid.Empty ||
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

        var initial = await repository.FindByIdAsync(command.FileEntryId, cancellationToken);
        var initialTarget = await repository.FindByIdAsync(command.TargetParentId, cancellationToken);
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

        var initialSource = await repository.FindByIdAsync(sourceParentId, cancellationToken);
        if (!IsActiveFolder(initialSource) || initialSource!.OwnerUserId != initial.OwnerUserId ||
            initialTarget!.OwnerUserId != initial.OwnerUserId)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileOperationNotAllowed, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileOperationNotAllowed, FileFailureKind.Conflict);
        }

        var initialPermissions = await ResolvePermissionsAsync(
            command.ActorUserId,
            [initial, initialSource, initialTarget],
            cancellationToken);
        if (initialPermissions.Values.Any(permission => !permission.Allows(ShareOperation.Edit)))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            new[] { initial.Id, sourceParentId, command.TargetParentId }
                .Concat(initialPermissions.Values
                    .Where(permission => permission.ShareTargetId is not null)
                    .Select(permission => permission.ShareTargetId!.Value)),
            cancellationToken);
        var entryExists = await repository.ReloadAsync(initial, cancellationToken);
        var sourceExists = await repository.ReloadAsync(initialSource, cancellationToken);
        var targetExists = await repository.ReloadAsync(initialTarget!, cancellationToken);
        var entry = initial;
        var sourceParent = initialSource;
        var targetParent = initialTarget;
        if (!entryExists || !sourceExists || !targetExists ||
            entry.Status != FileEntryStatus.Active || !IsActiveFolder(sourceParent) || !IsActiveFolder(targetParent) ||
            entry.ParentId != sourceParentId || sourceParent.OwnerUserId != entry.OwnerUserId ||
            targetParent!.OwnerUserId != entry.OwnerUserId)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var lockedPermissions = await ResolvePermissionsAsync(
            command.ActorUserId,
            [entry, sourceParent, targetParent],
            cancellationToken);
        if (lockedPermissions.Any(candidate =>
                !initialPermissions.TryGetValue(candidate.Key, out var initialPermission) ||
                !SamePermissionLockScope(initialPermission, candidate.Value) ||
                !candidate.Value.Allows(ShareOperation.Edit)))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(entry.OwnerUserId, entry, cancellationToken) ||
            await IsBlockedAsync(entry.OwnerUserId, sourceParent, cancellationToken) ||
            await IsBlockedAsync(entry.OwnerUserId, targetParent, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (entry.ParentId == command.TargetParentId)
        {
            await AuditSuccessAsync(command, cancellationToken);
            return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken));
        }

        if (entry.EntryType == FileEntryType.Folder &&
            (entry.Id == targetParent!.Id ||
             targetParent.RelativePath.StartsWith(entry.RelativePath + "/", StringComparison.Ordinal)))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileMoveCycle, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileMoveCycle, FileFailureKind.Conflict);
        }

        if (await repository.FindActiveChildAsync(
                entry.OwnerUserId,
                targetParent!.Id,
                entry.Name,
                cancellationToken) is not null)
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNameConflict, cancellationToken);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        var target = RelativeStoragePath.Create(targetParent.RelativePath).Append(FileName.Create(entry.Name));
        var descendants = entry.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(entry.OwnerUserId, entry.RelativePath, cancellationToken)
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
            command.ActorUserId,
            command.ActorDeviceId,
            command.RequestId,
            cancellationToken,
            descendants);
    }

    public async Task<FileResult<FileItem>> CreateFolderAsync(
        CreateFolderCommand command,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(command.Name, out var fileName) ||
            command.ActorUserId == Guid.Empty || command.ActorDeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RequestId))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        if (command.ParentId is null)
        {
            await provisioner.ProvisionAsync(command.ActorUserId, clock.UtcNow, cancellationToken);
        }

        var parent = command.ParentId is null
            ? await repository.FindRootAsync(command.ActorUserId, cancellationToken)
            : await repository.FindByIdAsync(command.ParentId.Value, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var ownerUserId = parent!.OwnerUserId;
        var permission = await ResolvePermissionAsync(command.ActorUserId, parent, cancellationToken);
        if (!permission.Allows(ShareOperation.Contribute))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            new[] { parent.Id }.Concat(OptionalId(permission.ShareTargetId)),
            cancellationToken);
        var parentExists = await repository.ReloadAsync(parent, cancellationToken);
        var lockedPermission = parentExists
            ? await ResolvePermissionAsync(command.ActorUserId, parent, cancellationToken)
            : null;
        if (!parentExists || !IsActiveFolder(parent) || parent.OwnerUserId != ownerUserId ||
            lockedPermission is null || !SamePermissionLockScope(permission, lockedPermission) ||
            !lockedPermission.Allows(ShareOperation.Contribute))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, parent, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (await repository.FindActiveChildAsync(ownerUserId, parent.Id, fileName.Value, cancellationToken) is not null)
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
            now,
            command.ActorDeviceId,
            command.RequestId,
            "FOLDER_CREATE");
        repository.Add(operation);
        await repository.SaveChangesAsync(cancellationToken);
        try
        {
            await fileStore.CreateDirectoryAsync(RelativeStoragePath.Create(entry.RelativePath), cancellationToken);
        }
        catch (IOException)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNameConflict, FileFailureKind.Conflict);
        }

        operation.MarkFilesystemDone(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        var completedAt = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        repository.Add(entry);
        repository.Add(CreateAudit(
            command.ActorUserId, command.ActorDeviceId, entry.Id,
            FileOperationType.CreateFolder, "SUCCESS", command.RequestId, completedAt));
        operation.Complete(completedAt);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken));
    }

    public async Task<FileResult<FileItem>> UploadAsync(
        UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        if (!FileName.TryCreate(command.FileName, out var fileName) ||
            command.ActorUserId == Guid.Empty || command.ActorDeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RequestId) ||
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

        var parent = await repository.FindByIdAsync(command.DestinationFolderId, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var ownerUserId = parent!.OwnerUserId;
        var permission = await ResolvePermissionAsync(command.ActorUserId, parent, cancellationToken);
        if (!permission.Allows(ShareOperation.Contribute))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            new[] { parent.Id }.Concat(OptionalId(permission.ShareTargetId)),
            cancellationToken);
        var parentExists = await repository.ReloadAsync(parent, cancellationToken);
        var lockedPermission = parentExists
            ? await ResolvePermissionAsync(command.ActorUserId, parent, cancellationToken)
            : null;
        if (!parentExists || !IsActiveFolder(parent) || parent.OwnerUserId != ownerUserId ||
            lockedPermission is null || !SamePermissionLockScope(permission, lockedPermission) ||
            !lockedPermission.Allows(ShareOperation.Contribute))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await IsBlockedAsync(ownerUserId, parent, cancellationToken))
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
                    return FileResult<FileItem>.Success(await MapOwnerAsync(completed, cancellationToken));
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
            now,
            command.ActorDeviceId,
            command.RequestId,
            "MULTIPART");
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
        try
        {
            if (fileVersions is not null)
            {
                _ = await fileVersions.EnsureCurrentAsync(
                    entry,
                    FileVersionChangeKind.Upload,
                    operation.Id,
                    command.ActorUserId,
                    command.ActorDeviceId,
                    cancellationToken,
                    operation);
            }
        }
        catch (IOException)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var completedAt = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        repository.Add(entry);
        repository.Add(CreateAudit(
            command.ActorUserId, command.ActorDeviceId, entry.Id,
            FileOperationType.Upload, "SUCCESS", command.RequestId, completedAt));
        operation.Complete(completedAt);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken));
    }

    public async Task<FileResult<DownloadFile>> DownloadAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var entry = authorizationService is null
            ? await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken)
            : await repository.FindByIdAsync(entryId, cancellationToken);
        if (entry is null || entry.Status == FileEntryStatus.Trashed || entry.EntryType != FileEntryType.File)
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (!await CanViewAsync(ownerUserId, entry, cancellationToken))
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (entry.Status == FileEntryStatus.MissingCandidate)
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.FileMissingCandidate, FileFailureKind.Conflict);
        }

        if (entry.Status == FileEntryStatus.Missing)
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.FileMissing, FileFailureKind.Conflict);
        }

        if (await IsBlockedAsync(entry.OwnerUserId, entry, cancellationToken))
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (!await StorageAvailableAsync(StorageIntent.Read, cancellationToken))
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }


        ObservedStorageEntry? observed;
        try
        {
            observed = await snapshotReader.InspectAsync(RelativeStoragePath.Create(entry.RelativePath), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        if (observed is null)
        {
            return await MarkDownloadMissingAsync(entry, cancellationToken);
        }

        if (observed.IsolationReason is not null ||
            observed.OwnerUserId != entry.OwnerUserId ||
            observed.EntryType != FileEntryType.File ||
            !string.Equals(observed.RelativePath.Value, entry.RelativePath, StringComparison.Ordinal) ||
            !await StorageAvailableAsync(StorageIntent.Read, cancellationToken))
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        try
        {
            if (!await CanViewAsync(ownerUserId, entry, cancellationToken))
            {
                return FileResult<DownloadFile>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
            }

            var permission = await ResolvePermissionAsync(ownerUserId, entry, cancellationToken);
            return FileResult<DownloadFile>.Success(
                new DownloadFile(
                    Map(entry, await ResolveOwnerAsync(entry.OwnerUserId, cancellationToken), permission),
                    await fileStore.OpenReadAsync(RelativeStoragePath.Create(entry.RelativePath), cancellationToken)));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return await MarkDownloadMissingAsync(entry, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileResult<DownloadFile>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }
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
        var owner = await ResolveOwnerAsync(ownerUserId, cancellationToken);
        return FileResult<FilePage>.Success(
            new FilePage(
                null,
                entries.Select(entry => Map(entry, owner, OwnerPermission(entry.Id), retentionDays)).ToArray(),
                page,
                pageSize,
                count));
    }

    public async Task<FileResult<FileItem>> TrashAsync(
        TrashFileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorUserId == Guid.Empty || command.ActorDeviceId == Guid.Empty ||
            command.FileEntryId == Guid.Empty || string.IsNullOrWhiteSpace(command.RequestId))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (!await StorageAvailableAsync(StorageIntent.CreateOrUpdate, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var entry = await repository.FindByIdAsync(command.FileEntryId, cancellationToken);
        if (entry is null || entry.Status != FileEntryStatus.Active || entry.ParentId is not Guid parentId)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var ownerUserId = entry.OwnerUserId;
        var permission = await ResolvePermissionAsync(command.ActorUserId, entry, cancellationToken);
        if (!permission.Allows(ShareOperation.Edit))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            new[] { entry.Id, parentId }.Concat(OptionalId(permission.ShareTargetId)),
            cancellationToken);
        var entryExists = await repository.ReloadAsync(entry, cancellationToken);
        if (!entryExists || entry.Status != FileEntryStatus.Active || entry.ParentId != parentId ||
            entry.OwnerUserId != ownerUserId)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        var lockedPermission = await ResolvePermissionAsync(command.ActorUserId, entry, cancellationToken);
        if (!SamePermissionLockScope(permission, lockedPermission) ||
            !lockedPermission.Allows(ShareOperation.Edit))
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
            now,
            command.ActorDeviceId,
            command.RequestId,
            "USER");
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
        var completedAt = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        entry.Trash(target, completedAt);
        ApplyDescendantPaths(descendants, source.Value, target.Value, true, completedAt);
        repository.Add(CreateAudit(
            command.ActorUserId, command.ActorDeviceId, entry.Id,
            FileOperationType.Trash, "SUCCESS", command.RequestId, completedAt));
        operation.Complete(completedAt);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken, retentionDays));
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
        var entryExists = await repository.ReloadAsync(entry, cancellationToken);
        if (!entryExists ||
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
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken));
    }

    private async Task<FileResult<FileItem>> RelocateAsync(
        FileEntry entry,
        RelativeStoragePath target,
        Guid targetParentId,
        FileOperationType operationType,
        Guid actorUserId,
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
                actorUserId,
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
                actorUserId,
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
                actorUserId,
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
            clock.UtcNow,
            actorDeviceId,
            requestId,
            operationType.ToString().ToUpperInvariant());
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
                    actorUserId,
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
                actorUserId,
                actorDeviceId,
                entry.Id,
                operationType,
                "SUCCESS",
                requestId,
                now));
        operation.Complete(now);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            // The filesystem mutation is already durable and the operation remains
            // FILESYSTEM_DONE. Recovery will reconcile the catalog from the HDD.
            return FileResult<FileItem>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(await MapOwnerAsync(entry, cancellationToken));
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
            command.ActorUserId,
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
            command.ActorUserId,
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
            command.ActorUserId,
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
            command.ActorUserId,
            command.ActorDeviceId,
            command.FileEntryId,
            FileOperationType.Move,
            "SUCCESS",
            command.RequestId,
            cancellationToken);

    private async Task RecordAuditAsync(
        Guid actorUserId,
        Guid actorDeviceId,
        Guid entryId,
        FileOperationType operationType,
        string result,
        string requestId,
        CancellationToken cancellationToken)
    {
        repository.Add(
            CreateAudit(
                actorUserId,
                actorDeviceId,
                entryId,
                operationType,
                result,
                requestId,
                clock.UtcNow));
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static AuditLog CreateAudit(
        Guid actorUserId,
        Guid actorDeviceId,
        Guid entryId,
        FileOperationType operationType,
        string result,
        string requestId,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            actorUserId,
            actorDeviceId,
            null,
            operationType switch
            {
                FileOperationType.CreateFolder => "FOLDER_CREATE",
                FileOperationType.Upload => "FILE_UPLOAD",
                FileOperationType.Rename => "FILE_RENAME",
                FileOperationType.Move => "FILE_MOVE",
                FileOperationType.Trash => "FILE_TRASH",
                _ => "FILE_MUTATION",
            },
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

    private async Task<bool> StorageAvailableAsync(StorageIntent intent, CancellationToken cancellationToken) =>
        await storageGuard.InspectAsync(intent, cancellationToken) == StorageStatus.Available;

    private async Task<FileResult<DownloadFile>> MarkDownloadMissingAsync(
        FileEntry entry,
        CancellationToken cancellationToken)
    {
        await using var mutationLock = await repository.AcquireMutationLocksAsync([entry.Id], cancellationToken);
        if (await repository.ReloadAsync(entry, cancellationToken) && entry.Status == FileEntryStatus.Active)
        {
            entry.MarkMissingCandidate(Guid.NewGuid(), clock.UtcNow);
            try
            {
                await repository.SaveChangesAsync(cancellationToken);
            }
            catch (FilePersistenceConflictException)
            {
                return FileResult<DownloadFile>.Fail(FileErrorCodes.IndexConflict, FileFailureKind.Conflict);
            }
        }

        return FileResult<DownloadFile>.Fail(FileErrorCodes.FileMissing, FileFailureKind.Conflict);
    }

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
            entry.Status switch
            {
                FileEntryStatus.MissingCandidate => "MISSING_CANDIDATE",
                FileEntryStatus.Missing => "MISSING",
                FileEntryStatus.Trashed => "TRASHED",
                _ => "ACTIVE",
            },
            entry.FileVersion,
            entry.TrashedAt,
            entry.Status == FileEntryStatus.Trashed && entry.ParentId is null && entry.TrashedAt is not null
                ? entry.TrashedAt.Value.AddDays(retentionDays)
                : null,
            entry.MissingDetectedAt,
            entry.MissingLastCheckedAt,
            entry.CreatedAt,
            entry.UpdatedAt);

    private async Task<bool> CanViewAsync(
        Guid actorUserId,
        FileEntry entry,
        CancellationToken cancellationToken) =>
        actorUserId == entry.OwnerUserId ||
        authorizationService is not null &&
        await authorizationService.AllowsAsync(actorUserId, entry.Id, ShareOperation.View, cancellationToken);

    private async Task<EffectivePermission> ResolvePermissionAsync(
        Guid actorUserId,
        FileEntry entry,
        CancellationToken cancellationToken) =>
        actorUserId == entry.OwnerUserId || authorizationService is null
            ? OwnerPermission(entry.Id)
            : await authorizationService.ResolveAsync(actorUserId, entry.Id, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolvePermissionsAsync(
        Guid actorUserId,
        IReadOnlyList<FileEntry> entries,
        CancellationToken cancellationToken)
    {
        var uniqueEntries = entries.DistinctBy(entry => entry.Id).ToArray();
        if (uniqueEntries.Length == 0)
        {
            return new Dictionary<Guid, EffectivePermission>();
        }

        if (uniqueEntries.All(entry => entry.OwnerUserId == actorUserId) || authorizationService is null)
        {
            return uniqueEntries.ToDictionary(entry => entry.Id, entry => OwnerPermission(entry.Id));
        }

        return await authorizationService.ResolveBatchAsync(
            actorUserId,
            uniqueEntries.Select(entry => entry.Id).ToArray(),
            cancellationToken);
    }

    private async Task<FileOwnerItem> ResolveOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await repository.FindOwnerAsync(ownerUserId, cancellationToken) ?? new FileOwnerItem(ownerUserId, string.Empty);

    private static EffectivePermission OwnerPermission(Guid entryId) =>
        new(entryId, EffectivePermissionLevel.Owner, PermissionSource.Owner, null, null);

    private async Task<FileItem> MapOwnerAsync(
        FileEntry entry,
        CancellationToken cancellationToken,
        int retentionDaysOverride = 30) =>
        Map(
            entry,
            await ResolveOwnerAsync(entry.OwnerUserId, cancellationToken),
            OwnerPermission(entry.Id),
            retentionDaysOverride);

    internal static FileItem Map(
        FileEntry entry,
        FileOwnerItem owner,
        EffectivePermission permission,
        int retentionDaysOverride = 30) =>
        Map(entry, retentionDaysOverride) with
        {
            Owner = owner,
            Permission = permission.Permission switch
            {
                EffectivePermissionLevel.Owner or EffectivePermissionLevel.Manager => "MANAGER",
                EffectivePermissionLevel.Editor => "EDITOR",
                EffectivePermissionLevel.Contributor => "CONTRIBUTOR",
                EffectivePermissionLevel.Viewer => "VIEWER",
                _ => null,
            },
            PermissionSource = permission.Source?.ToString().ToUpperInvariant(),
            ShareTargetId = permission.Source == PermissionSource.Owner ? null : permission.ShareTargetId,
        };
}

public sealed class UploadSizeMismatchException : IOException
{
    public UploadSizeMismatchException()
        : base("The upload exceeded its declared size.")
    {
    }
}
