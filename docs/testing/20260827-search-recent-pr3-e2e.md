# Search and recent files PR3 release verification

## Scope and privacy boundary

- Date: 2026-08-27 (Australia/Melbourne)
- Release: Server and signed Android Release `0.7.0-pr3.1`
- Server target: Raspberry Pi 4 Model B Rev 1.4, ARM64, 4 CPU cores, 8,009,612 KiB physical memory
- Android target: OPPO CPH2333, Android 13 / API 33
- Performance data used a dedicated disposable PostgreSQL database containing exactly 300,000 synthetic entries and 10 synthetic users.
- Functional E2E data uses a PR3-only limited identifier and is removed at the end of the run.
- Search text, file and user names, identifiers, host addresses, physical paths, tokens, and credentials are intentionally excluded.

## Rollout protection and result

- A matched PostgreSQL custom-format dump and Storage Root archive were taken before the Search and Recent migrations. Both archive listings were readable before rollout, and the files remain root-owned with restricted group read access.
- The Storage identity matched the configured value, the exFAT volume was mounted read-write with `nosuid`, `nodev`, and `noexec`, and no unfinished file operation or active upload session existed before rollout.
- Search migration, Recent migration, API, Worker, and signed Android installation completed in the planned order. The Android-only response-mapping correction was then released as `0.7.0-pr3.1`; the Server artifact was redeployed under the same final release identifier with no additional migration pending.
- Post-rollout verification found API, Worker, PostgreSQL, and Nginx active; health protocol version 2; API and Storage available; `pg_trgm` 1.6; and an initially empty Recent table.
- The final production-equivalent database size was 51,140,275 bytes. Empty-table index sizes were 16,384 bytes for the name-prefix index, 49,152 bytes for the trigram index, and 8,192 bytes for each Recent secondary index.
- Search indexes are non-transactional concurrent migration operations. Rollback therefore requires stopping writers and restoring the matched database and Storage backup together; schema-only rollback after accepted writes is not treated as safe.

## Raspberry Pi search performance

The fixed suite covered 20 representative permission and filter combinations. It included owned, directly shared, inherited, multiple-path, and missing-state data; short prefix and trigram contains matching; filter-only requests; owner and share-target filters; combined filters; and a later page. The first pass immediately followed seed and analyze. It represents cold application/query execution but does not claim an operating-system page-cache drop, which was intentionally avoided on the production-equivalent host. Three subsequent iterations produced the warm result.

| Metric | First pass | Warm |
| --- | ---: | ---: |
| Samples | 20 | 60 |
| p50 | 576 ms | 486 ms |
| p95 | 3,882 ms | 1,509 ms |
| Maximum | 4,054 ms | 4,072 ms |
| Errors | 0 | 0 |

The normal warm p95 requirement of less than 2,000 ms passed. The complete run took 65 seconds. Peak observations were two dedicated-database connections, 107% runner CPU, 99% PostgreSQL CPU, 112,700 KiB runner RSS, 178,556 KiB PostgreSQL RSS, and 7,051,044 KiB minimum system memory available. CPU percentages can exceed 100% for a multi-threaded process on this four-core host.

| Index | Size | Observation |
| --- | ---: | --- |
| Name prefix | 36,397,056 bytes | `Bitmap Index Scan` confirmed with `EXPLAIN (ANALYZE, BUFFERS)` |
| Name trigram | 17,932,288 bytes | `Bitmap Index Scan` confirmed with `EXPLAIN (ANALYZE, BUFFERS)` |
| Owner/parent/status/update | 2,646,016 bytes | 2,936 scans observed during the fixed suite |
| Parent | 2,572,288 bytes | Available for bounded hierarchy traversal |

The prior same-schema 300,000-row migration rehearsal created the prefix index in 2 seconds and the trigram index in 9 seconds. The final query kept metadata and eligible-entry CTEs non-materialized, bounded recursive authorization traversal to 64 levels with cycle detection, and showed no need for an additional query or index change.

## Functional E2E

The final Server artifact was exercised through the public HTTPS endpoint against the real PostgreSQL catalog and exFAT Storage Root.

- Owner, Viewer, Contributor, Editor, Manager, Admin, and an unshared user produced the expected visibility boundary. Admin received no implicit cross-user result. Direct, inherited, nested-ancestor, and direct-plus-inherited paths resolved to the strongest current permission and its deterministic source.
- Name, entry type, category, inclusive updated-time and size ranges, owner, effective share target, status, combined filters, and stable one-item pagination returned the expected authorized counts.
- Rename, move, trash, restore, permission change, share removal and restoration, and purge were reflected by the next Search and Recent request. A revoked user received neither Search nor Recent metadata and could not fetch stale detail.
- The two-scan missing-file transition produced `MISSING_CANDIDATE` and then `MISSING`. Both states remained visible as metadata to authorized users, while recording the non-active file was rejected. Restoring the same payload revived the same catalog entry as `ACTIVE`.
- Reopening a file kept one Recent row and advanced its server timestamp. Histories remained user-specific; folders and missing files were rejected; temporary revocation hid the retained row; restored access exposed it at its prior time; and purge cascaded the history.
- Ten concurrent Search requests and ten concurrent Recent writes completed successfully while a share permission changed. API, Worker, and PostgreSQL restart tests converged to the current permission with all services active afterward.
- Nginx, API, Worker, and PostgreSQL logs were checked after the run. No search query, functional test file/user value, physical path, authorization token, or credential pattern was found.

The Server functional data was purged through the API, then the seven limited test accounts and only their completed operation, upload, device, session, and audit rows were deleted with count assertions in one database transaction. The known empty test Storage directory was removed with `rmdir`. Final checks found zero limited test users, unfinished file operations, active upload sessions, orphan shares, and orphan Recent rows. PostgreSQL, Nginx, API, and Worker were active, and the configured Storage UUID, exFAT options, ownership, and identity marker all matched.

## Android device E2E

The final signed, non-debuggable Android Release was installed on the physical device and exercised with limited Owner and shared-user accounts. The APK used version code 8, APK Signature Scheme v3, the production release certificate fingerprint, the embedded private Root CA, and the configured HTTPS hostname.

- The LAN route was selected as `LOCAL_DIRECT`. Home navigation opened Search and Recent, and the signed application returned owned, directly shared, and inherited results with the expected owner, effective permission/source, entry type, category, size, update time, status, and human-readable share source.
- The saved private-network client was then enabled and Wi-Fi was temporarily removed while the mobile underlay remained available. The application selected `REMOTE_SECURE`; sign-in, Search, and Recent succeeded through the private-network address while continuing to use the same HTTPS hostname, Root CA, authentication, and API contract. Wi-Fi and the private-network switch were restored to their original states afterward.
- Signed-release Search opened a Folder result and displayed both child files without adding a Folder/list/search-only Recent event. Opening a File detail added a Recent row. Opening a second File produced newest-first ordering, and reopening the first retained one row for that File while moving it to the front.
- Viewer, Contributor, Editor, and Manager changes were reflected after Refresh. Viewer exposed download only; Editor exposed the existing mutation controls but no sharing control; Manager exposed sharing and mutation controls. A stale Editor result was revalidated as Manager on open. After share removal, the stale result failed closed, did not navigate, and disappeared on Refresh. Admin and unshared-user isolation was also verified at the public API boundary.
- Revocation hid an existing Recent row without deleting its timestamp. Restoring access exposed the same row at its prior server time. Refresh now reloads share metadata as well as result data, so a share removed and recreated while the screen is open resolves to a readable share name rather than an internal identifier.
- A live Wi-Fi interruption caused Recent Refresh to retain the screen safely and show the result-unknown retry state. Re-enabling Wi-Fi and refreshing restored the same authorized result. Expired endpoint-specific authentication required sign-in again and did not expose cached data.
- The physical Server missing-file transition was combined with device instrumentation for `MISSING_CANDIDATE`/`MISSING` rendering and fail-closed open behavior. Trash, restore, purge, permission loss, pagination, empty results, long and one-to-two-character terms, Unicode and special characters, inclusive range filters, rotation, narrow screen, scrolling, keyboard/IME submission, and dialog behavior were covered by the connected physical-device suite.
- Personal and Shared navigation, upload/download, resumable upload cancellation/retry, rename, move, trash/restore/purge, missing-file controls, and storage-unavailable fail-closed behavior remained covered by the existing physical-device regression modules. The signed flow additionally rechecked existing file-detail capability controls. Storage identity and mount-state verification before and after the run confirmed that no fallback write reached the operating-system root.

During signed-release verification, two presentation defects were found and corrected: Owner permission from the wire is normalized to the existing capability model, and share-target identifiers are resolved to readable labels in Search, Recent, and the destination Folder browser. Unknown targets use a generic label and never expose the internal identifier. Targeted Search and Files instrumentation passed after these corrections.

## Final cleanup and environment state

The Android functional Folder was trashed and permanently purged through the Owner API. Purge removed its child files, share, and Recent references. Exactly two limited Android users and only their completed operation, session, device, audit, root-entry, and upload-session rows were then removed in a single database transaction. No production user, file, share, backup, or operational credential was selected by the cleanup predicates.

Final checks found zero limited Android users, unfinished file operations, active upload sessions, orphan shares, and orphan Recent rows. PostgreSQL, Nginx, API, and Worker were active. The Storage volume remained exFAT and mounted read-write with `nosuid`, `nodev`, and `noexec`; its configured identity and ownership checks remained valid. Temporary credentials and response captures were removed from the workstation after final verification.
