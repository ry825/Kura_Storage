# File/folder sharing and Android E2E record

## Scope

- Release under test: `0.6.0-pr4`
- Date: 2026-08-23
- Environment: production-equivalent Raspberry Pi, PostgreSQL, physical storage, LAN, ZeroTier, and a signed Android release
- Credentials, user identifiers, addresses, certificate fingerprints, and physical paths are intentionally omitted.

## Protection and rollout

- Confirmed the formal storage identity, active services, zero unfinished file operations, and zero active upload sessions before rollout.
- Created PostgreSQL and storage backups and confirmed that the backup inputs were readable.
- Applied the sharing migration before switching the API and Worker to the new release, then built the signed Android artifact.
- The first rollout attempt stopped before migration when required deployment templates were absent. Services were restored, the incomplete release directory alone was removed, and the corrected rollout was repeated.
- Confirmed the rollback constraint: after the schema migration, rollback must preserve sharing rows and must not silently remove recipient upload sessions.

## Automated and server E2E results

| Area | Result |
| --- | --- |
| Owner, Viewer, Contributor, Editor, Manager, and Admin actors | Passed |
| Direct file share, multiple ancestor-folder paths, direct/inherited source metadata, and strongest effective permission | Passed |
| List, detail, range download, folder creation, multipart upload, interrupted/resumed upload, rename, move, and trash authorization | Passed |
| Immediate permission update and share/member revocation | Passed |
| Inheritance change after move and direct-share preservation | Passed |
| Owner restore and recipient restore denial | Passed |
| Purge removal of descendant shares and members | Passed |
| Two-observation MISSING confirmation and missing-index deletion cleanup | Passed |
| Admin implicit-access denial | Passed |
| API, Worker, and PostgreSQL restart convergence | Passed |

The server scenario used six temporary users and exercised both successful and rejected operations through the public HTTPS API. Multipart and resumable uploads wrote to the physical storage. The MISSING scenario physically removed only the uniquely identified test file, performed two independent scans, advanced only that test row beyond the configured confirmation delay, and deleted the resulting missing index through the owner API.

## Network and failure injection

- LAN access passed with the configured HTTPS hostname, TLS validation, authentication, and sharing contract.
- The Raspberry Pi self-check through its ZeroTier interface passed with the same hostname and TLS contract.
- Missing idempotency/header requests were rejected as designed during negative-path validation.
- PostgreSQL, API, and Worker restarts completed with authorization state and storage/index state converged. A resumable upload was paused after its first chunk, resumed after API/Worker restart, and completed with the original checksum.
- A pre-existing inotify watch limit was below the documented recommendation; it did not block this rollout or test run.

## Android device flow

- Installed the signed non-debuggable `0.6.0-pr4` release and used five isolated temporary users on a physical Android 13 device.
- The Owner created both a direct File share and an inherited Folder share from the Android UI, selected candidates, assigned Viewer, Contributor, Editor, and Manager, and confirmed the Manager warning.
- The Viewer received only the direct File share and saw the Owner, `VIEWER`, `DIRECT`, and file-only scope. Download was available while Rename, Move, Trash, sharing management, Restore, and Purge were absent. File sharing offered Viewer, Editor, and Manager but not Contributor.
- The Contributor opened the shared Folder, saw the Folder name, Owner, `CONTRIBUTOR`, `DIRECT`, and share source, created a descendant Folder, and uploaded a large file. Wi-Fi interruption produced a recoverable failure with a “Resume from confirmed position” action; after Wi-Fi recovery the same upload resumed and completed.
- The Editor renamed the uploaded File, moved it into the Contributor-created Folder, observed inherited permission/source metadata, and moved it to Trash. The recipient UI did not expose Restore or Purge.
- The Manager opened sharing settings, changed the Contributor permission, removed that Member, then removed the entire Folder share. The settings screen immediately changed to the explicit access-lost state and no longer exposed the target.
- Admin implicit-access denial and mutation denials were confirmed by the server E2E; Android Viewer and unknown-permission UI remained fail-closed.
- The physical-device run exposed and fixed four reachability/recovery defects: clipped sharing actions in the File detail dialog, a non-scrollable sharing-settings screen, a shared Folder root labeled as personal “My files”, and a non-`IOException` SocketFactory failure that crashed the OkHttp dispatcher during network loss.
- `connectedDebugAndroidTest --max-workers=1` completed for all Android modules after the fixes, including 17 feature-files and 5 feature-sharing tests with zero failures.

## Cleanup and final state

- Removed only test-user storage roots, upload temporary data, upload-session data, file entries, operations, and shares.
- Revoked test refresh sessions and devices and disabled the temporary test users.
- Removed the two Android Download fixtures and reinstalled the signed release to clear the temporary device registration and credentials.
- Final checks: zero active test devices, zero test entries, zero test shares, zero active test upload sessions, zero total shares/members, zero unfinished file operations, and zero active upload sessions.
- All services were active and the formal storage identity and applied migration set were valid after cleanup.

## Duration

- Release build and rollout: approximately 10 minutes, excluding the safely stopped first attempt.
- Server and network E2E, failure injection, and cleanup: approximately 15 minutes across diagnostic and final runs.
- Android connected-device flow, defect correction, reruns, and cleanup: approximately 45 minutes.
