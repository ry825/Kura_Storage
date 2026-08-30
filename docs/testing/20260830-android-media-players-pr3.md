# Android media players PR3 E2E record

## Scope

- Date: 2026-08-30 (Australia/Melbourne)
- Server: Raspberry Pi production service over `LOCAL_DIRECT`
- Android: OPPO CPH2333, Android 13 (API 33)
- App: signed, non-debuggable Release `0.9.0-pr3-test` (`versionCode` 12)
- Fixtures: H.264/AAC MP4 (32 seconds, about 3 MiB) and MP3 (about 71 seconds, 279 KiB)

Android 13 is the available current physical device and is the PR3 acceptance
device, consistently with the accepted PR2 device scope. No Android 10 device
or emulator image is available in this environment, so Android 10 was not
claimed as executed.

## Physical player E2E

The signed release retained the existing authenticated session and selected
`LOCAL_DIRECT`. The following scenarios passed against the real server:

- Video low and medium displayed bounded queued/running progress and only
  became playable after the derivative was ready.
- Original video required the 2.9 MiB transfer confirmation and then played
  through the authenticated Range endpoint with the 32-second duration.
- Video play/pause, seek, -10/-3/+3/+10 seconds, playback speed, low/medium/
  original switching, and position/state retention were exercised.
- Audio exposed only original quality, required the 278.9 KiB confirmation,
  displayed the no-artwork placeholder, and played with seek and 1.5x speed.
- Portrait/landscape rotation retained the route, position, quality, speed,
  and playback state. Backgrounding paused playback and foregrounding retained
  the player route without auto-playing.
- Playback completion displayed `Replay`; activating it restarted playback and
  changed the control to `Pause`.
- Disabling Wi-Fi during playback and restoring it left the player in an
  operable state. The application did not start another media item.

The five primary playback buttons now use the same 48 dp height. Physical UI
bounds were 144 px high for -10, -3, play/replay, +3, and +10 on the 3x-density
device; this specifically verifies the reported vertically stretched `+10s`
regression.

## Network and mobile-path verification

MockWebServer and instrumented DataSource tests validate the initial Range,
seek Range, exact `Content-Range`, 401 refresh, 416, short response, disconnect,
bounded retry, and close behavior. Controller tests verify that low/medium map
only to `video-low`/`video-medium`, original is confirmation-gated, a `202`
response is never passed to Media3, and no next item is prepared or played.

A cellular-data plus ZeroTier attempt was also performed with Wi-Fi disabled.
The phone established `tun0`, but the server ZeroTier address was unreachable
from that path. The application failed closed with `KuraStorage is unreachable`
and did not retry indefinitely. Mobile load-control values (5-15 second
buffer), Wi-Fi values (15-50 seconds), confirmation behavior, disconnect
handling, and no-next-item behavior remain deterministically covered by the
automated tests. Wi-Fi and ZeroTier were restored to their pre-test state.

## Issues found and corrected

- Cancelling the first original-quality confirmation left the player stuck in
  Loading. Cancellation now falls back to the last playable quality or medium.
- Activity recreation during rotation could leave only the title bar. The
  player activity now handles orientation and screen-size changes while the
  player state is retained.
- Ended playback displayed `Pause`. It now displays a functional `Replay`.
- The five playback controls could receive unequal heights, making `+10s`
  vertically stretched. They now use equal weights and an explicit 48 dp
  height, with a Compose regression assertion.

## Automated verification

- `./scripts/ci/verify-android.sh`: passed (965 tasks; JVM tests, build,
  ktlint, detekt, and Android lint).
- Physical connected tests: `feature-media` 12/12 and `core-data` 6/6 passed.
- `./scripts/ci/verify-config.sh`: passed.
- `./scripts/ci/verify-security.sh`: passed.
- `git diff --check`: passed.
- Signed Release assembly: passed.
