using System.Diagnostics;
using KuraStorage.Application.Search;
using KuraStorage.Infrastructure.Persistence;
using KuraStorage.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace KuraStorage.IntegrationTests;

public sealed class TagSearchPerformanceTests(ITestOutputHelper output)
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid RootId = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid SharedOwnerId = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid SharedRootId = Guid.Parse("10000000-0000-4000-8000-000000000004");
    private static readonly Guid[] AllTagIds = Enumerable.Range(1, 200)
        .Select(index => Guid.Parse($"20000000-0000-4000-8000-{index:D12}"))
        .ToArray();
    private static readonly Guid[] SelectedTagIds = AllTagIds.Take(10).ToArray();
    private static readonly Guid[] AttachedTagIds = AllTagIds.Take(20).ToArray();

    [Fact]
    public async Task TagQueries_OnThreeHundredThousandEntries_UseIndexesAndStayUnderTwoSeconds()
    {
        if (Environment.GetEnvironmentVariable("KURASTORAGE_RUN_TAG_SEARCH_PERF") != "1")
        {
            return;
        }

        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("tag_search_performance")
            .WithUsername("kurastorage")
            .WithPassword("integration-only-password")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<KuraStorageDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new KuraStorageDbContext(options);
        await database.Database.MigrateAsync();
        var seedStopwatch = Stopwatch.StartNew();
        await SeedAsync(postgres.GetConnectionString());
        seedStopwatch.Stop();

        var service = new SearchService(new PostgreSqlSearchRepository(database));
        var queries = new SearchQuery[]
        {
            new(TagIds: [SelectedTagIds[0]], PageSize: 100),
            new(TagIds: SelectedTagIds, PageSize: 100),
            new(Text: "performance-tag-file-100", TagIds: [SelectedTagIds[0]], PageSize: 100),
            new(EntryType: "FILE", Status: "MISSING", TagIds: [SelectedTagIds[0]], PageSize: 100),
            new(OwnerUserId: SharedOwnerId, TagIds: [SelectedTagIds[0]], PageSize: 100),
            new(EntryType: "FILE", TagIds: SelectedTagIds, Page: 100, PageSize: 100),
        };
        foreach (var query in queries)
        {
            var warm = await service.SearchAsync(ActorId, query, CancellationToken.None);
            Assert.True(warm.IsSuccess);
        }

        var durations = new List<TimeSpan>(queries.Length * 3);
        for (var iteration = 0; iteration < 3; iteration++)
        {
            foreach (var query in queries)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await service.SearchAsync(ActorId, query, CancellationToken.None);
                stopwatch.Stop();
                Assert.True(result.IsSuccess);
                Assert.InRange(result.Value!.Items.Count, 0, 100);
                durations.Add(stopwatch.Elapsed);
            }
        }

        var ordered = durations.Order().ToArray();
        var p50 = ordered[(int)Math.Ceiling(ordered.Length * 0.50) - 1];
        var p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
        var plan = await ExplainTagIntersectionAsync(postgres.GetConnectionString());
        output.WriteLine(
            "Redacted tag search performance: seed_ms={0:F0}, samples={1}, p50_ms={2:F0}, p95_ms={3:F0}, max_ms={4:F0}",
            seedStopwatch.Elapsed.TotalMilliseconds,
            durations.Count,
            p50.TotalMilliseconds,
            p95.TotalMilliseconds,
            ordered[^1].TotalMilliseconds);
        Console.Error.WriteLine(
            "Redacted tag search performance: seed_ms={0:F0}, samples={1}, p50_ms={2:F0}, p95_ms={3:F0}, max_ms={4:F0}",
            seedStopwatch.Elapsed.TotalMilliseconds,
            durations.Count,
            p50.TotalMilliseconds,
            p95.TotalMilliseconds,
            ordered[^1].TotalMilliseconds);
        Assert.Contains("Index", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on entry_tags", plan, StringComparison.Ordinal);
        Assert.True(p95 < TimeSpan.FromSeconds(2), $"Redacted tag-search p95 was {p95.TotalMilliseconds:F0} ms.");
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            VALUES
                (@actor, 'TAGPERFORMANCE', 'Tag Performance', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()),
                (@shared_owner, 'TAGPERFSHARED', 'Tag Performance Shared', 'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now());
            INSERT INTO users
                (id, username_normalized, display_name, password_hash, role, status,
                 failed_login_count, lock_type, created_at, updated_at)
            SELECT md5('tag-performance-user-' || value)::uuid,
                   'TAGPERFORMANCE' || value,
                   'Tag Performance ' || value,
                   'hash', 'MEMBER', 'ACTIVE', 0, 'NONE', now(), now()
            FROM generate_series(3, 10) AS value;

            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type, size,
                 status, missing_detected_at, missing_last_checked_at, missing_observation_id,
                 file_version, created_at, updated_at)
            VALUES
                (@root, @actor, NULL, 'FOLDER', 'Files', @root_path, NULL, 0, 'ACTIVE',
                 NULL, NULL, NULL, 1, now(), now()),
                (@shared_root, @shared_owner, NULL, 'FOLDER', 'Files', @shared_root_path, NULL, 0, 'ACTIVE',
                 NULL, NULL, NULL, 1, now(), now());

            INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
            VALUES (md5('tag-performance-share')::uuid, @shared_root, @shared_owner, now(), now());
            INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at)
            VALUES (md5('tag-performance-share')::uuid, @actor, 'VIEWER', now(), now());

            INSERT INTO file_entries
                (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type, size,
                 status, missing_detected_at, missing_last_checked_at, missing_observation_id,
                 file_version, created_at, updated_at)
            SELECT
                md5('tag-performance-file-' || value)::uuid,
                CASE WHEN value <= 270000 THEN @actor ELSE @shared_owner END,
                CASE WHEN value <= 270000 THEN @root ELSE @shared_root END,
                'FILE',
                'performance-tag-file-' || lpad(value::text, 6, '0') || '.txt',
                CASE WHEN value <= 270000 THEN @root_path ELSE @shared_root_path END ||
                    '/performance-tag-file-' || lpad(value::text, 6, '0') || '.txt',
                'text/plain',
                value,
                CASE WHEN value % 100 = 0 THEN 'MISSING' ELSE 'ACTIVE' END,
                CASE WHEN value % 100 = 0 THEN now() - interval '2 minutes' ELSE NULL END,
                CASE WHEN value % 100 = 0 THEN now() ELSE NULL END,
                CASE WHEN value % 100 = 0 THEN md5('observation-' || value)::uuid ELSE NULL END,
                1,
                now(),
                timestamp with time zone '2026-01-01 00:00:00+00' + value * interval '1 second'
            FROM generate_series(1, 300000) AS value;

            INSERT INTO tags (id, user_id, name, name_key, created_at, updated_at)
            SELECT id, @actor, 'Tag ' || ordinal, 'TAG ' || ordinal, now(), now()
            FROM unnest(@tag_ids::uuid[]) WITH ORDINALITY AS item(id, ordinal);
            INSERT INTO tags (id, user_id, name, name_key, created_at, updated_at)
            SELECT md5('tag-performance-tag-' || owner.id || '-' || ordinal)::uuid,
                   owner.id,
                   'Tag ' || ordinal,
                   'TAG ' || ordinal,
                   now(), now()
            FROM users AS owner
            CROSS JOIN generate_series(1, 200) AS ordinal
            WHERE owner.id <> @actor;

            INSERT INTO entry_tags (tag_id, entry_id, attached_at)
            SELECT tag.id, entry.id, now()
            FROM file_entries AS entry
            CROSS JOIN unnest(@attached_tag_ids::uuid[]) AS tag(id)
            WHERE entry.parent_id IN (@root, @shared_root)
              AND ((tag.id = @first_tag AND substring(entry.name from '[0-9]{6}')::integer % 5 = 0) OR
                   substring(entry.name from '[0-9]{6}')::integer % 10 = 0);

            DO $$
            BEGIN
                IF (SELECT count(*) FROM users) <> 10 OR
                   (SELECT count(*) FROM tags) <> 2000 OR
                   (SELECT count(*) FROM file_entries WHERE entry_type = 'FILE') <> 300000 OR
                   (SELECT max(tag_count) FROM
                       (SELECT count(*) AS tag_count FROM entry_tags GROUP BY entry_id) AS counts) <> 20 THEN
                    RAISE EXCEPTION 'Synthetic tag-search dataset assertion failed';
                END IF;
            END $$;

            ANALYZE users;
            ANALYZE file_entries;
            ANALYZE tags;
            ANALYZE entry_tags;
            """,
            connection)
        {
            CommandTimeout = 300,
        };
        command.Parameters.AddWithValue("actor", ActorId);
        command.Parameters.AddWithValue("root", RootId);
        command.Parameters.AddWithValue("shared_owner", SharedOwnerId);
        command.Parameters.AddWithValue("shared_root", SharedRootId);
        command.Parameters.AddWithValue("root_path", $"users/{ActorId:N}/files");
        command.Parameters.AddWithValue("shared_root_path", $"users/{SharedOwnerId:N}/files");
        command.Parameters.AddWithValue("tag_ids", AllTagIds);
        command.Parameters.AddWithValue("attached_tag_ids", AttachedTagIds);
        command.Parameters.AddWithValue("first_tag", SelectedTagIds[0]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ExplainTagIntersectionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT)
            SELECT relation.entry_id
            FROM entry_tags AS relation
            WHERE relation.tag_id = ANY(@tag_ids)
            GROUP BY relation.entry_id
            HAVING count(DISTINCT relation.tag_id) = 10
            LIMIT 100;
            """,
            connection)
        {
            CommandTimeout = 30,
        };
        command.Parameters.AddWithValue("tag_ids", SelectedTagIds);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }
}
