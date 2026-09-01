using System.Text;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Domain.Files;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class FileVersionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureCurrent_PublishesMissingCurrentRecordAndAddsItOnce()
    {
        var entry = CreateFile("text/plain", Encoding.UTF8.GetByteCount("hello"));
        var repository = new VersionRepository();
        var versionStore = new VersionStore();
        var service = CreateService(repository, versionStore, new ReadStore("hello"));
        var operationId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var actorDeviceId = Guid.NewGuid();
        var operation = CreateOperation(entry, operationId, actorDeviceId);

        var result = await service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, operationId, actorUserId, actorDeviceId, default, operation);

        Assert.NotNull(result);
        Assert.Equal(entry.Id, result.FileEntryId);
        Assert.Equal(entry.FileVersion, result.Version);
        Assert.Equal(entry.Size, result.Size);
        Assert.Equal(FileVersionChangeKind.Upload, result.ChangeKind);
        Assert.Equal(actorUserId, result.ActorUserId);
        Assert.Equal(actorDeviceId, result.ActorDeviceId);
        Assert.Equal(Now, result.CreatedAt);
        Assert.Same(result, Assert.Single(repository.Added));
        Assert.Equal(operationId, versionStore.OperationId);
        Assert.Equal(entry.FileVersion, operation.ResultFileVersion);
        Assert.Equal(result.ContentRelativePath, operation.VersionContentRelativePath);
        Assert.Equal(result.Sha256, operation.VersionSha256);
        Assert.Equal(FileVersionPublishStage.Published, operation.VersionPublishStage);
    }

    [Fact]
    public async Task EnsureCurrent_ExistingMatchingRecordIsIdempotent()
    {
        var entry = CreateFile("application/json", 2);
        var existing = Record(entry, 2);
        var repository = new VersionRepository(existing);
        var service = CreateService(repository, new VersionStore(), new ReadStore("{}"));
        var operationId = Guid.NewGuid();
        var operation = CreateOperation(entry, operationId, Guid.NewGuid());

        var result = await service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, operationId, null, null, default, operation);

        Assert.Same(existing, result);
        Assert.Empty(repository.Added);
        Assert.Equal(existing.ContentRelativePath, operation.VersionContentRelativePath);
    }

    [Fact]
    public async Task EnsureCurrent_RejectsRecordThatDoesNotMatchCurrentEntry()
    {
        var entry = CreateFile("text/plain", 4);
        var repository = new VersionRepository(Record(entry, 3));
        var service = CreateService(repository, new VersionStore(), new ReadStore("text"));

        await Assert.ThrowsAsync<FileVersionConsistencyException>(() => service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, Guid.NewGuid(), null, null, default));
    }

    [Theory]
    [InlineData("image/jpeg", 1)]
    [InlineData("text/plain", FileVersionRecord.MaximumContentBytes + 1)]
    public async Task EnsureCurrent_UnsupportedFileDoesNotReadOrPublish(string mimeType, long size)
    {
        var entry = CreateFile(mimeType, size);
        var readStore = new ReadStore("unused");
        var versionStore = new VersionStore();
        var service = CreateService(new VersionRepository(), versionStore, readStore);

        var result = await service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, Guid.NewGuid(), null, null, default);

        Assert.Null(result);
        Assert.False(readStore.Opened);
        Assert.Null(versionStore.OperationId);
    }

    [Fact]
    public async Task EnsureCurrent_UnavailableStorageFailsBeforeRead()
    {
        var entry = CreateFile("text/plain", 1);
        var readStore = new ReadStore("x");
        var service = CreateService(
            new VersionRepository(), new VersionStore(), readStore, StorageStatus.Unavailable);

        await Assert.ThrowsAsync<FileVersionStorageUnavailableException>(() => service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, Guid.NewGuid(), null, null, default));
        Assert.False(readStore.Opened);
    }

    [Fact]
    public async Task EnsureCurrent_InvalidUtf8IsNotRecorded()
    {
        var entry = CreateFile("text/plain", 2);
        var versionStore = new VersionStore { ReturnNull = true };
        var repository = new VersionRepository();
        var service = CreateService(repository, versionStore, new ReadStore([0xc3, 0x28]));

        var result = await service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, Guid.NewGuid(), null, null, default);

        Assert.Null(result);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task EnsureBaseline_WithoutMutationRepositoryFailsConfigurationCheck()
    {
        var service = CreateService(new VersionRepository(), new VersionStore(), new ReadStore("x"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureBaselineAsync(
            Guid.NewGuid(), FileVersionChangeKind.ExternalChange, Guid.NewGuid(), null, null, default));
    }

    [Fact]
    public async Task EnsureCurrent_EmptyOperationIdIsRejectedBeforeStorageAccess()
    {
        var service = CreateService(new VersionRepository(), new VersionStore(), new ReadStore("x"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.EnsureCurrentAsync(
            CreateFile("text/plain", 1), FileVersionChangeKind.Upload, Guid.Empty, null, null, default));
    }

    [Fact]
    public async Task EnsureCurrent_PublishedSizeMismatchFailsConsistencyCheck()
    {
        var entry = CreateFile("text/plain", 1);
        var service = CreateService(
            new VersionRepository(), new VersionStore { PublishedSizeDelta = 1 }, new ReadStore("x"));

        await Assert.ThrowsAsync<FileVersionConsistencyException>(() => service.EnsureCurrentAsync(
            entry, FileVersionChangeKind.Upload, Guid.NewGuid(), null, null, default));
    }

    private static FileVersionService CreateService(
        VersionRepository repository,
        VersionStore versionStore,
        ReadStore readStore,
        StorageStatus status = StorageStatus.Available) =>
        new(repository, versionStore, readStore, new Guard(status), new Clock());

    private static FileEntry CreateFile(string mimeType, long size)
    {
        var ownerId = Guid.NewGuid();
        return FileEntry.CreateFile(
            Guid.NewGuid(), ownerId, Guid.NewGuid(), FileName.Create("note.txt"),
            RelativeStoragePath.Create($"users/{ownerId:N}/files/note.txt"), mimeType, size, Now);
    }

    private static FileVersionRecord Record(FileEntry entry, long size)
    {
        var sha = new string('a', 64);
        return new FileVersionRecord(
            Guid.NewGuid(), entry.Id, entry.FileVersion, size, sha,
            $"versions/{entry.OwnerUserId:N}/{entry.Id:N}/{entry.FileVersion}/{sha}.bin",
            FileVersionChangeKind.Upload, null, null, Now);
    }

    private static FileOperation CreateOperation(FileEntry entry, Guid operationId, Guid actorDeviceId) =>
        new(
            operationId,
            entry.OwnerUserId,
            FileOperationType.Upload,
            entry.Id,
            Guid.NewGuid().ToString(),
            $"upload-temp/{entry.OwnerUserId:N}/{operationId:N}.upload",
            entry.RelativePath,
            entry.Size,
            null,
            Now,
            actorDeviceId);

    private sealed class VersionRepository(params FileVersionRecord[] records) : IFileVersionRepository
    {
        public List<FileVersionRecord> Added { get; } = [];

        public Task<FileVersionRecord?> FindAsync(Guid fileEntryId, long version, CancellationToken cancellationToken) =>
            Task.FromResult(records.SingleOrDefault(record =>
                record.FileEntryId == fileEntryId && record.Version == version));

        public void Add(FileVersionRecord record) => Added.Add(record);
    }

    private sealed class VersionStore : IFileVersionStore
    {
        public Guid? OperationId { get; private set; }

        public bool ReturnNull { get; init; }

        public long PublishedSizeDelta { get; init; }

        public Task<PublishedFileVersion?> TryPublishAsync(
            Guid ownerUserId, Guid fileEntryId, long version, Guid operationId, Stream source,
            long expectedSize, CancellationToken cancellationToken)
        {
            OperationId = operationId;
            if (ReturnNull)
            {
                return Task.FromResult<PublishedFileVersion?>(null);
            }

            var sha = new string('b', 64);
            return Task.FromResult<PublishedFileVersion?>(new PublishedFileVersion(
                RelativeStoragePath.Create(
                    $"version-temp/{ownerUserId:N}/{fileEntryId:N}/{version}/{operationId:N}.part"),
                RelativeStoragePath.Create($"versions/{ownerUserId:N}/{fileEntryId:N}/{version}/{sha}.bin"),
                checked(expectedSize + PublishedSizeDelta),
                sha));
        }
    }

    private sealed class ReadStore : IFileStore
    {
        private readonly byte[] content;

        public ReadStore(string content) : this(Encoding.UTF8.GetBytes(content)) { }

        public ReadStore(byte[] content) => this.content = content;

        public bool Opened { get; private set; }

        public Task<Stream> OpenReadAsync(RelativeStoragePath path, CancellationToken cancellationToken)
        {
            Opened = true;
            return Task.FromResult<Stream>(new MemoryStream(content));
        }

        public Task<bool> HasCapacityAsync(long requiredBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureUserAreaAsync(Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredUpload> WriteUploadTempAsync(Guid ownerUserId, Guid operationId, Stream source, long expectedSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveAsync(RelativeStoragePath source, RelativeStoragePath target, bool sourceIsDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTreeIfExistsAsync(RelativeStoragePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(RelativeStoragePath path, bool directory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Guard(StorageStatus status) : IStorageGuard
    {
        public Task<StorageStatus> InspectAsync(StorageIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
