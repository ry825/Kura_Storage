using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;

namespace KuraStorage.Application.Media;

public interface IRequiredPhotoDerivativeProvisioner
{
    Task<bool> EnsureLowAsync(
        FileEntry source,
        MediaJobOrigin origin,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class RequiredPhotoDerivativeProvisioner(
    IMediaRepository media,
    MediaRuntimeOptions options) : IRequiredPhotoDerivativeProvisioner
{
    public Task<bool> EnsureLowAsync(
        FileEntry source,
        MediaJobOrigin origin,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (source.EntryType != FileEntryType.File || source.Status != FileEntryStatus.Active ||
            !MediaContractRules.Supports(source.MimeType, MediaVariant.ImageLow))
        {
            return Task.FromResult(false);
        }

        return media.StageRequiredLowAsync(
            source,
            options.ImageProfileVersion,
            origin,
            now,
            cancellationToken);
    }
}
