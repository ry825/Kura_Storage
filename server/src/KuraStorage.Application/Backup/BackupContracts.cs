using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Backup;

public static class BackupErrorCodes
{
    public const string InvalidRequest = "BACKUP_INVALID_REQUEST";
    public const string NotFound = "BACKUP_DESTINATION_NOT_FOUND";
    public const string CurrentStateBlocked = "BACKUP_CURRENT_STATE_BLOCKED";
    public const string VersionConflict = "BACKUP_VERSION_CONFLICT";
}

public enum BackupCompareDecision
{
    New,
    Changed,
    AlreadyUploaded,
    BlockedCurrentState,
}

public sealed record BackupCompareCandidate(
    string LocalDocumentKey,
    string RelativePath,
    long Size,
    DateTimeOffset ModifiedAt,
    string? Checksum);

public sealed record BackupCompareCommand(
    Guid UserId,
    Guid DeviceId,
    Guid DestinationFolderId,
    IReadOnlyList<BackupCompareCandidate> Items);

public sealed record BackupCompareItem(
    string LocalDocumentKey,
    BackupCompareDecision Decision,
    Guid? RemoteFileId,
    long? ExpectedRemoteFileVersion,
    string? ErrorCode);

public sealed record BackupCompareResult(IReadOnlyList<BackupCompareItem> Items);

public sealed record BackupDestination(
    Guid Id,
    Guid OwnerUserId,
    FileEntryType EntryType,
    FileEntryStatus Status);

public sealed record BackupReceiptState(
    BackupReceipt Receipt,
    FileEntryStatus? RemoteFileStatus,
    long? RemoteFileVersion);
