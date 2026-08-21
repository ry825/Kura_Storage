using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class FileOperationRecoveryService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    TrashPurgeService trashPurge,
    ISystemClock clock)
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

        operation.Complete(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
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
