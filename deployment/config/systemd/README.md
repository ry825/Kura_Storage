# KuraStorage systemd unit

The units are rendered by the deployment scripts. The existing exFAT device is
mounted by UUID at `KURASTORAGE_STORAGE_MOUNT_PATH` with the numeric
`kurastorage-api` UID/GID, restrictive file/directory masks, and
`nodev,nosuid,noexec`. `KURASTORAGE_STORAGE_ROOT` is a child directory, not the
mount point. The API and retention Worker depend on that mount unit, run as
`kurastorage-api`, and receive no Linux capabilities. The API binds only a Unix
socket. The Worker has no listener, uses only Unix-domain access for PostgreSQL,
and receives lower CPU and IO weights so purge work does not dominate requests.
