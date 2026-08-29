# Media video Worker PR3 verification

## Scope

This record covers fixed-profile video low/medium MP4 generation, durable
progress and heartbeat handling, stale-job recovery, video delivery, Worker
observability, and independent Worker deployment. Cache cleanup, Raspberry Pi
performance acceptance, and Android playback remain in PR4.

## Runtime dependency observations

The Raspberry Pi was queried read-only on 2026-08-29. Debian 12 arm64 provides
FFmpeg and ffprobe 5.1.6 with `libx264`, AAC, and `-progress` support. The
`kurastorage-api` service account can execute the tools and access the HDD
workspace. The observed service constraints are a 3 GiB memory limit, CPU and
I/O weights of 50, 128 tasks, and a 45-second stop timeout.

Deployment verification now checks the resolved FFmpeg version, H.264/AAC
encoders, ffprobe, and progress support. The Worker remains independently
restartable and has no HTTP listener. Its systemd process group is terminated
together and an out-of-memory stop is surfaced instead of leaving child
transcoders behind.

## Video fixtures and real tools

The real-tool integration test ran against FFmpeg/ffprobe 8.0.1 from a
disposable `/tmp` extraction; nothing was installed on the development host.
It successfully generated and probed:

- a short horizontal video without audio using the low profile; and
- a longer vertical video with multiple audio streams using the medium profile.

The resulting files were complete `mp4` containers with H.264 video, optional
AAC audio, dimensions within 720p/1080p limits, a maximum 30 fps rate, no
upscaling, and one selected audio stream. Unit and integration fixtures cover
corrupt input, unsupported codecs and output metadata, oversized/continuous
progress output, invalid probe data, cancellation, timeout, and process-tree
termination.

## Recovery and failure matrix

Automated failure injection verifies DB disconnection and unknown completion,
HDD unavailable/read-only/capacity failures, source read failure, process kill
and cancellation, API rehost with a durable queued job, stale Worker recovery,
and atomic publish followed by an unknown DB completion. These paths keep the
source read-only, do not expose partial output, use exact job/attempt temporary
paths for cleanup, and prevent an old Worker from overwriting a recovered job.

Periodic heartbeat rejection stops work without changing newer durable state.
A heartbeat exception stops the in-flight transcode, releases its generation
lease, and requeues the job as `MEDIA_WORKER_UNAVAILABLE` without terminating
the Worker loop. Retry is limited to three total executions: the initial run
plus automatic retries after 30 seconds and two minutes.

## Automated verification

The final server verification completed with zero build warnings:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 81 | 0 | 0 |
| Application | 231 | 0 | 0 |
| Integration | 175 | 0 | 0 |

Merged Coverlet line coverage is 94.76% overall. The added state, validation,
and authorization boundaries meet the 95% requirement: `MediaContracts.cs`
96.83%, `MediaJobRunner.cs` 97.32%, and `PreviewService.cs` 95.21%.

`verify-server.sh`, `verify-config.sh`, `verify-security.sh`,
`verify-deployment.sh`, formatting verification, OpenAPI parsing, and
`git diff --check` passed. Config and deployment checks used disposable
`shellcheck` and `nginx` packages from `/tmp` because those tools are not
installed on the development host.
