# External indexing Raspberry Pi verification — 2026-08-22

## Scope and environment

PR2 was deployed as the temporary `0.5.0-pr2.5` ARM64 build on a Raspberry Pi 4
Model B Rev 1.4 (4 cores, 7.64 GiB RAM), Linux `6.6.74+rpt-rpi-v8`, PostgreSQL,
Nginx, and the production exFAT HDD mounted read-write with `noexec` and
`noatime`. The Worker systemd unit had `LimitNOFILE=65536` and a 45-second stop
timeout. The kernel exposed 61,621 user watches and 16,384 queued events. The
watch value is below the documented 65,536 recommendation, so deployment emits
a warning and does not change sysctl automatically.

Before deployment, a PostgreSQL custom-format dump and a compressed Storage
Root archive were created. Their SHA-256 manifest was verified, `pg_restore
--list` read the database dump, and the archive table of contents was readable.
The migration, API, Worker, Nginx, PostgreSQL, HDD mount, read-only guard, and
Storage ID then passed the production deployment verifier.

## Functional recovery matrix

| Scenario | Result |
| --- | --- |
| External file and folder create | New entries became `ACTIVE`. |
| Content update | Metadata changed and the file version increased. |
| Rename and folder move | IDs were retained; placement-only changes did not increase the file version; descendant paths followed the folder. |
| Delete and later recreation | The entry moved through `MISSING_CANDIDATE` and `MISSING`, then the same ID became `ACTIVE` after rediscovery. |
| Worker stopped while a file was added | The startup scan indexed the missed file after restart. |
| inotify burst/overflow | Bounded event processing requested completed `OVERFLOW` scans and converged without an unbounded queue. |
| HDD unmount/remount | The scan returned the storage-unavailable result, existing entries were not mass-marked missing, and the same Storage ID recovered after remount. |
| PostgreSQL outage | The Worker survived the transient failure and indexed the pending file after PostgreSQL returned. |
| Worker restart | Graceful stop/restart closed the watcher and startup reconciliation recovered missed changes. |

The native folder-move test also found and fixed a watch-rebase defect: a
paired directory move had rebased the watch and then removed it again on
`IN_MOVE_SELF`. The regression test now verifies that changes below the moved
folder remain observable. Burst testing also exposed an event/scan insert race;
PostgreSQL unique violations are normalized to a reconciliation retry, and a
stale `RUNNING` scan is recovered as `FAILED` with `WORKER_INTERRUPTED` before
the next apply scan.

## Reproducible scale measurement

A dedicated, disposable directory was populated with 10,000 empty files. This
is a reproducible scale fixture rather than a claim that 10,000 files are equal
to the planned 300,000-file catalog. The 10,001-entry measurement includes the
fixture folder. A bounded batch size of 500 and event queue capacity of 4,096
were used.

| Measurement | Result |
| --- | ---: |
| Physical creation time | 35.274 s |
| Event start to 10,001 indexed entries | 206.516 s |
| Post-creation convergence portion | 171.242 s |
| Worker maximum RSS | 228,144 KiB (222.8 MiB) |
| Worker accounted HDD bytes during event run | 16,384 bytes |
| PostgreSQL transaction commits | +27,051 |
| PostgreSQL tuples inserted / updated | +36,173 / +24,096 |
| Full dry-run wall time | 141.751 s |
| Dry-run user / system CPU | 23.162 s / 1.518 s |
| Dry-run average CPU | 17.4% of one core (4.4% of four-core capacity) |
| Dry-run maximum RSS | 158,236 KiB (154.5 MiB) |
| Dry-run accounted HDD bytes | 0 bytes (page-cache and exFAT accounting apply) |

The Worker remained bounded and completed the overflow-triggered recovery
scans. The database counters include watcher events, scan staging, scan-run
updates, and polling, so they describe the whole convergence workload rather
than only final catalog inserts.

During the create burst, 80 health requests had zero failures, averaged 3.80 ms,
and reached 8.80 ms maximum. During the full dry-run, another 80 health requests
had zero failures, averaged 3.85 ms, and reached 44.68 ms maximum.

A separate authenticated dry-run concurrency check confirmed the scan process
was still active after all requests completed:

| API operation | HTTP | Elapsed |
| --- | ---: | ---: |
| List containing the scale fixture | 200 | 1.427 s |
| Multipart upload | 200 | 0.498 s |
| Download with byte-for-byte comparison | 200 | 0.066 s |
| Refresh-token rotation | 200 | 0.243 s |

After verification, all dedicated physical fixtures, catalog rows, operations,
and the temporary API user were removed. The final state had zero matching test
entries, zero unfinished operations, zero running scans, all production
services active, and `Indexing.Enabled=false` restored as required until PR3.
