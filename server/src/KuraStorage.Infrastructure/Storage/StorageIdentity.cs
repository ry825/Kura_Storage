using System.Text.Json;

namespace KuraStorage.Infrastructure.Storage;

public static class StorageIdentity
{
    public static bool Matches(string expectedStorageId, string identityFileContents)
    {
        if (string.IsNullOrWhiteSpace(expectedStorageId) ||
            string.IsNullOrWhiteSpace(identityFileContents))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(identityFileContents);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("storageId", out var storageId) &&
                storageId.ValueKind == JsonValueKind.String &&
                string.Equals(storageId.GetString(), expectedStorageId, StringComparison.Ordinal) &&
                root.TryGetProperty("formatVersion", out var formatVersion) &&
                formatVersion.ValueKind == JsonValueKind.Number &&
                formatVersion.TryGetInt32(out var version) &&
                version == 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
