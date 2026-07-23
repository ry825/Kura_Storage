using System.Security.Cryptography;
using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class FileService(
    IFileRepository repository,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    IUserStorageProvisioner provisioner,
    ISystemClock clock)
{
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
            if (!await StorageAvailableAsync(true, cancellationToken))
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
        return entry?.Status == FileEntryStatus.Active
            ? FileResult<FileItem>.Success(Map(entry))
            : FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
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

        if (!await StorageAvailableAsync(true, cancellationToken))
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

        if (!await StorageAvailableAsync(true, cancellationToken))
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

        if (!await StorageAvailableAsync(false, cancellationToken) ||
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
            new FilePage(null, entries.Select(Map).ToArray(), page, pageSize, count));
    }

    public async Task<FileResult<FileItem>> TrashAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!await StorageAvailableAsync(true, cancellationToken))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.StorageUnavailable, FileFailureKind.StorageUnavailable);
        }

        var entry = await repository.FindOwnedAsync(ownerUserId, entryId, cancellationToken);
        if (entry is null || entry.Status != FileEntryStatus.Active || entry.ParentId is null)
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileNotFound, FileFailureKind.NotFound);
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
        return FileResult<FileItem>.Success(Map(entry));
    }

    public async Task<FileResult<FileItem>> RestoreAsync(
        Guid ownerUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!await StorageAvailableAsync(true, cancellationToken))
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

        var parent = await repository.FindOwnedAsync(ownerUserId, parentId, cancellationToken);
        if (!IsActiveFolder(parent))
        {
            return FileResult<FileItem>.Fail(FileErrorCodes.FileRestoreConflict, FileFailureKind.Conflict);
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

    private async Task<bool> StorageAvailableAsync(bool write, CancellationToken cancellationToken) =>
        await storageGuard.InspectAsync(write, cancellationToken) == StorageStatus.Available;

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

    internal static FileItem Map(FileEntry entry) =>
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
