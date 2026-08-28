using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Search;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class SearchServiceTests
{
    [Theory]
    [InlineData(" a ", "a%", SearchMatchMode.Prefix)]
    [InlineData("Ab", "ab%", SearchMatchMode.Prefix)]
    [InlineData("Report", "%report%", SearchMatchMode.Contains)]
    [InlineData("100%_\\", "%100\\%\\_\\\\%", SearchMatchMode.Contains)]
    public void Validate_NormalizesAndEscapesSearchText(
        string input,
        string expectedPattern,
        SearchMatchMode expectedMode)
    {
        var result = SearchService.Validate(new SearchQuery(Text: input));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPattern, result.Value!.EscapedPattern);
        Assert.Equal(expectedMode, result.Value.MatchMode);
    }

    [Fact]
    public void Validate_NormalizesUnicodeToNfcAndCountsCodePoints()
    {
        var decomposed = "e\u0301x";

        var result = SearchService.Validate(new SearchQuery(Text: decomposed));

        Assert.True(result.IsSuccess);
        Assert.Equal("éx", result.Value!.NormalizedText);
        Assert.Equal(SearchMatchMode.Prefix, result.Value.MatchMode);
    }

    [Fact]
    public void Validate_AcceptsEveryFilterAndConvertsTimeToUtc()
    {
        var from = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(10));
        var result = SearchService.Validate(
            new SearchQuery(
                EntryType: "file",
                FileCategory: "document",
                Status: "missing_candidate",
                UpdatedFrom: from,
                UpdatedTo: from.AddHours(1),
                MinSize: 0,
                MaxSize: 42,
                OwnerUserId: Guid.NewGuid(),
                ShareTargetId: Guid.NewGuid(),
                Page: 2,
                PageSize: 100));

        Assert.True(result.IsSuccess);
        Assert.Equal("FILE", result.Value!.EntryType);
        Assert.Equal(FileCategory.Document, result.Value.FileCategory);
        Assert.Equal("MISSING_CANDIDATE", result.Value.Status);
        Assert.Equal(TimeSpan.Zero, result.Value.UpdatedFrom!.Value.Offset);
        Assert.Equal(2, result.Value.Page);
    }

    [Theory]
    [InlineData(null, null, null, null, 1, 50, SearchErrorCodes.InvalidQuery)]
    [InlineData(" ", null, null, null, 1, 50, SearchErrorCodes.InvalidQuery)]
    [InlineData("a", "unknown", null, null, 1, 50, SearchErrorCodes.InvalidFilter)]
    [InlineData("a", "folder", "image", null, 1, 50, SearchErrorCodes.InvalidFilter)]
    [InlineData("a", null, null, "trashed", 1, 50, SearchErrorCodes.InvalidFilter)]
    [InlineData("a", null, null, null, 0, 50, SearchErrorCodes.InvalidFilter)]
    [InlineData("a", null, null, null, 1, 101, SearchErrorCodes.InvalidFilter)]
    public void Validate_RejectsInvalidInputs(
        string? text,
        string? entryType,
        string? category,
        string? status,
        int page,
        int pageSize,
        string expectedCode)
    {
        var result = SearchService.Validate(
            new SearchQuery(text, entryType, category, status, Page: page, PageSize: pageSize));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Failure!.Code);
    }

    [Fact]
    public void Validate_RejectsRangesNegativeValuesEmptyIdsAndOversizedUnicode()
    {
        Assert.Equal(
            SearchErrorCodes.InvalidFilter,
            SearchService.Validate(new SearchQuery(Text: "a", MinSize: 2, MaxSize: 1)).Failure!.Code);
        Assert.Equal(
            SearchErrorCodes.InvalidFilter,
            SearchService.Validate(new SearchQuery(Text: "a", MinSize: -1)).Failure!.Code);
        Assert.Equal(
            SearchErrorCodes.InvalidFilter,
            SearchService.Validate(
                new SearchQuery(
                    Text: "a",
                    UpdatedFrom: DateTimeOffset.UtcNow,
                    UpdatedTo: DateTimeOffset.UtcNow.AddDays(-1))).Failure!.Code);
        Assert.Equal(
            SearchErrorCodes.InvalidFilter,
            SearchService.Validate(new SearchQuery(Text: "a", OwnerUserId: Guid.Empty)).Failure!.Code);
        Assert.Equal(
            SearchErrorCodes.InvalidFilter,
            SearchService.Validate(new SearchQuery(Text: "a", Page: int.MaxValue, PageSize: 100)).Failure!.Code);
        Assert.Equal(
            SearchErrorCodes.InvalidQuery,
            SearchService.Validate(new SearchQuery(Text: string.Concat(Enumerable.Repeat("😀", 201)))).Failure!.Code);
    }

    [Fact]
    public void Validate_AcceptsOneToTenUniqueTagsAsAStandaloneFilter()
    {
        var ten = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();

        var valid = SearchService.Validate(new SearchQuery(TagIds: ten));
        var duplicate = SearchService.Validate(new SearchQuery(TagIds: [ten[0], ten[0]]));
        var eleven = SearchService.Validate(new SearchQuery(TagIds: ten.Append(Guid.NewGuid()).ToArray()));

        Assert.True(valid.IsSuccess);
        Assert.Equal(ten, valid.Value!.TagIds);
        Assert.Equal(SearchErrorCodes.InvalidFilter, duplicate.Failure!.Code);
        Assert.Equal(SearchErrorCodes.InvalidFilter, eleven.Failure!.Code);
    }

    [Theory]
    [InlineData("image/jpeg", FileCategory.Image)]
    [InlineData("video/mp4", FileCategory.Video)]
    [InlineData("audio/flac", FileCategory.Audio)]
    [InlineData("text/plain", FileCategory.Document)]
    [InlineData("application/pdf", FileCategory.Document)]
    [InlineData("application/zip", FileCategory.Archive)]
    [InlineData("application/octet-stream", FileCategory.Other)]
    [InlineData(null, FileCategory.Other)]
    public void ClassifyMimeType_IsStableAndFailsUnknownToOther(string? mimeType, FileCategory expected)
    {
        Assert.Equal(expected, SearchService.ClassifyMimeType(mimeType));
    }

    [Fact]
    public async Task SearchAsync_UsesActorAndValidatedFilterOnce()
    {
        var repository = new FakeSearchRepository();
        var actor = Guid.NewGuid();
        var result = await new SearchService(repository).SearchAsync(
            actor,
            new SearchQuery(Text: "Report"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(actor, repository.ActorUserId);
        Assert.Equal("%report%", repository.Filter!.EscapedPattern);
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task SearchAsync_InvalidInputDoesNotCallRepository()
    {
        var repository = new FakeSearchRepository();
        var result = await new SearchService(repository).SearchAsync(
            Guid.NewGuid(),
            new SearchQuery(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, repository.CallCount);
    }

    private sealed class FakeSearchRepository : ISearchRepository
    {
        public int CallCount { get; private set; }
        public Guid ActorUserId { get; private set; }
        public SearchFilter? Filter { get; private set; }

        public Task<SearchPage?> SearchAsync(
            Guid actorUserId,
            SearchFilter filter,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ActorUserId = actorUserId;
            Filter = filter;
            return Task.FromResult<SearchPage?>(new SearchPage([], filter.Page, filter.PageSize, 0));
        }
    }
}
