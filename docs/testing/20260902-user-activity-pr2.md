# User Activity PR2 verification

Date: 2026-09-02

## Scope

This record covers the permission-aware user activity API, opaque keyset
pagination, the local administrator search, Security Audit recording, and the
query index added in PR2. All generated users, entries, names, IDs, and activity
details were synthetic.

## Automated verification

- `ActivityQueryServiceTests`: cursor round-trip and corruption handling, public
  response mapping, type/page-size validation, Admin filter validation, CLI
  parsing, table/JSON escaping, cancellation, and broken-pipe propagation.
- `UserActivityQueryTests`: actor, owner, direct/inherited and multiple-path
  sharing, role-independent authorization, move, trash/restore, revocation,
  purged snapshots, keyset/type filters, invalid requests, authentication, Admin
  combined filters, and redacted audit summaries.
- `UserActivityMigrationTests`: migration up/down/re-up, the global activity
  ordering index, original activity constraints/indexes, and existing Audit and
  Share preservation.
- `OpenApiContractTests`: `/activities`, schemas, pagination, visibility notes,
  and stable validation errors.

The final Release suites completed with zero build warnings: Domain 119,
Application 332, and Integration 217 tests (668 total), with no failures or
skips. Merged Domain/Application line coverage is 89.53% overall; Domain is
92.69% and Application is 88.81%. New Application query files are individually
covered at 95% or higher (`ActivityCursorCodec` 100%, contracts 100%, query
service 97.83%, Admin parser/output 95.71%). The PostgreSQL permission/Admin
query repositories have 99.17% line, 92.42% branch, and 100% method coverage
after their general/Admin class split.

## 300,000-entry / 1,000,000-activity measurement

Command:

```bash
KURASTORAGE_RUN_USER_ACTIVITY_PERF=1 dotnet test \
  server/tests/KuraStorage.IntegrationTests/KuraStorage.IntegrationTests.csproj \
  --no-build --filter FullyQualifiedName~UserActivityPerformanceTests
```

Environment: PostgreSQL 17 Alpine Testcontainer on the development host (14.98
GiB total host memory). The seed contains 10 users, 300,000 file entries,
1,000,000 activities across all five types, current ownership/sharing, activity
that remains after sharing is absent, and purged target snapshots.

Measured result:

- seed: 171,429 ms;
- insert sample: 1,000 rows at 48.5 microseconds per row;
- `user_activities` table: 248,455,168 bytes;
- `user_activities` indexes: 551,567,360 bytes;
- total relation: 800,022,528 bytes;
- logical row backup estimate: 237,305,608 bytes;
- maximum p50 across the measured query paths: 241.1 ms;
- maximum p95 across the measured query paths: 269.1 ms;
- test process CPU across the repeated query phase: 1,089.1 ms;
- test process working set after the query phase: 195,588,096 bytes.

Each of these paths was executed ten times: user first page, user following
page, user type filter, Admin actor, Admin owner, Admin type/date, and Admin file
filter. `EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT)` was additionally executed for
user first/following/type and Admin actor/owner/type/date/file plans. The test
requires buffer data to be present and fails when any path's p95 reaches 2,000
ms. No offset pagination, HDD traversal, result-body logging, or real user data
is involved.

## Result

The measured maximum p95 was 269.1 ms, within the normal two-second acceptance
target. The global `(occurred_at DESC, id DESC)` index supports unfiltered
newest-first traversal while SQL re-evaluates current permission before the
bounded page is returned.

Repository checks also passed: `verify-config.sh`, `verify-server.sh`,
`verify-security.sh`, `verify-deployment.sh`, `dotnet format`, EF Core pending
model detection, OpenAPI contract tests, and `git diff --check`. Config and
deployment checks used temporary ShellCheck/Nginx tools; restricted kernel
ruleset and listen-socket access followed the scripts' documented fallback.
