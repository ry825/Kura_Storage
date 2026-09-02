using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Identity;

namespace KuraStorage.Application.Abstractions;

public interface IUserActivityRepository
{
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Device?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    Task<UserActivity?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken);

    void Add(UserActivity activity);
}
