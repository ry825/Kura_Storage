# Media Worker PR2 verification

## Scope

This record covers the PR2 image and thumbnail Worker, Media Job APIs, and
lease-protected derivative delivery. Video low/medium MP4 generation, cache
cleanup, and Android UI are outside this PR.

## Runtime dependency observations

The Raspberry Pi was queried read-only on 2026-08-29.

| Item | Observed result |
| --- | --- |
| OS architecture | Debian 12 `arm64` |
| `libvips-tools` / `libvips42` candidate | `8.14.1-3+deb12u3`; not installed before PR2 deployment |
| FFmpeg installed / candidate | `8:5.1.6-0+deb12u1+rpt1` / `8:5.1.9-0+deb12u1+rpt1` |
| Poppler installed / candidate | `22.12.0-2+b1` / `22.12.0-2+deb12u3` |
| FFmpeg encoders | `libwebp`, `libwebp_anim`, and `libx264` present |

The deployment scripts install the current authenticated Debian candidates,
verify binary paths/loaders/encoders, and record the resolved target inventory
instead of silently relying on the pre-PR2 host state.

## Automated verification

- `ExternalMediaGeneratorTests`: exact libvips, pdftoppm, FFmpeg, and ffprobe
  argument contracts, output metadata enforcement, fallback frame extraction,
  and workspace cleanup.
- `ExternalMediaToolIntegrationTests`: real libvips/Poppler/FFmpeg execution for
  image, PDF first-page, and video thumbnails plus corrupt image rejection. The
  test is enabled in CI by `KURASTORAGE_RUN_MEDIA_TOOL_TESTS=1`.
- `MediaProcessRunnerTests`: environment allowlist, non-shell arguments,
  timeout/process-tree termination, bounded diagnostics, and absolute paths.
- `MediaApiTests`: authorization, 200/202/206/404/409/416 behavior, concurrent
  request and retry convergence, Retry-After/job URL, Unicode RFC 5987 names,
  and post-delivery lease release.
- `MediaPersistenceTests` and `LeasedMediaResultTests`: generation/delivery
  owner tokens, renewal, maximum lease projection, atomic READY completion,
  range boundaries, 64 KiB streaming, cancellation, and release after stream
  disposal failure.
- `MediaJobRunnerTests`: no-work polling, generation ownership loss, heartbeat
  rejection, source/storage/generator failures, cancellation requeue, stale
  completion cleanup, and published-file cleanup after unexpected failure.

The real-tool test was additionally run in a disposable Ubuntu 26.04 container
with `libvips-tools`, `ffmpeg`, and `poppler-utils`; image, PDF, video, and
corrupt-input cases passed. CI repeats this test on every server job.

The final server verification completed with zero build warnings and the
following test totals:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 81 | 0 | 0 |
| Application | 219 | 0 | 0 |
| Integration | 166 | 0 | 0 |

Merged Coverlet line coverage is 88.51% for Domain and Application together
(Domain 91.81%, Application 87.59%). The PR2 state-transition, validation, and
authorization boundary files meet the 95% requirement: `MediaContracts.cs`
98.11%, `MediaJobRunner.cs` 99.02%, and `PreviewService.cs` 95.18%.

`verify-server.sh`, `verify-config.sh`, `verify-security.sh`,
`verify-deployment.sh`, formatting verification, OpenAPI YAML parsing, and
`git diff --check` all passed. Config and deployment grammar checks ran in the
repository verification image because the development host does not install
the `shellcheck` and `nginx` command-line tools.
