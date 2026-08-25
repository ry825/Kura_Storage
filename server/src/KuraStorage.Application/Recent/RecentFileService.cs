using KuraStorage.Application.Abstractions;
namespace KuraStorage.Application.Recent;

public sealed class RecentFileService(
    IRecentFileRepository recentFiles,
    ISystemClock clock)
{
    public async Task<RecentFileResult<bool>> RecordAsync(
        Guid actorUserId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || fileId == Guid.Empty)
        {
            return RecentFileResult<bool>.Fail(
                RecentFileErrorCodes.FileNotFound,
                RecentFileFailureKind.NotFound);
        }

        return await recentFiles.TryUpsertAuthorizedAsync(
            actorUserId,
            fileId,
            clock.UtcNow,
            cancellationToken)
            ? RecentFileResult<bool>.Success(true)
            : NotFound();
    }

    public async Task<RecentFileResult<RecentFilePage>> ListAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
        {
            return RecentFileResult<RecentFilePage>.Fail(
                RecentFileErrorCodes.InvalidRequest,
                RecentFileFailureKind.InvalidRequest);
        }

        return RecentFileResult<RecentFilePage>.Success(
            await recentFiles.ListAsync(actorUserId, page, pageSize, cancellationToken));
    }

    private static RecentFileResult<bool> NotFound() =>
        RecentFileResult<bool>.Fail(RecentFileErrorCodes.FileNotFound, RecentFileFailureKind.NotFound);
}
