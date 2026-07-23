namespace KuraStorage.Domain.Files;

public readonly record struct FileName
{
    public const int MaximumLength = 255;

    private FileName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out FileName fileName)
    {
        fileName = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Normalize().Trim();
        if (normalized.Length > MaximumLength ||
            normalized is "." or ".." ||
            normalized.IndexOfAny(['/', '\\', '\0']) >= 0 ||
            normalized.Any(char.IsControl))
        {
            return false;
        }

        fileName = new FileName(normalized);
        return true;
    }

    public static FileName Create(string value) =>
        TryCreate(value, out var fileName)
            ? fileName
            : throw new ArgumentException("The file name is invalid.", nameof(value));

    public override string ToString() => Value;
}
