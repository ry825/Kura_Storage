using KuraStorage.Application.Abstractions;

namespace KuraStorage.Infrastructure.Media;

public sealed class SystemMediaWaiter : IMediaWaiter
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
