namespace KuraStorage.Infrastructure.Storage;

public static class StorageIdentity
{
    public static bool Matches(string expectedStorageId, string identityFileContents) =>
        !string.IsNullOrWhiteSpace(expectedStorageId) &&
        string.Equals(identityFileContents.Trim(), expectedStorageId, StringComparison.Ordinal);
}
