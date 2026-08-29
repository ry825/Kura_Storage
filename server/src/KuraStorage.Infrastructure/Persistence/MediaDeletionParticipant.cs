using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KuraStorage.Infrastructure.Persistence;

public sealed class MediaDeletionParticipant(
    KuraStorageDbContext database,
    IOptions<MediaOptions> mediaOptions) :
    IPermanentDeleteParticipant,
    IFileIndexDeletionParticipant
{
    private readonly MediaOptions media = mediaOptions.Value;

    public Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken) =>
        ListPhysicalArtifactsAsync(target.DescendantIds.Append(target.RootId).ToArray(), cancellationToken);

    public Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        FileIndexDeletionTarget target,
        CancellationToken cancellationToken) =>
        ListPhysicalArtifactsAsync(target.EntryIds, cancellationToken);

    public Task DeleteManagementDataAsync(
        PermanentDeleteTarget target,
        CancellationToken cancellationToken) =>
        DeleteManagementDataAsync(target.DescendantIds.Append(target.RootId).ToArray(), cancellationToken);

    public Task DeleteManagementDataAsync(
        FileIndexDeletionTarget target,
        CancellationToken cancellationToken) =>
        DeleteManagementDataAsync(target.EntryIds, cancellationToken);

    private async Task<IReadOnlyList<RelativeStoragePath>> ListPhysicalArtifactsAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return [];
        }

        var derivatives = await database.FileDerivatives
            .AsNoTracking()
            .Where(item => entryIds.Contains(item.SourceFileId))
            .Select(item => new { item.Id, item.RelativePath })
            .ToListAsync(cancellationToken);
        var derivativeIds = derivatives.Select(item => item.Id).ToArray();
        var jobIds = derivativeIds.Length == 0
            ? []
            : await database.MediaJobs
                .AsNoTracking()
                .Where(job => derivativeIds.Contains(job.DerivativeId))
                .Select(job => job.Id)
                .ToArrayAsync(cancellationToken);
        var paths = derivatives
            .Where(item => item.RelativePath is not null)
            .Select(item => SourceDirectory(item.RelativePath!))
            .Concat(jobIds.Select(jobId => RelativeStoragePath.Create($"{media.TemporaryRoot}/{jobId:N}")))
            .Distinct()
            .OrderBy(path => path.Value, StringComparer.Ordinal)
            .ToArray();
        return paths;
    }

    private async Task DeleteManagementDataAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return;
        }

        var derivatives = await database.FileDerivatives
            .Where(item => entryIds.Contains(item.SourceFileId))
            .ToListAsync(cancellationToken);
        database.FileDerivatives.RemoveRange(derivatives);
    }

    private RelativeStoragePath SourceDirectory(string relativePath)
    {
        var segments = relativePath.Split('/');
        if (segments.Length < 3 || !string.Equals(segments[0], media.DerivativeRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A persisted derivative path is outside the configured derivative root.");
        }

        return RelativeStoragePath.Create(string.Join('/', segments.Take(3)));
    }
}
