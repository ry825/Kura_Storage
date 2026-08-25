# Search API PR1 performance record

## Scope and environment

- Date: 2026-08-25 (Australia/Melbourne)
- Server: Raspberry Pi 4 Model B Rev 1.4, ARM64, 4 CPU cores, 8,009,612 KiB physical memory
- Database: PostgreSQL 17 on the Raspberry Pi, using a dedicated disposable database
- Dataset: exactly 300,000 synthetic `file_entries` across 10 synthetic users, including owned files, folders, direct shares, inherited folder shares, and `MISSING_CANDIDATE` / `MISSING` states
- Execution path: `SearchService` through `PostgreSqlSearchRepository`; the production 10-second command timeout remained enabled
- Search text, file names, user identifiers, database credentials, host addresses, and physical paths are intentionally excluded from this record.

## Method and result

The fixed suite contained 20 representative searches covering contains and short-prefix matching, entry type, every file category, state, size, updated time, owner, share target, combined filters, and a later page. Each case ran once for warm-up and three measured times, producing 60 samples.

| Metric | Result |
| --- | ---: |
| Samples | 60 |
| Successful samples | 60 |
| Error rate | 0% |
| p50 | 480 ms |
| p95 | 1,475 ms |
| Maximum | 3,752 ms |
| Acceptance threshold | p95 < 2,000 ms |

The p95 requirement passed. The maximum belonged to the deliberately expensive later-page case; it did not move the fixed workload's p95 above the acceptance threshold.

An initial Raspberry Pi run measured p95 4,863 ms. The cause was PostgreSQL materializing the reusable eligible-entry CTE for all 300,000 rows before applying ownership and share access. Marking the metadata and eligible-entry CTEs `NOT MATERIALIZED` allowed ownership and shared-tree predicates to reach the indexed base table. The corrected implementation produced the result above. Short-prefix matching was also expressed with `starts_with`, allowing PostgreSQL to derive the prefix range from the B-tree index rather than choosing the trigram index for a one-character prefix.

## Index and migration observations

| Index | Size |
| --- | ---: |
| `ix_file_entries_lower_name_trgm` | 17,932,288 bytes |
| `ix_file_entries_lower_name_prefix_id` | 36,397,056 bytes |
| `ix_file_entries_owner_parent_status_updated_at` | 2,646,016 bytes |
| `IX_file_entries_parent_id` | 2,572,288 bytes |
| `ux_file_entries_managed_owner_path` | 51,396,608 bytes |

The two PR1 indexes were dropped only from the disposable database and recreated with `CREATE INDEX CONCURRENTLY` against the 300,000-row dataset. Prefix-index creation took 2 seconds and trigram-index creation took 9 seconds. The database still contained exactly 300,000 entries and both indexes after recreation.

## Query-plan observations

`EXPLAIN (ANALYZE, BUFFERS)` was run with synthetic, non-user data after warm-up.

| Path | Observed plan | Execution | Spill / unnecessary scan |
| --- | --- | ---: | --- |
| Selective contains | Bitmap Index Scan on `ix_file_entries_lower_name_trgm` | 0.672 ms | No temp spill |
| One-character prefix | Bitmap Index Scan on `ix_file_entries_lower_name_prefix_id` | 0.116 ms | No temp spill |
| Inherited share traversal | Primary-key lookup for share roots and Bitmap Index Scan on `ix_file_entries_owner_parent_status_updated_at` for descendants | 74.357 ms | No temp spill; sequential scans were limited to the synthetic 54-row share and 45-row member relations |

The final query projects only response columns, bounds recursive descent to 64 levels with cycle detection, and does not access physical storage. No plan wrote temporary blocks.

## Reproduction and cleanup boundary

- `server/tests/performance/datasets/search-seed.sql` creates the fixed anonymous dataset only after its dedicated-database confirmation value is supplied.
- `server/tests/performance/k6/search.js` defines the public-API workload, warm-up, error-rate threshold, and p95 threshold.
- `SearchPerformanceTests` runs the repository-level 20-case measurement only when `KURASTORAGE_SEARCH_PERF_CONNECTION` points to a prepared dedicated database.
- The Raspberry Pi database, transferred schema/seed, and temporary ARM64 runner are disposable PR1 test assets and are removed after the remaining PR1 verification is complete.
