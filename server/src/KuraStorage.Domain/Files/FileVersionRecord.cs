namespace KuraStorage.Domain.Files;

public sealed class FileVersionRecord
{
    public const long MaximumContentBytes = 1024 * 1024;

    private FileVersionRecord()
    {
    }

    public FileVersionRecord(
        Guid id,
        Guid fileEntryId,
        long version,
        long size,
        string sha256,
        string contentRelativePath,
        FileVersionChangeKind changeKind,
        Guid? actorUserId,
        Guid? actorDeviceId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || fileEntryId == Guid.Empty)
        {
            throw new ArgumentException("Version and file IDs are required.");
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (size is < 0 or > MaximumContentBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (!IsSha256(sha256))
        {
            throw new ArgumentException("A lowercase SHA-256 value is required.", nameof(sha256));
        }

        if (!IsVersionPath(contentRelativePath, fileEntryId, version, sha256))
        {
            throw new ArgumentException("The version content path is invalid.", nameof(contentRelativePath));
        }

        if (!Enum.IsDefined(changeKind))
        {
            throw new ArgumentOutOfRangeException(nameof(changeKind));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor user ID must not be empty.", nameof(actorUserId));
        }

        if (actorDeviceId == Guid.Empty)
        {
            throw new ArgumentException("An actor device ID must not be empty.", nameof(actorDeviceId));
        }

        if (actorDeviceId is not null && actorUserId is null)
        {
            throw new ArgumentException("An actor device requires an actor user.", nameof(actorDeviceId));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAt));
        }

        Id = id;
        FileEntryId = fileEntryId;
        Version = version;
        Size = size;
        Sha256 = sha256;
        ContentRelativePath = contentRelativePath;
        ChangeKind = changeKind;
        ActorUserId = actorUserId;
        ActorDeviceId = actorDeviceId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid FileEntryId { get; private set; }

    public long Version { get; private set; }

    public long Size { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public string ContentRelativePath { get; private set; } = string.Empty;

    public FileVersionChangeKind ChangeKind { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public Guid? ActorDeviceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsVersionPath(string? value, Guid fileEntryId, long version, string sha256)
    {
        if (!RelativeStoragePath.TryCreate(value, out var path))
        {
            return false;
        }

        var segments = path.Value.Split('/');
        return segments.Length == 5 &&
               segments[0] == "versions" &&
               Guid.TryParseExact(segments[1], "N", out _) &&
               string.Equals(segments[2], fileEntryId.ToString("N"), StringComparison.Ordinal) &&
               string.Equals(segments[3], version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
               string.Equals(segments[4], sha256 + ".bin", StringComparison.Ordinal);
    }
}
