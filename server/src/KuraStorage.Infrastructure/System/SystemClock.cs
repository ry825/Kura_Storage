using KuraStorage.Application.Abstractions;

namespace KuraStorage.Infrastructure.System;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
