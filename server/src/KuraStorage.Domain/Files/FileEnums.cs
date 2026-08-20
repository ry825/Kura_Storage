namespace KuraStorage.Domain.Files;

public enum FileEntryType
{
    File,
    Folder,
}

public enum FileEntryStatus
{
    Active,
    Trashed,
}

public enum FileOperationType
{
    Upload,
    CreateFolder,
    Trash,
    Restore,
    Rename,
    Move,
    Purge,
}

public enum FileOperationStatus
{
    Pending,
    FilesystemDone,
    Completed,
    RecoveryRequired,
}
