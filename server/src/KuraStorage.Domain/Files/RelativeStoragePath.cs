namespace KuraStorage.Domain.Files;

public readonly record struct RelativeStoragePath
{
    private RelativeStoragePath(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out RelativeStoragePath path)
    {
        path = default;
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOfAny(['\\', '\0']) >= 0 ||
            Path.IsPathFullyQualified(value))
        {
            return false;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            return false;
        }

        path = new RelativeStoragePath(string.Join('/', segments));
        return true;
    }

    public static RelativeStoragePath Create(string value) =>
        TryCreate(value, out var path)
            ? path
            : throw new ArgumentException("The relative storage path is invalid.", nameof(value));

    public RelativeStoragePath Append(FileName name) => Create($"{Value}/{name.Value}");

    public override string ToString() => Value;
}
