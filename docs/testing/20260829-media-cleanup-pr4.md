# Media Cache Cleanup PR4 Verification Record

## Scope and environment

- Date: 2026-08-29
- Target: Raspberry Pi 4 Model B, 8 GB RAM, Debian 12 ARM64, exFAT HDD
- Candidate: `0.4.0-media-pr4` (Pi E2E used code-identical RC2 before the final artifact build)
- Runtime tools: libvips 8.14.1, FFmpeg 5.1.9, Poppler 22.12.0
- Network routes: LAN and ZeroTier, one configured HTTPS hostname and the same private CA

Host addresses, usernames, storage identifiers, database credentials, tokens, file names, and physical paths are intentionally omitted.

## Representative generation profile

The benchmark generated synthetic fixtures in a unique `/tmp` workspace and removed that workspace on exit. GNU `time` reported wall time, aggregate CPU percentage, maximum resident set size, and filesystem input/output counts. Output sizes are logical bytes. Results are a single representative run and are not a latency SLO.

| Profile | Elapsed | CPU | Max RSS | fs in/out | Output bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Photo thumbnail, 512 px WebP Q75 | 0.32 s | 109% | 45,744 KiB | 0 / 32 | 5,510 |
| Photo low, 1280 px WebP Q70 | 0.67 s | 122% | 56,020 KiB | 0 / 64 | 27,108 |
| Photo medium, 2560 px WebP Q82 | 1.46 s | 125% | 72,372 KiB | 0 / 248 | 120,560 |
| Video thumbnail, 512 px WebP Q75 | 0.44 s | 154% | 99,268 KiB | 0 / 16 | 5,828 |
| Video low, 720p H.264/AAC | 7.64 s | 310% | 311,304 KiB | 0 / 2,448 | 1,252,435 |
| Video medium, 1080p H.264/AAC | 12.89 s | 309% | 549,628 KiB | 0 / 6,272 | 3,208,922 |
| PDF first-page raster | 1.52 s | 99% | 60,288 KiB | 8 / 144 | intermediate |
| PDF thumbnail, 512 px WebP Q75 | 0.62 s | 104% | 49,160 KiB | 0 / 16 | 2,000 |

The source fixtures were a 4000×3000 JPEG (699,285 bytes), six-second 1080p H.264/AAC video (11,691,188 bytes), and one-page synthetic PDF (2,502 bytes). The video API accepted a request in under one second, and two queued video jobs never exceeded one simultaneously RUNNING job.

The three measured thumbnail classes average 4,446 bytes. A simple `mean × 300,000` estimate is 1,333,800,000 bytes (about 1.24 GiB). This excludes filesystem allocation overhead and does not model a production MIME distribution. Capacity monitoring therefore reserves at least 2 GiB for 300,000 thumbnails and alerts on measured derivative-root growth and HDD free-space thresholds; thumbnail capacity remains separate from the 10 GiB low/medium cache watermark.

## Cache cleanup and lifecycle E2E

- Exactly 10 GiB of READY low/medium cache did not start capacity cleanup.
- Adding one byte above 10 GiB removed the stable oldest LRU entry and stopped at or below 6 GiB without deleting the newer entry.
- `expiresAt` immediately before the 24-hour boundary remained; `expiresAt <= Server UTC now` was eligible. Delivery lease expiry and access timestamp updates were exercised.
- Thumbnail, PENDING, active-generation/delivery lease, and normal `DELETING` candidates were excluded. Dedicated `DELETING` recovery was covered by persistence tests.
- A physical deletion failure restored READY state without touching the source; replacing the invalid target allowed the next cleanup run to finish.
- Trash removed low/medium variants but retained the thumbnail; restore reused it; permanent purge and MISSING index deletion removed all derivatives. Rename and move reused the same source version and cache.
- Owner, direct share, inherited share, outsider, permission change/removal, job state, explicit retry, and Range delivery authorization succeeded or failed as specified. No implicit original-quality fallback or partial output was observed.
- LAN and ZeroTier both passed TLS and API checks through the same configured hostname.

Large watermark tests used tiny physical files with synthetic database size metadata, so the HDD was not filled merely to exercise thresholds.

## Regression, failure injection, and recovery

- Regression covered list/detail, search, recent, favorites/tags, organization, share, upload/download, rename/move, trash/restore/purge, and MISSING flows.
- Source File ID, owner, version, logical size, and SHA-256 remained unchanged by generation and cleanup.
- PostgreSQL disconnect caused the Worker to fail without rewriting durable state; normal startup resumed after database recovery.
- HDD unmount caused fail-closed storage-unavailable behavior. Remount verified the same Storage ID before API and Worker restart.
- An impossible minimum-free-space reserve requeued generation with `STORAGE_UNAVAILABLE`; no disk-fill test was performed.
- Application rollback RC2 → RC1 and roll-forward RC1 → RC2 both switched the release symlink and restored healthy services. Matched database and Storage backups were retained.
- The API remained available for original-file operations while the Worker was stopped, and durable queue processing resumed after restart.
- API, Worker, Nginx safe access/error, and PostgreSQL service journals contained no tested file name, physical path, user name, search term, token, or full conversion command. Direct administrator SQL fault-injection text may exist in raw database administration logs and is not an application logging assertion.

## Release evidence

`./scripts/ci/build-release.sh 0.4.0-media-pr4` produced a linux-arm64 Server archive and signed Android APK. `sha256sum --check` passed for both artifacts. The Server archive contained no appsettings file, environment file, private key, credential file, or deployment secret. The target runtime package inventory is retained as the Media runtime SBOM alongside the release evidence.

All test users and artifacts used an explicit `PR4M-` marker. Final cleanup targets only those IDs and their derivative/job/lease/temp records, leaving operational backups and non-test catalog data untouched.
