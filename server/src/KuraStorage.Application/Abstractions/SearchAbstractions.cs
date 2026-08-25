using KuraStorage.Application.Search;

namespace KuraStorage.Application.Abstractions;

public interface ISearchRepository
{
    Task<SearchPage> SearchAsync(
        Guid actorUserId,
        SearchFilter filter,
        CancellationToken cancellationToken);
}
