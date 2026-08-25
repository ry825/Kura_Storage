using System.Diagnostics;
using KuraStorage.Application.Search;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace KuraStorage.IntegrationTests;

public sealed class SearchPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RepresentativeQueries_OnDedicatedThreeHundredThousandEntryDataset_HaveP95UnderTwoSeconds()
    {
        var connectionString = Environment.GetEnvironmentVariable("KURASTORAGE_SEARCH_PERF_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var actorId = Guid.Parse("dc8fb2e8-d393-ff02-e3e5-3545614a87f2");
        var queries = RepresentativeQueries();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var database = new KuraStorageDbContext(options);
        var service = new SearchService(new PostgreSqlSearchRepository(database));

        for (var index = 0; index < queries.Count; index++)
        {
            try
            {
                var warm = await service.SearchAsync(actorId, queries[index], CancellationToken.None);
                Assert.True(warm.IsSuccess);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Redacted representative search case {index + 1} failed during warm-up.",
                    exception);
            }
        }

        var durations = new List<TimeSpan>(queries.Count * 3);
        for (var iteration = 0; iteration < 3; iteration++)
        {
            foreach (var query in queries)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await service.SearchAsync(actorId, query, CancellationToken.None);
                stopwatch.Stop();
                Assert.True(result.IsSuccess);
                Assert.InRange(result.Value!.Items.Count, 0, 100);
                durations.Add(stopwatch.Elapsed);
            }
        }

        var ordered = durations.Order().ToArray();
        var p50 = ordered[(int)Math.Ceiling(ordered.Length * 0.50) - 1];
        var p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
        output.WriteLine(
            "Redacted search performance: samples={0}, p50_ms={1:F0}, p95_ms={2:F0}, max_ms={3:F0}",
            durations.Count,
            p50.TotalMilliseconds,
            p95.TotalMilliseconds,
            ordered[^1].TotalMilliseconds);
        Assert.True(
            p95 < TimeSpan.FromSeconds(2),
            $"Redacted search performance p95 was {p95.TotalMilliseconds:F0} ms for {durations.Count} samples.");
    }

    private static IReadOnlyList<SearchQuery> RepresentativeQueries() =>
    [
        new(Text: "performance-file-100", PageSize: 50),
        new(Text: "per", PageSize: 50),
        new(Text: "pe", PageSize: 50),
        new(EntryType: "FILE", PageSize: 100),
        new(EntryType: "FOLDER", PageSize: 100),
        new(FileCategory: "IMAGE", PageSize: 100),
        new(FileCategory: "VIDEO", PageSize: 100),
        new(FileCategory: "AUDIO", PageSize: 100),
        new(FileCategory: "DOCUMENT", PageSize: 100),
        new(FileCategory: "ARCHIVE", PageSize: 100),
        new(FileCategory: "OTHER", PageSize: 100),
        new(Status: "ACTIVE", PageSize: 100),
        new(Status: "MISSING_CANDIDATE", PageSize: 100),
        new(Status: "MISSING", PageSize: 100),
        new(MinSize: 1_048_576, MaxSize: 1_048_676, PageSize: 100),
        new(UpdatedFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), PageSize: 100),
        new(Text: "file-20", FileCategory: "DOCUMENT", PageSize: 100),
        new(Text: "performance", Status: "ACTIVE", Page: 10, PageSize: 100),
        new(OwnerUserId: Guid.Parse("c4c6fcaa-4113-9611-e8af-0c5b710871a4"), PageSize: 100),
        new(ShareTargetId: Guid.Parse("be58d0b2-c159-1b3f-d317-9dfe81f381ca"), PageSize: 100),
    ];
}
