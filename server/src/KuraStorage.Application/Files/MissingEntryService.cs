using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Indexing;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class MissingEntryService(
    IFileRepository repository,
    IManagedFileSystemSnapshotReader snapshotReader,
    IStorageGuard storageGuard,
    IEnumerable<IFileIndexDeletionParticipant> participants,
    ISystemClock clock,
    IndexingOptions options)
{
    private readonly TimeSpan confirmationDelay = TimeSpan.FromMinutes(options.MissingConfirmationDelayMinutes);

    public async Task<FileResult<FileItem>> RecheckAsync(
        MissingFileCommand command,
        CancellationToken cancellationToken)
    {
        if (!Valid(command))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
        {
            return await FailureAsync<FileItem>(
                command, "FILE_MISSING_RECHECK", FileErrorCodes.StorageUnavailable,
                FileFailureKind.StorageUnavailable, cancellationToken);
        }

        var initial = await repository.FindOwnedAsync(command.OwnerUserId, command.FileEntryId, cancellationToken);
        if (!IsMissingState(initial))
        {
            return await FailureAsync<FileItem>(
                command, "FILE_MISSING_RECHECK", FileErrorCodes.FileNotFound,
                FileFailureKind.NotFound, cancellationToken);
        }

        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            [initial!.Id, initial.ParentId!.Value], cancellationToken);
        if (!await repository.ReloadAsync(initial, cancellationToken) || !IsMissingState(initial))
        {
            return await FailureAsync<FileItem>(
                command, "FILE_MISSING_RECHECK", FileErrorCodes.FileNotFound,
                FileFailureKind.NotFound, cancellationToken);
        }

        if (await repository.HasIncompleteOperationAsync(
                command.OwnerUserId, initial.Id, initial.RelativePath, cancellationToken))
        {
            return await FailureAsync<FileItem>(
                command, "FILE_MISSING_RECHECK", FileErrorCodes.FileStateConflict,
                FileFailureKind.Conflict, cancellationToken);
        }

        ObservedStorageEntry? observed;
        try
        {
            observed = await snapshotReader.InspectAsync(
                RelativeStoragePath.Create(initial.RelativePath), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return await FailureAsync<FileItem>(
                command, "FILE_MISSING_RECHECK", FileErrorCodes.StorageUnavailable,
                FileFailureKind.StorageUnavailable, cancellationToken);
        }

        if (await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken) != StorageStatus.Available)
        {
            return await FailureAsync<FileItem>(
                command, "FILE_MISSING_RECHECK", FileErrorCodes.StorageUnavailable,
                FileFailureKind.StorageUnavailable, cancellationToken);
        }

        var now = clock.UtcNow;
        if (observed is not null)
        {
            if (observed.IsolationReason is not null ||
                observed.OwnerUserId != command.OwnerUserId ||
                observed.EntryType != initial.EntryType ||
                !string.Equals(observed.RelativePath.Value, initial.RelativePath, StringComparison.Ordinal))
            {
                return await FailureAsync<FileItem>(
                    command, "FILE_MISSING_RECHECK", FileErrorCodes.StorageUnavailable,
                    FileFailureKind.StorageUnavailable, cancellationToken);
            }

            IndexReconciliationPrimitives.ApplyPresent(
                initial,
                observed.Size,
                observed.MimeType,
                observed.SourceModifiedAt,
                observed.SourceFileKey,
                now,
                contentMayHaveChanged: true);
        }
        else if (initial.Status == FileEntryStatus.MissingCandidate &&
                 initial.MissingDetectedAt is DateTimeOffset detectedAt &&
                 now >= detectedAt + confirmationDelay)
        {
            initial.ConfirmMissing(Guid.NewGuid(), now, confirmationDelay);
        }
        else if (initial.Status == FileEntryStatus.MissingCandidate)
        {
            initial.RecordMissingCandidateCheck(now);
        }
        else
        {
            initial.RecordMissingCheck(now);
        }

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        repository.Add(Audit(command, "FILE_MISSING_RECHECK", "SUCCESS", now));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.IndexConflict, FileFailureKind.Conflict);
        }

        return FileResult<FileItem>.Success(FileService.Map(initial));
    }

    public async Task<FileResult<bool>> DeleteIndexEntryAsync(
        MissingFileCommand command,
        CancellationToken cancellationToken)
    {
        if (!Valid(command))
        {
            return FileResult<bool>.Fail(FileErrorCodes.ValidationFailed, FileFailureKind.BadRequest);
        }

        var initial = await repository.FindOwnedAsync(command.OwnerUserId, command.FileEntryId, cancellationToken);
        if (initial is not { Status: FileEntryStatus.Missing, ParentId: not null })
        {
            return await FailureAsync<bool>(
                command, "FILE_MISSING_INDEX_DELETE", FileErrorCodes.FileNotFound,
                FileFailureKind.NotFound, cancellationToken);
        }

        var descendants = initial.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(command.OwnerUserId, initial.RelativePath, cancellationToken)
            : [];
        await using var mutationLock = await repository.AcquireMutationLocksAsync(
            descendants.Select(entry => entry.Id).Append(initial.Id), cancellationToken);
        if (!await repository.ReloadAsync(initial, cancellationToken) ||
            initial.Status != FileEntryStatus.Missing || initial.ParentId is null)
        {
            return await FailureAsync<bool>(
                command, "FILE_MISSING_INDEX_DELETE", FileErrorCodes.FileNotFound,
                FileFailureKind.NotFound, cancellationToken);
        }

        descendants = initial.EntryType == FileEntryType.Folder
            ? await repository.ListDescendantsAsync(command.OwnerUserId, initial.RelativePath, cancellationToken)
            : [];
        if (descendants.Any(entry => entry.Status != FileEntryStatus.Missing) ||
            await HasIncompleteOperationAsync(initial, descendants, cancellationToken))
        {
            return await FailureAsync<bool>(
                command, "FILE_MISSING_INDEX_DELETE", FileErrorCodes.FileStateConflict,
                FileFailureKind.Conflict, cancellationToken);
        }

        var target = new FileIndexDeletionTarget(
            initial.Id,
            command.OwnerUserId,
            descendants.Select(entry => entry.Id).Append(initial.Id).ToArray());
        var now = clock.UtcNow;
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var participant in participants)
            {
                await participant.DeleteManagementDataAsync(target, cancellationToken);
            }

            repository.RemoveRange(descendants.OrderByDescending(entry => entry.RelativePath.Count(c => c == '/')));
            repository.Remove(initial);
            repository.Add(Audit(command, "FILE_MISSING_INDEX_DELETE", "SUCCESS", now));
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            return FileResult<bool>.Fail(FileErrorCodes.IndexConflict, FileFailureKind.Conflict);
        }

        return FileResult<bool>.Success(true);
    }

    private async Task<bool> HasIncompleteOperationAsync(
        FileEntry root,
        IReadOnlyList<FileEntry> descendants,
        CancellationToken cancellationToken)
    {
        foreach (var entry in descendants.Prepend(root))
        {
            if (await repository.HasIncompleteOperationAsync(
                    entry.OwnerUserId, entry.Id, entry.RelativePath, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMissingState(FileEntry? entry) =>
        entry is { ParentId: not null } &&
        entry.Status is FileEntryStatus.MissingCandidate or FileEntryStatus.Missing;

    private static bool Valid(MissingFileCommand command) =>
        command.OwnerUserId != Guid.Empty &&
        command.ActorDeviceId != Guid.Empty &&
        command.FileEntryId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(command.RequestId) &&
        command.RequestId.Length <= 128;

    private static AuditLog Audit(
        MissingFileCommand command,
        string action,
        string result,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            command.OwnerUserId,
            command.ActorDeviceId,
            null,
            action,
            "FILE_ENTRY",
            command.FileEntryId.ToString(),
            result,
            command.RequestId,
            now,
            AuditActorType.UserDevice);

    private async Task<FileResult<T>> FailureAsync<T>(
        MissingFileCommand command,
        string action,
        string code,
        FileFailureKind kind,
        CancellationToken cancellationToken)
    {
        repository.Add(Audit(command, action, code, clock.UtcNow));
        await repository.SaveChangesAsync(cancellationToken);
        return FileResult<T>.Fail(code, kind);
    }
}
