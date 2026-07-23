namespace KuraStorage.Domain.Files;

public readonly record struct FileVersion
{
    public FileVersion(long value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public long Value { get; }

    public FileVersion Next() => new(checked(Value + 1));
}
