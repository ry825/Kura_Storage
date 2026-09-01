using System.Text;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class TextFileContractTests
{
    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("text/csv")]
    [InlineData("application/json")]
    [InlineData("application/xml")]
    [InlineData("application/yaml")]
    public void SupportedMimeTypes_AreAcceptedCaseInsensitively(string mimeType)
    {
        Assert.True(TextFileRules.IsSupportedMimeType(mimeType.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("image/jpeg")]
    [InlineData("text/html")]
    public void UnknownOrMissingMimeTypes_AreRejected(string? mimeType)
    {
        Assert.False(TextFileRules.IsSupportedMimeType(mimeType));
    }

    [Fact]
    public void TryEncode_NormalizesLeadingBomAndUsesStrictBomlessUtf8()
    {
        Assert.True(TextFileRules.TryEncode("\uFEFFhello 🌏", out var encoded));

        Assert.Equal("hello 🌏", new UTF8Encoding(false, true).GetString(encoded));
        Assert.False(encoded.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Fact]
    public void TryEncode_AcceptsEmptyAndExactByteLimit()
    {
        Assert.True(TextFileRules.TryEncode(string.Empty, out var empty));
        Assert.Empty(empty);

        var content = new string('a', checked((int)FileVersionRecord.MaximumContentBytes));
        Assert.True(TextFileRules.TryEncode(content, out var encoded));
        Assert.Equal(FileVersionRecord.MaximumContentBytes, encoded.Length);
    }

    [Fact]
    public void TryEncode_RejectsByteOverflowAndUnpairedSurrogate()
    {
        Assert.False(TextFileRules.TryEncode(
            new string('a', checked((int)FileVersionRecord.MaximumContentBytes + 1)),
            out _,
            out var overflowFailure));
        Assert.False(TextFileRules.TryEncode("bad\uD800text", out _, out var encodingFailure));
        Assert.Equal(TextEncodingFailure.SizeLimitExceeded, overflowFailure);
        Assert.Equal(TextEncodingFailure.InvalidEncoding, encodingFailure);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 100, true)]
    [InlineData(0, 50, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    [InlineData(int.MaxValue, 100, false)]
    public void PagingValidation_IsBoundedAndOverflowSafe(int page, int pageSize, bool expected)
    {
        Assert.Equal(expected, TextFileRules.ValidPage(page, pageSize));
    }

    [Theory]
    [InlineData(FileVersionChangeKind.Upload, "UPLOAD")]
    [InlineData(FileVersionChangeKind.TextEdit, "TEXT_EDIT")]
    [InlineData(FileVersionChangeKind.ExternalChange, "EXTERNAL_CHANGE")]
    [InlineData(FileVersionChangeKind.Restore, "RESTORE")]
    public void ChangeKindMapping_UsesStableApiValues(FileVersionChangeKind kind, string expected)
    {
        Assert.Equal(expected, TextFileRules.ToContractChangeKind(kind));
    }

    [Fact]
    public void ChangeKindMapping_FailsClosedForUnknownValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TextFileRules.ToContractChangeKind((FileVersionChangeKind)int.MaxValue));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(long.MaxValue, true)]
    public void VersionValidation_RequiresPositiveValue(long version, bool expected)
    {
        Assert.Equal(expected, TextFileRules.ValidVersion(version));
    }

    [Fact]
    public void MutationValidation_RequiresExpectedVersionAndOperationId()
    {
        Assert.True(TextFileRules.ValidMutation(1, Guid.NewGuid()));
        Assert.False(TextFileRules.ValidMutation(0, Guid.NewGuid()));
        Assert.False(TextFileRules.ValidMutation(1, Guid.Empty));
    }
}
