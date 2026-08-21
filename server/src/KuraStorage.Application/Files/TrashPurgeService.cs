using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class TrashPurgeService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    IEnumerable<IPermanentDeleteParticipant> participants,
    ISystemClock clock,
    TrashPurgeOptions? purgeOptions = null)
{
    private readonly int retentionDays = purgeOptions?.RetentionDays ?? 30;

    public async Task<FileResult<bool>> PurgeAsync(
        PurgeFileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.OwnerUserId == Guid.Empty ||
            command.FileEntryId == Guid.Empty ||
            command.Trigger == PurgeTrigger.User &&
                (command.ActorDeviceId is null || command.ActorDeviceId == Guid.Empty) ||
            string.IsNullOrWhiteSpace(command.RequestId) ||
            command.RequestId.Length > 128 ||
            !Guid.TryParse(command.IdempotencyKey, out _))
        {
            return FileResult<bool>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        var idempotencyLockId = Guid.Parse(command.IdempotencyKey);
        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            [command.FileEntryId, idempotencyLockId],
            cancellationToken);
        var lockedExisting = await repository.FindOperationAsync(
            command.OwnerUserId,
            command.IdempotencyKey,
            cancellationToken);
        if (lockedExisting is not null)
        {
            if (lockedExisting.OperationType != FileOperationType.Purge ||
                lockedExisting.FileEntryId != command.FileEntryId)
            {
                await AuditFailureAsync(command, FileErrorCodes.IdempotencyConflict, cancellationToken);
                return FileResult<bool>.Fail(FileErrorCodes.IdempotencyConflict, FileFailureKind.Conflict);
            }

            if (lockedExisting.Status == FileOperationStatus.Completed)
            {
                return FileResult<bool>.Success(false);
            }

            if (lockedExisting.Status == FileOperationStatus.RecoveryRequired)
            {
                await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
                return FileResult<bool>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
            }

            await RecoverAsync(lockedExisting, cancellationToken);
            return lockedExisting.Status == FileOperationStatus.Completed
                ? FileResult<bool>.Success(false)
                : FileResult<bool>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        if (await storageGuard.InspectAsync(StorageIntent.Delete, cancellationToken) != StorageStatus.Available)
        {
            await AuditFailureAsync(command, FileErrorCodes.StorageUnavailable, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var lockedEntry = await repository.FindOwnedAsync(
            command.OwnerUserId,
            command.FileEntryId,
            cancellationToken);
        if (!IsEligible(lockedEntry, command))
        {
            await AuditFailureAsync(command, FileErrorCodes.FileNotFound, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
        }

        if (await repository.HasIncompleteOperationAsync(
                command.OwnerUserId,
                lockedEntry!.Id,
                lockedEntry.RelativePath,
                cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var target = await BuildTargetAsync(lockedEntry, cancellationToken);
        if (await fileStore.ExistsAsync(target.TrashContainer, false, cancellationToken))
        {
            await AuditFailureAsync(command, FileErrorCodes.RecoveryRequired, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }

        var artifacts = new List<RelativeStoragePath>();
        foreach (var participant in participants)
        {
            artifacts.AddRange(await participant.ListPhysicalArtifactsAsync(target, cancellationToken));
        }

        var operation = new FileOperation(
            Guid.NewGuid(),
            command.OwnerUserId,
            FileOperationType.Purge,
            command.FileEntryId,
            command.IdempotencyKey,
            target.TrashContainer.Value,
            null,
            target.TotalSize,
            null,
            clock.UtcNow,
            command.ActorDeviceId,
            command.RequestId,
            command.Trigger.ToString().ToUpperInvariant());
        repository.Add(operation);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            foreach (var artifact in artifacts)
            {
                await fileStore.DeleteTreeIfExistsAsync(artifact, cancellationToken);
            }

            await fileStore.DeleteTreeIfExistsAsync(target.TrashContainer, cancellationToken);
        }
        catch (UnsafeStorageTreeException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.RecoveryRequired, FileFailureKind.Conflict);
        }
        catch (IOException)
        {
            await AuditFailureAsync(command, FileErrorCodes.StorageUnavailable, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            await AuditFailureAsync(command, FileErrorCodes.StorageUnavailable, cancellationToken);
            return FileResult<bool>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }
        operation.MarkFilesystemDone(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        await FinalizeAsync(operation, lockedEntry, target, command, cancellationToken);
        return FileResult<bool>.Success(true);
    }

    public async Task RecoverAsync(FileOperation operation, CancellationToken cancellationToken)
    {
        if (operation.OperationType != FileOperationType.Purge ||
            operation.FileEntryId is not Guid entryId ||
            operation.SourceRelativePath is null)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        if (await storageGuard.InspectAsync(StorageIntent.Delete, cancellationToken) != StorageStatus.Available)
        {
            return;
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync([entryId], cancellationToken);
        var entry = await repository.FindOwnedAsync(operation.OwnerUserId, entryId, cancellationToken);
        if (!IsPurgeRoot(entry))
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        var target = await BuildTargetAsync(entry!, cancellationToken);
        if (!string.Equals(target.TrashContainer.Value, operation.SourceRelativePath, StringComparison.Ordinal))
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        if (await fileStore.ExistsAsync(target.TrashContainer, false, cancellationToken))
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        try
        {
            if (operation.Status == FileOperationStatus.Pending)
            {
                foreach (var participant in participants)
                {
                    var artifacts = await participant.ListPhysicalArtifactsAsync(target, cancellationToken);
                    foreach (var artifact in artifacts)
                    {
                        await fileStore.DeleteTreeIfExistsAsync(artifact, cancellationToken);
                    }
                }

                await fileStore.DeleteTreeIfExistsAsync(target.TrashContainer, cancellationToken);
                operation.MarkFilesystemDone(clock.UtcNow);
                await repository.SaveChangesAsync(cancellationToken);
            }
        }
        catch (UnsafeStorageTreeException)
        {
            await RequireRecoveryAsync(operation, cancellationToken);
            return;
        }

        var recoveryTrigger = Enum.TryParse<PurgeTrigger>(operation.Trigger, true, out var parsedTrigger)
            ? parsedTrigger
            : PurgeTrigger.RetentionWorker;
        var recoveryCommand = new PurgeFileCommand(
            operation.OwnerUserId,
            operation.ActorDeviceId,
            entryId,
            operation.IdempotencyKey ?? operation.Id.ToString(),
            operation.RequestId ?? operation.Id.ToString(),
            recoveryTrigger);
        await FinalizeAsync(operation, entry!, target, recoveryCommand, cancellationToken);
    }

    private async Task FinalizeAsync(
        FileOperation operation,
        FileEntry root,
        PermanentDeleteTarget target,
        PurgeFileCommand command,
        CancellationToken cancellationToken)
    {
        var descendants = root.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(root.OwnerUserId, root.RelativePath, cancellationToken)
            : [];
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        foreach (var participant in participants)
        {
            await participant.DeleteManagementDataAsync(target, cancellationToken);
        }

        repository.RemoveRange(descendants.OrderByDescending(item => item.RelativePath.Count(character => character == '/')));
        repository.Remove(root);
        repository.Add(CreateAudit(command, "SUCCESS"));
        operation.Complete(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<PermanentDeleteTarget> BuildTargetAsync(
        FileEntry root,
        CancellationToken cancellationToken)
    {
        var descendants = root.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(root.OwnerUserId, root.RelativePath, cancellationToken)
            : [];
        var container = RelativeStoragePath.Create(
            root.RelativePath[..root.RelativePath.LastIndexOf('/')]);
        return new PermanentDeleteTarget(
            root.Id,
            root.OwnerUserId,
            root.EntryType.ToString().ToUpperInvariant(),
            container,
            descendants.Select(item => item.Id).ToArray(),
            checked(root.Size + descendants.Sum(item => item.Size)));
    }

    private async Task AuditFailureAsync(
        PurgeFileCommand command,
        string code,
        CancellationToken cancellationToken)
    {
        repository.Add(CreateAudit(command, code));
        await repository.SaveChangesAsync(cancellationToken);
    }

    private AuditLog CreateAudit(PurgeFileCommand command, string result) =>
        new(
            Guid.NewGuid(),
            command.Trigger == PurgeTrigger.User ? command.OwnerUserId : null,
            command.ActorDeviceId,
            null,
            command.Trigger == PurgeTrigger.User ? "FILE_PURGE_MANUAL" : "FILE_PURGE_RETENTION",
            "FILE_ENTRY",
            command.FileEntryId.ToString(),
            result,
            command.RequestId,
            clock.UtcNow,
            command.Trigger == PurgeTrigger.User ? AuditActorType.UserDevice : AuditActorType.SystemWorker);

    private async Task RequireRecoveryAsync(FileOperation operation, CancellationToken cancellationToken)
    {
        operation.RequireRecovery(FileErrorCodes.RecoveryRequired, clock.UtcNow);
        var manual = string.Equals(operation.Trigger, "USER", StringComparison.OrdinalIgnoreCase);
        repository.Add(
            new AuditLog(
                Guid.NewGuid(),
                manual ? operation.OwnerUserId : null,
                manual ? operation.ActorDeviceId : null,
                null,
                manual ? "FILE_PURGE_MANUAL" : "FILE_PURGE_RETENTION",
                "FILE_ENTRY",
                operation.FileEntryId?.ToString(),
                FileErrorCodes.RecoveryRequired,
                operation.RequestId ?? operation.Id.ToString(),
                clock.UtcNow,
                manual ? AuditActorType.UserDevice : AuditActorType.System));
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static bool IsPurgeRoot(FileEntry? entry) =>
        entry is { Status: FileEntryStatus.Trashed, ParentId: null };

    private bool IsEligible(FileEntry? entry, PurgeFileCommand command) =>
        IsPurgeRoot(entry) &&
        (command.Trigger == PurgeTrigger.User ||
            entry!.TrashedAt is not null &&
            entry.TrashedAt <= clock.UtcNow.AddDays(-retentionDays));
}
