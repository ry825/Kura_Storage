using System.Globalization;
using System.Text;

namespace KuraStorage.Domain.Backup;

public sealed record BackupDocumentMetadata
{
    public const int MaximumRelativePathLength = 2048;

    public BackupDocumentMetadata(
        string localDocumentKey,
        string relativePath,
        long size,
        DateTimeOffset sourceModifiedAt,
        string? checksum)
    {
        if (!Guid.TryParseExact(localDocumentKey, "D", out var key) || key == Guid.Empty)
        {
            throw new ArgumentException("The local document key must be an opaque UUID.", nameof(localDocumentKey));
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (sourceModifiedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The source timestamp must be UTC.", nameof(sourceModifiedAt));
        }

        LocalDocumentKey = key.ToString("D", CultureInfo.InvariantCulture);
        RelativePath = NormalizeRelativePath(relativePath);
        Size = size;
        SourceModifiedAt = sourceModifiedAt;
        Checksum = NormalizeChecksum(checksum);
    }

    public string LocalDocumentKey { get; }
    public string RelativePath { get; }
    public long Size { get; }
    public DateTimeOffset SourceModifiedAt { get; }
    public string? Checksum { get; }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumRelativePathLength ||
            value.IndexOfAny(['\\', '\0']) >= 0 || Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("The relative path is invalid.", nameof(value));
        }

        var normalized = value.Normalize(NormalizationForm.FormC);
        var segments = normalized.Split('/');
        if (segments.Length == 0 || segments.Any(segment =>
                string.IsNullOrEmpty(segment) || segment is "." or ".." || segment.Length > 255 ||
                segment.Any(char.IsControl)))
        {
            throw new ArgumentException("The relative path is invalid.", nameof(value));
        }

        return string.Join('/', segments);
    }

    private static string? NormalizeChecksum(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("The checksum must be a SHA-256 value.", nameof(value));
        }

        return normalized;
    }
}
