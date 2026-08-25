using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Recent;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class RecentFileServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-25T02:00:00Z");

    [Fact]
    public async Task RecordAsync_UsesActorFileAndServerClock()
    {
        var repository = new FakeRepository { RecordAllowed = true };
        var actor = Guid.NewGuid();
        var file = Guid.NewGuid();
        var service = new RecentFileService(repository, new FixedClock(Now));

        var result = await service.RecordAsync(actor, file, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(actor, repository.RecordedUserId);
        Assert.Equal(file, repository.RecordedFileId);
        Assert.Equal(Now, repository.RecordedAt);
        Assert.Equal(1, repository.RecordCalls);
    }

    [Fact]
    public async Task RecordAsync_UnauthorizedOrInvalid_IsExistenceHidingNotFound()
    {
        var repository = new FakeRepository();
        var service = new RecentFileService(repository, new FixedClock(Now));

        var denied = await service.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        var invalid = await service.RecordAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(RecentFileErrorCodes.FileNotFound, denied.Failure!.Code);
        Assert.Equal(RecentFileFailureKind.NotFound, denied.Failure.Kind);
        Assert.Equal(RecentFileErrorCodes.FileNotFound, invalid.Failure!.Code);
        Assert.Equal(1, repository.RecordCalls);
    }

    [Fact]
    public async Task ListAsync_ValidatesPagingAndUsesActorOnly()
    {
        var repository = new FakeRepository();
        var actor = Guid.NewGuid();
        var service = new RecentFileService(repository, new FixedClock(Now));

        var result = await service.ListAsync(actor, 2, 100, CancellationToken.None);
        var invalid = await service.ListAsync(actor, 0, 101, CancellationToken.None);
        var overflow = await service.ListAsync(actor, int.MaxValue, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(actor, repository.ListedUserId);
        Assert.Equal(2, repository.Page);
        Assert.Equal(100, repository.PageSize);
        Assert.Equal(RecentFileErrorCodes.InvalidRequest, invalid.Failure!.Code);
        Assert.Equal(RecentFileErrorCodes.InvalidRequest, overflow.Failure!.Code);
        Assert.Equal(1, repository.ListCalls);
    }

    [Fact]
    public async Task RepositoryCancellationAndDatabaseErrorsPropagate()
    {
        var expected = new InvalidOperationException("database unavailable");
        var repository = new FakeRepository { Exception = expected };
        var service = new RecentFileService(repository, new FixedClock(Now));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RepositoryCancellationPropagates()
    {
        var repository = new FakeRepository { Exception = new OperationCanceledException() };
        var service = new RecentFileService(repository, new FixedClock(Now));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), new CancellationToken(canceled: true)));
    }

    private sealed class FakeRepository : IRecentFileRepository
    {
        public bool RecordAllowed { get; init; }
        public Exception? Exception { get; init; }
        public Guid? RecordedUserId { get; private set; }
        public Guid? RecordedFileId { get; private set; }
        public DateTimeOffset? RecordedAt { get; private set; }
        public int RecordCalls { get; private set; }
        public Guid? ListedUserId { get; private set; }
        public int Page { get; private set; }
        public int PageSize { get; private set; }
        public int ListCalls { get; private set; }

        public Task<bool> TryUpsertAuthorizedAsync(
            Guid userId,
            Guid fileId,
            DateTimeOffset openedAt,
            CancellationToken cancellationToken)
        {
            RecordCalls++;
            if (Exception is not null)
            {
                throw Exception;
            }

            RecordedUserId = userId;
            RecordedFileId = fileId;
            RecordedAt = openedAt;
            return Task.FromResult(RecordAllowed);
        }

        public Task<RecentFilePage> ListAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            ListedUserId = userId;
            Page = page;
            PageSize = pageSize;
            return Task.FromResult(new RecentFilePage([], page, pageSize, 0));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
