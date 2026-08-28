using KuraStorage.Application.Organization;

namespace KuraStorage.Application.Abstractions;

public interface IOrganizationRepository
{
    Task<OrganizationRepositoryOutcome> TryAddFavoriteAuthorizedAsync(
        Guid userId,
        Guid entryId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task RemoveFavoriteAsync(Guid userId, Guid entryId, CancellationToken cancellationToken);

    Task<FavoritePage> ListFavoritesAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TagItem>> ListTagsAsync(Guid userId, CancellationToken cancellationToken);

    Task<OrganizationRepositoryResult<TagItem>> TryCreateTagAsync(
        Guid userId,
        string name,
        string nameKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<OrganizationRepositoryResult<TagItem>> TryRenameTagAsync(
        Guid userId,
        Guid tagId,
        string name,
        string nameKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<OrganizationRepositoryOutcome> DeleteTagAsync(
        Guid userId,
        Guid tagId,
        CancellationToken cancellationToken);

    Task<EntryOrganizationState?> GetEntryOrganizationAsync(
        Guid userId,
        Guid entryId,
        CancellationToken cancellationToken);

    Task<OrganizationRepositoryOutcome> TryAttachTagAuthorizedAsync(
        Guid userId,
        Guid entryId,
        Guid tagId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task DetachTagAsync(
        Guid userId,
        Guid entryId,
        Guid tagId,
        CancellationToken cancellationToken);
}
