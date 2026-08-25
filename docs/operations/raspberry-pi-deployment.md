# Raspberry Pi deployment

## Scope and prerequisites

This procedure installs the MVP API and trash-retention Worker on Debian 12 ARM64 with PostgreSQL 17,
Nginx, nftables, an existing shared exFAT HDD, and externally managed ZeroTier.
Run deployment commands from the Raspberry Pi local console or an explicitly
authorized LAN administration session. Do not expose SSH through ZeroTier.
The generated firewall permits TCP 22 only from the configured LAN CIDR so
that key-authenticated maintenance remains possible; it rejects SSH arriving
through the ZeroTier interface.

Required packages are `.NET` self-contained artifact dependencies, PostgreSQL
17, Nginx, nftables, `curl`, `gettext-base`, OpenSSL, `findmnt`, `blkid`, and
Linux exFAT support. The deployment unit mounts the existing filesystem by
UUID at `/mnt/KuraStorage-hdd` and uses `/mnt/KuraStorage-hdd/KuraStorage` as
the application data root. It does not format the device or delete existing
files.

exFAT has no POSIX per-file ownership or journal. The mount unit therefore
assigns the entire filesystem to the numeric `kurastorage-api` UID and shared
`kurastorage` GID with `fmask=0007,dmask=0007,nodev,nosuid,noexec`. Only the
API and explicitly configured operations user belong to this group. Do not maintain a conflicting
mount definition for the same target in `/etc/fstab`. After power loss or an
unsafe disconnect, inspect and repair the filesystem before restarting writes,
then verify database/file-operation consistency.

## Prepare non-secret configuration

1. Copy `deployment/config/server/environment.example` outside the repository.
2. Replace every `SET_LOCALLY` value and set mode `0600`, owned by root.
3. Keep the Root CA private key offline. Supply only the Root CA public
   certificate, server certificate/key, ES256 JWT key, and release artifact.
4. Set `KURASTORAGE_DEPLOY_CONFIG` to the protected configuration file.

For the current Raspberry Pi, set the storage values as follows after reading
the exFAT UUID with `blkid`:

```text
KURASTORAGE_STORAGE_MOUNT_PATH=/mnt/KuraStorage-hdd
KURASTORAGE_STORAGE_ROOT=/mnt/KuraStorage-hdd/KuraStorage
KURASTORAGE_STORAGE_MOUNT_UNIT='mnt-KuraStorage\x2dhdd.mount'
KURASTORAGE_STORAGE_ACCESS_GROUP=kurastorage
KURASTORAGE_STORAGE_ACCESS_USER=ryama
```

The ZeroTier Network ID and member credentials are never inputs to deployment
scripts. Join and authorize the Raspberry Pi and Android device using the
external ZeroTier controller before E2E validation.

## Certificate and signing preparation

Use a protected offline directory:

```bash
export KURASTORAGE_CA_PASSPHRASE_FILE=/protected/ca-passphrase
./scripts/maintenance/generate-tls-certificates.sh \
  /protected/kurastorage-pki api.kurastorage.home.arpa
./scripts/maintenance/generate-jwt-signing-key.sh \
  /protected/kurastorage-jwt/jwt-signing-key.pem
```

Move `root-ca/root-ca.key` offline immediately after issuance. Validate an
existing chain with `verify-tls-certificates.sh` before every deployment.

## Initial install

Build and checksum the release as described in `release.md`, copy the server
artifact and non-CA-private material to protected temporary paths on the Pi,
then run:

```bash
sudo --preserve-env=KURASTORAGE_DEPLOY_CONFIG \
  deployment/raspberry-pi/install.sh
```

The installer creates the restricted OS account, remounts the existing exFAT
HDD with the required ownership and safety options, validates the mount source
without formatting it, creates the
PostgreSQL role/database, applies migrations explicitly, renders Nginx,
systemd, and nftables configuration, activates a versioned release, and runs
the deployment verifier. It does not install or configure ZeroTier.
If UFW is already active, the installer keeps it enabled and adds only the
matching KuraStorage LAN SSH, LAN HTTPS, and ZeroTier HTTPS allow rules. This
is required because an accept verdict in the KuraStorage nftables base chain
does not bypass a later UFW base chain with a drop policy.

If the Pi already has an older KuraStorage deployment whose
`__EFMigrationsHistory` does not match this repository, do not run the new
migrations against that database. First create a custom-format `pg_dump` and
copy the old release, `/etc/kurastorage`, service, Nginx, firewall, and mount
configuration into a root-only backup directory. Configure a different
database name for the new MVP install. Keep the old database and backup until
the new install, reboot, and rollback checks have all succeeded.

Create the first administrator locally without placing the password in shell
history:

```bash
cd /opt/kurastorage/current
sudo -u kurastorage-api env \
  DOTNET_ENVIRONMENT=Production \
  KURASTORAGE_SECRETS_DIR=/etc/kurastorage/secrets \
  ./KuraStorage.AdminCli user create admin Administrator ADMIN --password-stdin
```

Type the password through standard input. Do not pipe a shell-literal password.

## Service operations

```bash
sudo systemctl status kurastorage-api nginx postgresql
sudo systemctl status kurastorage-worker
sudo systemctl start kurastorage-api
sudo systemctl start kurastorage-worker
sudo systemctl stop kurastorage-api
sudo systemctl stop kurastorage-worker
sudo systemctl restart kurastorage-api
sudo systemctl restart kurastorage-worker
sudo --preserve-env=KURASTORAGE_DEPLOY_CONFIG \
  deployment/raspberry-pi/verify.sh
```

The API must run as `kurastorage-api` and expose only
`/run/kurastorage/api.sock`. Nginx is the only TCP HTTPS listener.
The installed Nginx systemd drop-in waits for the LAN address, the ZeroTier
Managed IP, and the API Unix socket before validating and binding HTTPS.
This prevents a boot-time failure when ZeroTier assigns its address after the
base network has reached `network-online.target`.

The Worker exposes no socket or TCP port. It runs once after process start and
then every configured `KURASTORAGE_TRASH_INTERVAL_HOURS`. Inspect only
operation identifiers and aggregate counts:

```bash
sudo systemctl status kurastorage-worker
sudo journalctl -u kurastorage-worker --since today
sudo -u postgres psql --dbname=kurastorage --command \
  "SELECT id, started_at, completed_at, status, examined_root_count, deleted_root_count, released_bytes, error_count FROM trash_purge_runs ORDER BY started_at DESC LIMIT 10;"
```

`released_bytes` and the Admin API `trashBytes` are estimates based on database
file-size snapshots, not exFAT allocation measurements. If a run fails, verify
PostgreSQL, the HDD mount and identity, and read-only state before restarting
the Worker. A restart recovers stopped runs and incomplete purge journals before
examining new candidates. Do not edit run counters or delete journal rows.

### External index watcher and rescan

PR2 installs the inotify and full-rescan workers, but keeps `Indexing.Enabled`
set to `false` until the Protocol 2 Android app, API, Worker, and migration are
deployed as one reviewed rollout. Deploy in this order: distribute and verify
the Android Protocol 2 build; stop mutation traffic for the maintenance window;
back up PostgreSQL and the Storage Root; deploy API and Worker artifacts; apply
the migration; run the dry-run below; set the .NET environment key
`Indexing__Enabled=true` for the Worker; restart the Worker; then run and verify
a full APPLY scan. Do not enable indexing while a
Protocol 1 client can still reach File APIs. First review aggregate dry-run counts;
the command does not print physical paths or persist scan state:

```bash
cd /opt/kurastorage/current
sudo -u kurastorage-api env \
  DOTNET_ENVIRONMENT=Production \
  KURASTORAGE_SECRETS_DIR=/etc/kurastorage/secrets \
  ./KuraStorage.AdminCli index rescan --dry-run
sudo install -d -m 0755 /etc/systemd/system/kurastorage-worker.service.d
printf '[Service]\nEnvironment=Indexing__Enabled=true\n' | \
  sudo tee /etc/systemd/system/kurastorage-worker.service.d/indexing.conf >/dev/null
sudo systemctl daemon-reload
sudo systemctl restart kurastorage-worker
```

The Admin CLI working directory must be the selected release directory so it
loads that release's `appsettings.json`. After enabling the Worker, inspect its
startup APPLY summary instead of starting a competing manual APPLY scan. To
disable indexing, remove only the reviewed `indexing.conf` drop-in, run
`systemctl daemon-reload`, and restart the Worker. Do not use
`KURASTORAGE_Indexing__Enabled`; the standard .NET configuration provider reads
the unprefixed `Indexing__Enabled` key.

After deployment, confirm Health reports `protocolVersion: 2`. On Android,
verify `MISSING_CANDIDATE` is shown as an item being checked, `MISSING` exposes
recheck and index-only deletion, and an unknown status requests an app update.
The index-only delete endpoint must remain usable while the HDD is unavailable
because it changes only PostgreSQL. Its confirmation must explicitly state that
no HDD file is deleted. Recheck requires the matching mounted Storage ID and
returns `STORAGE_UNAVAILABLE` without advancing missing state when storage or an
individual path cannot be read safely.

The defaults start a scan after Worker startup, scan every six hours, debounce
the same relative path for 500ms, pair Move events for one second, and bound the
event queue at 4096. The management CLI, startup scan, scheduled scan, and
overflow recovery share one PostgreSQL advisory lock; an overlapping run is
rejected and must not be treated as success.

Check the process descriptor limit and the kernel watch limit before enabling:

```bash
systemctl show kurastorage-worker --property=LimitNOFILE
sysctl fs.inotify.max_user_watches fs.inotify.max_queued_events
find /srv/kurastorage/users -type d | wc -l
```

`LimitNOFILE` is 65536 and `fs.inotify.max_user_watches` should be at least
65536 for the planned dataset. Raise a lower kernel value only to the measured
directory count plus operational headroom through a reviewed file under
`/etc/sysctl.d/`; do not set an unlimited or maximum integer value. The deploy
scripts report a low value but do not mutate host sysctl settings.

For a watcher stop, watch-limit error, queue overflow, or failed scan, keep the
HDD mounted, inspect `journalctl -u kurastorage-worker`, run the dry-run command,
and restart the Worker only after PostgreSQL, mount identity, and permissions
are healthy. A restart recreates watches before the startup scan and resumes
event processing only after a successful scan. Repeated individual-path errors
must not be fixed by changing database rows manually.

If the HDD is disconnected, leave the Worker running or stop it cleanly; it
must not mark every entry missing. Reconnect only the HDD whose
`.storage-identity` matches configuration, verify the mount, then restart the
Worker and run a dry-run. For a replacement HDD, keep indexing disabled and use
the documented storage replacement and database restore procedure. Never join
a different Storage ID to the existing catalog.

When the Admin API reports a capacity warning, first permanently delete only
known-unneeded trash through the authenticated manual operation, then plan
storage expansion, and finally investigate mount or filesystem faults. The
warning never shortens the 30-day minimum and never selects younger trash for
automatic deletion.

To verify the storage boundary independently:

```bash
sudo --preserve-env=KURASTORAGE_STORAGE_MOUNT_PATH,KURASTORAGE_STORAGE_ROOT,KURASTORAGE_STORAGE_DEVICE_UUID,KURASTORAGE_STORAGE_ID \
  scripts/maintenance/verify-storage.sh
```

## Upgrade and rollback

### Resumable upload operations

The Upload Session API is additive: older Android clients continue to use
`POST /api/v1/files/upload`, while the new client requires the resumable
endpoints and never silently falls back to whole-file upload. Apply the
database migration and server release before distributing the matching
Android APK.

Before deployment or a destructive fault-injection test, back up PostgreSQL
with `scripts/maintenance/backup-database.sh` and take a storage-root backup
using [backup-recovery.md](backup-recovery.md). Do not include a production
file name, device identifier, temporary path, token, or full checksum in a
ticket or normal log.

Inspect session state with aggregate, non-sensitive queries:

```sql
SELECT status, count(*)
FROM upload_sessions
GROUP BY status
ORDER BY status;

SELECT count(*) AS expired_active_sessions
FROM upload_sessions
WHERE status = 'ACTIVE' AND expires_at <= CURRENT_TIMESTAMP;
```

Use `kurastorage.upload.active_sessions`, `kurastorage.upload.sessions`,
`kurastorage.upload.cleanup`, `kurastorage.upload.recovery`,
`kurastorage.upload.chunks`, and `kurastorage.upload.failures` for routine
monitoring. A client may safely retry a network failure,
429, or temporary 503 with the same Session ID, Idempotency Key, and
server-confirmed offset. An offset conflict must be followed by a Session GET;
never force a client offset into the database. Expired, cancelled, revoked, or
source-changed uploads require explicit user action and must not be silently
recreated.

For explicit cancellation, use the Android confirmation action or
`DELETE /api/v1/upload-sessions/{sessionId}` as the owning authenticated
device. Cleanup handles terminal-session temporary files idempotently. For a
`RECOVERY_REQUIRED` session, preserve the database and storage backup, stop
new writes if storage integrity is uncertain, and restart the matching server
release so startup recovery can reconcile known states. Do not edit
`received_bytes`, remove a temporary file, or publish it manually.
Startup Recovery and Cleanup finish before the API begins accepting HTTP
requests. If startup remains unhealthy, inspect the service journal and the
storage/database prerequisites instead of bypassing that gate.

Before rolling back to a release without Upload Session support, ensure no
rows are `ACTIVE`, `COMPLETING`, or `RECOVERY_REQUIRED`. Cancel active sessions
with the owning clients, allow terminal cleanup to finish, and resolve
recovery-required sessions on the current release. `rollback.sh` enforces this
check in addition to unfinished purge journals. If the old release cannot read
the newer schema, restore the reviewed PostgreSQL and storage-root backup as a
matched pair; never roll back only one side.

### File sharing rollout and rollback

File sharing and shared-destination uploads require the `AddFileSharing`
migration. During the maintenance window, stop API and Worker mutation traffic,
take matched PostgreSQL and Storage Root backups, apply migrations explicitly,
then deploy and start the API and Worker from the same release. Do not start a
sharing-capable server against the previous schema, and do not expose sharing
endpoints until migration and health checks have succeeded.

After rollout, verify aggregate state without recording names or paths:

```sql
SELECT count(*) AS shares, (SELECT count(*) FROM share_members) AS members
FROM shares;

SELECT status, count(*)
FROM upload_sessions
GROUP BY status
ORDER BY status;
```

If a member is removed or reduced below `CONTRIBUTOR` while an Upload Session
is active, completion is intentionally rejected and its temporary content is
not published. Have the same authenticated actor/device cancel the session, or
allow expiry and the normal cleanup worker to remove the temporary file. Never
move a session temporary file into an owner's tree manually.

Migration Down refuses to run while any Share exists or any Upload Session has
different actor and target-owner IDs. Before a schema rollback, remove Shares
through the authenticated API, cancel or finish shared-target sessions on the
current release, wait for cleanup, and verify both conditions with aggregate
queries. If those conditions cannot be met safely, restore the pre-upgrade
PostgreSQL and Storage Root backups as a matched pair instead of forcing the
migration or deleting rows directly.

For the Android sharing rollout, keep the order `PostgreSQL backup and Storage
Root backup -> migration -> API -> Worker -> signed Android release`. Confirm
the API and Worker use the same immutable release and that health checks pass
before installing the client. The Android release must use the configured
HTTPS hostname and the same public Root CA for LAN and ZeroTier; do not build a
route-specific APK. After installation, verify owned and received share lists,
all four permissions, direct and inherited sources, and member/share removal.
An older client may continue personal file operations, but it must not be used
to infer or cache sharing authorization.

Before rollback, stop new mutations and inspect aggregate Share, member,
non-completed Upload Session, and File Operation counts without selecting user
names or physical paths. Finish or cancel shared-target sessions with the
matching actor/device. Because Migration Down refuses active Shares and
actor/target-owner sessions, prefer application rollback only while the schema
remains compatible. If schema restoration is required, restore the PostgreSQL
and Storage Root backups as the matched pair captured before rollout; never
delete Share rows or session rows directly to force rollback.

### Search index rollout and rollback

`AddSearchIndexes` enables `pg_trgm` and builds two partial expression indexes
on managed `file_entries`. Before applying it, take the normal PostgreSQL backup,
confirm free database volume is at least the current `file_entries` table plus
50 percent headroom, record the active session count, and stop neither API nor
Worker unless the measured I/O pressure requires a maintenance window. The
indexes use `CREATE INDEX CONCURRENTLY`, so the migration must be run by a role
allowed to create extensions and indexes and must not be wrapped in an external
transaction. Production API startup never applies it automatically.

Apply the Migration before deploying the Search API. Monitor `pg_stat_progress_create_index`,
database volume, CPU, and I/O until both indexes are valid. If creation is
interrupted, retain the backup, remove only an invalid index with the reviewed
Migration Down or an explicit `DROP INDEX CONCURRENTLY`, and rerun the same
Migration; do not drop `file_entries` or rewrite names. Verify definitions without
selecting names or paths:

```sql
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'file_entries'
  AND indexname IN (
    'ix_file_entries_lower_name_trgm',
    'ix_file_entries_lower_name_prefix_id')
ORDER BY indexname;
```

Rollback removes only these two indexes concurrently. It deliberately retains
`pg_trgm` because another feature may share the extension. Index rollback loses
no FileEntry or Share data, but it removes the Search performance guarantee;
disable the Search endpoint or restore the matching application release before
running Down. Record elapsed build time and index sizes using aggregate output
only.

Before an upgrade or rollback that includes the permanent-delete migration,
take PostgreSQL and Storage Root backups, stop the Worker, and inspect unresolved purge journals without selecting file names
or paths:

```sql
SELECT id, owner_user_id, file_entry_id, status, error_code, updated_at
FROM file_operations
WHERE operation_type = 'PURGE' AND status <> 'COMPLETED'
ORDER BY updated_at, id;
```

Do not roll the schema back while this query returns rows. Restart the matching
API release so startup recovery can finish `PENDING` or `FILESYSTEM_DONE`
operations; investigate `RECOVERY_REQUIRED` without manually deleting catalog
rows or storage paths.

For an upgrade, use a new version and artifact in the protected configuration:

```bash
sudo --preserve-env=KURASTORAGE_DEPLOY_CONFIG \
  deployment/raspberry-pi/upgrade.sh
```

Upgrade creates a PostgreSQL backup, stops the Worker, extracts a new immutable
version, applies migrations explicitly, switches `current`, restarts API and
Worker independently, and verifies health. API remains independently operable
while the Worker is stopped; a Worker failure must not stop the API.
Its pre-upgrade dump is stored under `/var/backups/kurastorage` with
`root:postgres` ownership and is preserved by uninstall.

After an upgrade that changes file operations, install the matching signed
release APK on the authorized Android device and validate both LAN and
ZeroTier routes. Exercise file and folder rename, file move, and a folder move
with descendants. Confirm the item ID, `fileVersion`, and downloaded SHA-256
remain unchanged, and that same-name conflicts and cyclic folder moves do not
overwrite data. Repeat the main scenario ten times on each route. Before an
HDD-unavailable test, take database and storage-root backups; verify the API
rejects the operation and does not write to the OS root. Finish by rebooting
the Pi and Android device and rerunning `verify.sh` plus the main smoke test.

For a release that changes permanent deletion or retention, also exercise a
trashed file and a trashed folder with descendants on both routes. Verify that
cancel sends no purge request, confirmation is visibly irreversible, a
successful purge disappears only after an authoritative refresh, and an
unknown network result offers refresh or retry with the original idempotency
key. Confirm that the server-provided retention deadline is displayed without
the Android client recalculating 30 days. As both an administrator and member,
verify that only the administrator can request and view capacity, trash,
expired-root, and latest-run details. Reproduce the retention boundary with a
test clock or test-only data; never reduce the production retention setting
below 30 days. While capacity warning is active, confirm that pre-deadline
entries remain and that manual or deadline purge can release capacity. After
failure and recovery tests, reconcile the database, storage root, purge
journals, audit records, and purge runs without writing file names or paths to
logs.

For a release that adds external index reconciliation, keep `Indexing:Enabled`
set to `false` until the Protocol 2 Android build and matching Server/Worker are
deployed. After backing up PostgreSQL and the Storage Root, apply the migration
explicitly and inspect the catalog without exposing paths:

```bash
cd /opt/kurastorage/current
sudo -u kurastorage-api env \
  DOTNET_ENVIRONMENT=Production \
  KURASTORAGE_SECRETS_DIR=/etc/kurastorage/secrets \
  ./KuraStorage.AdminCli database migrate
sudo -u kurastorage-api env \
  DOTNET_ENVIRONMENT=Production \
  KURASTORAGE_SECRETS_DIR=/etc/kurastorage/secrets \
  ./KuraStorage.AdminCli index rescan --dry-run
```

The commands print only a scan ID, status, and aggregate counts. Exit code `0`
means a complete scan, `1` a failed, cancelled, or partially failed scan, `2`
invalid arguments, `3` another scan holds the global lock, and `4` the mount or
Storage ID is unavailable. Do not infer individual deletion from an unavailable
HDD. Resolve the mount, identity, and read access first, then rerun dry-run.
Failed APPLY staging is retained for 24 hours by default and is removed by a
later scan after the retention boundary.

Before rolling the schema back, require this query to return zero:

```sql
SELECT count(*)
FROM file_entries
WHERE status IN ('MISSING_CANDIDATE', 'MISSING');
```

Migration Down deliberately fails while either new status remains. Restore or
otherwise reconcile those entries with the matching release before retrying
schema rollback; do not rewrite the statuses merely to bypass the guard.

Application rollback switches to `previous`:

```bash
sudo --preserve-env=KURASTORAGE_DEPLOY_CONFIG \
  deployment/raspberry-pi/rollback.sh
```

MVP migrations must remain backward compatible. Database restoration is not
automatic; use the reviewed process in `backup-recovery.md` only after deciding
that schema/data rollback is necessary.

## Uninstall

```bash
export KURASTORAGE_CONFIRM_UNINSTALL=REMOVE_APPLICATION_KEEP_DATA
sudo --preserve-env=KURASTORAGE_DEPLOY_CONFIG,KURASTORAGE_CONFIRM_UNINSTALL \
  deployment/raspberry-pi/uninstall.sh
```

Uninstall stops and removes the generated API and exFAT mount units, unmounts
the HDD when it is not busy, removes the KuraStorage-specific UFW coexistence
rules when UFW is installed, and removes Nginx, nftables, and logrotate
configuration. It deliberately preserves releases, database, HDD data,
certificates, and keys for manual recovery or disposal.
