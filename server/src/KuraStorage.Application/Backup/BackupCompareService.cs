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
                results.Add(new BackupCompareItem(item.LocalDocumentKey, BackupCompareDecision.New, null, null, null));
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
