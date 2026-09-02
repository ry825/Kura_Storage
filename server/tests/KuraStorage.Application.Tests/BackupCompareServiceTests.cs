using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Backup;
using KuraStorage.Application.Sharing;
using KuraStorage.Application.Transfers;
using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class BackupCompareServiceTests
{
    private static readonly DateTimeOffset ModifiedAt = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Compare_ClassifiesNewChangedAlreadyUploadedAndBlockedDeterministically()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var already = Candidate();
        var changed = Candidate();
        var missing = Candidate();
        var repository = new FakeBackupRepository(folderId);
        repository.AddState(userId, deviceId, already, FileEntryStatus.Active, sameMetadata: true, version: 3);
        repository.AddState(userId, deviceId, changed, FileEntryStatus.Active, sameMetadata: false, version: 4);
        repository.AddState(userId, deviceId, missing, FileEntryStatus.Missing, sameMetadata: false, version: 2);
        var service = new BackupCompareService(repository, new AllowAuthorizationService(), new UploadSessionOptions());

        var created = Candidate();
        var result = await service.CompareAsync(
            new BackupCompareCommand(userId, deviceId, folderId, [changed, created, missing, already]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!.Items,
            item => Assert.Equal(BackupCompareDecision.Changed, item.Decision),
            item => Assert.Equal(BackupCompareDecision.New, item.Decision),
            item => Assert.Equal(BackupCompareDecision.BlockedCurrentState, item.Decision),
            item => Assert.Equal(BackupCompareDecision.AlreadyUploaded, item.Decision));
        Assert.Equal(4, result.Value.Items[0].ExpectedRemoteFileVersion);
        Assert.Null(result.Value.Items[1].RemoteFileId);
    }

    [Fact]
    public async Task Compare_RejectsDuplicateOversizedAndInvalidCandidatesBeforeRepositoryAccess()
    {
        var repository = new FakeBackupRepository(Guid.NewGuid());
        var service = new BackupCompareService(repository, new AllowAuthorizationService(), new UploadSessionOptions());
        var candidate = Candidate();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var duplicate = await service.CompareAsync(
            new BackupCompareCommand(userId, deviceId, repository.FolderId, [candidate, candidate]),
            CancellationToken.None);
        var oversized = await service.CompareAsync(
            new BackupCompareCommand(
                userId,
                deviceId,
                repository.FolderId,
                Enumerable.Range(0, 101).Select(_ => Candidate()).ToArray()),
            CancellationToken.None);
        var invalid = await service.CompareAsync(
            new BackupCompareCommand(
                userId,
                deviceId,
                repository.FolderId,
                [candidate with { RelativePath = "../secret" }]),
            CancellationToken.None);
        var tooLarge = await service.CompareAsync(
            new BackupCompareCommand(
                userId,
                deviceId,
                repository.FolderId,
                [candidate with { Size = new UploadSessionOptions().MaximumFileBytes + 1 }]),
            CancellationToken.None);

        Assert.All([duplicate, oversized, invalid, tooLarge], result =>
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(BackupErrorCodes.InvalidRequest, result.Failure!.Code);
        });
        Assert.Equal(0, repository.ReadCount);
    }

    [Fact]
    public async Task Compare_FailsClosedForInactiveDeviceOrNonContributingFolder()
    {
        var repository = new FakeBackupRepository(Guid.NewGuid()) { DeviceActive = false };
        var command = new BackupCompareCommand(Guid.NewGuid(), Guid.NewGuid(), repository.FolderId, [Candidate()]);

        var inactive = await new BackupCompareService(repository, new AllowAuthorizationService(), new UploadSessionOptions())
            .CompareAsync(command, CancellationToken.None);
        repository.DeviceActive = true;
        var forbidden = await new BackupCompareService(repository, new DenyAuthorizationService(), new UploadSessionOptions())
            .CompareAsync(command, CancellationToken.None);

        Assert.Equal(BackupErrorCodes.NotFound, inactive.Failure!.Code);
        Assert.Equal(BackupErrorCodes.NotFound, forbidden.Failure!.Code);
    }

    [Fact]
    public async Task Compare_BlocksReceiptWhenRemoteFilePermissionWasRevoked()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var candidate = Candidate();
        var repository = new FakeBackupRepository(folderId);
        repository.AddState(userId, deviceId, candidate, FileEntryStatus.Active, sameMetadata: false, version: 2);
        var service = new BackupCompareService(
            repository,
            new DestinationOnlyAuthorizationService(folderId),
            new UploadSessionOptions());

        var result = await service.CompareAsync(
            new BackupCompareCommand(userId, deviceId, folderId, [candidate]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(BackupCompareDecision.BlockedCurrentState, item.Decision);
        Assert.Null(item.RemoteFileId);
        Assert.Null(item.ExpectedRemoteFileVersion);
    }

    private static BackupCompareCandidate Candidate() =>
        new(Guid.NewGuid().ToString("D"), "Photos/file.jpg", 1, ModifiedAt, new string('a', 64));

    private sealed class FakeBackupRepository(Guid folderId) : IBackupRepository
    {
        private readonly Dictionary<string, BackupReceiptState> states = [];

        public Guid FolderId { get; } = folderId;
        public bool DeviceActive { get; set; } = true;
        public int ReadCount { get; private set; }

        public void AddState(
            Guid userId,
            Guid deviceId,
            BackupCompareCandidate candidate,
            FileEntryStatus status,
            bool sameMetadata,
            long version)
        {
            var receipt = new BackupReceipt(
                Guid.NewGuid(), userId, deviceId, candidate.LocalDocumentKey, Guid.NewGuid(),
                candidate.RelativePath, sameMetadata ? candidate.Size : candidate.Size + 1,
                candidate.ModifiedAt, candidate.Checksum, version, ModifiedAt);
            states[candidate.LocalDocumentKey] = new BackupReceiptState(receipt, status, version);
        }

        public Task<bool> IsDeviceActiveAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(DeviceActive);
        }

        public Task<BackupDestination?> FindDestinationAsync(Guid folderId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<BackupDestination?>(folderId == FolderId
                ? new BackupDestination(folderId, Guid.NewGuid(), FileEntryType.Folder, FileEntryStatus.Active)
                : null);
        }

        public Task<IReadOnlyDictionary<string, BackupReceiptState>> ListReceiptStatesAsync(
            Guid userId,
            Guid deviceId,
            IReadOnlyCollection<string> localDocumentKeys,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyDictionary<string, BackupReceiptState>>(
                states.Where(pair => localDocumentKeys.Contains(pair.Key)).ToDictionary());
        }

        public Task<BackupReceipt?> FindReceiptAsync(
            Guid userId,
            Guid deviceId,
            string localDocumentKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(states.TryGetValue(localDocumentKey, out var state) ? state.Receipt : null);

        public void Add(BackupReceipt receipt) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<EffectivePermission> ResolveAsync(Guid actorUserId, Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(new EffectivePermission(entryId, EffectivePermissionLevel.Contributor, PermissionSource.Direct, entryId, Guid.NewGuid()));
        public Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(Guid actorUserId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EffectivePermission>>(entryIds.ToDictionary(
                entryId => entryId,
                entryId => new EffectivePermission(entryId, EffectivePermissionLevel.Editor, PermissionSource.Direct, entryId, Guid.NewGuid())));
        public Task<bool> AllowsAsync(Guid actorUserId, Guid entryId, ShareOperation operation, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class DenyAuthorizationService : IAuthorizationService
    {
        public Task<EffectivePermission> ResolveAsync(Guid actorUserId, Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(new EffectivePermission(entryId, EffectivePermissionLevel.None, null, null, null));
        public Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(Guid actorUserId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EffectivePermission>>(entryIds.ToDictionary(
                entryId => entryId,
                entryId => new EffectivePermission(entryId, EffectivePermissionLevel.None, null, null, null)));
        public Task<bool> AllowsAsync(Guid actorUserId, Guid entryId, ShareOperation operation, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class DestinationOnlyAuthorizationService(Guid destinationFolderId) : IAuthorizationService
    {
        public Task<EffectivePermission> ResolveAsync(Guid actorUserId, Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(new EffectivePermission(entryId, EffectivePermissionLevel.None, null, null, null));

        public Task<IReadOnlyDictionary<Guid, EffectivePermission>> ResolveBatchAsync(
            Guid actorUserId,
            IReadOnlyCollection<Guid> entryIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EffectivePermission>>(entryIds.ToDictionary(
                entryId => entryId,
                entryId => new EffectivePermission(entryId, EffectivePermissionLevel.None, null, null, null)));

        public Task<bool> AllowsAsync(
            Guid actorUserId,
            Guid entryId,
            ShareOperation operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(entryId == destinationFolderId && operation == ShareOperation.Contribute);
    }
}
