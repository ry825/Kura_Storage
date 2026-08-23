using System.Data;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Sharing;
using KuraStorage.Domain.Audit;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Identity;
using KuraStorage.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class ShareRepository(KuraStorageDbContext dbContext) : IShareRepository
{
    public async Task<IShareTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new ShareTransaction(
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));

    public async Task<IReadOnlyList<ShareCandidate>> ListCandidatesAsync(
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id != actorUserId && user.Status == UserStatus.Active)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new ShareCandidate(user.Id, user.DisplayName))
            .ToListAsync(cancellationToken);

    public async Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<FileEntry?> FindEntryAsync(Guid entryId, CancellationToken cancellationToken) =>
        await dbContext.FileEntries.SingleOrDefaultAsync(entry => entry.Id == entryId, cancellationToken);

    public async Task<Share?> FindByIdAsync(Guid shareId, CancellationToken cancellationToken) =>
        await dbContext.Shares
            .Include(share => share.Members)
            .SingleOrDefaultAsync(share => share.Id == shareId, cancellationToken);

    public async Task<Share?> FindByTargetAsync(Guid targetEntryId, CancellationToken cancellationToken) =>
        await dbContext.Shares
            .Include(share => share.Members)
            .SingleOrDefaultAsync(share => share.TargetEntryId == targetEntryId, cancellationToken);

    public async Task<ShareView?> GetViewAsync(Guid shareId, CancellationToken cancellationToken)
    {
        var rows = await ViewRows([shareId]).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : Map(rows);
    }

    public async Task<IReadOnlyList<ShareView>> ListViewsAsync(
        Guid actorUserId,
        ShareScope scope,
        FileEntryType? targetType,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var ids = await VisibleShares(actorUserId, scope, targetType)
            .OrderByDescending(share => share.UpdatedAt)
            .ThenBy(share => share.Id)
            .Skip(skip)
            .Take(take)
            .Select(share => share.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await ViewRows(ids).ToListAsync(cancellationToken);
        var views = rows.GroupBy(row => row.ShareId).ToDictionary(group => group.Key, Map);
        return ids.Select(id => views[id]).ToArray();
    }

    public Task<int> CountViewsAsync(
        Guid actorUserId,
        ShareScope scope,
        FileEntryType? targetType,
        CancellationToken cancellationToken) =>
        VisibleShares(actorUserId, scope, targetType).CountAsync(cancellationToken);

    public void Add(Share share) => dbContext.Shares.Add(share);

    public void Add(AuditLog auditLog) => dbContext.AuditLogs.Add(auditLog);

    public void Remove(Share share) => dbContext.Shares.Remove(share);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new SharePersistenceConflictException(exception);
        }
    }

    private IQueryable<Share> VisibleShares(Guid actorUserId, ShareScope scope, FileEntryType? targetType) =>
        dbContext.Shares
            .AsNoTracking()
            .Where(share => scope == ShareScope.Owned
                ? share.OwnerUserId == actorUserId
                : share.Members.Any(member => member.UserId == actorUserId))
            .Where(share => dbContext.FileEntries.Any(entry =>
                entry.Id == share.TargetEntryId &&
                entry.Status == FileEntryStatus.Active &&
                (targetType == null || entry.EntryType == targetType) &&
                !dbContext.FileOperations.Any(operation =>
                    operation.OwnerUserId == entry.OwnerUserId &&
                    operation.Status != FileOperationStatus.Completed &&
                    dbContext.FileEntries.Any(operationTarget =>
                        operationTarget.Id == operation.FileEntryId &&
                        (operationTarget.Id == entry.Id ||
                         entry.RelativePath.StartsWith(operationTarget.RelativePath + "/"))))));

    private IQueryable<ShareViewRow> ViewRows(IReadOnlyCollection<Guid> shareIds) =>
        from share in dbContext.Shares.AsNoTracking()
        join entry in dbContext.FileEntries.AsNoTracking() on share.TargetEntryId equals entry.Id
        join owner in dbContext.Users.AsNoTracking() on share.OwnerUserId equals owner.Id
        join member in dbContext.ShareMembers.AsNoTracking() on share.Id equals member.ShareId
        join memberUser in dbContext.Users.AsNoTracking() on member.UserId equals memberUser.Id
        where shareIds.Contains(share.Id) &&
              entry.Status == FileEntryStatus.Active &&
              !dbContext.FileOperations.Any(operation =>
                  operation.OwnerUserId == entry.OwnerUserId &&
                  operation.Status != FileOperationStatus.Completed &&
                  dbContext.FileEntries.Any(operationTarget =>
                      operationTarget.Id == operation.FileEntryId &&
                      (operationTarget.Id == entry.Id ||
                       entry.RelativePath.StartsWith(operationTarget.RelativePath + "/"))))
        select new ShareViewRow(
            share.Id,
            share.TargetEntryId,
            entry.EntryType,
            entry.Name,
            share.OwnerUserId,
            owner.DisplayName,
            member.UserId,
            memberUser.DisplayName,
            member.Permission,
            share.CreatedAt,
            share.UpdatedAt);

    private static ShareView Map(IEnumerable<ShareViewRow> source)
    {
        var rows = source.ToArray();
        var first = rows[0];
        return new ShareView(
            first.ShareId,
            first.TargetEntryId,
            first.EntryType,
            first.Name,
            first.OwnerUserId,
            first.OwnerDisplayName,
            rows.Select(row => new ShareViewMember(row.MemberUserId, row.MemberDisplayName, row.Permission)).ToArray(),
            first.CreatedAt,
            first.UpdatedAt);
    }

    private sealed record ShareViewRow(
        Guid ShareId,
        Guid TargetEntryId,
        FileEntryType EntryType,
        string Name,
        Guid OwnerUserId,
        string OwnerDisplayName,
        Guid MemberUserId,
        string MemberDisplayName,
        SharePermission Permission,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed class ShareTransaction(IDbContextTransaction transaction) : IShareTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
