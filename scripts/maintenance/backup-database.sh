#!/usr/bin/env bash
set -euo pipefail

database="${KURASTORAGE_POSTGRES_DATABASE:-kurastorage}"
backup_directory="${KURASTORAGE_BACKUP_DIRECTORY:-}"
[[ "${EUID}" -eq 0 ]] || {
    printf 'This command must run as root.\n' >&2
    exit 1
}
[[ "${database}" =~ ^[a-z][a-z0-9_]*$ ]] || {
    printf 'Invalid database name.\n' >&2
    exit 2
}
[[ "${backup_directory}" == /* ]] || {
    printf 'KURASTORAGE_BACKUP_DIRECTORY must be an absolute path.\n' >&2
    exit 2
}

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
install -d -m 0750 -o postgres -g postgres "${backup_directory}"
backup_file="${backup_directory}/kurastorage-${timestamp}.dump"
runuser -u postgres -- pg_dump --format=custom --file="${backup_file}" "${database}"
chmod 0640 "${backup_file}"
sha256sum "${backup_file}" >"${backup_file}.sha256"
chmod 0640 "${backup_file}.sha256"
printf 'Database backup created: %s\n' "${backup_file}"
