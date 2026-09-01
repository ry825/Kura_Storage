# Android text editor and file version PR3 verification

## Scope

- Date: 2026-09-01 (Australia/Melbourne)
- Server: Raspberry Pi 4 Model B, PostgreSQL 17, and the production-equivalent exFAT HDD
- Candidate: signed, non-debuggable `0.10.0-text-pr3-rc1` (`versionCode` 15)
- Routes: Raspberry Pi LAN `LOCAL_DIRECT`, physical Android `REMOTE_SECURE` over ZeroTier, and Raspberry Pi ZeroTier HTTPS self-check
- Fixtures: six limited users across the API and physical-device runs, isolated API-client devices, UTF-8 text files, and unique `pr3-text-*` identifiers

No password, token, private key, physical path, File ID, user-provided file body,
or response body is recorded in this document.

## Protection and rollout

The release build produced a linux-arm64 Server archive and signed Android APK.
Both SHA-256 checks passed; the APK uses one RSA-4096 signer, APK Signature
Scheme v3, the approved certificate, application ID, version name/code, and no
debuggable flag. The Server archive contains the API, Admin CLI, and Worker and
contains no appsettings, environment, or key file.

Before upgrade, the API, Worker, PostgreSQL, and Nginx were active; the exFAT HDD
was mounted read-write with `nosuid`, `nodev`, and `noexec`; and the selected
database had zero unfinished File operations, active Upload Sessions, or
queued/running Media Jobs. The standard upgrader created
`pre-0.10.0-text-pr3-rc1.dump`, applied `AddTextFileVersions`, activated the
candidate, and passed the deployment verifier. The existing warning that
`fs.inotify.max_user_watches=61621` is below the recommended 65536 remains; the
upgrade did not modify the host sysctl.

## Raspberry Pi API, PostgreSQL, and HDD verification

The isolated API-client matrix passed the following checks against the physical
HDD and PostgreSQL database:

- Multipart Upload created UTF-8 version 1, and current-text retrieval matched
  content, encoding, hash/size metadata, and `FileEntry.fileVersion`.
- Two separately registered devices saved with the same expected version at the
  same time. Exactly one returned 200 and the other returned 409; reloading
  returned the winning version without a force-overwrite path.
- The losing draft was uploaded under a separate name and retrieved byte-for-byte.
- Newest-first history, past-version content, actor/change metadata, and restore
  passed. Restore published a new current version and retained both prior contents.
- VIEWER could read current/history but could not save; EDITOR could save. Removing
  VIEWER membership immediately hid current/history. Revoking the second Owner
  device caused its next authenticated request to return 401.
- A physical-file update was confirmed by an explicit Admin APPLY rescan and
  published as `EXTERNAL_CHANGE`. A second update also converged through APPLY.
- The API was terminated with SIGKILL and automatically restarted by systemd.
  Current version 5 and historical versions 1 and 4 remained byte-correct after
  the first crash/restart check.
- API/Worker Journal scanning found no unique external-content marker. The
  Raspberry Pi ZeroTier-address HTTPS self-check returned 200 with the same
  hostname, Root CA, authentication, and text endpoint contract.

Two direct inotify observations did not reach a new catalog version within 20
and 30 seconds on this exFAT deployment. Explicit APPLY rescans converged without
error (first: 28 enumerated/3 updated; second: 28 enumerated/1 updated). This
record therefore claims external-version persistence and scan recovery, but does
not claim low-latency inotify convergence for this run.

## Automated Android verification

- Android JVM tests: 204 passed, 0 failed/skipped/errors in the latest complete run.
- Text critical-state line coverage: 337/338 (99.70%), above the 95% gate.
- Domain/Application line coverage: 3905/4625 (84.43%), above the project gate.
- `feature-text` Unit tests and Android-test APK assembly passed after the final
  editor/history-state corrections.
- All 65 current instrumented tests passed on an OPPO CPH2333 running Android
  13 (API 33). The final `feature-text` run comprised six tests, including two
  `captureToImage()` screenshot smoke tests for the conflict and history screens.
- `./scripts/ci/verify-android.sh` passed 1118 tasks after the final correction,
  including `detekt`, `ktlint`, Android lint, Debug assembly, Unit tests, SBOM
  generation, Android-test APK assembly, and all configured coverage gates.
- `git diff --check` passed.

## Physical Android verification

The signed, non-debuggable release APK was clean-installed on an OPPO CPH2333
running Android 13 (API 33). Package inspection confirmed versionCode 15 and
versionName `0.10.0-text-pr3-rc1`. One physical Android device and one separately
registered API client formed the required two-device equivalent; this record does
not claim that two physical Android handsets were used.

The following signed-release flows passed:

- Owner opened the supported UTF-8 text file, entered edit mode, retained a dirty
  draft across portrait/landscape recreation, saved version 2, previewed version
  1, and restored it as a new current version while retaining prior content.
- The independent API device saved after the Android editor loaded its base
  version. Android received the conflict state without overwriting, rendered a
  bounded line diff, reloaded the winning version, and repeated the conflict.
  `Save as copy` launched the Android document picker and uploaded
  `notes-copy.txt`; API verification found exactly one version-1 copy.
- VIEWER opened version 5 as `Read only` with no edit action. After Owner removed
  the membership, an explicit Shared refresh removed the file. EDITOR opened the
  same file with `Can edit` and saved version 6.
- Disabling Wi-Fi made history unavailable. Re-enabling Wi-Fi restored the direct
  route from 192.168.1.110 to the Raspberry Pi LAN address and refreshed version
  6 successfully.
- With the saved ZeroTier network enabled and Wi-Fi disabled, Android had
  `tun0` address 10.244.71.217/16 and routed the Raspberry Pi ZeroTier address
  through `tun0`. The app reported `Connection: REMOTE_SECURE` and retrieved
  version 6 with the expected content. ZeroTier was then disabled and Wi-Fi/LAN
  restored to their initial states.
- Revoking the active CPH2333 EDITOR device with the Admin CLI caused the next
  app start to show the device-revoked message; retry returned to local device
  registration.

## Cleanup and final state

Both isolated folders were deleted through the normal Trash and Purge APIs.
Current/history endpoints then returned 404, and no version metadata remained for
the test users. Six `pr3-text-*` users were disabled, all 16 Refresh Sessions and
15 Devices were revoked, test operations and four empty user roots were removed,
and scoped storage directories were deleted. Seven final DB/orphan counters were
zero. API, Worker, PostgreSQL, Nginx, and the HDD mount were healthy afterward.
A final Journal scan found none of the unique physical-draft markers.
