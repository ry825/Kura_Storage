# KuraStorage systemd unit

The units are rendered by the deployment scripts. The existing exFAT device is
mounted by UUID at `KURASTORAGE_STORAGE_MOUNT_PATH` with the numeric
`kurastorage-api` UID/GID, restrictive file/directory masks, and
`nodev,nosuid,noexec`. `KURASTORAGE_STORAGE_ROOT` is a child directory, not the
mount point. The API depends on that mount unit, runs as `kurastorage-api`,
binds only a Unix socket, and receives no Linux capabilities.
