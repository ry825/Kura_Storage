# MISSING management Raspberry Pi and Android E2E (2026-08-22)

## Scope and environment

- Raspberry Pi 4, PostgreSQL, systemd API/Worker, Nginx, and the dedicated exFAT HDD were tested with Protocol 2.
- A signed non-debuggable Android release was installed on a physical Android device and used through the local-direct route.
- A dedicated member account and isolated test tree were used. Credentials, user identifiers, file identifiers, host addresses, and physical paths are not recorded here.
- PostgreSQL and the Storage Root were backed up before rollout. The archive was readable, and the deployment upgrade created an additional pre-migration database backup.
- Indexing was enabled through the Worker `Indexing__Enabled=true` systemd environment override only after the Android Protocol 2 build, API, and Worker were deployed and the aggregate dry-run reported no changes or errors.

## MISSING state and API results

1. Two uploaded files were removed outside KuraStorage. A startup APPLY scan marked both `MISSING_CANDIDATE`; the Android list displayed them as files being checked.
2. A distinct APPLY scan after the five-minute confirmation delay changed both entries to `MISSING`. No single inotify observation confirmed `MISSING`.
3. Restoring one physical file and invoking explicit recheck returned `ACTIVE`, preserved the same file ID, and produced a byte-identical download.
4. Recheck and index deletion against an entry owned by another user both returned `404 FILE_NOT_FOUND`, without returning its metadata.
5. For the remaining `MISSING` entry, the HDD mount was detached from both the host and the API service mount namespace. Health returned HTTP 200 with `storage=UNAVAILABLE`.
6. While storage was unavailable, index-only deletion returned HTTP 204 and reduced the database `MISSING` count by exactly one. The operation did not inspect or mutate the HDD. The same Storage ID was verified after remount, and API and Worker returned to active state.
7. Android refresh removed the index-deleted entry, showed the revived entry normally, and showed an externally deleted folder as being checked. After delayed confirmation it displayed the missing state and its recheck/index-only actions.

## Existing operation and external-change regression

- Multipart upload, full download and byte comparison, HTTP Range (`206`), folder creation, rename, move, trash listing, restore, and permanent purge succeeded.
- A 300,000-byte resumable upload was split at byte 262,144. The authoritative session reported the resumed offset, completion succeeded, and the downloaded bytes matched.
- External file rename and folder move preserved the same ID. External content update increased `fileVersion`. External folder deletion produced candidates for the folder and descendants.
- A file and folder created while the Worker was stopped were discovered by the startup scan after restart.
- A 201-entry event burst converged in 7,614 ms. An authenticated list request during the burst completed in 0.048 seconds.
- API restart, Worker restart, missing confirmation scan, and HDD detach/reconnect all converged without a running scan or unfinished file operation remaining.

## Failure injection and corrective finding

Running API rename/move/trash immediately beside inotify reconciliation exposed an optimistic catalog race after the filesystem mutation was already durable. Before correction it escaped as HTTP 500. The API now returns the stable recovery-required conflict while leaving the operation at `FILESYSTEM_DONE`; startup recovery reconciles it from the HDD. Unit regression tests cover rename, trash, and restore at that boundary, and the physical-device run confirmed a 409 rather than a 500 before the normal debounced operation path completed successfully.

The first HDD-detach attempt also showed that a running hardened service retains its own mount namespace and that the systemd `Requires=` relationship automatically restores the host mount. The successful test temporarily runtime-masked the mount unit, detached the mount from both namespaces, and used an exit trap to unmask, remount, verify identity, and restart services. No persistent unit change was made.

## Validation and cleanup

- Server, Android, configuration, security, deployment, formatting, contract, and connected-device suites were run after the final code changes; final counts are recorded in the steering completion record.
- Dedicated physical test content, database entries, device sessions, credentials, and temporary files were removed after verification. Production content was not modified.
- Final checks confirmed the expected Storage ID, active API/Worker, no running scan, no unfinished file operation, and enabled indexing.
