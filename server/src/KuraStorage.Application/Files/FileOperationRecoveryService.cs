using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class FileOperationRecoveryService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    TrashPurgeService trashPurge,
    ISystemClock clock,
    FileVersionService? fileVersions = null,
    IFileVersionRepository? versions = null,
    IFileVersionStore? versionStore = null,
    UserActivityFactory? activities = null)
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var operations = await repository.ListIncompleteOperationsAsync(cancellationToken);
        foreach (var operation in operations)
        {
            if (operation.OperationType == FileOperationType.Purge)
            {
                await trashPurge.RecoverAsync(operation, cancellationToken);
                continue;
            }

            if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
            {
                return;
            }

            await RecoverOneAsync(operation, cancellationToken);
        }
    }

    private async Task RecoverOneAsync(FileOperation operation, CancellationToken cancellationToken)
    {
        // Backup replacement recovery is coordinated with its UploadSession and Receipt.
        // Completing it here would lose the authenticated device/document context.
        if (operation.OperationType == FileOperationType.BackupUpdate)
        {
            return;
        }

        if (operation.OperationType is FileOperationType.TextEdit or FileOperationType.VersionRestore)
        {
            await RecoverTextMutationAsync(operation, cancellationToken);
            return;
        }

        if (operation.OperationType is FileOperationType.Rename or FileOperationType.Move)
        {
            await RecoverRelocationAsync(operation, cancellationToken);
            return;
        }

        if (operation.SourceRelativePath is null || operation.TargetRelativePath is null)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        var directory = operation.OperationType is FileOperationType.CreateFolder or FileOperationType.Trash or FileOperationType.Restore;
        var targetExists = await fileStore.ExistsAsync(
            RelativeStoragePath.Create(operation.TargetRelativePath),
            directory,
            cancellationToken);
        if (operation.Status == FileOperationStatus.Pending && targetExists)
        {
            operation.MarkFilesystemDone(clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
        }
        else if (operation.Status == FileOperationStatus.Pending)
        {
            if (operation.OperationType == FileOperationType.Upload)
            {
                await fileStore.DeleteIfExistsAsync(
                    RelativeStoragePath.Create(operation.SourceRelativePath),
                    cancellationToken);
            }

            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        if (operation.Status != FileOperationStatus.FilesystemDone)
        {
            return;
        }

        if (operation.FileEntryId is not Guid entryId)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        var entry = await repository.FindOwnedAsync(operation.OwnerUserId, entryId, cancellationToken);
        if (entry is null)
        {
            operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        var target = RelativeStoragePath.Create(operation.TargetRelativePath);
        if (operation.OperationType == FileOperationType.Trash && entry.Status == FileEntryStatus.Active)
        {
            var descendants = entry.EntryType == FileEntryType.Folder
                ? await repository.ListDescendantsAsync(
                    operation.OwnerUserId,
                    operation.SourceRelativePath,
                    cancellationToken)
                : [];
            entry.Trash(target, clock.UtcNow);
            foreach (var descendant in descendants)
            {
                descendant.TrashDescendant(
                    ReplacePrefix(
                        descendant.RelativePath,
                        operation.SourceRelativePath,
                        operation.TargetRelativePath),
                    clock.UtcNow);
            }
        }
        else if (operation.OperationType == FileOperationType.Restore &&
                 entry.Status == FileEntryStatus.Trashed &&
                 entry.OriginalParentId is Guid parentId)
        {
            var descendants = entry.EntryType == FileEntryType.Folder
                ? await repository.ListDescendantsAsync(
                    operation.OwnerUserId,
                    operation.SourceRelativePath,
                    cancellationToken)
                : [];
            entry.Restore(parentId, target, clock.UtcNow);
            foreach (var descendant in descendants)
            {
                descendant.RestoreDescendant(
                    ReplacePrefix(
                        descendant.RelativePath,
                        operation.SourceRelativePath,
                        operation.TargetRelativePath),
                    clock.UtcNow);
            }
        }
        else if (operation.OperationType is FileOperationType.Upload or FileOperationType.CreateFolder)
        {
            if (entry.RelativePath != operation.TargetRelativePath)
            {
                operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
                await repository.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        if (operation.OperationType == FileOperationType.Upload && fileVersions is not null)
        {
            try
            {
                _ = await fileVersions.EnsureCurrentAsync(
                    entry,
                    FileVersionChangeKind.Upload,
                    operation.Id,
                    operation.ActorUserId,
                    operation.ActorDeviceId,
                    cancellationToken,
                    operation);
            }
            catch (IOException)
            {
                operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
                await repository.SaveChangesAsync(CancellationToken.None);
                return;
            }
        }

        if (activities is not null &&
            operation.OperationType == FileOperationType.Upload &&
            operation.ActorUserId is Guid uploadActorUserId &&
            operation.ActorDeviceId is Guid uploadActorDeviceId)
        {
            await activities.AddUploadAsync(
                operation.Id,
                uploadActorUserId,
                uploadActorDeviceId,
                entry,
                entry.FileVersion,
                cancellationToken);
        }
        else if (activities is not null &&
                 operation.OperationType == FileOperationType.Trash &&
                 operation.ActorUserId is Guid trashActorUserId &&
                 operation.ActorDeviceId is Guid trashActorDeviceId)
        {
            await activities.AddDeleteAsync(
                operation.Id,
                trashActorUserId,
                trashActorDeviceId,
                entry,
                ActivityDeleteKind.Trashed,
                cancellationToken);
        }

        operation.Complete(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task RecoverTextMutationAsync(
        FileOperation operation,
        CancellationToken cancellationToken)
    {
        if (versions is null ||
            versionStore is null ||
            operation.FileEntryId is not Guid entryId ||
            operation.PreviousFileVersion is not long previousVersion ||
            operation.ResultFileVersion is not long resultVersion ||
            operation.ExpectedSize is not long expectedSize ||
            operation.ExpectedSha256 is not string expectedSha256 ||
            operation.TargetRelativePath is null)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync([entryId], cancellationToken);
        var entry = await repository.FindByIdAsync(entryId, cancellationToken);
        var record = await versions.FindAsync(entryId, resultVersion, cancellationToken);
        if (entry is null ||
            record is null ||
            entry.OwnerUserId != operation.OwnerUserId ||
            record.Size != expectedSize ||
            !string.Equals(record.Sha256, expectedSha256, StringComparison.Ordinal) ||
            !string.Equals(record.ContentRelativePath, operation.VersionContentRelativePath, StringComparison.Ordinal) ||
            record.ChangeKind != (operation.OperationType == FileOperationType.TextEdit
                ? FileVersionChangeKind.TextEdit
                : FileVersionChangeKind.Restore))
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        if (operation.Status == FileOperationStatus.RecoveryRequired)
        {
            operation.Retry(clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
        }

        if (entry.FileVersion == resultVersion)
        {
            if (activities is not null &&
                record.ActorUserId is Guid actorUserId &&
                record.ActorDeviceId is Guid actorDeviceId)
            {
                await activities.AddEditAsync(
                    operation.Id,
                    actorUserId,
                    actorDeviceId,
                    entry,
                    resultVersion,
                    operation.OperationType == FileOperationType.TextEdit
                        ? ActivityEditKind.TextSave
                        : ActivityEditKind.VersionRestore,
                    cancellationToken);
            }

            operation.Complete(clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        if (entry.FileVersion != previousVersion || entry.Status != FileEntryStatus.Active)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        try
        {
            await using var content = await versionStore.OpenReadAsync(
                RelativeStoragePath.Create(record.ContentRelativePath),
                record.Size,
                record.Sha256,
                cancellationToken);
            var replacement = await fileStore.WriteUploadTempAsync(
                entry.OwnerUserId,
                operation.Id,
                content,
                record.Size,
                cancellationToken);
            if (replacement.Size != record.Size ||
                !string.Equals(replacement.Sha256, record.Sha256, StringComparison.Ordinal))
            {
                await RequireRecoveryAsync(operation, cancellationToken);
                return;
            }

            await fileStore.ReplaceAsync(
                replacement.Path,
                RelativeStoragePath.Create(operation.TargetRelativePath),
                cancellationToken);
            if (operation.Status == FileOperationStatus.Pending)
            {
                operation.MarkFilesystemDone(clock.UtcNow);
                await repository.SaveChangesAsync(cancellationToken);
            }

            var now = clock.UtcNow;
            await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
            entry.ApplyManagedContentChange(record.Size, previousVersion, now);
            if (activities is not null &&
                record.ActorUserId is Guid actorUserId &&
                record.ActorDeviceId is Guid actorDeviceId)
            {
                await activities.AddEditAsync(
                    operation.Id,
                    actorUserId,
                    actorDeviceId,
                    entry,
                    resultVersion,
                    operation.OperationType == FileOperationType.TextEdit
                        ? ActivityEditKind.TextSave
                        : ActivityEditKind.VersionRestore,
                    cancellationToken);
            }

            repository.Add(
                new AuditLog(
                    Guid.NewGuid(),
                    record.ActorUserId,
                    record.ActorDeviceId,
                    null,
                    operation.OperationType == FileOperationType.TextEdit
                        ? "FILE_TEXT_EDIT"
                        : "FILE_VERSION_RESTORE",
                    "FILE_ENTRY",
                    entry.Id.ToString(),
                    "SUCCESS",
                    operation.RequestId,
                    now));
            operation.Complete(now);
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or FilePersistenceConflictException)
        {
            await RequireRecoveryAsync(operation, CancellationToken.None);
        }
    }

    private async Task RecoverRelocationAsync(
        FileOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.FileEntryId is not Guid entryId ||
            operation.SourceRelativePath is null ||
            operation.TargetRelativePath is null)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        var entry = await repository.FindOwnedAsync(operation.OwnerUserId, entryId, cancellationToken);
        if (entry is null)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        var sourceParent = await repository.FindActiveFolderByPathAsync(
            operation.OwnerUserId,
            ParentPath(operation.SourceRelativePath),
            cancellationToken);
        var targetParent = await repository.FindActiveFolderByPathAsync(
            operation.OwnerUserId,
            ParentPath(operation.TargetRelativePath),
            cancellationToken);
        if (sourceParent is null || targetParent is null)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        var lockIds = new[] { entry.Id, entry.ParentId, sourceParent?.Id, targetParent?.Id }
            .OfType<Guid>();
        await using var mutationLock = await repository.AcquireMutationLocksAsync(lockIds, cancellationToken);
        if (!await repository.ReloadAsync(entry, cancellationToken))
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        var source = RelativeStoragePath.Create(operation.SourceRelativePath);
        var target = RelativeStoragePath.Create(operation.TargetRelativePath);
        var directory = entry.EntryType == FileEntryType.Folder;
        var sourceExists = await fileStore.ExistsAsync(source, directory, cancellationToken);
        var targetExists = await fileStore.ExistsAsync(target, directory, cancellationToken);
        var sourceWrongType = await fileStore.ExistsAsync(source, !directory, cancellationToken);
        var targetWrongType = await fileStore.ExistsAsync(target, !directory, cancellationToken);
        if (sourceWrongType || targetWrongType)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }
        var databaseAtSource = entry.RelativePath == source.Value;
        var databaseAtTarget = entry.RelativePath == target.Value;

        if (databaseAtSource && sourceExists && !targetExists)
        {
            try
            {
                await fileStore.MoveAsync(source, target, directory, cancellationToken);
            }
            catch (IOException)
            {
                sourceExists = await fileStore.ExistsAsync(source, directory, CancellationToken.None);
                targetExists = await fileStore.ExistsAsync(target, directory, CancellationToken.None);
                if (sourceExists || !targetExists)
                {
                    await RequireRecoveryAsync(operation, CancellationToken.None);
                    return;
                }
            }

            sourceExists = false;
            targetExists = true;
        }

        if (databaseAtTarget && !sourceExists && targetExists)
        {
            await using var completedTransaction = await repository.BeginTransactionAsync(cancellationToken);
            if (activities is not null &&
                operation.OperationType == FileOperationType.Move &&
                operation.ActorUserId is Guid completedMoveActorUserId &&
                operation.ActorDeviceId is Guid completedMoveActorDeviceId)
            {
                await activities.AddMoveAsync(
                    operation.Id,
                    completedMoveActorUserId,
                    completedMoveActorDeviceId,
                    entry,
                    sourceParent!,
                    targetParent!,
                    cancellationToken);
            }

            operation.Complete(clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
            await completedTransaction.CommitAsync(cancellationToken);
            return;
        }

        if (!databaseAtSource || sourceExists || !targetExists)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        if (operation.Status == FileOperationStatus.Pending)
        {
            operation.MarkFilesystemDone(clock.UtcNow);
            await repository.SaveChangesAsync(cancellationToken);
        }

        Guid targetParentId;
        FileName targetName;
        if (operation.OperationType == FileOperationType.Rename)
        {
            if (entry.ParentId is not Guid currentParentId)
            {
                await RequireRecoveryAsync(operation, cancellationToken);
                return;
            }

            targetParentId = currentParentId;
            if (!FileName.TryCreate(FileNamePart(target.Value), out targetName))
            {
                await RequireRecoveryAsync(operation, cancellationToken);
                return;
            }
        }
        else
        {
            targetParent = await repository.FindActiveFolderByPathAsync(
                operation.OwnerUserId,
                ParentPath(target.Value),
                cancellationToken);
            if (targetParent is null)
            {
                await RequireRecoveryAsync(operation, cancellationToken);
                return;
            }

            targetParentId = targetParent.Id;
            targetName = FileName.Create(entry.Name);
        }

        var descendants = directory
            ? await repository.ListDescendantsAsync(
                operation.OwnerUserId,
                source.Value,
                cancellationToken)
            : [];
        var now = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        if (operation.OperationType == FileOperationType.Rename)
        {
            entry.Rename(targetName, target, now);
        }
        else
        {
            entry.MoveTo(targetParentId, target, now);
        }

        foreach (var descendant in descendants)
        {
            descendant.RelocateDescendant(
                ReplacePrefix(descendant.RelativePath, source.Value, target.Value),
                now);
        }

        if (activities is not null &&
            operation.OperationType == FileOperationType.Move &&
            operation.ActorUserId is Guid moveActorUserId &&
            operation.ActorDeviceId is Guid moveActorDeviceId)
        {
            await activities.AddMoveAsync(
                operation.Id,
                moveActorUserId,
                moveActorDeviceId,
                entry,
                sourceParent!,
                targetParent!,
                cancellationToken);
        }

        repository.Add(
            new AuditLog(
                Guid.NewGuid(),
                operation.OwnerUserId,
                null,
                null,
                operation.OperationType == FileOperationType.Rename
                    ? "FILE_RENAME"
                    : "FILE_MOVE",
                "FILE_ENTRY",
                entry.Id.ToString(),
                "SUCCESS",
                operation.Id.ToString(),
                now));
        operation.Complete(now);
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RequireRecoveryAsync(
        FileOperation operation,
        CancellationToken cancellationToken)
    {
        operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static string ParentPath(string value) => value[..value.LastIndexOf('/')];

    private static string FileNamePart(string value) => value[(value.LastIndexOf('/') + 1)..];

    private static RelativeStoragePath ReplacePrefix(string value, string source, string target) =>
        RelativeStoragePath.Create(target + value[source.Length..]);
}
