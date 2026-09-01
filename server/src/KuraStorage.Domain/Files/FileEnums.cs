namespace KuraStorage.Domain.Files;

public enum FileEntryType
{
    File,
    Folder,
}

public enum FileEntryStatus
{
    Active,
    MissingCandidate,
    Missing,
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

public enum FileVersionPublishStage
{
    Temporary,
    Published,
    Completed,
}

public enum FileVersionChangeKind
{
    Upload,
    TextEdit,
    ExternalChange,
    Restore,
}
