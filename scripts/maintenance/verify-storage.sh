#!/usr/bin/env bash
set -euo pipefail

storage_mount_path="${KURASTORAGE_STORAGE_MOUNT_PATH:-}"
storage_root="${KURASTORAGE_STORAGE_ROOT:-}"
storage_device_uuid="${KURASTORAGE_STORAGE_DEVICE_UUID:-}"
storage_id="${KURASTORAGE_STORAGE_ID:-}"
storage_uid="${KURASTORAGE_STORAGE_UID:-$(id -u kurastorage-api 2>/dev/null || true)}"
storage_access_group="${KURASTORAGE_STORAGE_ACCESS_GROUP:-kurastorage}"
storage_gid="${KURASTORAGE_STORAGE_GID:-$(getent group "${storage_access_group}" 2>/dev/null | cut -d: -f3)}"
[[ "${storage_mount_path}" == /* && "${storage_root}" == "${storage_mount_path%/}/"* &&
    -n "${storage_device_uuid}" && -n "${storage_id}" && "${storage_uid}" =~ ^[0-9]+$ &&
    "${storage_gid}" =~ ^[0-9]+$ ]] || {
    printf 'Set the storage mount path, root, device UUID, ID, UID, and GID.\n' >&2
    exit 2
}
mountpoint -q "${storage_mount_path}" || {
    printf 'Storage mount path is not mounted: %s\n' "${storage_mount_path}" >&2
    exit 1
}
[[ ! -L "${storage_mount_path}" && ! -L "${storage_root}" && -d "${storage_root}" ]] || {
    printf 'Storage paths must be real directories, not symbolic links.\n' >&2
    exit 1
}
[[ "$(readlink -f "${storage_mount_path}")" == "${storage_mount_path}" &&
    "$(readlink -f "${storage_root}")" == "${storage_root}" ]] || {
    printf 'Storage paths must not contain symbolic-link components.\n' >&2
    exit 1
}
actual_target="$(findmnt --noheadings --output TARGET --target "${storage_root}" | tr -d '[:space:]')"
actual_source="$(findmnt --noheadings --output SOURCE --target "${storage_mount_path}" | tr -d '[:space:]')"
actual_type="$(findmnt --noheadings --output FSTYPE --target "${storage_mount_path}" | tr -d '[:space:]')"
actual_options="$(findmnt --noheadings --output OPTIONS --target "${storage_mount_path}" | tr -d '[:space:]')"
expected_device="/dev/disk/by-uuid/${storage_device_uuid}"
[[ "${actual_target}" == "${storage_mount_path}" ]] || {
    printf 'Storage root is not on the configured mount path.\n' >&2
    exit 1
}
[[ "$(readlink -f "${actual_source}")" == "$(readlink -f "${expected_device}")" ]] || {
    printf 'Mounted device does not match the configured UUID.\n' >&2
    exit 1
}
[[ "${actual_type,,}" == "exfat" ]] || {
    printf 'Storage filesystem is not exFAT.\n' >&2
    exit 1
}
for expected_option in rw nodev nosuid noexec noatime "uid=${storage_uid}" \
    "gid=${storage_gid}" fmask=0007 dmask=0007 iocharset=utf8 errors=remount-ro; do
    [[ ",${actual_options}," == *",${expected_option},"* ]] || {
        printf 'Storage mount option is missing: %s\n' "${expected_option}" >&2
        exit 1
    }
done
[[ "$(stat --format=%u "${storage_root}")" == "${storage_uid}" &&
    "$(stat --format=%g "${storage_root}")" == "${storage_gid}" ]] || {
    printf 'Storage root ownership does not match the configured UID/GID.\n' >&2
    exit 1
}
[[ -f "${storage_root}/.storage-identity" ]] || {
    printf 'Storage identity file is missing.\n' >&2
    exit 1
}
python3 - "${storage_root}/.storage-identity" "${storage_id}" <<'PY' || {
import json
import pathlib
import sys

try:
    value = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
except (OSError, UnicodeDecodeError, json.JSONDecodeError):
    raise SystemExit(1)
if not isinstance(value, dict):
    raise SystemExit(1)
if value.get("storageId") != sys.argv[2] or value.get("formatVersion") != 1:
    raise SystemExit(1)
PY
    printf 'Storage identity does not match.\n' >&2
    exit 1
}
find "${storage_root}" -xdev -type l -print -quit | {
    if read -r symbolic_link; then
        printf 'Symbolic link found in storage: %s\n' "${symbolic_link}" >&2
        exit 1
    fi
}
printf 'exFAT storage mount, options, and identity verified.\n'
