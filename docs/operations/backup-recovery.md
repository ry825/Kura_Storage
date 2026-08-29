# Backup and recovery

## Backup boundary

The MVP backup set consists of:

- PostgreSQL database dump;
- the existing exFAT HDD application root at
  `/mnt/KuraStorage-hdd/KuraStorage`, including `.storage-identity`, `users`,
  trash, upload operation state, `derivatives`, and `derivative-temp`;
- protected deployment configuration, TLS server material, and JWT signing key;
- the matching versioned server artifact and release checksum.

The offline Root CA private key and Android signing key are managed separately
and must never be copied to the Raspberry Pi backup.

## Database backup

Store backups outside the database filesystem and preferably outside the
KuraStorage HDD:

```bash
export KURASTORAGE_BACKUP_DIRECTORY=/protected/backup/kurastorage
sudo --preserve-env=KURASTORAGE_BACKUP_DIRECTORY \
  ./scripts/maintenance/backup-database.sh
```

For an HDD backup, stop the API, unmount `/mnt/KuraStorage-hdd`, run the
platform exFAT filesystem check after any unsafe disconnect, and copy the
`KuraStorage` directory to a separate backup device. exFAT does not preserve
POSIX ownership or modes; access control is restored by the configured mount
UID, GID, and masks. Restart only after remounting by the configured UUID and
running `scripts/maintenance/verify-storage.sh`. Never copy live upload
temporary files as if they were a consistent database/filesystem snapshot.
The derivative roots are cache data, but they must be copied with the matching
database when a restorable point-in-time image is required. Never restore a
`derivatives` tree from a different database snapshot. After restore, stale
`RUNNING` jobs may be recovered only when their heartbeat is older than the
configured threshold and no active generation lease exists; do not update job
statuses manually.

## Manual restore

1. Stop the API.
2. Verify release and backup checksums.
3. Restore the `KuraStorage` directory to the configured exFAT mount without
   formatting or deleting unrelated existing data, then verify the device
   UUID, filesystem type, mount options, and `.storage-identity`.
4. Restore PostgreSQL only when required:

```bash
export KURASTORAGE_CONFIRM_DATABASE_RESTORE=RESTORE_REPLACES_DATABASE
sudo --preserve-env=KURASTORAGE_CONFIRM_DATABASE_RESTORE \
  ./scripts/maintenance/restore-database.sh /protected/backup/file.dump
```

5. Run `database migrate` from the selected release directory with
   `DOTNET_ENVIRONMENT=Production` and
   `KURASTORAGE_SECRETS_DIR=/etc/kurastorage/secrets`.
6. Start the API and execute `deployment/raspberry-pi/verify.sh`.
7. Check `FileOperation` recovery and confirm that incomplete uploads are not
   visible in normal listings.

Database restoration is destructive and requires the explicit confirmation
value. The restore script preserves ownership through the local database role
and fails immediately on restore errors.

## Index reconciliation after restore or HDD replacement

After restoring PostgreSQL and the matching Storage Root, keep
`Indexing.Enabled=false`, verify the mounted Storage ID, change to the selected
release directory, and run `./KuraStorage.AdminCli index rescan --dry-run` as
`kurastorage-api` with the production environment and secrets directory. Review
only aggregate counts, then
run an APPLY rescan before enabling the Worker. A different HDD or Storage ID
must not be attached to the restored catalog. If an HDD is intentionally
replaced, follow the reviewed data migration procedure and reconcile ownership
and managed paths before any APPLY scan; never rewrite `MISSING` states merely
to bypass rollback guards.
