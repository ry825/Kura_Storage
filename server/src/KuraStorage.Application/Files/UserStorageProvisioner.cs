using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Files;

namespace KuraStorage.Application.Files;

public sealed class UserStorageProvisioner(IFileRepository repository, IFileStore fileStore) : IUserStorageProvisioner
{
    public async Task ProvisionAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await repository.FindRootAsync(userId, cancellationToken) is not null)
        {
            return;
        }

        await fileStore.EnsureUserAreaAsync(userId, cancellationToken);
        repository.Add(FileEntry.CreateRoot(userId, now));
        await repository.SaveChangesAsync(cancellationToken);
    }
}
