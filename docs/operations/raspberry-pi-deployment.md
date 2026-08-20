# Raspberry Pi deployment

## Scope and prerequisites

This procedure installs the MVP API on Debian 12 ARM64 with PostgreSQL 17,
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
sudo systemctl start kurastorage-api
sudo systemctl stop kurastorage-api
sudo systemctl restart kurastorage-api
sudo --preserve-env=KURASTORAGE_DEPLOY_CONFIG \
  deployment/raspberry-pi/verify.sh
```

The API must run as `kurastorage-api` and expose only
`/run/kurastorage/api.sock`. Nginx is the only TCP HTTPS listener.
The installed Nginx systemd drop-in waits for the LAN address, the ZeroTier
Managed IP, and the API Unix socket before validating and binding HTTPS.
This prevents a boot-time failure when ZeroTier assigns its address after the
base network has reached `network-online.target`.

To verify the storage boundary independently:

```bash
sudo --preserve-env=KURASTORAGE_STORAGE_MOUNT_PATH,KURASTORAGE_STORAGE_ROOT,KURASTORAGE_STORAGE_DEVICE_UUID,KURASTORAGE_STORAGE_ID \
  scripts/maintenance/verify-storage.sh
```

## Upgrade and rollback

Before an upgrade or rollback that includes the permanent-delete migration,
stop the API and inspect unresolved purge journals without selecting file names
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

Upgrade creates a PostgreSQL backup, extracts a new immutable version, applies
migrations explicitly, switches `current`, restarts, and verifies health.
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
