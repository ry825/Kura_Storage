using KuraStorage.Application.Organization;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class TagNameNormalizerTests
{
    [Fact]
    public void Normalize_TrimsAndUsesNfcForDisplayAndCaseInsensitiveKey()
    {
        var result = TagNameNormalizer.Normalize("  Cafe\u0301  ");

        Assert.Equal("Café", result.Name);
        Assert.Equal("CAFÉ", result.NameKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    [InlineData("bad\u0000name")]
    public void Normalize_RejectsEmptyAndControlCharacters(string value)
    {
        Assert.Throws<ArgumentException>(() => TagNameNormalizer.Normalize(value));
    }

    [Fact]
    public void Normalize_CountsUnicodeCodePoints()
    {
        var fiftyEmoji = string.Concat(Enumerable.Repeat("😀", 50));
        var result = TagNameNormalizer.Normalize(fiftyEmoji);

        Assert.Equal(50, result.Name.EnumerateRunes().Count());
        Assert.Throws<ArgumentException>(() => TagNameNormalizer.Normalize(fiftyEmoji + "a"));
    }
}
