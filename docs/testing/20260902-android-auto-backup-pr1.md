# Android automatic backup PR1 verification

Date: 2026-09-02

## Scope

This record covers the server-side `BackupReceipt`, authenticated-device
candidate comparison, Backup context on existing Upload Sessions, and atomic
`NEW` / `CHANGED` completion. Android Room, scanning, network policy,
WorkManager, and UI remain in later pull requests.

All users, devices, paths, document keys, file contents, and credentials used
by the tests were synthetic. PostgreSQL 17 Testcontainers and the disposable
test storage root were used; no production data was read or recorded.

## Automated verification

The final Release verification completed with zero build warnings:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 130 | 0 | 0 |
| Application | 336 | 0 | 0 |
| Integration | 224 | 0 | 0 |

Backup-specific tests cover metadata and path invariants, deterministic
`NEW` / `CHANGED` / `ALREADY_UPLOADED` / `BLOCKED_CURRENT_STATE`
classification, request bounds, duplicate keys, inactive devices, destination
and remote-file permission checks, pending-session uniqueness, cancellation,
checksum-protected publication, stale versions, repeated completion, shared
folder ownership, permission downgrade during transfer, filesystem-done
recovery, retained Favorite / Tag / Recent / Share relationships, Backup user
activity, and immutable text-version history.

Merged Coverlet line coverage for Domain and Application is 7,644/8,544
(89.47%). The new Backup Domain and Application files are 241/246 (97.97%):
`BackupDocumentMetadata` 100%, `BackupReceipt` 95.38%, `BackupUploadContext`
96%, `BackupCompareService` 100%, and Backup contracts 96.30%.

## Migration and consistency

`AddBackupReceipts` passed Up, Down, and re-Up against PostgreSQL 17. The test
verifies the seven Upload Session Backup columns, Receipt uniqueness, foreign
keys and purge cascade, Device revocation preservation, and existing File,
Upload Session, Share, and User Activity rows. EF Core reported no pending model
changes after the migration and model snapshot were generated.

`CHANGED` completion keeps the File ID, parent, name, owner, sharing, Favorite,
Tag, and Recent state while incrementing the version once. The existing
FileOperation journal owns filesystem recovery; FileEntry, UserActivity,
Receipt, and completion state are committed in one database transaction.
Cancelled, incomplete, permission-revoked, stale-version, and failed recovery
paths do not advance the Receipt.

## Repository verification

- `./scripts/ci/verify-server.sh` passed formatting, Release build, all 690
  tests, and required server artifact checks.
- `./scripts/ci/verify-config.sh`, `./scripts/ci/verify-security.sh`, and
  `./scripts/ci/verify-deployment.sh` passed.
- OpenAPI contract tests and EF Core pending-model detection passed.
- Config and deployment checks used temporary ShellCheck and Nginx tools. The
  scripts accepted their documented restricted-environment paths for systemd
  service metadata, kernel ruleset access, and listen-socket access.
- Review found no Backup document key, relative path, file name, physical path,
  token, or user input in log or metric labels. Backup metrics use only bounded
  result and decision values.
- `git diff --check` passed.
