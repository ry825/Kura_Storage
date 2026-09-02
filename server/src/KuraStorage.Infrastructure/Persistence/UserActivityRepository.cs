using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class UserActivityRepository(KuraStorageDbContext dbContext) : IUserActivityRepository
{
    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<Device?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
        dbContext.Devices.SingleOrDefaultAsync(device => device.Id == deviceId, cancellationToken);

    public Task<UserActivity?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        dbContext.UserActivities.SingleOrDefaultAsync(
            activity => activity.OperationId == operationId,
            cancellationToken);

    public void Add(UserActivity activity) => dbContext.UserActivities.Add(activity);
}
