namespace KuraStorage.Domain.Transfers;

public enum UploadSessionStatus
{
    Active,
    Completing,
    Completed,
    Cancelled,
    Expired,
    RecoveryRequired,
}
