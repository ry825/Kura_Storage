using System.Globalization;
using System.Text;

namespace KuraStorage.Application.Organization;

public static class TagNameNormalizer
{
    public const int MaximumCodePoints = 50;

    public static NormalizedTagName Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var name = value.Trim().Normalize(NormalizationForm.FormC);
        var runes = name.EnumerateRunes().ToArray();
        if (runes.Length is < 1 or > MaximumCodePoints)
        {
            throw new ArgumentException("The tag name must contain between 1 and 50 Unicode code points.", nameof(value));
        }

        if (runes.Any(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control))
        {
            throw new ArgumentException("The tag name must not contain control characters.", nameof(value));
        }

        return new NormalizedTagName(
            name,
            name.ToUpperInvariant().Normalize(NormalizationForm.FormC));
    }
}

public sealed record NormalizedTagName(string Name, string NameKey);
