using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Media;

public sealed class MediaDerivativeStatusService(
    IMediaDerivativeStatusRepository repository,
    MediaRuntimeOptions options)
{
    public Task<MediaDerivativeStatusSnapshot> GetAsync(CancellationToken cancellationToken) =>
        repository.GetLowStatusAsync(options.ImageProfileVersion, cancellationToken);
}
