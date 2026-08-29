# Favorites and tags PR2 Raspberry Pi performance record

## Scope

- Date: 2026-08-29 (Australia/Melbourne)
- Runtime: Raspberry Pi 4 Model B, PostgreSQL 17, ARM64 Release runner
- Isolation: dedicated disposable database containing synthetic data only
- Dataset: 300,000 File entries, 10 Users, 200 Tags per User, 630,000 Entry-Tag relations, and a maximum of 20 Tags per Entry
- Permission paths: 270,000 owned entries and 30,000 entries under an inherited Viewer share
- Cases: one Tag, ten-Tag AND, Tag only, name plus Tag, shared owner plus Tag, MISSING plus Tag, and a later page
- Page size: 100 maximum

No production User, File, Tag, path, token, credential, query value, or response body was recorded. The production database and Storage Root were not used for the performance seed.

## Accepted result

Every case was warmed before three measured iterations. The 18 measured samples completed without an error.

| Measure | Result |
| --- | ---: |
| p50 | 740 ms |
| p95 | 1,948 ms |
| Maximum | 1,948 ms |
| Errors | 0 |
| Database size | 368,907,955 bytes |
| `entry_tags` primary index | 42,975,232 bytes |
| `entry_tags(entry_id, tag_id)` index | 39,043,072 bytes |

The normal two-second target passed. The plan used an Index Only Scan for the Tag relation path, bounded results to 100, and preserved the maximum depth 64 permission traversal. The same committed Testcontainers fixture passed on the development host with seed time 74,491 ms, p50 302 ms, p95／maximum 824 ms, and zero errors.

## Tuning result

The first Raspberry Pi run produced p95 2,707 ms. `EXPLAIN (ANALYZE, BUFFERS)` showed that the Tag intersection was expanded separately into the Owner and Shared permission branches and that several sorts spilled to temporary files.

The accepted implementation:

- materializes the Tag match set once per Tag-filtered request;
- materializes the filtered eligible set only when Tags are specified, while retaining the existing non-materialized path for Tag-free Search;
- reads a single Tag directly from the `(tag_id, entry_id)` key and reserves `GROUP BY/HAVING` for two to ten Tags; and
- applies `SET LOCAL work_mem = '16MB'` only inside the Tag Search Repeatable Read transaction.

Intermediate p95 values were 2,250 ms after removing duplicate Tag work and 2,029 ms after adding the single-Tag path without the transaction-local memory setting. The final configuration was accepted only after it passed the target.

## Cleanup

Before removal, count assertions confirmed 10 Users, 300,000 File entries, 2,000 Tags, 630,000 Entry-Tag relations, and a maximum of 20 Tags per Entry. The dedicated database, seed, and ARM64 runner were then removed. A final check confirmed that the disposable database no longer existed and PostgreSQL, Nginx, API, and Worker remained active.
