# Android automatic backup PR3 verification

Date: 2026-09-03

## Scope

This record covers MediaStore and SAF streaming scanners, stable source
identity mapping, metadata-first checksum selection, atomic Room queue
updates, completed-scan checkpoints, local-missing recovery, trigger
convergence, and rule-scoped in-flight scan sharing. Network policy,
WorkManager transfer execution, server comparison, and upload remain in PR4.

All provider authorities, content URIs, paths, names, account scopes, rule
identifiers, checksums, and file bytes used by automated tests are anonymous
fixtures. No production credentials, endpoints, files, media, or provider
identifiers were read or recorded.

## Scanner and persistence verification

- JVM tests cover incremental and full scanning, generation rollback,
  incomplete scans, checksum-read disappearance, disabled rules, duplicate
  provider rows, 500-item batching, stable opaque identity, unsafe paths,
  six typed trigger routes, notification debounce, and concurrent-trigger
  coalescing.
- Android tests use fake ContentResolver query boundaries for projected
  MediaStore generation queries and nested DocumentsContract traversal. They
  also cover provider cycles, permission denial, hard item limits, Room schema
  migration 1 to 2, source-identity uniqueness, local-missing transitions,
  reappearance, remote-reference preservation, account isolation, and
  database reopen.
- A completed full scan may mark an unobserved local item `LOCAL_MISSING` in
  the same transaction that advances its checkpoint. Incomplete scans and
  exceptions advance neither. No scanner or persistence component depends on
  a server delete, trash, or receipt-removal API.

## Anonymous 10,000-item measurement

The JVM performance fixture scanned 10,000 anonymous documents, of which 10
had changed metadata and exposed synthetic 1 MiB streams.

| Measurement | Result |
| --- | ---: |
| Scanner elapsed time | 189 ms |
| Total test command wall time | 12.54 s |
| Maximum command resident set | 119,468 KiB |
| Content streams opened | 10 |
| Bytes read for checksums | 10,485,760 |
| Room-equivalent batches | 20 x 500 items |
| MockWebServer delete/trash requests | 0 |

The remaining 9,990 unchanged items reused stored checksums and were not read.
The command used GNU `time -v`; its resident-set figure includes Gradle and
the test JVM, so it is a conservative process-level observation rather than
an Android heap measurement.

## Repository verification

- `./scripts/ci/verify-android.sh` passed Debug app and AndroidTest APK
  assembly, all JVM tests, coverage verification, CycloneDX SBOM generation,
  ktlint, detekt, and Android Lint. The final post-connected-test run completed
  in 4 minutes 35 seconds.
- Backup-critical JVM line coverage is 310/314 lines (98.73%). Android Domain
  and Application line coverage is 4,581/5,488 lines (83.47%).
- `./scripts/ci/verify-security.sh` and `git diff --check` passed.
- CycloneDX emitted the existing non-fatal Media3 Compose effective-POM
  warning; both BOM files and the complete Android verification task were
  produced successfully.

## Android 13 connected verification

- `:core-data:connectedDebugAndroidTest` passed 10/10 tests on both an
  Android 13 API 33 AOSP ATD emulator and a CPH2333 Android 13 device.
- `:core-database:connectedDebugAndroidTest` passed 7/7 tests on both the
  API 33 emulator and the Android 13 device.
- The API 30+ MediaStore generation-selection path was therefore exercised
  on the deployment environment's Android version. The API 29 fallback,
  which omits generation columns and selection, remains covered by the
  version-gated implementation and JVM/build verification rather than being
  substituted for the Android 13 connected gate.
