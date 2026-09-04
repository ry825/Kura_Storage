# Android UI PR2: App shell and Home

## Scope

PR2 aligns the signed-in application shell and Home screen with `009-home.png` while retaining the product requirements as the source of truth. It adds the five fixed top-level destinations—Home, Files, Shared, Search, and Settings—and keeps protected and immersive routes outside the bottom-navigation shell.

## Reference comparison

| Reference area | Production implementation | State and interaction evidence |
| --- | --- | --- |
| Brand and overview | Original Compose KuraStorage logo, short overview text, and semantic section headings | `HomeScreenTest` checks the current-status heading and deterministic Light/Dark fixtures. |
| Connection summary | Displays the authoritative `ConnectionRoute` and `StorageAvailability`; no SSID or inferred route | Test fixture verifies ZeroTier and available-storage labels. |
| Automatic backup summary | Derives waiting, uploading, and failed counts from `BackupProgressSnapshot` | JVM tests cover status reduction; Compose tests cover the visible attention state and counts. |
| Primary destinations | My files, Family shared, and Recently opened remain first-class Home cards | Compose tests exercise callbacks without embedding feature implementations in Home. |
| Categories | Photos, Videos, Audio, and Documents open Search with the existing file-category filter | The category callback and route helper are tested; no category-specific server API was introduced. |
| Recent files | Shows at most four repository results with type icon, name, last-opened timestamp, and size | Content, empty, loading, and localized recent-error branches are state driven. Inactive or unknown entries are not opened. |
| Secondary destinations | Favorites, Tags, Activity, and Trash remain on Home; media, backup, connection, and logout actions move to Settings | Admin-only storage/trash/cache affordances are hidden from members. Cache remains a disabled status row until its formally assigned server/UI work. |

## Navigation verification

- `KuraStorageAppShell` owns the shared top bar, bottom navigation, snackbar host, floating-action slot, and window-inset-aware scaffold.
- Only the five top-level routes show the shell. Authentication, connection, detail, dialog-owned, and viewer routes render without bottom navigation.
- Top-level navigation uses `launchSingleTop`, `popUpTo(Home)` with saved state, and restoration. Tests verify all five items, selected semantics, same-tab reselection, Back to Home, and the unauthenticated case.
- Session-scoped view-model keys include the active service session. A route/session replacement closes prior services, clears media and backup state, and clears the protected navigation stack before authentication.
- Home passes entry IDs and category values through app-level callbacks, preserving feature-module boundaries.

## Adaptive and capture verification

Deterministic test-only `captureToImage()` fixtures cover a 360 dp-wide Home at 200% font scale in Dark theme and an 800 × 360 dp landscape Home in Light theme. Status and category cards use one column on compact or large-text layouts and bounded two-column layouts only when sufficient width is available. A Settings fixture verifies that logout remains reachable by scrolling at 200% font scale in landscape. No screenshot fixture or sample content is compiled into production behavior.

The Android 13 / API 33 emulator connected suites passed 8 app tests and 4 settings tests. They cover the five-tab shell, Home content and partial errors, Admin/Member disclosure, 360 dp, landscape, 200% text, Light/Dark rendering, heading/navigation semantics, and scrolling reachability.

The previously used Android 13 physical device was not attached during this PR2 run, and its documented wireless-debug endpoint was not accepting connections. The deterministic API 33 fixtures therefore provide the PR2 display evidence; the planned PR10 physical-device pass remains responsible for final cross-screen confirmation on hardware.

## Intentional differences from `009-home.png`

- ZeroTier replaces legacy VPN terminology, and the app does not offer ZeroTier connection, disconnection, or member authorization controls.
- Decorative trees and sample photography are omitted. File icons and all text/count data come from production state.
- Settings and logout are in the fixed Settings destination rather than extending the Home page. This keeps Home focused while preserving every existing route.
- Admin capacity warnings are shown only when the authenticated role exposes an authoritative admin storage state. Members do not receive capacity or cache-management details.
- Responsive reflow and scroll reachability take precedence over fixed mockup coordinates.

## Automated verification

| Check | Result |
| --- | --- |
| App JVM and navigation tests | Passed |
| App and Settings API 33 connected tests | Passed: 8 app tests and 4 settings tests |
| 360 dp, landscape, 200% text, Light/Dark capture fixtures | Passed |
| `git diff --check` | Passed |
| Full Android verification script | Passed: 1,387 tasks in 2m 51s; build, JVM tests, lint, ktlint, detekt, coverage, APK, and SBOM |
