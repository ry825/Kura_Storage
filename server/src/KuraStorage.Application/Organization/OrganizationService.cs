using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Organization;

public sealed class OrganizationService(IOrganizationRepository repository, ISystemClock clock)
{
    public async Task<OrganizationResult<bool>> AddFavoriteAsync(
        Guid actorUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!ValidIds(actorUserId, entryId))
        {
            return NotFound<bool>();
        }

        var outcome = await repository.TryAddFavoriteAuthorizedAsync(
            actorUserId,
            entryId,
            clock.UtcNow,
            cancellationToken);
        return outcome is OrganizationRepositoryOutcome.Created or OrganizationRepositoryOutcome.NoChange
            ? OrganizationResult<bool>.Success(true)
            : NotFound<bool>();
    }

    public async Task<OrganizationResult<bool>> RemoveFavoriteAsync(
        Guid actorUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!ValidIds(actorUserId, entryId))
        {
            return NotFound<bool>();
        }

        await repository.RemoveFavoriteAsync(actorUserId, entryId, cancellationToken);
        return OrganizationResult<bool>.Success(true);
    }

    public async Task<OrganizationResult<FavoritePage>> ListFavoritesAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
        {
            return OrganizationResult<FavoritePage>.Fail(
                OrganizationErrorCodes.InvalidFavoritesRequest,
                OrganizationFailureKind.InvalidRequest);
        }

        return OrganizationResult<FavoritePage>.Success(
            await repository.ListFavoritesAsync(actorUserId, page, pageSize, cancellationToken));
    }

    public async Task<OrganizationResult<IReadOnlyList<TagItem>>> ListTagsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Invalid<IReadOnlyList<TagItem>>();
        }

        return OrganizationResult<IReadOnlyList<TagItem>>.Success(
            await repository.ListTagsAsync(actorUserId, cancellationToken));
    }

    public async Task<OrganizationResult<TagItem>> CreateTagAsync(
        Guid actorUserId,
        CreateTagCommand command,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || command is null || !TryNormalize(command.Name, out var normalized))
        {
            return Invalid<TagItem>();
        }

        var result = await repository.TryCreateTagAsync(
            actorUserId,
            normalized!.Name,
            normalized.NameKey,
            clock.UtcNow,
            cancellationToken);
        return MapTagMutation(result);
    }

    public async Task<OrganizationResult<TagItem>> RenameTagAsync(
        Guid actorUserId,
        RenameTagCommand command,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || command is null || command.TagId == Guid.Empty ||
            !TryNormalize(command.Name, out var normalized))
        {
            return Invalid<TagItem>();
        }

        return MapTagMutation(await repository.TryRenameTagAsync(
            actorUserId,
            command.TagId,
            normalized!.Name,
            normalized.NameKey,
            clock.UtcNow,
            cancellationToken));
    }

    public async Task<OrganizationResult<bool>> DeleteTagAsync(
        Guid actorUserId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        if (!ValidIds(actorUserId, tagId))
        {
            return TagNotFound<bool>();
        }

        return await repository.DeleteTagAsync(actorUserId, tagId, cancellationToken) == OrganizationRepositoryOutcome.NoChange
            ? OrganizationResult<bool>.Success(true)
            : TagNotFound<bool>();
    }

    public async Task<OrganizationResult<EntryOrganizationState>> GetEntryOrganizationAsync(
        Guid actorUserId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        if (!ValidIds(actorUserId, entryId))
        {
            return NotFound<EntryOrganizationState>();
        }

        var state = await repository.GetEntryOrganizationAsync(actorUserId, entryId, cancellationToken);
        return state is null
            ? NotFound<EntryOrganizationState>()
            : OrganizationResult<EntryOrganizationState>.Success(state);
    }

    public async Task<OrganizationResult<bool>> AttachTagAsync(
        Guid actorUserId,
        Guid entryId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        if (!ValidIds(actorUserId, entryId, tagId))
        {
            return NotFound<bool>();
        }

        var outcome = await repository.TryAttachTagAuthorizedAsync(
            actorUserId,
            entryId,
            tagId,
            clock.UtcNow,
            cancellationToken);
        return outcome switch
        {
            OrganizationRepositoryOutcome.Created or OrganizationRepositoryOutcome.NoChange =>
                OrganizationResult<bool>.Success(true),
            OrganizationRepositoryOutcome.EntryLimitExceeded => OrganizationResult<bool>.Fail(
                OrganizationErrorCodes.EntryTagLimitExceeded,
                OrganizationFailureKind.InvalidRequest),
            _ => NotFound<bool>(),
        };
    }

    public async Task<OrganizationResult<bool>> DetachTagAsync(
        Guid actorUserId,
        Guid entryId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        if (!ValidIds(actorUserId, entryId, tagId))
        {
            return OrganizationResult<bool>.Success(true);
        }

        await repository.DetachTagAsync(actorUserId, entryId, tagId, cancellationToken);
        return OrganizationResult<bool>.Success(true);
    }

    private static OrganizationResult<TagItem> MapTagMutation(OrganizationRepositoryResult<TagItem> result) =>
        result.Outcome switch
        {
            OrganizationRepositoryOutcome.Created or OrganizationRepositoryOutcome.NoChange when result.Value is not null =>
                OrganizationResult<TagItem>.Success(result.Value),
            OrganizationRepositoryOutcome.Conflict => OrganizationResult<TagItem>.Fail(
                OrganizationErrorCodes.TagNameConflict,
                OrganizationFailureKind.Conflict),
            OrganizationRepositoryOutcome.UserLimitExceeded => OrganizationResult<TagItem>.Fail(
                OrganizationErrorCodes.TagLimitExceeded,
                OrganizationFailureKind.InvalidRequest),
            _ => TagNotFound<TagItem>(),
        };

    private static bool TryNormalize(string? value, out NormalizedTagName? normalized)
    {
        try
        {
            normalized = TagNameNormalizer.Normalize(value!);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = null;
            return false;
        }

    }

    private static bool ValidIds(params Guid[] ids) => ids.All(id => id != Guid.Empty);

    private static OrganizationResult<T> Invalid<T>() => OrganizationResult<T>.Fail(
        OrganizationErrorCodes.InvalidOrganizationRequest,
        OrganizationFailureKind.InvalidRequest);

    private static OrganizationResult<T> NotFound<T>() => OrganizationResult<T>.Fail(
        OrganizationErrorCodes.FileNotFound,
        OrganizationFailureKind.NotFound);

    private static OrganizationResult<T> TagNotFound<T>() => OrganizationResult<T>.Fail(
        OrganizationErrorCodes.TagNotFound,
        OrganizationFailureKind.NotFound);
}
