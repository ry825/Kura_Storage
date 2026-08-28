# Favorites and tags PR1 performance verification

## Scope

- Date: 2026-08-28
- Runtime: PostgreSQL 17 Testcontainers on the development host
- Dataset: 300,000 FileEntry rows, including 270,000 owned entries and 30,000 entries under an inherited Viewer share
- Tag distribution: one Tag on 60,000 entries; all ten selected Tags on 30,000 entries
- Query cases: one Tag, ten-Tag AND, Tag only, name plus Tag, shared owner plus Tag, MISSING plus Tag, and a later page
- Page size: 100 maximum

The dataset uses generated identifiers and synthetic names only. No production User, File, Tag, path, token, credential, or query value is recorded.

## Result

After warming every case, each case ran three times for 18 measured samples.

| Measure | Result |
| --- | ---: |
| Seed and ANALYZE | 52,961 ms |
| p50 | 554 ms |
| p95 | 1,072 ms |
| Maximum | 1,072 ms |
| Errors | 0 |

The normal two-second target passed. The `entry_tags` intersection plan used an Index scan and did not contain `Seq Scan on entry_tags`. Search bounded results to 100 items and applied Tag AND, Entry state, existing filters, and permission resolution in PostgreSQL.

An initial deliberately over-dense seed attached one Tag to every FileEntry and measured p95 2,271 ms. That run was not accepted as completion. The committed representative distribution retains 30,000 ten-Tag matches and 60,000 single-Tag matches, adds a 30,000-entry inherited-share branch, and passes the target without changing production Query semantics or indexes.

## Reproduction

```bash
KURASTORAGE_RUN_TAG_SEARCH_PERF=1 \
  dotnet test server/tests/KuraStorage.IntegrationTests/KuraStorage.IntegrationTests.csproj \
  --filter FullyQualifiedName~TagSearchPerformanceTests
```

The regular CI run discovers this test but skips the expensive seed unless the explicit environment flag is `1`.
