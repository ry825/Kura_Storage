using KuraStorage.Application.Media;
using KuraStorage.Domain.Media;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class MediaContractRulesTests
{
    [Theory]
    [InlineData(null, MediaVariant.Original)]
    [InlineData("original", MediaVariant.Original)]
    [InlineData("thumbnail", MediaVariant.Thumbnail)]
    [InlineData("image-low", MediaVariant.ImageLow)]
    [InlineData("image-medium", MediaVariant.ImageMedium)]
    [InlineData("video-low", MediaVariant.VideoLow)]
    [InlineData("video-medium", MediaVariant.VideoMedium)]
    [InlineData(" IMAGE-LOW ", MediaVariant.ImageLow)]
    public void Variant_ParsesOnlyPublishedValues(string? value, MediaVariant expected)
    {
        Assert.True(MediaContractRules.TryParseVariant(value, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(MediaContractRules.TryParseVariant("unknown", out _));
    }

    [Theory]
    [InlineData("image/jpeg", MediaVariant.Thumbnail, true)]
    [InlineData("video/mp4", MediaVariant.Thumbnail, true)]
    [InlineData("application/pdf", MediaVariant.Thumbnail, true)]
    [InlineData("image/png", MediaVariant.ImageLow, true)]
    [InlineData("video/mp4", MediaVariant.ImageLow, false)]
    [InlineData("video/mp4", MediaVariant.VideoLow, true)]
    [InlineData("video/3gpp", MediaVariant.VideoMedium, true)]
    [InlineData("video/webm", MediaVariant.VideoMedium, true)]
    [InlineData("image/jpeg", MediaVariant.VideoLow, false)]
    [InlineData("text/plain", MediaVariant.Thumbnail, false)]
    [InlineData("image/webp", MediaVariant.ImageMedium, true)]
    [InlineData("image/gif", MediaVariant.ImageMedium, true)]
    [InlineData("image/avif", MediaVariant.ImageMedium, true)]
    [InlineData("image/heic", MediaVariant.ImageMedium, true)]
    [InlineData("image/heif", MediaVariant.ImageMedium, true)]
    [InlineData("video/quicktime", MediaVariant.Thumbnail, true)]
    [InlineData("video/webm", MediaVariant.Thumbnail, true)]
    [InlineData("video/x-matroska", MediaVariant.Thumbnail, true)]
    [InlineData(null, MediaVariant.Original, true)]
    public void Supports_UsesServerMimeAllowList(string? mime, MediaVariant variant, bool expected) =>
        Assert.Equal(expected, MediaContractRules.Supports(mime, variant));

    [Fact]
    public void DerivativeType_ProfileAndDownloadName_AreServerDerived()
    {
        Assert.Equal(DerivativeType.PdfThumbnail,
            MediaContractRules.ToDerivativeType("application/pdf", MediaVariant.Thumbnail));
        Assert.Equal(DerivativeType.Thumbnail,
            MediaContractRules.ToDerivativeType("video/mp4", MediaVariant.Thumbnail));
        Assert.Equal(DerivativeType.Thumbnail,
            MediaContractRules.ToDerivativeType("image/png", MediaVariant.Thumbnail));
        Assert.Equal(DerivativeType.ImageLow,
            MediaContractRules.ToDerivativeType("image/jpeg", MediaVariant.ImageLow));
        Assert.Equal(DerivativeType.ImageMedium,
            MediaContractRules.ToDerivativeType("image/jpeg", MediaVariant.ImageMedium));
        Assert.Equal(7, MediaContractRules.ProfileVersion(MediaVariant.Thumbnail, 7, 9, 11));
        Assert.Equal(9, MediaContractRules.ProfileVersion(MediaVariant.ImageLow, 7, 9, 11));
        Assert.Equal(9, MediaContractRules.ProfileVersion(MediaVariant.ImageMedium, 7, 9, 11));
        Assert.Equal(11, MediaContractRules.ProfileVersion(MediaVariant.VideoLow, 7, 9, 11));
        Assert.Equal(11, MediaContractRules.ProfileVersion(MediaVariant.VideoMedium, 7, 9, 11));
        Assert.Equal("family.photo_thumbnail.webp",
            MediaContractRules.DownloadName("family.photo.JPG", MediaVariant.Thumbnail));
        Assert.Equal("family.photo_low.webp",
            MediaContractRules.DownloadName("family.photo.JPG", MediaVariant.ImageLow));
        Assert.Equal("family.photo_medium.webp",
            MediaContractRules.DownloadName("family.photo.JPG", MediaVariant.ImageMedium));
        Assert.Equal("family.video_low.mp4",
            MediaContractRules.DownloadName("family.video.MOV", MediaVariant.VideoLow));
        Assert.Equal("family.video_medium.mp4",
            MediaContractRules.DownloadName("family.video.MOV", MediaVariant.VideoMedium));
        Assert.Equal("thumbnail", MediaContractRules.PublishedVariant(DerivativeType.Thumbnail));
        Assert.Equal("thumbnail", MediaContractRules.PublishedVariant(DerivativeType.PdfThumbnail));
        Assert.Equal("image-low", MediaContractRules.PublishedVariant(DerivativeType.ImageLow));
        Assert.Equal("image-medium", MediaContractRules.PublishedVariant(DerivativeType.ImageMedium));
        Assert.Equal("video-low", MediaContractRules.PublishedVariant(DerivativeType.VideoLow));
        Assert.Equal("video-medium", MediaContractRules.PublishedVariant(DerivativeType.VideoMedium));
        Assert.Equal("image/webp", MediaContractRules.ContentType(DerivativeType.Thumbnail));
        Assert.Equal("video/mp4", MediaContractRules.ContentType(DerivativeType.VideoLow));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaContractRules.ToDerivativeType("image/jpeg", MediaVariant.Original));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaContractRules.DownloadName("photo.jpg", MediaVariant.Original));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaContractRules.ProfileVersion(MediaVariant.Original, 1, 1, 1));
    }

    [Fact]
    public void Disposition_RejectsUnknownValues()
    {
        Assert.True(MediaContractRules.TryParseDisposition(null, out var inline));
        Assert.Equal(MediaDisposition.Inline, inline);
        Assert.True(MediaContractRules.TryParseDisposition("attachment", out var attachment));
        Assert.Equal(MediaDisposition.Attachment, attachment);
        Assert.True(MediaContractRules.TryParseDisposition(" INLINE ", out inline));
        Assert.Equal(MediaDisposition.Inline, inline);
        Assert.False(MediaContractRules.TryParseDisposition("form-data", out _));
    }

    [Fact]
    public void Result_ExposesSuccessAndFailureWithoutMixingThem()
    {
        var success = MediaResult<int>.Success(42);
        Assert.True(success.IsSuccess);
        Assert.Equal(42, success.Value);
        Assert.Null(success.Failure);

        var failure = MediaResult<int>.Fail(MediaErrorCodes.VariantUnsupported, MediaFailureKind.BadRequest);
        Assert.False(failure.IsSuccess);
        Assert.Equal(0, failure.Value);
        Assert.Equal(MediaErrorCodes.VariantUnsupported, failure.Failure!.Code);
        Assert.Equal(MediaFailureKind.BadRequest, failure.Failure.Kind);
    }

    [Fact]
    public async Task PreviewRequest_RejectsInvalidApplicationBoundaryBeforeAccessingDependencies()
    {
        var service = new PreviewService(
            null!, null!, null!, null!, null!, null!, null!, null!, new MediaRuntimeOptions());

        var result = await service.RequestAsync(
            new MediaContentRequest(Guid.Empty, Guid.Empty, "original", "unknown"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaErrorCodes.VariantUnsupported, result.Failure!.Code);
        Assert.Equal(MediaFailureKind.BadRequest, result.Failure.Kind);

        foreach (var videoVariant in new[] { "video-low", "video-medium" })
        {
            var videoResult = await service.RequestAsync(
                new MediaContentRequest(Guid.NewGuid(), Guid.NewGuid(), videoVariant, "inline"),
                CancellationToken.None);
            Assert.False(videoResult.IsSuccess);
            Assert.Equal(MediaErrorCodes.VariantUnsupported, videoResult.Failure!.Code);
            Assert.Equal(MediaFailureKind.BadRequest, videoResult.Failure.Kind);
        }
    }
}
