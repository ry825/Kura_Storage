# User Activity PR3 verification

Date: 2026-09-02

## Scope

This record covers the Android activity contract, repository, session-scoped
paging, Compose screen, navigation, physical-device UI verification, and the
final Raspberry Pi end-to-end exercise. All users, names, entries, IDs, and
credentials used by the exercise were synthetic.

## Automated Android verification

- `ActivityModelsTest` and `ActivityApiContractTest`: typed activity/detail
  mapping, public DTO/query shape, opaque cursor, and fail-closed unknown values.
- `ActivityRepositoryTest`: 401 refresh and one retry, cancellation, generation
  replacement, duplicate cursor protection, paging, and malformed response
  rejection.
- `ActivityViewModelTest`: loading, empty, paging, filter generation, duplicate
  load prevention, offline error, retry, and refresh recovery.
- `ActivityNavigationTest`: distinct session keys prevent one user's ViewModel
  and page state from being reused by another session.
- `ActivityScreenTest`: all five typed operations and details, filters, paging,
  refresh, accessible open action, non-openable purged snapshots, loading,
  empty, error, unknown activity, screenshot capture, and font scale 2.0.

`./scripts/ci/verify-android.sh` completed 1,206 Gradle tasks successfully. It
assembled the app and all test APKs, ran unit tests and coverage verification,
generated the direct dependency SBOM, and passed ktlint, detekt, and Android
lint. `git diff --check` also passed. The activity screen's two instrumented
tests completed on an OPPO CPH2333 running Android 13 in 5.366 seconds with no
failures. The test-only host activity enables screen-on behavior only in the
Android test manifest and is absent from the production manifest.

## Release and physical-device checks

The Raspberry Pi was backed up before deployment with matched storage and
PostgreSQL snapshots. Both archives were checksum/list/restore-structure
verified, kept owner-restricted, and retained under `/var/backups/kurastorage`.
The deployment applied `AddUserActivities` and `AddUserActivityQueryIndex`, then
passed service, mount, migration, and health verification as
`0.11.0-activity-pr3-rc1`.

The Android release was built as version code 16 with the production CA and
release signing material. APK signature, non-debuggable manifest, version, and
upgrade installation were verified before installing it on the physical
device. With ZeroTier disabled, the device correctly reported the server as
unreachable from a different Wi-Fi subnet. With the KuraStorage ZeroTier
network enabled, health detection reached the server and the fresh install
correctly refused remote device registration with `Local connection required`.
This demonstrates both network selection and the local-only registration
boundary. The current physical location did not provide a Wi-Fi subnet shared
with the Pi, so credentials were not bypassed or injected into the signed app.
The full activity UI was therefore exercised on the same device by the
instrumented tests, while authenticated data and authorization behavior were
exercised against the deployed API as described below.

After verification, font scale was restored to 1.0, Wi-Fi remained enabled,
ZeroTier was disabled, and the test package was uninstalled. The signed release
application remains installed.

## Raspberry Pi end-to-end result

The deployed API exercise produced Upload, no-op/retry Move, real Move, text
Edit, Share create, Share revoke, Trash, and Purge operations. The owner's final
newest-first result was exactly Delete/Purged, Delete/Trashed, Share/Revoked,
Share/Created, Edit, Move, and Upload. There was exactly one Move and one Upload,
and the purged entry retained only its safe snapshot with no openable target ID.
Type filtering and following-page cursor behavior also passed.

While sharing was active, the recipient saw Share, Edit, Move, and Upload rows;
an unrelated user saw none. After revocation, the recipient saw none. The same
exercise passed LAN access, token refresh, API process termination and recovery,
logout invalidation, and device revocation. The local Admin CLI returned the
same seven owner rows, and its search produced an `ACTIVITY_SEARCH` Security
Audit record. API, Worker, and Nginx logs contained no synthetic username
marker or result content.

## Cleanup

The exercise created four independent sets totaling 12 synthetic users, 28
activity rows, and 12 remaining file entries. Under stopped API/Worker services,
the exact prefixed records and their storage directories were removed; sessions
and devices were revoked and the synthetic users disabled. A post-cleanup query
confirmed zero active synthetic users, zero related activity rows, and zero
related file entries. API, Worker, PostgreSQL, and the storage mount were then
verified healthy. Credential state, temporary release files, transfer stages,
failed stages, and the cleanup script were removed. Production backup archives
and non-test data were not changed.
