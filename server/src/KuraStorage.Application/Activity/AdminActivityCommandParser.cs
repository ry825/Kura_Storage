using System.Globalization;
using System.Text.Json;

namespace KuraStorage.Application.Activity;

public sealed record AdminActivityCommand(AdminActivitySearchRequest Request, bool Json);

public static class AdminActivityCommandParser
{
    public static bool TryParse(IReadOnlyList<string> args, out AdminActivityCommand? command)
    {
        command = null;
        string? actor = null;
        string? owner = null;
        string? type = null;
        string? cursor = null;
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        Guid? fileId = null;
        var limit = 100;
        var json = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (!seen.Add(option))
            {
                return false;
            }

            if (option == "--json")
            {
                json = true;
                continue;
            }

            if (index + 1 >= args.Count)
            {
                return false;
            }

            var value = args[++index];
            switch (option)
            {
                case "--actor-user": actor = value; break;
                case "--owner-user": owner = value; break;
                case "--type": type = value; break;
                case "--cursor": cursor = value; break;
                case "--from" when TryUtc(value, out var parsedFrom): from = parsedFrom; break;
                case "--to" when TryUtc(value, out var parsedTo): to = parsedTo; break;
                case "--file-id" when Guid.TryParse(value, out var parsedFile) && parsedFile != Guid.Empty:
                    fileId = parsedFile;
                    break;
                case "--limit" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLimit):
                    limit = parsedLimit;
                    break;
                default: return false;
            }
        }

        var request = new AdminActivitySearchRequest(actor, owner, type, from, to, fileId, limit, cursor);
        if (!AdminActivityService.Validate(request).IsSuccess)
        {
            return false;
        }

        command = new AdminActivityCommand(request, json);
        return true;
    }

    private static bool TryUtc(string value, out DateTimeOffset result)
    {
        result = default;
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            !value.EndsWith('Z'))
        {
            return false;
        }

        result = parsed.ToUniversalTime();
        return true;
    }
}

public static class AdminActivityOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Write(AdminActivityPage page, bool json, TextWriter writer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(writer);
        cancellationToken.ThrowIfCancellationRequested();
        if (json)
        {
            writer.WriteLine(JsonSerializer.Serialize(page, JsonOptions));
            return;
        }

        writer.WriteLine("OCCURRED_AT\tTYPE\tACTOR\tTARGET\tOWNER");
        foreach (var item in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteLine(
                $"{item.OccurredAt:O}\t{item.Type}\t{Escape(item.ActorDisplayName)}\t{Escape(item.TargetName)}\t{Escape(item.OwnerDisplayName)}");
        }

        if (page.NextCursor is not null)
        {
            writer.WriteLine($"next_cursor={page.NextCursor}");
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
