using KuraStorage.Application.Identity;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class UsernameNormalizerTests
{
    [Theory]
    [InlineData(" Alice ", "ALICE")]
    [InlineData("Ａｌｉｃｅ", "ALICE")]
    [InlineData("Straße", "STRAßE")]
    public void Normalize_WhenInputUsesEquivalentForms_ReturnsStableCanonicalUsername(
        string input,
        string expected)
    {
        Assert.Equal(expected, UsernameNormalizer.Normalize(input));
    }
}
