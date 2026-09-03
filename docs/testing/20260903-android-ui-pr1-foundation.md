# Android UI PR1: Design system foundation

## Scope

This record audits the 36 Android reference mockups before the UI migration and records the PR1 design-system verification. Product behavior follows `docs/product-requirements.md` and `docs/functional-design.md`; the mockups define visual hierarchy only.

## Baseline mockup audit

Legend: **Yes** means that a production route and state owner exist, **Partial** means that the behavior is embedded in another screen or lacks one or more formal states, and **Missing** means that the formal destination or contract does not exist yet.

| ID | Reference | Formal screen / section | Production UI | State owner | Navigation | Existing test | Baseline state coverage and assigned work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 001 | `connection-auth/001-splash.png` | Startup; 9.1, 10.1 | Missing; system theme enters `KuraStorageApp` | Application startup | Start route is `CONNECTION` | `BackupApplicationRuntimeTest` only | No dedicated splash/dark/API 29 fixture. PR3. |
| 002 | `connection-auth/002-connection-check.png` | Connection check; 10.1, 11.1 | `ConnectionScreen` | `ConnectionViewModel` / `ConnectionStatus.Checking` | `CONNECTION` | `ConnectionScreenTest` | Checking exists; richer progress and semantics are missing. PR3. |
| 003 | `connection-auth/003-local-connection-status.png` | Local direct; 10.1, 11.1 | `ConnectionScreen` | `ConnectionStatus.Connected(LOCAL_DIRECT)` | `CONNECTION` | `ConnectionScreenTest` | Content/storage states exist; network/server breakdown is partial. PR3. |
| 004 | `connection-auth/004-disconnected-status.png` | Disconnected; 10.1, 11.1 | `ConnectionScreen` | `Disconnected`, TLS, protocol states | `CONNECTION` | `ConnectionScreenTest` | Recoverable reasons exist; visual hierarchy/offline semantics are partial. PR3. |
| 005 | `connection-auth/005-vpn-connection.png` | ZeroTier guidance; 10.1, 11.1 | `ConnectionScreen` | `Connected(REMOTE_SECURE)` | `CONNECTION` | `ConnectionScreenTest` | Legacy VPN controls are intentionally excluded; separate-app guidance/recheck is PR3. |
| 006 | `connection-auth/006-login.png` | Login; 10.1 | `AuthScreen` | `AuthViewModel`, `AuthUiState` | `AUTHENTICATION` | `AuthScreenTest` | Loading/form/error exist; focus, inline error, responsive form are PR3. |
| 007 | `connection-auth/007-initial-setup.png` | Initial device registration; 7.11, 10.1 | Partial in `AuthScreen` | `AuthViewModel` submitting state | `AUTHENTICATION` | `AuthScreenTest` | Registration progress is not a distinct presentation. PR3. |
| 008 | `connection-auth/008-device-registration-error.png` | Registration unavailable; 7.11, 10.1 | Partial in `AuthScreen` | `AuthUiState.Error` | `AUTHENTICATION` | `AuthScreenTest` | Route restriction exists below UI; explicit local-only/recovery view is PR3. |
| 009 | `home-navigation/009-home.png` | Home; 10.2, 11.2 | `HomeScreen` in `MainActivity` | App-scoped repositories/state | `HOME` | `HomeScreenTest` | Content/navigation exists; summaries, recent/category, partial-error states are PR2. |
| 010 | `home-navigation/010-my-files.png` | File list; 10.2, 10.3, 11.3 | `FileBrowserScreen` | `FileBrowserViewModel` | `FILES` | `FileBrowserScreenTest` | Loading/content/error/actions exist; empty/grid/breadcrumb/adaptive polish is PR4. |
| 011 | `home-navigation/011-recent-files.png` | Recent; 10.2, 11.2 | `RecentFilesScreen` | `RecentFilesViewModel`, `RecentFilesUiState` | `RECENT_FILES` | `SearchScreensTest` | Loading/empty/content/error/paging exist. Shared row and grouping move to PR5. |
| 012 | `home-navigation/012-shared-files.png` | Shared; 10.2, 10.3, 11.4.1 | `SharingScreen` | `SharingListViewModel` | `SHARING` | `SharingScreensTest` | Content/filter/error exist; unified entry metadata and adaptive states are PR5. |
| 013 | `home-navigation/013-category-browser.png` | MIME category; 11.2 | Missing dedicated presentation | Search repository/filter contract exists | Missing category route | No dedicated test | Reuse Search API; add destination/UI/test in PR5. |
| 014 | `home-navigation/014-search.png` | Search; 10.2 | `SearchScreen` | `SearchViewModel`, `SearchUiState` | `SEARCH` | `SearchScreensTest` | Filter/results/loading/empty/error exist; hierarchy/responsive polish is PR5. |
| 015 | `home-navigation/015-settings.png` | Settings hub; 10.2, 10.4 | Partial links in `HomeScreen` / backup settings | App-scoped state | Media/backup destinations | `HomeScreenTest` | No unified settings screen or cache destination. PR9. |
| 016 | `files-media/016-photo-viewer.png` | Photo viewer; 10.3, 11.5 | `PhotoViewerScreen` | `PhotoViewerViewModel`, `PhotoViewerUiState` | `PHOTO_VIEWER` | `MediaViewerScreenTest` | Quality/loading/error/content exist; common viewer/adaptive semantics are PR6. |
| 017 | `files-media/017-video-player.png` | Video player; 10.3, 11.6 | `MediaPlayerScreen` | `MediaPlayerViewModel`, `MediaPlayerUiState` | `VIDEO_PLAYER` | `MediaPlayerScreenTest` | Player/quality/generation/error exist; shared responsive controls are PR6. |
| 018 | `files-media/018-audio-player.png` | Audio player; 10.3, 11.6 | `MediaPlayerScreen` | `MediaPlayerViewModel`, `MediaPlayerUiState` | `AUDIO_PLAYER` | `MediaPlayerScreenTest` | Audio reuses player state; audio-specific information hierarchy is PR6. |
| 019 | `files-media/019-pdf-viewer.png` | PDF viewer; 10.3 | `PdfViewerScreen` | `PdfViewerViewModel`, `PdfViewerUiState` | `PDF_VIEWER` | `PdfDocumentControllerTest`, `MediaViewerScreenTest` | Page/loading/error exist; common header, limits, responsive controls are PR6. |
| 020 | `files-media/020-text-editor.png` | Text editor; 10.3, 11.7 | `TextEditorScreen` | `TextEditorViewModel`, `TextEditorUiState` | `TEXT_EDITOR` | `TextScreensTest` | Loading/edit/save/conflict/history exist; adaptive editor and semantics are PR7. |
| 021 | `files-media/021-unsupported-file.png` | Unsupported detail; 11.4 | Partial fallback from `FileBrowserScreen` | File browser state | File open callback | `FileBrowserScreenTest` | No dedicated reason/metadata presentation. PR4. |
| 022 | `files-media/022-file-details.png` | File detail; 10.3, 11.4 | Partial actions in `FileBrowserScreen` | `FileBrowserViewModel` | `FILES` entry actions | `FileBrowserScreenTest` | No dedicated detail hierarchy. PR4. |
| 023 | `files-media/023-folder-details.png` | Folder detail; 10.3, 11.4 | Partial actions in `FileBrowserScreen` | `FileBrowserViewModel` | `FILES` entry actions | `FileBrowserScreenTest` | No dedicated detail hierarchy. PR4. |
| 024 | `files-media/024-sharing-settings.png` | Share settings; 10.3, 11.4.1 | `SharingSettingsScreen` | `SharingSettingsViewModel` | `SHARING_SETTINGS` | `SharingScreensTest` | Listing/mutation/error exist; scope hierarchy/confirmation is PR5. |
| 025 | `files-media/025-share-permissions.png` | Member/permission selection; 11.4.1 | Partial in `SharingSettingsScreen` | `SharingSettingsViewModel` | `SHARING_SETTINGS` | `SharingScreensTest` | Selection is embedded; validation and responsive form are PR5. |
| 026 | `files-media/026-server-folder-selection.png` | Server destination; 11.8 | `MovePickerDialog`, `BackupDestinationPicker` | File/backup view models | `BACKUP_DESTINATION` / dialog | File and backup tests | Two functional pickers exist; common breadcrumb/permission UI is PR4/PR9. |
| 027 | `files-media/027-transfer-status.png` | Transfer status; 6.1, Android Step 5 | `TransferPanel` | `FileBrowserViewModel` transfer state | Embedded in `FILES` | `FileBrowserScreenTest` | Upload/download/error/progress exist; pause/resume/recovery hierarchy is PR4. |
| 028 | `files-media/028-trash.png` | Trash; 10.3, 7.9 | `FileBrowserScreen` trash mode | `FileBrowserViewModel` | `TRASH` | `FileBrowserScreenTest` | Restore/purge/dialog exist; retention/admin warning/adaptive list are PR4. |
| 029 | `files-media/029-missing-files.png` | Missing entries; 10.3, 11.12 | `FileBrowserScreen`, `MissingIndexDeleteDialog` | `FileBrowserViewModel` | `FILES` | `FileBrowserScreenTest` | Candidate/missing actions exist; timestamps, unknown state, hierarchy are PR4. |
| 030 | `backup-settings/030-backup-status.png` | Backup overview; 10.2, 11.8 | `BackupOverviewScreen` | `BackupOverviewViewModel` | `BACKUP_OVERVIEW` | `BackupScreensTest` | Counts/history/reason/action exist; card hierarchy and all wait states are PR9. |
| 031 | `backup-settings/031-backup-rules.png` | Backup rules | `BackupRulesScreen` | `BackupRulesViewModel` | `BACKUP_RULES` | `BackupScreensTest` | CRUD/empty/error exist; card/status/adaptive polish is PR9. |
| 032 | `backup-settings/032-backup-rule-editor.png` | Rule editor | `RuleEditorDialog` | `BackupRulesViewModel` | Dialog in `BACKUP_RULES` | `BackupScreensTest` | Fields/save/error exist; current values and responsive form are PR9. |
| 033 | `backup-settings/033-trusted-wifi.png` | Allowed Wi-Fi list; 10.4, 11.9 | `BackupWifiScreen` | `BackupWifiViewModel` | `BACKUP_WIFI` | `BackupScreensTest` | Permission/empty/content/error exist; status row hierarchy is PR9. |
| 034 | `backup-settings/034-trusted-wifi-editor.png` | Wi-Fi register/edit; 11.9 | `WifiRenameDialog` plus register action | `BackupWifiViewModel` | Dialog in `BACKUP_WIFI` | `BackupScreensTest` | Register/rename/delete exist; validation/current network form is PR9. |
| 035 | `backup-settings/035-quality-network-settings.png` | Quality/network; 10.4, 11.10 | `QualitySettingsScreen` | `QualitySettingsViewModel` | `MEDIA_SETTINGS` | `QualitySettingsScreenTest` | Quality/context/save state exist; legacy VPN wording remains excluded. PR9. |
| 036 | `backup-settings/036-cache-management.png` | Admin cache; 10.4, 11.11 | Missing | Missing server/API/repository/UI contract | Missing | Missing | Add durable cleanup request and admin-only status end-to-end in PR8, UI in PR9. |

## Cross-screen baseline findings

- Most screens directly use Material 3 defaults and repeated `Column`, `Card`, `Button`, and state layouts. PR1 introduces semantic tokens and shared primitives; feature adoption occurs in PR2 through PR9.
- Required state vocabulary is not uniformly represented. Loading/content/error are common, while empty, processing, permission, offline, partial-error, and blocking-error presentation varies by feature.
- Existing routes are functional but the signed-in five-item app shell, category destination, unified settings destination, file/folder detail destinations, and cache destination are absent or partial.
- Existing tests cover callbacks and key text but do not share 360 dp, 200% font-scale, dark theme, minimum touch-target, heading, selected, progress, or deterministic capture fixtures.

## Intentional differences from reference images

- The legacy `VPN` reference is implemented as ZeroTier guidance. KuraStorage never connects, disconnects, or authorizes ZeroTier members.
- Decorative trees are excluded. The PR1 mark is an original Compose drawing made from primitive geometry and includes no third-party asset.
- Sample names, users, timestamps, counts, photographs, and storage figures in reference images are not production fixtures.
- Responsive reflow and scrolling take precedence over fixed coordinates and pixel matching.
- The cache screen will exclude thumbnail usage from the 10 GB derivative-cache limit and will not expose the mockup-only bulk retry action.

## PR1 verification

Verification was completed on 2026-09-03. The first instrumented-test run exposed a theme fixture that did not recompose and a 40 dp icon-button semantics bound. The fixture now uses observable state and the icon button explicitly enforces a 48 dp minimum; the corrected suite passed in full.

| Check | Result | Evidence |
| --- | --- | --- |
| Android verification script | Passed | `./scripts/ci/verify-android.sh`: 1,387 tasks, including build, JVM tests, lint, ktlint, detekt, coverage, release APK, and CycloneDX SBOM; completed in 5m 9s. The existing Media3 effective-POM metadata warning remained non-fatal. |
| Core UI instrumented tests | Passed | `:core-ui:connectedDebugAndroidTest` on an Android 13 / API 33 Google APIs x86_64 emulator: 5/5 tests passed. |
| 360 dp and 200% font scale | Passed | `segmentedControlReflowsAtTwoHundredPercentFontScaleAndCaptures` uses a 360 dp fixture and 2.0 font scale and verifies adaptive vertical actions. |
| Light and dark themes | Passed | `lightAndDarkThemesProduceDeterministicDifferentSurfaces` recomposes both fixed schemes and captures each surface. |
| Accessibility semantics | Passed | Tests assert heading, selected state, error/live-region state, progress information, non-empty descriptions, and a 48 dp icon-button semantics bound. |
| Reference capture | Passed | Deterministic test-only `captureToImage()` calls cover theme and adaptive fixtures; component and state variants are verified through semantics assertions. No screenshot-only production branch exists. |
| Asset/dependency review | Passed | The logo and file icons are original Compose `Canvas` geometry with decorative semantics cleared by default. No bitmap was added, no source asset exceeds 20 KB, and only Compose UI test dependencies were added. Debug/release APKs and the 3,115,162-byte CycloneDX SBOM were generated successfully. |

`git diff --check` completed without errors. The debug APK was 47,104,050 bytes and the release APK was 36,294,803 bytes; neither review identified an unexpected packaged asset or production dependency.

## PR1 residual work

PR2 through PR9 own feature-screen adoption and the missing contracts identified above. PR10 owns the full Android 13 physical-device flow, TalkBack, rotation, 200% font scale, and final 36-screen evidence pass.
