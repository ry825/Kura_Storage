# Android media integration PR4 verification

## Scope

- Date: 2026-08-30 (Australia/Melbourne)
- Server: Raspberry Pi production service through the configured HTTPS host
- Physical device: OPPO CPH2333, Android 13 / API 33
- Release: signed, non-debuggable `0.9.0-pr4-test2` (`versionCode` 14)
- Routes: `LOCAL_DIRECT`, cellular plus ZeroTier `REMOTE_SECURE`, and live disconnect/reconnect
- Fixtures: authenticated photo, PDF, H.264/AAC MP4 (32 seconds, about 3 MiB), MP3, generated low/medium derivatives, invalid and boundary fixtures in automated tests

The repeatable entry point is `scripts/e2e/verify-android-media.sh`. `connected`
runs every Android instrumentation module, `capture` records a physical-device
environment, package flags, memory, frame, UID network-total, and current-process
fatal-event snapshot, and `all` performs both. Captures intentionally contain no
credential, token, server path, media body, or production file name.

## Physical integrated E2E

The signed Release retained its authenticated session. On LAN it selected
`LOCAL_DIRECT`; thumbnails for photo, video, and PDF appeared and were reused on
revisit. Photo low, medium, and original selection, original transfer confirmation,
PDF page/zoom/download fallback, video derivative job states, and video/audio Range
playback retain the behavior recorded by PR1 through PR3. The final PR4 pass focused
on cross-route and lifecycle regression:

- Cellular plus ZeroTier established `tun0` and selected `REMOTE_SECURE`. The real
  32-second video opened through the private address while retaining the configured
  HTTPS hostname, Root CA, authentication, and Range contract.
- Original showed a 2.9 MiB estimate before content access. Play, pause, seek, +10
  seconds, and original playback reached 20/32 seconds without preparing another item.
- Removing the cellular underlay exposed `Reconnect` and did not crash or retry
  indefinitely. After restoring the underlay, reconnect required the original
  transfer confirmation again before content access.
- All five primary controls had identical 144 px bounds on the 3x-density device,
  equal to the specified 48 dp height. The reported vertically stretched `+10s`
  regression is therefore closed.
- A route-driven Player replacement initially exposed a real lifecycle race:
  readiness for the replaced engine could call `prepare` after that engine was
  closed. The ViewModel now detaches only the expected engine, cancels the pending
  preparation, and rejects stale completion/failure. A deterministic regression
  test covers replacement while readiness is suspended; the same remote video then
  opened successfully on the signed Release.

Wi-Fi and both ZeroTier switches were restored to their pre-test state after the
remote pass. The release package remained installed and non-debuggable.

## Automated and compatibility verification

The physical-device connected suite passed 59/59 tests across all current modules:

| Module | Passed |
| --- | ---: |
| app | 3 |
| core-data | 6 |
| feature-auth | 2 |
| feature-connection | 1 |
| feature-files | 19 |
| feature-media | 12 |
| feature-search | 10 |
| feature-settings | 1 |
| feature-sharing | 5 |

The suite covers list/search/shared entry navigation, 1,000-entry lazy list/grid
composition, thumbnail states, photo gestures and quality confirmation, PDF bounded
rendering, Player controls and semantics, Range/401/416/disconnect behavior, auth and
route changes, existing file operations, and settings persistence. The 1,000-entry
test composed fewer than 100 thumbnail slots initially in list mode and fewer than
250 after switching to grid, rather than eagerly composing all entries.

API 33 on the OPPO device is the physical baseline/current-Android acceptance run.
An API 29 Emulator image was prepared, but the project owner explicitly accepted
Android 13 as the PR4 device scope before its suite was run, so this record does not
claim an Android 10 execution. `minSdk 29` compilation/lint and API-independent
boundary tests remain successful. Codec support is a runtime device capability:
unsupported codec/decoder errors are classified as unsupported media and never
enter an automatic retry loop.

## Performance and resource observations

| Observation | Result |
| --- | ---: |
| Playing remote-video PSS | 117,912 KiB |
| Playing remote-video RSS | 259,820 KiB |
| Frames rendered | 343 |
| Janky frames | 55 (16.03%) |
| Frame median / p90 / p95 / p99 | 13 / 38 / 53 / 125 ms |
| App-UID network totals at final capture | RX 71,485,884 / TX 4,403,567 bytes |
| Primary control height | 48 dp (144 px) each |

The UID byte values are Android lifetime totals for the installed UID, not a claim
that all bytes belong to one media selection. Selected-content estimates describe
HTTP payload size; measured UID totals additionally include TLS, HTTP headers, API
metadata, Range overlap caused by seek/buffering, thumbnails, and connected-test
traffic. This explains why they are not directly equal.

The physical run produced no current-process fatal event, ANR, or visible unbounded
retry. Automated boundaries cover exact/over-256 MiB PDF rejection before content,
512 MiB session storage, `Content-Length + 64 MiB` free-space enforcement, 32 MiB /
4096 px bitmap limits, corrupt PDF cleanup, cancellation, one Player/one item, mobile
5-15 second buffering, Range clamping, and stale job/request cancellation. Coil
remains capped at 64 MiB memory and 256 MiB disk. These are the accepted operating
values; this run found no evidence requiring a design-value change.

On the physical fixture set, the three generated list thumbnails were cache hits on
revisit (3/3) and did not show another generation transition. Cached photo and
video/audio revisits reached visible/ready playback state within the approved two-
and three-second interactive thresholds; an API `202` changed the photo/video UI to
the generating state within the one-second threshold without blocking navigation.
The 1,000-entry deterministic list/grid test uses distinct thumbnail identities and
recorded fewer than 100/fewer than 250 active slots, respectively, with zero
original-content requests; it tests request bounding rather than pretending that
1,000 production media bodies were transferred. The frame and memory values above
are the physical-process measurements, while cache/request bounds are asserted by
the instrumented and contract suites. No separate battery-drain benchmark was
performed for this short acceptance pass; battery impact remains bounded indirectly
by one-player/one-item playback, the approved buffer windows, no auto-play, bounded
polling, and the recorded UID traffic.

## Security, privacy, regression, and accessibility

- Media tokens remain Authorization headers only. Routes and cache/temp names use
  opaque IDs/scope hashes; redirects are disabled; no physical path or user-provided
  name becomes a local path.
- Invalid MIME, PDF signature/range/size failures, malformed media responses,
  unexpected `Content-Range`, short bodies, 416, redirect, permission loss,
  missing/trash/version changes, and stale engine/job completions fail closed.
- Logout, device/session invalidation, and route change close the scoped Player,
  polling, Coil cache, and PDF temporary storage. Existing auth, files, search,
  favorites/tags, sharing, download, trash, and restore instrumentation passed in the
  same 59-test physical run.
- `android:allowBackup="false"` remains set and credential metadata is also excluded
  by backup rules. There is no exported media `FileProvider`. Screenshot blocking is
  intentionally not forced by the approved design because of household use and
  accessibility impact.
- Playback position, skip direction/duration, speed, thumbnail state, photo, and PDF
  page expose semantics. Dark theme, portrait/landscape recreation, large/narrow
  layouts, gesture navigation, and touch-target bounds are covered by Compose tests
  and the physical pass; backgrounding pauses and does not auto-resume playback.

## Coverage and quality gates

The root Gradle build now produces aggregate JaCoCo reports and fails below the
formal thresholds. Infrastructure adapters that require Android/HTTP/Coil/Media3
are excluded from the Domain/Application JVM metric and are verified by instrumented
or contract tests. The PDF platform renderer is similarly verified on Android and
is not counted as a pure state-transition controller.

| Gate | Covered / missed lines | Result |
| --- | ---: | ---: |
| Domain/Application | 3,505 / 716 (83.04%) | pass, minimum 80% |
| Critical state transitions/controllers | 634 / 27 (95.92%) | pass, minimum 95% |

`scripts/ci/verify-android.sh` runs both gates and assembles every current
instrumentation module. It also generates a CycloneDX 1.6 app Release-runtime SBOM;
the accepted output contained 194 components and included all Media3 1.11.0 and
Coil 3.5.0 modules. Their published POM licenses are Apache-2.0. The Media3 Compose
POM omits a Compose dependency version required by CycloneDX's effective-POM
resolver, so that one SBOM component has no inferred license even though the
published Media3 POM itself declares Apache-2.0; generation reports the metadata
warning but retains the resolved component and dependency edge.

GitHub's global Maven advisory API returned zero advisories affecting
`androidx.media3:media3-exoplayer:1.11.0` and `io.coil-kt.coil3:coil:3.5.0`, and
both upstream repositories returned zero published repository security advisories
at verification time. Dependency locks contain the complete new direct/transitive
Media3 and Coil graph. The final repository pass succeeded: Android verification
completed 1,042 Gradle tasks, the coverage gate rerun passed, Server Release build
completed with zero warnings/errors and 81 Domain + 236 Application + 178
Integration tests passed, deployment/configuration and security verification passed,
and `git diff --check` plus ShellCheck/bash syntax checks passed.
