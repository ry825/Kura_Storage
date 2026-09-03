# Android automatic backup PR4 verification

Date: 2026-09-03

## Scope

This record covers automatic-backup network policy, unique WorkManager scan
and transfer chains, process-safe runtime reconstruction, server comparison,
resumable chunk upload, bounded batches, retry and wait-state transitions,
progress aggregation, and completed-history cleanup. Settings, progress, and
history UI plus Raspberry Pi end-to-end validation remain in PR5.

All account scopes, rule identifiers, document keys, paths, file names,
network identifiers, checksums, endpoints, and content used by automated
tests are anonymous fixtures. No production credentials, files, SSIDs,
BSSIDs, tokens, or provider identifiers were read or recorded.

## Policy and transfer verification

- JVM tests cover the complete connection and base-transport matrix, local
  direct Ethernet/Wi-Fi binding, allowed external Wi-Fi plus ZeroTier,
  metered and mobile rejection, source permission, storage, authentication,
  battery, charging, and disabled-rule gates.
- Transfer tests cover already-uploaded completion, new and changed uploads,
  persisted upload-session offset recovery, chunk-boundary policy changes,
  malformed compare responses, source mutation, remote conflict,
  authentication wait, bounded transient retry, and the absence of duplicate
  publication.
- MockWebServer contract tests verify exact backup comparison fields and
  upload context without weakening the existing authenticated request and
  idempotency contracts.
- WorkManager tests verify stable hashed unique-work names, trigger
  convergence, OS retry, runtime lookup after process-style reconstruction,
  and fail-closed behavior when application-owned runtime state is
  unavailable.

## Android 13 behavior

- The deployment device was a CPH2333 running Android 13 (API 33).
- Android 13 Wi-Fi policy evaluation follows the non-VPN Wi-Fi network below
  an active ZeroTier VPN, while retaining explicit route, TLS, server
  identity, and base-network binding checks.
- Large transfers use a foreground worker only when notification permission
  is available. On Android 13, denial of `POST_NOTIFICATIONS` leaves the
  durable queue untouched and reports a permission-required result instead
  of attempting an unsafe foreground transfer.
- Connected tests passed for `core-database` (9 tests), `core-data` (10
  tests), `feature-backup` (2 tests), and `app` (4 tests, including runtime
  reconstruction without an Activity). The app suite was rerun after the
  device vendor's 10-second sleep override invalidated the first UI attempt;
  keeping the display awake produced a clean 4/4 result.

## Repository verification

- `./scripts/ci/verify-android.sh` passed Debug and AndroidTest APK assembly,
  all JVM tests, coverage verification, CycloneDX SBOM generation, ktlint,
  detekt, and Android Lint.
- Backup-critical line coverage was 310/314 lines (98.73%). Android Domain
  and Application line coverage was 4,988/6,054 lines (82.39%).
- `./scripts/ci/verify-server.sh` passed 130 Domain, 336 Application, and 224
  Integration tests.
- `./scripts/ci/verify-config.sh`, `./scripts/ci/verify-security.sh`, and
  `git diff --check` passed.
- CycloneDX emitted the existing non-fatal Media3 Compose effective-POM
  warning; the SBOM and complete Android verification still succeeded.
