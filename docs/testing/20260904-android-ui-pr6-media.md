# Android UI PR6: Photo, video, audio, and PDF

## Scope

PR6 aligns the existing Android media viewers and players with references `016` through `019`. The UI change retains the established authenticated Coil, Media3, HTTP Range, persistent media-job, and bounded PDF temporary-file architecture. Server responses remain authoritative, original content still requires transfer confirmation, and unsupported or unknown states fail closed.

## Reference comparison

| Reference | Production implementation | State and interaction evidence |
| --- | --- | --- |
| `016-photo-viewer.png` | Places the title and current position above a bounded photo viewport with previous/next controls, pinch/pan/double-tap zoom, Low/Medium/Original selection, network and size context, quality-specific download, and details. | Compose and ViewModel tests cover confirmation, position, adjacent-entry revalidation, zoom bounds, quality generation/failure, missing entries, and 200% text. |
| `017-video-player.png` | Uses a 16:9 Media3 surface, play/pause, seek, 3/10-second skips, 0.5-3.0x speed, duration, full-screen orientation, quality selection, and typed conversion/reconnection panels. | JVM tests cover position/rate/play-state restoration across quality changes and rotation, stale-engine rejection, background pause, disconnect, unsupported codec, and no automatic Original fallback. |
| `018-audio-player.png` | Uses an accessible artwork/type placeholder, original size and transfer confirmation, common seek/skip/speed controls, and actionable disconnect/codec errors. Video quality and conversion controls are absent. | Compose tests assert the Original-only contract and 48 dp controls; ViewModel tests verify confirmation, Range playback, reconnection, and non-retrying codec failure. |
| `019-pdf-viewer.png` | Shows MIME, size/estimated transfer, Range support, limits, private-cache confirmation, indeterminate download/render progress, one-page rendering, page navigation/input, 1-4x zoom, and download fallback. | JVM and Android tests cover exact/over-256 MiB boundaries, 512 MiB session storage, free-space reserve, signature validation, lease/TTL cleanup, bounded bitmap rendering, and safe errors. |

## Adaptive, lifecycle, and accessibility evidence

- All viewer roots use safe drawing insets and a scrollable hierarchy. Adaptive action groups stack at 200% font scale or compact width.
- The API 33 connected suite rendered a 320 x 640, 200% font-scale, Dark-theme full-screen video fixture with capture evidence and kept the video surface, 48 dp skip controls, and 3.0x speed reachable.
- Player state is retained by the ViewModel when the engine is replaced during rotation. Backgrounding pauses playback, and returning does not create a second player or auto-resume unexpectedly.
- Seek, speed, skip direction/duration, photo/PDF content, loading progress, selected quality, and disabled controls expose visible text or semantics.

## Intentional differences from the references

- Quality is labelled Low, Medium, and Original instead of fabricated resolution labels. The server contract exposes profiles, not a guaranteed display resolution.
- The audio reference shows a 5-second back action, while the formal product contract requires both 3-second and 10-second movement for video and audio. The production player follows the formal contract.
- Decorative sample photography, artwork, waveforms, and document content are not bundled. Production content, the real video surface, the current PDF page, or an accessible type placeholder occupies those regions.
- PDF pages are rendered one at a time from a private temporary file. The app does not construct a long in-memory document preview resembling the static reference.
- A video conversion continues as a server job when the user leaves. The UI does not add WorkManager or imply that the Android process owns the conversion.

## Resource and route evidence

The accepted physical Android 13 baseline from `20260830-android-media-integration-pr4.md` remains applicable because this PR changes Compose presentation without changing buffer, cache, decoder, networking, or controller limits:

| Physical Android 13 observation | Accepted baseline |
| --- | ---: |
| Remote-video PSS / RSS | 117,912 / 259,820 KiB |
| Frames rendered / janky | 343 / 55 (16.03%) |
| Frame median / p90 / p95 / p99 | 13 / 38 / 53 / 125 ms |
| Primary control height | 48 dp (144 px at 3x density) |
| Fatal event / ANR / unbounded retry | None |

That physical pass covered both `LOCAL_DIRECT` and cellular plus ZeroTier `REMOTE_SECURE`, original-transfer confirmation, live disconnect/reconnect, and real photo/PDF/video/audio content. No physical device was attached for this UI-only PR6 rerun. Current UI regressions were verified on the same Android 13/API 33 platform using deterministic fixtures; PR10 remains responsible for the final post-alignment physical-device, TalkBack, rotation, and real-server pass.

## Verification

| Check | Result |
| --- | --- |
| API 33 feature-media connected tests | Passed: 14 tests |
| API 33 core-data connected tests | Passed: 10 tests |
| API 33 app connected tests | Passed: 8 tests |
| JVM and focused quality checks | Passed: media/app unit tests, compilation, ktlint, detekt, and `git diff --check` |
| Full Android verification | Passed: 1,387 Gradle tasks, including unit tests, coverage gates, ktlint, detekt, Android Lint, SBOM generation, and debug/test builds |
