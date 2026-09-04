# Android UI PR3: Startup, connection, and authentication

## Scope

PR3 aligns startup and the pre-authentication flow with references `001` through `008`. The production connection detector and authentication repository remain authoritative: local direct access is checked first, ZeroTier is represented only as a separately managed remote route, and first-device registration remains local-only.

## Reference comparison

| Reference | Production implementation | State and interaction evidence |
| --- | --- | --- |
| `001-splash.png` | API 29 uses a branded window background; API 31 and later use the platform System Splash icon/background. The first Compose frame repeats the KuraStorage logo, background, and app name without a fixed delay. | API 29, API 33, and API 36 cold starts were captured in Light/Dark modes. Resource compilation verifies both versioned theme paths. |
| `002-connection-check.png` | Shows progress plus the ordered local-direct, ZeroTier, and verified HTTPS/server-storage checks. | Compose tests verify the progress node and all three labels. |
| `003-local-connection-status.png` | Shows authoritative route, base network, verified server, and dedicated-storage availability. Local direct explicitly takes priority. | State tests cover local and remote success plus unavailable storage. Existing detector tests retain local-first behavior without SSID inference. |
| `004-disconnected-status.png` | Separates base network, external ZeroTier membership, server reachability, TLS/hostname failure, and storage availability guidance. | Disconnected, TLS failure, incompatible protocol, and storage-unavailable states have distinct titles and descriptions. |
| `005-vpn-connection.png` | Retains the reference information hierarchy, replacing legacy VPN controls with a separate ZeroTier-app check and an in-app recheck action. | Tests assert the ZeroTier wording, absence of `VPN`, and recheck callback. |
| `006-login.png` | Provides brand, explanation, username/password fields, masked password, show/hide action, IME flow, inline failure, and guarded submission. | Compose and ViewModel tests cover IME submission, password semantics, retained input, inline error, and duplicate-submit suppression. |
| `007-initial-setup.png` | The local-only registration form displays the target device name and keeps the form visible with progress while registration is running. | ViewModel tests cover local registration state and the single in-flight request. |
| `008-device-registration-error.png` | Remote unregistered devices receive local-direct guidance with no registration action. Validation/device-limit failures remain generic, while revoked and token-reuse states have security-specific recovery. | UI and ViewModel tests prove a remote state cannot invoke registration and do not reveal whether a username exists. |

## Adaptive, accessibility, and credential handling

- Both screens use safe-drawing insets and scrollable content. Deterministic 360 dp, 200% font-scale, Dark-theme fixtures reach the primary action and render with `captureToImage()`.
- Headings, progress, connection state descriptions, full-width minimum-touch-target buttons, and password show/hide descriptions are exposed through Compose semantics.
- The password field uses password keyboard/input semantics and masked visual transformation by default. Test captures keep it hidden; no password value is written to production logs or screenshot fixtures.
- Submission remains in `AuthUiState.Form`, preserving entered values while disabling fields and the action until completion. Authentication failures are inline and generic except for device/session security states that require a different recovery path.

## Intentional differences from the references

- KuraStorage does not connect, disconnect, or authorize ZeroTier membership. Those operations remain in the separate ZeroTier app.
- The current detector can prove a local or remote HTTPS route, but cannot truthfully distinguish an inactive ZeroTier tunnel from an unreachable server without adding prohibited ZeroTier control/SDK behavior. The disconnected view therefore presents separate diagnostic checks without claiming an unobserved cause.
- API 31+ System Splash controls the native splash composition and shows the branded icon/background; the app name appears in the immediate Compose frame. API 29 uses the matching window drawable before that frame.
- Decorative mockup imagery and sample credentials are omitted. All displayed device, route, storage, and error values come from production state.

## Android verification

| Check | Result |
| --- | --- |
| API 29 emulator connected tests | Passed: 5 Connection, 6 Auth, and 8 App tests |
| API 33 emulator connected tests | Passed: 5 Connection, 6 Auth, and 8 App tests at a compact 320 x 640 dp viewport |
| API 36 emulator connected tests | Passed: 5 Connection, 6 Auth, and 8 App tests |
| Startup and theme capture | Passed on API 29, API 33, and API 36; Light/Dark Compose frames remained readable after cold start |
| Compact/large-text capture fixtures | Passed at 360 dp, 200% text, and Dark theme |
| Full Android verification | Passed: 1,387 Gradle tasks, including unit tests, coverage gates, ktlint, detekt, Android Lint, SBOM generation, and debug/test builds |

The Android 13 physical device was not attached during this run. API 29, API 33, and API 36 emulators provide the PR3 platform and display evidence; the planned PR10 hardware pass remains responsible for final Android 13 physical-device, TalkBack, rotation, and full-flow confirmation.
