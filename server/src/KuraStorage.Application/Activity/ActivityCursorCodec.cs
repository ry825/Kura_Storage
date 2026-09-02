using System.Buffers.Binary;

namespace KuraStorage.Application.Activity;

public static class ActivityCursorCodec
{
    private const byte Version = 1;
    private const int PayloadLength = 25;

    public static string Encode(ActivityCursor cursor)
    {
        if (cursor.Id == Guid.Empty || cursor.OccurredAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A non-empty ID and UTC timestamp are required.", nameof(cursor));
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(payload[1..9], cursor.OccurredAt.UtcTicks);
        cursor.Id.TryWriteBytes(payload[9..]);
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out ActivityCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
            var payload = Convert.FromBase64String(normalized);
            if (payload.Length != PayloadLength || payload[0] != Version)
            {
                return false;
            }

            var occurredAt = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1, 8)), TimeSpan.Zero);
            var id = new Guid(payload.AsSpan(9, 16));
            if (id == Guid.Empty)
            {
                return false;
            }

            cursor = new ActivityCursor(occurredAt, id);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
