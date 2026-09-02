# Android automatic backup PR2 verification

Date: 2026-09-02

## Scope

This record covers the Android Room foundation, account-scoped backup rules,
persistable SAF read access, external Wi-Fi registration, and the
`feature-backup` use-case boundary. MediaStore and SAF scanning, network-route
policy, transfer workers, and UI remain in later pull requests.

All account identifiers, folder identifiers, URIs, SSIDs, BSSIDs, document
keys, and file metadata used by automated tests were synthetic. No production
credentials, endpoints, files, or Wi-Fi identifiers were read or recorded.

## Automated verification

- `./scripts/ci/verify-android.sh` passed Debug application and AndroidTest APK
  assembly, all JVM unit tests, every coverage gate, SBOM generation, ktlint,
  detekt, and Android Lint.
- Backup-critical JVM line coverage is 309/314 lines (98.41%). Android Domain
  and Application line coverage is 4,413/5,258 lines (83.93%).
- The new backup JVM suite contains 27 tests for typed model invariants,
  entity mapping, state transitions, account-scope hashing, Rule lifecycle,
  SAF permission retention, Wi-Fi permission versions, identifier
  normalization, duplicate and count limits, metered behavior, and hashed
  Work names.
- `:core-data:connectedDebugAndroidTest` passed 7 tests and
  `:core-database:connectedDebugAndroidTest` passed 4 tests on a CPH2333
  running Android 13. These cover existing Core Data regressions, permission
  fail-closed behavior, initial schema validation, uniqueness convergence,
  bounded atomic claim, expired-upload reconciliation, account isolation,
  cascade, and close/reopen process recreation.
- `./scripts/ci/verify-security.sh` and `git diff --check` passed.

An attempted repository-wide `connectedDebugAndroidTest` first stopped in the
existing App Compose suite while the physical device was asleep. After the
device was awakened it disconnected from ADB before the rerun started. The two
modules changed by this pull request had already completed all 11 connected
tests successfully; the repository-wide AndroidTest APK assembly also passed.

## Schema, permissions, and privacy

- Room schema version 1 is exported and validated without destructive
  migration fallback. The database stores no token, password, file content,
  or ZeroTier secret.
- `kurastorage_backup.db`, its WAL, and its shared-memory file are excluded
  from legacy Android backup. Cloud backup and device transfer exclude the
  application root, and application backup remains disabled.
- The manifest declares only Wi-Fi state, Nearby Wi-Fi, and the Coarse/Fine
  location permissions required for version-specific SSID/BSSID access. The
  runtime policy fails closed before identifier reads when any required
  permission or Location service is unavailable.
- Room rows and edits are scoped by a hash of verified server identity, user,
  and device. Wi-Fi matches are exposed only as candidate matches and do not
  replace route, TLS, host, server, user, device, or session validation.
- Review found no logging, analytics, notification, or metric output containing
  source URIs, relative paths, document keys, SSIDs, BSSIDs, file names, or
  account-scope identifiers.

## Dependency record

The generated CycloneDX SBOM contains Room 2.8.4 and WorkManager 2.11.2. The
CycloneDX plugin continues to print the existing Media3 Compose POM metadata
warning, but SBOM generation and the repository verification task complete
successfully.
