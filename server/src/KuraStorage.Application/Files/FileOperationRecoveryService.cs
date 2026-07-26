using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class FileOperationRecoveryService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    ISystemClock clock)
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        if (await storageGuard.InspectAsync(true, cancellationToken) != StorageStatus.Available)
        {
            return;
        }

        var operations = await repository.ListIncompleteOperationsAsync(cancellationToken);
        foreach (var operation in operations)
        {
            await RecoverOneAsync(operation, cancellationToken);
        }
    }

    private async Task RecoverOneAsync(FileOperation operation, CancellationToken cancellationToken)
    {
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

    private static RelativeStoragePath ReplacePrefix(string value, string source, string target) =>
        RelativeStoragePath.Create(target + value[source.Length..]);
}
