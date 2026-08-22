# Resumable Upload PR2 E2E Result

## Scope and environment

- Date: 2026-08-22 (Australia/Melbourne)
- Server: Raspberry Pi 4 Model B Rev 1.4, ARM64, 8 GiB RAM
- Storage: shared exFAT HDD mounted at the configured dedicated mount; no OS-root fallback
- Server release: `0.4.0-pr2.4`
- Android: OPPO CPH2333, Android 13, signed release APK
- Routes: `LOCAL_DIRECT` over LAN and `REMOTE_SECURE` over ZeroTier
- Upload configuration: preferred chunk 4 MiB, maximum chunk 8 MiB, two concurrent chunk writes, five active sessions per device, 5-second overload retry

The test account, device identifiers, credentials, physical storage paths, and access tokens are intentionally excluded.

## Transfer results

| Case | Route | Source size | Interruptions | Result |
|---|---|---:|---|---|
| Small binary | `LOCAL_DIRECT` | 1 MiB | None | Completed; source and server size/SHA-256 matched |
| Large video-shaped payload | `LOCAL_DIRECT` | 256 MiB | Nginx stopped for 7 s at confirmed offset 83,886,080; API stopped for 7 s at confirmed offset 159,383,552 | Resumed the same Session from each server-confirmed offset and completed; source and server size/SHA-256 matched |
| Small binary | `REMOTE_SECURE` | 1 MiB | Route changed from LAN to ZeroTier before upload | TLS, authentication, route classification, size, and SHA-256 verified |

Source hashes used for integrity comparison:

- 1 MiB payload: `92e4655aabe8e78bf590c75fb9f596361f2ff4894fc724edbc17f982f962f028`
- 256 MiB payload: `40edafee2a7bd8a18da9230dcd16aad1f3e147c79a78f93710ab0b6ab43708ee`

After each service recovery, visible progress resumed within the next retry/manual-resume cycle (no more than 5 seconds after the Resume action). The confirmed offset, Session ID, and Idempotency Key remained authoritative; the client did not infer completion from a lost response.

Disabling Wi-Fi caused the Android process to be reclaimed on one exploratory run. Restart showed the sign-in screen instead of claiming a resumable operation. This is the specified clear-failure behavior because process-death persistence with Room/WorkManager is outside PR2. Backgrounding without process death retained the ViewModel operation; API and Nginx interruptions produced an explicit paused/retry state.

## Resource observations

| Process | Baseline total PSS/RSS | Upload samples | Maximum observed |
|---|---:|---|---:|
| Android total PSS | 119,462 KiB | 97,881; 97,690; 106,646 KiB | 106,646 KiB |
| Server RSS | 140,560 KiB | 157,444; 167,940; 175,876 KiB | 175,876 KiB |

The 256 MiB source was never loaded as one byte array. Android PSS did not grow with the file size, and server RSS increased by at most 35,316 KiB over baseline, remaining bounded below the configured service memory limit. Four MiB chunks, the two-write concurrency limit, and retry behavior remained responsive on this Raspberry Pi and phone.

During a separate 32 MiB active upload, the authenticated file list responded in 60.9 ms, health in 5.5 ms, token refresh in 166.8 ms, and a 900-byte Range download in 64.1 ms. The same Session completed after an API restart and startup Recovery/Cleanup, while the prepared expired Session reached `EXPIRED` with `cleaned_at` set. The bounded cleanup implementation was also exercised by integration tests while active Session and existing File contracts remained independent.

The first concurrency runs exposed a startup race: a tracked recovery candidate could retain an old DB offset while waiting for the Session advisory lock, then truncate a newly accepted chunk to that stale offset. Candidate queries were changed to no-tracking, startup Recovery/Cleanup was moved to the hosted-service `StartingAsync` phase, and an integration regression test now requires candidates to be detached before the locked authoritative reload. The corrected ARM64 binary was SHA-256 matched to the deployed `0.4.0-pr2.4` release before the successful rerun. Failed-run E2E Sessions were backed up, terminally cancelled with explicit audit records, and their isolated temporary files removed before the final run.

## Fault and cleanup results

| Fault | Observed boundary | Publication/cleanup result |
|---|---|---|
| Corrupt chunk checksum | `422 CHUNK_CHECKSUM_MISMATCH` | Session explicitly cancelled; no public or temporary file remained |
| Capacity request above available space | `507 STORAGE_CAPACITY_INSUFFICIENT` | No Session/file created |
| HDD read-only | `503 STORAGE_UNAVAILABLE` | Session and temporary-file counts unchanged; mount restored read-write |
| HDD unmounted | systemd stopped API/Worker; Nginx returned 502 | Dedicated mount absent, fallback root absent, no public/temporary file; mount and services restored active |
| Device revoked | existing token rejected; login returned `403 DEVICE_REVOKED` | No new Session/file created |
| Session expired | `409 UPLOAD_SESSION_EXPIRED` | Session marked `EXPIRED`; cleanup removed temporary file |

The final database state contained only terminal Sessions (`COMPLETED`, `CANCELLED`, or `EXPIRED`), zero active Sessions, zero matching incomplete public files, and zero files under the upload-session temporary area. Audit records contained successful create, complete, cancel, device-revoke, and rejected-login events. The integration metric test confirmed upload counters and low-cardinality, non-sensitive dimensions.

## Compatibility and UI regression

- Legacy Multipart Upload completed with the 1 MiB payload.
- A 900-byte Range (`100-999`) returned `206`, the correct `Content-Range`, and byte-for-byte matching content.
- Android instrumentation tests cover confirmed progress, resume, cancel confirmation, system Back dismissal, long filename semantics, and 0%/100% boundaries.
- Physical rotation recreated the signed app in landscape and retained a clear sign-in state after device revocation; no stale success or upload state was shown.
