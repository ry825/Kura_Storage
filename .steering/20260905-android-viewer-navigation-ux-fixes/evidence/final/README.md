# Final verification evidence

Date: 2026-09-06

This file records automated and physical-device verification completed so far.
No device serial, SSID, BSSID, user identifier, file identifier, access token,
personal filename, or local absolute path is recorded here.

## Repository verification

- `./scripts/ci/verify-android.sh`: passed.
  - Debug build and AndroidTest APK assembly passed.
  - JVM unit tests: 358 passed, 0 failed, 0 errors, 0 skipped.
  - ktlint, detekt, and Android Lint passed.
  - The CycloneDX task emitted its known non-fatal effective-POM warning for
    Media3 Compose; the SBOM task and verification completed successfully.
  - Re-run after the physical-device PDF responsive fix: 1,392 actionable tasks,
    passed.
- `./scripts/ci/verify-server.sh`: passed.
  - Release build: 0 warnings, 0 errors.
  - Domain tests: 135 passed.
  - Application tests: 350 passed.
  - Integration/contract tests: 230 passed.
  - Server total: 715 passed, 0 failed, 0 skipped.
  - C# formatting and analyzer gates passed.
- `./scripts/ci/verify-config.sh`: passed with the repository checks fully
  parsed. Service-manager metadata, live kernel ruleset access, and Nginx listen
  socket access were unavailable in the isolated verification environment.
- `./scripts/ci/verify-security.sh`: passed.
- `git diff --check`: passed.

The repository verification commands above were run again on the final source
after the physical-device fixes: Android completed 1,392 actionable tasks,
Server completed 715 tests, and Config and Security both passed. The Config run
used the isolated tool bundle's MIME-types path; system service metadata, kernel
netlink/ruleset, and Nginx listen sockets remained intentionally unavailable in
the sandbox while all syntax and repository gates passed.

## Android API 33 connected verification

The API 33 test device was an emulator. Across the changed and directly affected
modules, 126 tests passed with 0 failures, 0 errors, and 0 skipped:

- app navigation: 9
- core data: 16
- core UI: 5
- file browser: 25
- media viewers/player: 21
- search/favorites/tags: 14
- settings: 11
- text editor/version history: 9
- backup UI/work boundaries: 11
- connection UI: 5

The Settings suite measured normal/supporting text and actionable UI contrast for
the fixed light/dark schemes and the API 33 dynamic light/dark schemes. The test
requires at least 4.5:1 for text and 3:1 for actionable UI colors.

## Coverage

- Domain/application line coverage: 83.52% (5,891 covered, 1,162 missed).
- Critical media-state line coverage: 95.08% (753 covered, 39 missed).
- Critical text-state line coverage: 98.84% (425 covered, 5 missed).
- Critical backup-state line coverage: 98.73% (310 covered, 4 missed).

All configured coverage verification gates passed.

## Android 13 physical-device connected verification

The current source was exercised on a 360 dp-wide Android 13 / API 33 physical
device. All 154 tests across the 15 instrumentation modules passed with 0 failures,
0 errors, and 0 skipped:

- app 9; core-data 16; core-database 11; core-ui 6
- activity 2; auth 8; backup 11; connection 5
- files 25; media 21; search 14; settings 11; sharing 6; text 9

The first physical run exposed a compact-width defect in the PDF failure state:
two status actions left only about 10 dp for the message column, expanded the panel
to nearly the full screen height, and reduced the page viewport and navigation to
zero height. `KuraStatusPanel` now places actions below its message below 480 dp.
The new compact-panel regression test and the original PDF navigation/error test
passed individually on the physical device, followed by the full affected suite.

The expanded all-module runner also reached a stale Sharing assertion that still
expected enum-style permission text. It now verifies the existing user-facing
`Can manage (Direct share)` label. The complete Sharing and Text suites then passed
on the same physical device.

## Live-server representative flows

The current Release build was installed over the existing authenticated app data
and exercised against the live server over the local-direct route. TLS/server
identity checks, session authentication, storage availability, file listing, and
authenticated Photo, Video, and PDF retrieval all succeeded.

- Video: an Original guaranteed-MIME asset played on the physical device. Play,
  pause, seek, 1.25x speed, fullscreen landscape, overlay auto-hide, one-tap
  overlay restoration, system-bar handling, Home/background, foreground return,
  position retention, and fullscreen Back were verified. A physical run exposed
  `PlayerSurface` consuming overlay taps; a transparent Compose tap layer above
  the surface and below the controls fixed it. The focused test and all 21 media
  instrumented tests then passed.
- Photo: Low, Medium, and Original all loaded. The UI displayed approximately
  265 KB, 790.6 KB, and 14 MB respectively rather than reusing Original size.
- PDF: the primary row opened the PDF confirmation rather than the text editor;
  the document rendered in-app without a separate save, showed two pages, moved
  between pages, zoomed from 1.0x to 1.5x, and safely handled Back during render.
  The physical run exposed and fixed both the media-vs-text route priority defect
  and a published-bitmap recycle race.
- Favorites, Search, and tag-filtered Search displayed thumbnails or safe
  generation/fallback states with metadata and reached the correct Photo/PDF
  destination.
- Settings was checked on the 360 dp-wide device in both Light and Dark modes at
  the device's enlarged font setting. Heading, item, current-value, description,
  and icon hierarchy remained readable. The original Dark mode was restored.

Exact `Content-Length` equality, Low/Medium video selection states, corrupt and
encrypted PDF classification, interrupted/size-boundary PDF cleanup, UTF-8 and
UTF-16 preservation, lossy-save acknowledgement, version conflicts/restoration,
folder-tap serialization, 100%/200% font layouts, TalkBack semantics, 48 dp touch
targets, and contrast thresholds are covered by the passing JVM, contract,
integration, and API 33 instrumented suites above. Manual checks were intentionally
limited to representative hardware-dependent rendering, lifecycle, and network
behavior; automated checks cover deterministic edge cases without modifying
pre-existing live data.

## Connection boundary status

- Local-direct Wi-Fi: passed on the physical device for startup, authenticated
  listing, Video, and PDF, with the Home status reporting available storage.
- Registered external Wi-Fi plus ZeroTier: passed after moving off the local
  subnet. The app selected `ZeroTier`, reported available storage and automatic
  backup ready with zero pending items, retained the authenticated session,
  returned Search/listing data, and downloaded/rendered a two-page PDF.
- Fail closed: with that external Wi-Fi retained and only ZeroTier disabled, the
  app reported no verified route and an unreachable server instead of treating
  the Wi-Fi match as identity or authorization. Re-enabling the existing ZeroTier
  network and rechecking restored `ZeroTier`, available storage, and authenticated
  file access.
- Mobile, unregistered Wi-Fi, TLS/identity failure, and expired authentication are
  additionally covered by the passing connection, authentication, backup-policy,
  network contract, and media tests. Mobile remains manual-only and an unmet route
  never starts automatic backup.

No screenshot is retained as evidence. Visual results above are anonymized prose;
temporary captures containing live data are tracked for exact cleanup.

## Fixture and device-state cleanup

No User, Device, Session, File, Folder, Favorite, Tag assignment, Share, backup
rule, or SAF export was created for the live checks. Existing content was only
read through normal authenticated application routes. The manifest therefore
contained only this run's device screenshots/UI hierarchies, a local APK copy,
and an extracted bugreport. Every exact manifest entry was checked before removal
and a read-only post-check reported zero remaining entries. No broad name match,
parent-folder deletion, cache purge, or production-data delete was used.

The app's private PDF temporary file was released through the normal viewer/session
cleanup path; partial-file and TTL cleanup are independently covered by the passing
tests. Expected read-side operational state such as recent-access metadata and
derivative cache jobs was not treated as user-created fixture data and was not
purged, avoiding changes to unrelated cached derivatives. Existing user content
and storage objects were not modified or deleted. Device Dark mode was restored,
rotation was returned to `free`, Wi-Fi was enabled, and the pre-existing ZeroTier
network was left online on the external Wi-Fi route that the user connected.
