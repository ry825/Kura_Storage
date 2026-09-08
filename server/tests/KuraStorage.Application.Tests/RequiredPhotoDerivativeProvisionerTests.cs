using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Media;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Media;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class RequiredPhotoDerivativeProvisionerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ensure_StagesCurrentPhotoWithConfiguredProfileAndOrigin()
    {
        var repository = new RecordingRepository();
        var service = new RequiredPhotoDerivativeProvisioner(
            repository,
            new MediaRuntimeOptions { ImageProfileVersion = 7 });
        var owner = Guid.NewGuid();
        var photo = FileEntry.CreateFile(
            Guid.NewGuid(), owner, Guid.NewGuid(), FileName.Create("photo.jpg"),
            RelativeStoragePath.Create($"users/{owner:N}/files/photo.jpg"), "image/jpeg", 10, Now);

        Assert.True(await service.EnsureLowAsync(photo, MediaJobOrigin.Ingest, Now, CancellationToken.None));
        Assert.Equal((photo.Id, 1L, 7, MediaJobOrigin.Ingest), repository.Call);
    }

    [Fact]
    public async Task Ensure_IgnoresNonPhotoAndInactivePhoto()
    {
        var repository = new RecordingRepository();
        var service = new RequiredPhotoDerivativeProvisioner(repository, new MediaRuntimeOptions());
        var owner = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var text = FileEntry.CreateFile(
            Guid.NewGuid(), owner, parent, FileName.Create("note.txt"),
            RelativeStoragePath.Create($"users/{owner:N}/files/note.txt"), "text/plain", 10, Now);
        var photo = FileEntry.CreateFile(
            Guid.NewGuid(), owner, parent, FileName.Create("photo.jpg"),
            RelativeStoragePath.Create($"users/{owner:N}/files/photo.jpg"), "image/jpeg", 10, Now);
        photo.Trash(RelativeStoragePath.Create($"users/{owner:N}/trash/photo.jpg"), Now);

        Assert.False(await service.EnsureLowAsync(text, MediaJobOrigin.Ingest, Now, CancellationToken.None));
        Assert.False(await service.EnsureLowAsync(photo, MediaJobOrigin.Ingest, Now, CancellationToken.None));
        Assert.Null(repository.Call);
    }

    [Fact]
    public async Task Ensure_UsesTheCurrentVersionAfterPhotoContentChanges()
    {
        var repository = new RecordingRepository();
        var service = new RequiredPhotoDerivativeProvisioner(
            repository,
            new MediaRuntimeOptions { ImageProfileVersion = 4 });
        var owner = Guid.NewGuid();
        var photo = FileEntry.CreateFile(
            Guid.NewGuid(), owner, Guid.NewGuid(), FileName.Create("photo.jpg"),
            RelativeStoragePath.Create($"users/{owner:N}/files/photo.jpg"), "image/jpeg", 10, Now);
        photo.ApplyManagedContentChange(12, expectedVersion: 1, Now.AddMinutes(1));

        Assert.True(await service.EnsureLowAsync(photo, MediaJobOrigin.Ingest, Now.AddMinutes(1), CancellationToken.None));
        Assert.Equal((photo.Id, 2L, 4, MediaJobOrigin.Ingest), repository.Call);
    }

    private sealed class RecordingRepository : IMediaRepository
    {
        public (Guid Id, long Version, int Profile, MediaJobOrigin Origin)? Call { get; private set; }
        public Task<bool> StageRequiredLowAsync(FileEntry source, int profileVersion, MediaJobOrigin origin, DateTimeOffset now, CancellationToken cancellationToken)
        { Call = (source.Id, source.FileVersion, profileVersion, origin); return Task.FromResult(true); }
        public Task<MediaRequestSnapshot> GetOrCreateRequestAsync(FileEntry source, DerivativeType derivativeType, int profileVersion, Guid requestedByUserId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaRequestSnapshot?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaGenerationContext?> TryAcquireGenerationAsync(Guid jobId, Guid workerToken, Guid leaseOwnerToken, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteGenerationAsync(Guid jobId, Guid workerToken, Guid leaseOwnerToken, PublishedDerivative published, DateTimeOffset now, DateTimeOffset? expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DerivativeLeaseHandle?> TryAcquireDeliveryAsync(Guid derivativeId, Guid ownerToken, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewLeaseAsync(Guid derivativeId, DerivativeLeaseType leaseType, Guid ownerToken, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseLeaseAsync(Guid derivativeId, DerivativeLeaseType leaseType, Guid ownerToken, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RecordDeliveryAccessAsync(Guid derivativeId, DateTimeOffset now, TimeSpan cacheTtl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
