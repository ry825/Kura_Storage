using System.Diagnostics.Metrics;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Transfers;
using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;

namespace KuraStorage.Application.Backup;

public sealed class BackupCompareService(
    IBackupRepository repository,
    IAuthorizationService authorization,
    UploadSessionOptions uploadOptions)
{
    public const int MaximumItems = 100;
    public const int MaximumMetadataCharacters = 256 * 1024;
    private static readonly Meter Meter = new("KuraStorage.Backup");
    private static readonly Counter<long> CompareRequests = Meter.CreateCounter<long>("kurastorage.backup.compare.requests");
    private static readonly Counter<long> CompareCandidates = Meter.CreateCounter<long>("kurastorage.backup.compare.candidates");

    public async Task<FileResult<BackupCompareResult>> CompareAsync(
        BackupCompareCommand command,
        CancellationToken cancellationToken)
    {
        if (command.UserId == Guid.Empty || command.DeviceId == Guid.Empty ||
            command.DestinationFolderId == Guid.Empty || command.Items.Count is < 1 or > MaximumItems)
        {
            return Invalid();
        }

        var metadata = new List<BackupDocumentMetadata>(command.Items.Count);
        var metadataCharacters = 0;
        try
        {
            foreach (var item in command.Items)
            {
                if (item.LocalDocumentKey is null || item.RelativePath is null ||
                    item.Size > uploadOptions.MaximumFileBytes)
                {
                    return Invalid();
                }

                metadataCharacters = checked(metadataCharacters + item.LocalDocumentKey.Length +
                    item.RelativePath.Length + (item.Checksum?.Length ?? 0));
                metadata.Add(new BackupDocumentMetadata(
                    item.LocalDocumentKey,
                    item.RelativePath,
                    item.Size,
                    item.ModifiedAt,
                    item.Checksum));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return Invalid();
        }

        if (metadataCharacters > MaximumMetadataCharacters ||
            metadata.Select(item => item.LocalDocumentKey).Distinct(StringComparer.Ordinal).Count() != metadata.Count)
        {
            return Invalid();
        }

        if (!await repository.IsDeviceActiveAsync(command.UserId, command.DeviceId, cancellationToken))
        {
            return NotFound();
        }

        var destination = await repository.FindDestinationAsync(command.DestinationFolderId, cancellationToken);
        if (destination is null || destination.EntryType != FileEntryType.Folder ||
            destination.Status != FileEntryStatus.Active ||
            !await authorization.AllowsAsync(
                command.UserId,
                destination.Id,
                ShareOperation.Contribute,
                cancellationToken))
        {
            return NotFound();
        }

        var states = await repository.ListReceiptStatesAsync(
            command.UserId,
            command.DeviceId,
            metadata.Select(item => item.LocalDocumentKey).ToArray(),
            cancellationToken);
        var remotePermissions = await authorization.ResolveBatchAsync(
            command.UserId,
            states.Values.Select(state => state.Receipt.RemoteFileId).Distinct().ToArray(),
            cancellationToken);
        var results = new List<BackupCompareItem>(metadata.Count);
        foreach (var item in metadata)
        {
            if (!states.TryGetValue(item.LocalDocumentKey, out var state))
            {
                results.Add(await ReassociateOrCreateAsync(command, item, cancellationToken));
                continue;
            }

            if (state.RemoteFileStatus != FileEntryStatus.Active || state.RemoteFileVersion is null ||
                state.RemoteFileVersion != state.Receipt.RemoteFileVersion ||
                !remotePermissions.TryGetValue(state.Receipt.RemoteFileId, out var remotePermission) ||
                !remotePermission.Allows(ShareOperation.Edit))
            {
                results.Add(new BackupCompareItem(
                    item.LocalDocumentKey,
                    BackupCompareDecision.BlockedCurrentState,
                    null,
                    null,
                    BackupErrorCodes.CurrentStateBlocked));
                continue;
            }

            results.Add(state.Receipt.Matches(item)
                ? new BackupCompareItem(
                    item.LocalDocumentKey,
                    BackupCompareDecision.AlreadyUploaded,
                    state.Receipt.RemoteFileId,
                    state.RemoteFileVersion,
                    null)
                : new BackupCompareItem(
                    item.LocalDocumentKey,
                    BackupCompareDecision.Changed,
                    state.Receipt.RemoteFileId,
                    state.RemoteFileVersion,
                    null));
        }

        CompareRequests.Add(1, new KeyValuePair<string, object?>("result", "success"));
        foreach (var group in results.GroupBy(item => item.Decision))
        {
            CompareCandidates.Add(
                group.LongCount(),
                new KeyValuePair<string, object?>("decision", group.Key.ToString().ToLowerInvariant()));
        }
        return FileResult<BackupCompareResult>.Success(new BackupCompareResult(results));
    }

    private async Task<BackupCompareItem> ReassociateOrCreateAsync(
        BackupCompareCommand command,
        BackupDocumentMetadata item,
        CancellationToken cancellationToken)
    {
        var candidates = await repository.ListReassociationCandidatesAsync(
            command.UserId,
            command.DestinationFolderId,
            item.RelativePath,
            cancellationToken);
        var matching = candidates
            .Where(candidate => candidate.RemoteFileStatus == FileEntryStatus.Active &&
                                candidate.RemoteFileVersion == candidate.Receipt.RemoteFileVersion &&
                                candidate.Receipt.MatchesContent(item))
            .GroupBy(candidate => candidate.Receipt.RemoteFileId)
            .Select(group => group.First())
            .ToArray();
        if (matching.Length != 1)
        {
            return new BackupCompareItem(item.LocalDocumentKey, BackupCompareDecision.New, null, null, null);
        }

        var candidate = matching[0];
        var permission = await authorization.ResolveAsync(
            command.UserId,
            candidate.Receipt.RemoteFileId,
            cancellationToken);
        if (!permission.Allows(ShareOperation.Edit))
        {
            return new BackupCompareItem(item.LocalDocumentKey, BackupCompareDecision.New, null, null, null);
        }

        repository.Add(new BackupReceipt(
            Guid.NewGuid(),
            command.UserId,
            command.DeviceId,
            item.LocalDocumentKey,
            candidate.Receipt.RemoteFileId,
            item.RelativePath,
            item.Size,
            item.SourceModifiedAt,
            item.Checksum,
            candidate.RemoteFileVersion,
            DateTimeOffset.UtcNow));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (FilePersistenceConflictException)
        {
            var raced = await repository.FindReceiptAsync(command.UserId, command.DeviceId, item.LocalDocumentKey, cancellationToken);
            if (raced is null || raced.RemoteFileId != candidate.Receipt.RemoteFileId ||
                raced.RemoteFileVersion != candidate.RemoteFileVersion || !raced.MatchesContent(item))
            {
                return new BackupCompareItem(item.LocalDocumentKey, BackupCompareDecision.New, null, null, null);
            }
        }
        return new BackupCompareItem(
            item.LocalDocumentKey,
            BackupCompareDecision.AlreadyUploaded,
            candidate.Receipt.RemoteFileId,
            candidate.RemoteFileVersion,
            null);
    }

    private static FileResult<BackupCompareResult> Invalid()
    {
        CompareRequests.Add(1, new KeyValuePair<string, object?>("result", "invalid"));
        return FileResult<BackupCompareResult>.Fail(BackupErrorCodes.InvalidRequest, FileFailureKind.BadRequest);
    }

    private static FileResult<BackupCompareResult> NotFound()
    {
        CompareRequests.Add(1, new KeyValuePair<string, object?>("result", "not_found"));
        return FileResult<BackupCompareResult>.Fail(BackupErrorCodes.NotFound, FileFailureKind.NotFound);
    }
}
