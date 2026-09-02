using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Activity;
using KuraStorage.Domain.Activity;
using System.Text;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class ActivityQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Cursor_RoundTripsAndRejectsMalformedValues()
    {
        var expected = new ActivityCursor(Now, Guid.NewGuid());
        var encoded = ActivityCursorCodec.Encode(expected);

        Assert.True(ActivityCursorCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(expected, decoded);
        Assert.False(ActivityCursorCodec.TryDecode("not-a-cursor", out _));
        Assert.False(ActivityCursorCodec.TryDecode(string.Empty, out _));
    }

    [Fact]
    public void Cursor_RejectsInvalidEncodeEmptyIdVersionTicksAndPayloadId()
    {
        Assert.Throws<ArgumentException>(() => ActivityCursorCodec.Encode(new ActivityCursor(Now, Guid.Empty)));
        Assert.Throws<ArgumentException>(
            () => ActivityCursorCodec.Encode(new ActivityCursor(Now.ToOffset(TimeSpan.FromHours(1)), Guid.NewGuid())));
        Assert.False(ActivityCursorCodec.TryDecode("A", out _));

        var emptyIdPayload = new byte[25];
        emptyIdPayload[0] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(emptyIdPayload.AsSpan(1, 8), Now.UtcTicks);
        Assert.False(ActivityCursorCodec.TryDecode(ToCursor(emptyIdPayload), out _));

        var invalidTicksPayload = new byte[25];
        invalidTicksPayload[0] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(invalidTicksPayload.AsSpan(1, 8), long.MaxValue);
        Guid.NewGuid().TryWriteBytes(invalidTicksPayload.AsSpan(9));
        Assert.False(ActivityCursorCodec.TryDecode(ToCursor(invalidTicksPayload), out _));

        static string ToCursor(byte[] payload) =>
            Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Theory]
    [InlineData(null, 50, true)]
    [InlineData("upload", 100, true)]
    [InlineData("UNKNOWN", 50, false)]
    [InlineData("", 50, false)]
    [InlineData(null, 0, false)]
    [InlineData(null, 101, false)]
    public void ListValidation_IsStrict(string? type, int pageSize, bool expected)
    {
        var result = ActivityQueryService.Validate(new ActivityListRequest(type, null, pageSize));
        Assert.Equal(expected, result.IsSuccess);
    }

    [Fact]
    public async Task List_UsesActorAndReturnsOpaqueNextCursorWithoutInternalIds()
    {
        var actor = Guid.NewGuid();
        var repository = new FakeUserRepository(CreateRecord(Now), CreateRecord(Now.AddMinutes(-1)));
        var result = await new ActivityQueryService(repository).ListAsync(
            actor,
            new ActivityListRequest(PageSize: 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(actor, repository.ActorUserId);
        Assert.Equal(2, repository.Filter!.Limit);
        Assert.Single(result.Value!.Items);
        Assert.NotNull(result.Value.NextCursor);
        Assert.Equal("UPLOAD", result.Value.Items[0].Type);
    }

    [Fact]
    public void AdminValidation_EnforcesLimitsPeriodUtcAndSelectors()
    {
        var valid = AdminActivityService.Validate(
            new AdminActivitySearchRequest(
                ActorUser: "member",
                From: Now.ToOffset(TimeSpan.FromHours(10)),
                To: Now.AddDays(365),
                Limit: 1000));
        Assert.True(valid.IsSuccess);
        Assert.Equal(TimeSpan.Zero, valid.Value!.From!.Value.Offset);

        Assert.False(AdminActivityService.Validate(new AdminActivitySearchRequest(Limit: 1001)).IsSuccess);
        Assert.False(AdminActivityService.Validate(new AdminActivitySearchRequest(From: Now, To: Now.AddDays(366))).IsSuccess);
        Assert.False(AdminActivityService.Validate(new AdminActivitySearchRequest(ActorUser: "bad\nvalue")).IsSuccess);
        Assert.False(AdminActivityService.Validate(new AdminActivitySearchRequest(FileId: Guid.Empty)).IsSuccess);
    }

    [Fact]
    public void AdminCommandParser_ParsesCombinedFiltersAndRejectsAmbiguousInput()
    {
        var fileId = Guid.NewGuid();
        var args = new[]
        {
            "--actor-user", "member", "--owner-user", Guid.NewGuid().ToString(),
            "--type", "EDIT", "--from", "2026-09-01T00:00:00.0000000Z",
            "--to", "2026-09-02T00:00:00.0000000Z", "--file-id", fileId.ToString(),
            "--limit", "1000", "--json",
        };

        Assert.True(AdminActivityCommandParser.TryParse(args, out var command));
        Assert.True(command!.Json);
        Assert.Equal(fileId, command.Request.FileId);
        Assert.False(AdminActivityCommandParser.TryParse(["--type", "UPLOAD", "--type", "MOVE"], out _));
        Assert.False(AdminActivityCommandParser.TryParse(["--from", "2026-09-01T00:00:00+10:00"], out _));
        Assert.False(AdminActivityCommandParser.TryParse(["--unknown", "value"], out _));
    }

    [Fact]
    public async Task AdminSearch_UsesSeparateRepositoryAuditorAndSafePublicResult()
    {
        var repository = new FakeAdminRepository(CreateRecord(Now), CreateRecord(Now.AddSeconds(-1)));
        var service = new AdminActivityService(repository, new FixedClock(Now));
        var result = await service.SearchAsync(
            new AdminActivitySearchRequest(ActorUser: "member", Limit: 1),
            "local-admin",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.Filter!.Limit);
        Assert.Equal("local-admin", repository.ActorOsUser);
        Assert.Equal(Now, repository.OccurredAt);
        Assert.Single(result.Value!.Items);
        Assert.Equal("UPLOAD", result.Value.Items[0].Type);
        Assert.NotNull(result.Value.NextCursor);
    }

    [Fact]
    public async Task AdminSearch_InvalidOrUnknownSelectorFailsClosed()
    {
        var repository = new FakeAdminRepository();
        var service = new AdminActivityService(repository, new FixedClock(Now));

        var invalid = await service.SearchAsync(
            new AdminActivitySearchRequest(Limit: 0), "admin", CancellationToken.None);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(0, repository.CallCount);

        repository.ReturnUnknownSelector = true;
        var unknown = await service.SearchAsync(
            new AdminActivitySearchRequest(ActorUser: "missing"), "admin", CancellationToken.None);
        Assert.False(unknown.IsSuccess);
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public void AdminOutput_EscapesTableSerializesJsonAndPropagatesPipeFailure()
    {
        var item = new ActivityItem(
            "UPLOAD", Now, "Actor\tName", "Device", Guid.NewGuid(), "FILE", "Target\nName", "Owner",
            null, null, 1, null, null, null, null, null);
        var page = new AdminActivityPage([item], "next-token");
        using var table = new StringWriter();
        AdminActivityOutput.Write(page, false, table, CancellationToken.None);
        Assert.Contains("Actor\\tName", table.ToString(), StringComparison.Ordinal);
        Assert.Contains("Target\\nName", table.ToString(), StringComparison.Ordinal);
        Assert.Contains("next_cursor=next-token", table.ToString(), StringComparison.Ordinal);

        using var json = new StringWriter();
        AdminActivityOutput.Write(page, true, json, CancellationToken.None);
        Assert.Contains("\"type\":\"UPLOAD\"", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("operationId", json.ToString(), StringComparison.Ordinal);

        Assert.Throws<IOException>(
            () => AdminActivityOutput.Write(page, false, new FailingWriter(), CancellationToken.None));
        Assert.Throws<OperationCanceledException>(
            () => AdminActivityOutput.Write(page, false, TextWriter.Null, new CancellationToken(canceled: true)));
    }

    private static ActivityRecord CreateRecord(DateTimeOffset occurredAt) => new(
        Guid.NewGuid(), UserActivityType.Upload, occurredAt, "Actor", "Device", Guid.NewGuid(),
        ActivityTargetType.File, "File", "Owner", null, null, 1, null, null, null, null, null);

    private sealed class FakeUserRepository(params ActivityRecord[] records) : IUserActivityQueryRepository
    {
        public Guid ActorUserId { get; private set; }
        public ActivityQueryFilter? Filter { get; private set; }

        public Task<IReadOnlyList<ActivityRecord>> ListAsync(
            Guid actorUserId,
            ActivityQueryFilter filter,
            CancellationToken cancellationToken)
        {
            ActorUserId = actorUserId;
            Filter = filter;
            return Task.FromResult<IReadOnlyList<ActivityRecord>>(records);
        }
    }

    private sealed class FakeAdminRepository(params ActivityRecord[] records) : IUserActivityAdminQueryRepository
    {
        public int CallCount { get; private set; }
        public AdminActivitySearchFilter? Filter { get; private set; }
        public string? ActorOsUser { get; private set; }
        public DateTimeOffset OccurredAt { get; private set; }
        public bool ReturnUnknownSelector { get; set; }

        public Task<IReadOnlyList<ActivityRecord>?> SearchAsync(
            AdminActivitySearchFilter filter,
            string actorOsUser,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Filter = filter;
            ActorOsUser = actorOsUser;
            OccurredAt = occurredAt;
            return Task.FromResult<IReadOnlyList<ActivityRecord>?>(ReturnUnknownSelector ? null : records);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value) => throw new IOException("closed pipe");
    }
}
