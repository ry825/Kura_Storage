# Android automatic backup PR5 verification

Date: 2026-09-03

## Scope and privacy

This record covers the settings, rule, allowed-Wi-Fi, progress, and history UI;
account-scoped runtime wiring; Android connected behavior; Raspberry Pi API and
HDD behavior; performance; and release/security checks for the final automatic
backup pull request.

Repository records use only anonymous labels and aggregate measurements. Actual
credentials, tokens, endpoint addresses, SSIDs, BSSIDs, SAF URIs, device document
keys, account identifiers, physical paths, and file content are excluded.

## UI and Android verification

- Settings navigation reaches backup status/history, rules, and allowed external
  Wi-Fi without introducing feature-to-feature dependencies.
- Rule editing supports MediaStore or SAF sources, the personal server root or an
  allowed folder, enabled state, both network modes, initial charging, and minimum
  battery. The UI continuously explains one-way behavior and recovery by
  reselecting unavailable sources or destinations.
- Current Wi-Fi registration requires a separate explicit confirmation. Permission
  denial remains fail-closed and links to the relevant permission or app settings.
- Progress exposes last success, lifecycle counts, rule counts, typed wait reasons,
  run-now, pause/resume, and bounded failure retry. History distinguishes completed,
  failed, waiting, uploading, and locally missing items without displaying physical
  paths or opaque document keys.
- Compose connected tests verify text status labels, click semantics, explicit Wi-Fi
  confirmation, 48 dp-or-larger Material controls, and font scale 2.0 under the dark
  color scheme. Status meaning is present as text and semantics rather than color or
  icon alone.
- On an Android 13 physical device, the release app selected an anonymous SAF tree
  and the personal server root, created a rule, uploaded new and changed content,
  retained history after force-stop/reopen, and showed a removed local source as
  `Local missing` without removing the server copy.
- Android 13 connected suites passed 31 tests: `core-database` 10, `core-data` 10,
  `feature-backup` 7, and `app` 4.
- Android 10 API 29 Google APIs x86_64 connected suites passed 34 tests:
  `core-database` 11, `core-data` 10, `feature-backup` 9, and `app` 4. This
  exercises the pre-API-30 MediaStore path, pre-Android-13 media permission,
  Room persistence, WorkManager, Compose accessibility states, and app wiring.
- Android 16 API 36 Google APIs x86_64 connected suites passed the same 34 tests
  on the final diff. The Emulator entered forced deep Doze (`IDLE`) without a
  KuraStorage service running, then returned to light/deep `ACTIVE`; WorkManager
  Test Driver coverage verifies deferred retry and unique continuation behavior.
- JVM boundary tests verify that a transfer run claims at most 100 items, defers
  the next item at the 2 GiB budget, stops at the 20-minute deadline, persists
  the confirmed chunk offset, and reports remaining work for the next unique
  WorkManager continuation. The foreground threshold is 100 items or 100 MiB;
  smaller work does not use an always-on foreground service.

## Raspberry Pi API and HDD verification

- An authenticated release build used local-direct HTTPS against the Raspberry Pi
  and real HDD. New content produced one file and one receipt at version 1; changed
  content converged both to version 2; an unchanged rerun left both at version 2.
- Removing the source from the Android device left the active server file unchanged.
  Earlier API E2E also retained favorite, tag, recent, and file identity across a
  changed backup and verified idempotent completion and pending-session convergence.
- HDD unavailable testing failed closed and left database aggregates unchanged.
- Release candidate `0.12.0-backup-pr5-rc3` created a pre-upgrade database
  backup, applied the already-current migration set, passed deployment health
  verification, and restored API, Worker, Nginx, PostgreSQL, and the HDD mount
  after an unmount/remount failure test.
- Nginx, API, and PostgreSQL were then restarted individually. An immediate
  authenticated request observed the expected transient 502 while API startup
  completed; the repeated authenticated backup E2E subsequently passed in full,
  with API, Worker, Nginx, and PostgreSQL all active.
- A release-candidate observation found that a managed upload's filesystem mtime
  was initially recorded from the service clock. The index worker could therefore
  classify KuraStorage's own write as an external change and advance only the file
  version. The final implementation inspects the published file, stores its actual
  size, MIME, mtime, and source key at PostgreSQL microsecond precision, and applies
  the same rule in normal and recovery paths. Integration tests run an apply-mode
  index scan after both normal changed completion and filesystem-done recovery and
  verify that file and receipt versions remain equal.
- After the rc3 rollout and enabled index Worker startup, the new rc3 E2E
  receipt remained at version 2 with its file at version 2. Four anonymous
  historical rc1 fixtures still preserve the previously observed mismatch and
  were deliberately not rewritten or deleted by this verification.

## Performance measurements

The existing anonymous scanner fixture covers 10,000 items with 10 changed 1 MiB
streams. The process-level RSS includes Gradle and the test JVM.

| Measurement | Result |
| --- | ---: |
| 10,000-item initial scan with 1 KiB streams | 1,032 ms |
| Initial scan streams / bytes read | 10,000 / 10,240,000 bytes |
| 10,000-item incremental scan | 57 ms |
| Content streams opened | 10 |
| Bytes read for checksums | 10 MiB |
| Room-equivalent batches | 20 x 500 items |
| Process maximum RSS | 119,468 KiB |
| Unchanged item content reads | 0 |
| API 29 Room insertion, 10,000 rows / 20 batches | 1,001 ms |
| API 29 closed Room database size | 7,622,656 bytes |
| API 29 Emulator battery around the 10,000-row Room test | 100% to 100% (AC powered; functional observation only) |

The Emulator battery reading is not a physical-device energy benchmark and no
energy-consumption conclusion is drawn from the unchanged percentage.

Real Raspberry Pi measurements used anonymous requests over validated HTTPS.

| Measurement | Result |
| --- | ---: |
| Compare, 100 candidates, 20 sequential requests | 111.43 ms mean / 170.17 ms p95 / 314.64 ms max |
| Compare, 10 concurrent requests x 100 candidates | 409.74 ms total wall time |
| 16 MiB upload, four 4 MiB chunks, run 1 | 4.293 s / 3.73 MiB/s |
| 16 MiB upload, four 4 MiB chunks, run 2 | 4.747 s / 3.37 MiB/s |
| API CPU during measured run 2 | 346 scheduler ticks (approximately 3.46 CPU seconds at 100 Hz) |
| HDD sectors during measured run 2 | 0 read / 33,185 written (approximately 16.2 MiB at 512-byte sectors) |
| `backup_receipts`, 11-row live fixture | 98,304 bytes total / 8,192 bytes table heap |

PostgreSQL `EXPLAIN` selected the
`ux_backup_receipts_user_device_document` index for the user, device, and local
document key lookup. The live fixture also contained the device, remote-file,
compare, primary-key, and receipt uniqueness indexes expected by the migration.

## Security and release verification

- Token responses include server-authenticated `userId` and `deviceId`; Android
  combines them with the TLS-validated server identity for Room and Work scope.
  The server continues to derive authorization identity from the access token and
  server-side session rather than trusting these values in a backup request.
- Release construction verifies a non-debuggable, v3-signed APK. Android Auto Backup
  excludes the Room database, WAL, and shared-memory files.
- Automated contract and integration tests reject invalid paths, malformed or
  duplicate document keys, another user/device, revoked permission, stale versions,
  oversized comparison input, and inconsistent server responses.
- Final repository-diff scans found no real endpoint or E2E credential. The rc3
  artifacts contained no E2E credential, and the post-rollout API/Worker journal
  scan found no token, document key, SSID/BSSID, content URI, or managed physical
  path. The release APK necessarily contains the public build-time hostname and
  route addresses used by its validated connection configuration; it contains no
  associated secret or credential.

## Final verification commands

The final pull-request run completed the repository gates after implementation and
documentation changes:

- `./scripts/ci/verify-android.sh`
- `./scripts/ci/verify-server.sh`
- `./scripts/ci/verify-config.sh`
- `./scripts/ci/verify-security.sh`
- `./scripts/ci/verify-deployment.sh`
- `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`
- Android 10 API 29, Android 16 API 36, and Android 13 physical-device connected suites
- OpenAPI, migration, SBOM, lint, detekt, ktlint, coverage, release build, and
  `git diff --check`

Results: Android verification passed in 5 minutes 28 seconds; Server verification
passed with 130 Domain, 336 Application, and 224 PostgreSQL Integration tests;
Config, Security, Deployment, .NET format, and `git diff --check` passed. The API
29 and API 36 final connected runs each passed 34 tests. The rc3 release APK was
non-debuggable, signed with APK Signature Scheme v3, and used version code 14.
