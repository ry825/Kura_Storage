# Favorites and tags PR2 E2E record

## Scope

- Date: 2026-08-29 (Australia/Melbourne)
- Server: Raspberry Pi 4 Model B, ARM64 Release `0.8.0-pr2.2`
- Android: OPPO CPH2333, Android 13, signed non-debuggable Release `0.8.0-pr2.2` (`versionCode` 11)
- Routes: `LOCAL_DIRECT` over the Raspberry Pi Wi-Fi LAN and `REMOTE_SECURE` over ZeroTier
- Isolation: users, files, folders, shares, tags, and favorites used the `pr2-ft-*` limited identifier

No password, token, private key, Tag value outside the limited identifier, real User name, real File name, physical path, or response body is recorded here.

## Protection and rollout

The production database and Storage Root were matched before rollout. PostgreSQL and Storage Root backups were taken and their readability, checksums, ownership, and listing were verified. The retained pre-PR2 backups are:

- `/var/backups/kurastorage/pre-0.8.0-pr2-matched.dump`
- `/var/backups/kurastorage/storage-pre-0.8.0-pr2-matched.tar.gz`

Upgrade backups were also created before each final rollout, including `/var/backups/kurastorage/pre-0.8.0-pr2.2.dump`. The `AddFavoritesAndTags` migration was applied before the Favorites／Tags API was exercised. The final rollout had no pending migration, activated `0.8.0-pr2.2`, and passed Nginx, API, Worker, PostgreSQL, Storage mount identity, and health verification. Rollback remains application-first while retaining the backward-compatible tables; schema Down requires a new backup and explicit approval because it deletes private organization metadata.

## API E2E

The following tests passed with limited Owner, Viewer, and Admin users:

- Favorite File／Folder registration, idempotent removal and addition, stable pagination, concurrent duplicate PUT requests, and per-User isolation.
- Tag create, rename, delete, normalized-name conflict, 20／21 Entry boundary, 10／11 Search boundary, duplicate Tag rejection, and concurrent mutation.
- Tag-only AND search and combined name, type, date, size, Owner／shared source, status, and later-page filtering.
- Private Tag isolation from an unshared User and Admin, permission loss and reacquisition, Share removal, and stale-access rejection.
- Rename, Move, Trash, Restore, Purge, and physical-file removal through `MISSING` with Favorites／Search reflecting authoritative state.
- Upload, byte-identical Download, Recent, missing-index deletion, and existing Personal／Shared／Search behavior.

## Signed Android E2E

The signed Release was registered only after the device joined the same physical Wi-Fi LAN as the Raspberry Pi. Registration over ZeroTier was rejected with `Local connection required`, as designed.

On `LOCAL_DIRECT`, the signed app successfully:

- registered and authenticated a limited Android user;
- created a limited Folder;
- added and removed the Folder as a Favorite;
- created, attached, detached, and deleted a limited Tag;
- displayed the Favorite from the Home entry with current Owner, Permission, and Source metadata;
- returned the Folder from Tag-only Search; and
- logged out and removed the device credential locally.

On `REMOTE_SECURE`, with Wi-Fi temporarily disabled and ZeroTier as the only KuraStorage route, the same registered device automatically refreshed its session and displayed the Favorite and Tag-only Search result through the same HTTPS Hostname and API contract. Wi-Fi, ZeroTier, display rotation, and stay-awake settings were restored after the test.

Physical rotation exposed an existing startup defect: a stored Refresh Token displayed the password form instead of refreshing after Activity recreation. `AuthViewModel` was changed to refresh a valid stored credential automatically. A Unit Test covers password-free restoration, and the final signed build returned to authenticated Home after both process restart and portrait／landscape recreation.

## Performance

The committed 300,000-entry fixture and Raspberry Pi measurements passed the normal two-second target. Raspberry Pi p50 was 740 ms and p95／maximum was 1,948 ms with zero errors. One-Tag, ten-Tag AND, name＋Tag, shared＋Tag, MISSING＋Tag, and later-page cases used the intended bounded indexed path. Full details are in `docs/testing/20260829-favorites-tags-pr2-performance.md`.

## Automated verification

- Android standard verification: 784 tasks passed.
- Physical connected tests: `feature-search` 10／10 and `app` 3／3 passed on the final HEAD.
- Server verification: Domain 59, Application 174, and Integration 116 tests passed.
- Configuration, security, and deployment verification passed.
- Release verification confirmed APK Signature Scheme v3, one RSA-4096 signer, the approved certificate fingerprint, `versionCode` 11, `versionName` `0.8.0-pr2.2`, and no debuggable flag.
- `git diff --check` passed.

## Log and cleanup result

Final scans found none of the limited identifiers, payload markers, bearer／JWT patterns, or Storage path marker in API, Worker, PostgreSQL, Nginx, or Android logs.

Cleanup first stopped safely on a temporary-table SQL syntax error before deleting any row; the Worker restart trap succeeded. After correcting and rechecking the script, exactly 10 limited users and their 17 File entries, 23 Tags, 5 Favorites, 3 Shares, related operation rows, and UUID-scoped Storage directories were removed. The final assertions were:

- limited users: 0;
- orphan Favorites: 0;
- orphan Entry-Tag relations: 0;
- unfinished File operations: 0; and
- active Upload sessions: 0.

Real Users, real Files, real Shares, backups, deployment configuration, and operational credentials were preserved. Final Nginx, API, Worker, PostgreSQL, Storage, and release verification passed after cleanup.
