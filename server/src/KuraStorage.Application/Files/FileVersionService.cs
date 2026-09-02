using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class FileVersionService(
    IFileVersionRepository versions,
    IFileVersionStore versionStore,
    IFileStore fileStore,
    IStorageGuard storageGuard,
    ISystemClock clock,
    IFileRepository? files = null)
{
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/csv",
        "application/json",
        "application/xml",
        "application/yaml",
    };

    public async Task<FileVersionRecord?> EnsureBaselineAsync(
        Guid fileEntryId,
        FileVersionChangeKind changeKind,
        Guid operationId,
        Guid? actorUserId,
        Guid? actorDeviceId,
        CancellationToken cancellationToken)
    {
        if (files is null)
        {
            throw new InvalidOperationException("A file repository is required for baseline creation.");
        }

        if (fileEntryId == Guid.Empty)
        {
            throw new ArgumentException("A file entry ID is required.", nameof(fileEntryId));
        }

        await using var mutationLock = await files.AcquireMutationLocksAsync([fileEntryId], cancellationToken);
        var entry = await files.FindByIdAsync(fileEntryId, cancellationToken);
        if (entry is null || !IsSupported(entry))
        {
            return null;
        }

        if (await files.HasIncompleteOperationAsync(
                entry.OwnerUserId,
                entry.Id,
                entry.RelativePath,
                cancellationToken))
        {
            throw new FileVersionOperationBlockedException();
        }

        var record = await EnsureCurrentAsync(
            entry,
            changeKind,
            operationId,
            actorUserId,
            actorDeviceId,
            cancellationToken);
        if (record is not null)
        {
            await files.SaveChangesAsync(cancellationToken);
        }

        return record;
    }

    public async Task<FileVersionRecord?> EnsureCurrentAsync(
        FileEntry entry,
        FileVersionChangeKind changeKind,
        Guid operationId,
        Guid? actorUserId,
        Guid? actorDeviceId,
        CancellationToken cancellationToken,
        FileOperation? operation = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(operationId));
        }

        if (!IsSupported(entry))
        {
            return null;
        }

        if (await storageGuard.InspectAsync(StorageIntent.CreateOrUpdate, cancellationToken) != StorageStatus.Available)
        {
            throw new FileVersionStorageUnavailableException();
        }

        var existing = await versions.FindAsync(entry.Id, entry.FileVersion, cancellationToken);
        if (existing is not null)
        {
            if (existing.Size != entry.Size)
            {
                throw new FileVersionConsistencyException();
            }

            operation?.RecordPublishedVersion(
                entry.FileVersion == 1 ? null : checked(entry.FileVersion - 1),
                entry.FileVersion,
                TemporaryPath(entry, operationId),
                existing.ContentRelativePath,
                existing.Sha256,
                clock.UtcNow);

            return existing;
        }

        await using var source = await fileStore.OpenReadAsync(
            RelativeStoragePath.Create(entry.RelativePath),
            cancellationToken);
        var published = await versionStore.TryPublishAsync(
            entry.OwnerUserId,
            entry.Id,
            entry.FileVersion,
            operationId,
            source,
            entry.Size,
            cancellationToken);
        if (published is null)
        {
            return null;
        }

        if (published.Size != entry.Size)
        {
            throw new FileVersionConsistencyException();
        }

        var record = new FileVersionRecord(
            Guid.NewGuid(),
            entry.Id,
            entry.FileVersion,
            published.Size,
            published.Sha256,
            published.Path.Value,
            changeKind,
            actorUserId,
            actorDeviceId,
            clock.UtcNow);
        operation?.RecordPublishedVersion(
            entry.FileVersion == 1 ? null : checked(entry.FileVersion - 1),
            entry.FileVersion,
            published.TemporaryPath.Value,
            published.Path.Value,
            published.Sha256,
            clock.UtcNow);
        versions.Add(record);
        return record;
    }

    public async Task<FileVersionRecord?> PublishNextAsync(
        FileEntry entry,
        FileVersionChangeKind changeKind,
        FileOperation operation,
        Guid actorUserId,
        Guid actorDeviceId,
        Stream source,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(operation);
        if (!IsSupported(entry))
        {
            return null;
        }

        var nextVersion = checked(entry.FileVersion + 1);
        var existing = await versions.FindAsync(entry.Id, nextVersion, cancellationToken);
        if (existing is not null)
        {
            if (existing.Size != expectedSize ||
                !string.Equals(existing.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase) ||
                existing.ChangeKind != changeKind)
            {
                throw new FileVersionConsistencyException();
            }

            if (operation.ResultFileVersion is null)
            {
                operation.RecordPublishedVersion(
                    entry.FileVersion,
                    nextVersion,
                    TemporaryPath(entry, nextVersion, operation.Id),
                    existing.ContentRelativePath,
                    existing.Sha256,
                    clock.UtcNow);
            }
            return existing;
        }

        var published = await versionStore.TryPublishAsync(
            entry.OwnerUserId,
            entry.Id,
            nextVersion,
            operation.Id,
            source,
            expectedSize,
            cancellationToken);
        if (published is null || published.Size != expectedSize ||
            !string.Equals(published.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileVersionConsistencyException();
        }

        var record = new FileVersionRecord(
            Guid.NewGuid(),
            entry.Id,
            nextVersion,
            published.Size,
            published.Sha256,
            published.Path.Value,
            changeKind,
            actorUserId,
            actorDeviceId,
            clock.UtcNow);
        versions.Add(record);
        operation.RecordPublishedVersion(
            entry.FileVersion,
            nextVersion,
            published.TemporaryPath.Value,
            published.Path.Value,
            published.Sha256,
            clock.UtcNow);
        return record;
    }

    public static bool IsSupported(FileEntry entry) =>
        entry.EntryType == FileEntryType.File &&
        entry.Status == FileEntryStatus.Active &&
        entry.Size <= FileVersionRecord.MaximumContentBytes &&
        entry.MimeType is not null &&
        SupportedMimeTypes.Contains(entry.MimeType);

    private static string TemporaryPath(FileEntry entry, Guid operationId) =>
        TemporaryPath(entry, entry.FileVersion, operationId);

    private static string TemporaryPath(FileEntry entry, long version, Guid operationId) =>
        $"version-temp/{entry.OwnerUserId:N}/{entry.Id:N}/{version}/{operationId:N}.part";
}

public sealed class FileVersionConsistencyException : IOException;

public sealed class FileVersionStorageUnavailableException : IOException;

public sealed class FileVersionOperationBlockedException : IOException;
