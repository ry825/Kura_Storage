#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
    printf 'Usage: %s BACKUP_FILE\n' "$0" >&2
    exit 2
fi

database="${KURASTORAGE_POSTGRES_DATABASE:-kurastorage}"
backup_file="$1"
[[ "${EUID}" -eq 0 ]] || {
    printf 'This command must run as root.\n' >&2
    exit 1
}
[[ "${KURASTORAGE_CONFIRM_DATABASE_RESTORE:-}" == "RESTORE_REPLACES_DATABASE" ]] || {
    printf 'Set KURASTORAGE_CONFIRM_DATABASE_RESTORE=RESTORE_REPLACES_DATABASE.\n' >&2
    exit 2
}
[[ "${database}" =~ ^[a-z][a-z0-9_]*$ ]] || {
    printf 'Invalid database name.\n' >&2
    exit 2
}
[[ -f "${backup_file}" && -f "${backup_file}.sha256" ]] || {
    printf 'Backup and checksum files are required.\n' >&2
    exit 2
}

(
    cd "$(dirname "${backup_file}")"
    sha256sum --check "$(basename "${backup_file}").sha256"
)
systemctl stop kurastorage-api.service
runuser -u postgres -- pg_restore \
    --clean \
    --if-exists \
    --exit-on-error \
    --no-owner \
    --dbname="${database}" \
    "${backup_file}"
systemctl start kurastorage-api.service
printf 'Database restored. Run deployment/raspberry-pi/verify.sh now.\n'
