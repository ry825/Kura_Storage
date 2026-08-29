#!/usr/bin/env bash
set -euo pipefail

DEPLOYMENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly DEPLOYMENT_DIR
readonly INSTALL_ROOT="/opt/kurastorage"
readonly CONFIG_ROOT="/etc/kurastorage"
# Used by install.sh after sourcing this library.
# shellcheck disable=SC2034
readonly STATE_ROOT="/var/lib/kurastorage"
readonly BACKUP_ROOT="/var/backups/kurastorage"

die() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

require_root() {
    [[ "${EUID}" -eq 0 ]] || die "This command must run as root."
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || die "Required command is unavailable: $1"
}

install_media_dependencies() {
    [[ "$(dpkg --print-architecture)" == "arm64" ]] ||
        die "Media dependencies require the reviewed Debian 12 ARM64 deployment target."
    apt-get update
    apt-get install --yes --no-install-recommends libvips-tools ffmpeg poppler-utils
    verify_media_dependencies

    local inventory
    inventory="$(mktemp)"
    dpkg-query --show --showformat='${Package}\t${Version}\t${Architecture}\n' \
        libvips-tools libvips42 ffmpeg poppler-utils >"${inventory}"
    install -m 0644 -o root -g root "${inventory}" "${STATE_ROOT}/media-runtime-packages.sbom"
    rm -f "${inventory}"
}

verify_media_dependencies() {
    local tool_path
    for tool_path in \
        "${KURASTORAGE_MEDIA_VIPS_PATH}" \
        "${KURASTORAGE_MEDIA_FFMPEG_PATH}" \
        "${KURASTORAGE_MEDIA_FFPROBE_PATH}" \
        "${KURASTORAGE_MEDIA_PDFTOPPM_PATH}"; do
        [[ -x "${tool_path}" ]] || die "Configured media tool is not executable: ${tool_path}"
    done

    local vips_version vips_operations ffmpeg_encoders ffprobe_version pdftoppm_version
    vips_version="$("${KURASTORAGE_MEDIA_VIPS_PATH}" --version)"
    vips_operations="$("${KURASTORAGE_MEDIA_VIPS_PATH}" -l)"
    ffmpeg_encoders="$("${KURASTORAGE_MEDIA_FFMPEG_PATH}" -hide_banner -encoders 2>/dev/null)"
    ffprobe_version="$("${KURASTORAGE_MEDIA_FFPROBE_PATH}" -version 2>/dev/null)"
    pdftoppm_version="$("${KURASTORAGE_MEDIA_PDFTOPPM_PATH}" -v 2>&1)"

    grep -q '^vips-' <<<"${vips_version}" ||
        die "libvips version verification failed."
    grep -q 'VipsForeignLoadJpeg' <<<"${vips_operations}" ||
        die "libvips JPEG loader is unavailable."
    grep -q 'VipsForeignLoadPng' <<<"${vips_operations}" ||
        die "libvips PNG loader is unavailable."
    grep -q 'VipsForeignSaveWebp' <<<"${vips_operations}" ||
        die "libvips WebP encoder is unavailable."
    grep -q 'libwebp' <<<"${ffmpeg_encoders}" ||
        die "FFmpeg libwebp encoder is unavailable."
    grep -q '^ffprobe version' <<<"${ffprobe_version}" ||
        die "ffprobe version verification failed."
    grep -q '^pdftoppm version' <<<"${pdftoppm_version}" ||
        die "pdftoppm version verification failed."
}

ensure_media_storage() {
    mkdir -p \
        "${KURASTORAGE_STORAGE_ROOT}/${KURASTORAGE_MEDIA_DERIVATIVE_ROOT}" \
        "${KURASTORAGE_STORAGE_ROOT}/${KURASTORAGE_MEDIA_TEMPORARY_ROOT}"
}

ufw_is_active() {
    command -v ufw >/dev/null 2>&1 && ufw status | grep -q '^Status: active$'
}

configure_ufw_coexistence() {
    ufw_is_active || return 0

    ufw allow from "${KURASTORAGE_LAN_CIDR}" to "${KURASTORAGE_LAN_API_IP}" \
        port 22 proto tcp comment 'KuraStorage LAN SSH'
    ufw allow from "${KURASTORAGE_LAN_CIDR}" to "${KURASTORAGE_LAN_API_IP}" \
        port 443 proto tcp comment 'KuraStorage LAN HTTPS'
    ufw allow in on "${KURASTORAGE_ZEROTIER_INTERFACE}" \
        from "${KURASTORAGE_ZEROTIER_CIDR}" to "${KURASTORAGE_ZEROTIER_API_IP}" \
        port 443 proto tcp comment 'KuraStorage ZeroTier HTTPS'
}

verify_ufw_coexistence() {
    ufw_is_active || return 0

    local added_rules
    added_rules="$(ufw show added)"
    grep -Fq \
        "ufw allow from ${KURASTORAGE_LAN_CIDR} to ${KURASTORAGE_LAN_API_IP} port 22 proto tcp" \
        <<<"${added_rules}" || die "UFW does not allow KuraStorage LAN SSH."
    grep -Fq \
        "ufw allow from ${KURASTORAGE_LAN_CIDR} to ${KURASTORAGE_LAN_API_IP} port 443 proto tcp" \
        <<<"${added_rules}" || die "UFW does not allow KuraStorage LAN HTTPS."
    grep -Fq \
        "ufw allow in on ${KURASTORAGE_ZEROTIER_INTERFACE} from ${KURASTORAGE_ZEROTIER_CIDR} to ${KURASTORAGE_ZEROTIER_API_IP} port 443 proto tcp" \
        <<<"${added_rules}" || die "UFW does not allow KuraStorage ZeroTier HTTPS."
}

remove_ufw_coexistence() {
    command -v ufw >/dev/null 2>&1 || return 0

    ufw --force delete allow from "${KURASTORAGE_LAN_CIDR}" \
        to "${KURASTORAGE_LAN_API_IP}" port 22 proto tcp || true
    ufw --force delete allow from "${KURASTORAGE_LAN_CIDR}" \
        to "${KURASTORAGE_LAN_API_IP}" port 443 proto tcp || true
    ufw --force delete allow in on "${KURASTORAGE_ZEROTIER_INTERFACE}" \
        from "${KURASTORAGE_ZEROTIER_CIDR}" to "${KURASTORAGE_ZEROTIER_API_IP}" \
        port 443 proto tcp || true
}

load_config() {
    local config_file="${KURASTORAGE_DEPLOY_CONFIG:-}"
    [[ -n "${config_file}" && -f "${config_file}" ]] ||
        die "Set KURASTORAGE_DEPLOY_CONFIG to a root-owned deployment environment file."
    # shellcheck disable=SC1090
    source "${config_file}"

    local required=(
        KURASTORAGE_VERSION
        KURASTORAGE_API_HOSTNAME
        KURASTORAGE_LAN_API_IP
        KURASTORAGE_LAN_CIDR
        KURASTORAGE_ZEROTIER_API_IP
        KURASTORAGE_ZEROTIER_CIDR
        KURASTORAGE_ZEROTIER_INTERFACE
        KURASTORAGE_STORAGE_MOUNT_PATH
        KURASTORAGE_STORAGE_ROOT
        KURASTORAGE_STORAGE_DEVICE_UUID
        KURASTORAGE_STORAGE_ID
        KURASTORAGE_STORAGE_RESERVE_BYTES
        KURASTORAGE_STORAGE_WARNING_BYTES
        KURASTORAGE_MEDIA_DERIVATIVE_ROOT
        KURASTORAGE_MEDIA_TEMPORARY_ROOT
        KURASTORAGE_MEDIA_IMAGE_WAIT_MILLISECONDS
        KURASTORAGE_MEDIA_THUMBNAIL_PROFILE_VERSION
        KURASTORAGE_MEDIA_IMAGE_PROFILE_VERSION
        KURASTORAGE_MEDIA_VIDEO_PROFILE_VERSION
        KURASTORAGE_MEDIA_THUMBNAIL_MAX_DIMENSION
        KURASTORAGE_MEDIA_THUMBNAIL_WEBP_QUALITY
        KURASTORAGE_MEDIA_JOB_POLL_MILLISECONDS
        KURASTORAGE_MEDIA_JOB_HEARTBEAT_SECONDS
        KURASTORAGE_MEDIA_STALE_JOB_SECONDS
        KURASTORAGE_MEDIA_MAXIMUM_ATTEMPTS
        KURASTORAGE_MEDIA_GENERATION_LEASE_SECONDS
        KURASTORAGE_MEDIA_DELIVERY_LEASE_SECONDS
        KURASTORAGE_MEDIA_DELIVERY_LEASE_RENEWAL_SECONDS
        KURASTORAGE_MEDIA_CACHE_TTL_HOURS
        KURASTORAGE_MEDIA_CACHE_HIGH_WATERMARK_BYTES
        KURASTORAGE_MEDIA_CACHE_LOW_WATERMARK_BYTES
        KURASTORAGE_MEDIA_CLEANUP_INTERVAL_MINUTES
        KURASTORAGE_MEDIA_CLEANUP_BATCH_SIZE
        KURASTORAGE_MEDIA_TERMINAL_JOB_RETENTION_DAYS
        KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_MEDIA_JOBS
        KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_VIDEO_JOBS
        KURASTORAGE_MEDIA_VIPS_PATH
        KURASTORAGE_MEDIA_FFMPEG_PATH
        KURASTORAGE_MEDIA_FFPROBE_PATH
        KURASTORAGE_MEDIA_PDFTOPPM_PATH
        KURASTORAGE_TRASH_RETENTION_DAYS
        KURASTORAGE_TRASH_INTERVAL_HOURS
        KURASTORAGE_TRASH_BATCH_SIZE
        KURASTORAGE_TRASH_RETRY_DELAY_MINUTES
        KURASTORAGE_STORAGE_MOUNT_UNIT
        KURASTORAGE_STORAGE_ACCESS_GROUP
        KURASTORAGE_STORAGE_ACCESS_USER
        KURASTORAGE_TLS_CERT_FILE
        KURASTORAGE_TLS_KEY_FILE
        KURASTORAGE_TLS_CA_CERT_FILE
        KURASTORAGE_JWT_KEY_FILE
        KURASTORAGE_ARTIFACT_FILE
        KURASTORAGE_POSTGRES_MAJOR
        KURASTORAGE_POSTGRES_DATABASE
        KURASTORAGE_POSTGRES_ROLE
    )
    local name
    for name in "${required[@]}"; do
        [[ -n "${!name:-}" && "${!name}" != "SET_LOCALLY" ]] ||
            die "Deployment value is not set: ${name}"
        export "${name?}"
    done

    [[ "${KURASTORAGE_VERSION}" =~ ^[0-9A-Za-z._-]+$ ]] || die "Invalid version."
    [[ "${KURASTORAGE_API_HOSTNAME}" =~ ^[0-9A-Za-z.-]+$ ]] || die "Invalid API hostname."
    [[ "${KURASTORAGE_LAN_API_IP}" =~ ^[0-9.]+$ ]] || die "Invalid LAN API IP."
    [[ "${KURASTORAGE_ZEROTIER_API_IP}" =~ ^[0-9.]+$ ]] || die "Invalid ZeroTier API IP."
    [[ "${KURASTORAGE_LAN_CIDR}" =~ ^[0-9.]+/[0-9]{1,2}$ ]] || die "Invalid LAN CIDR."
    [[ "${KURASTORAGE_ZEROTIER_CIDR}" =~ ^[0-9.]+/[0-9]{1,2}$ ]] ||
        die "Invalid ZeroTier CIDR."
    [[ "${KURASTORAGE_ZEROTIER_INTERFACE}" =~ ^[0-9A-Za-z_.-]+$ ]] ||
        die "Invalid ZeroTier interface."
    [[ "${KURASTORAGE_STORAGE_MOUNT_PATH}" == /* ]] ||
        die "Storage mount path must be absolute."
    [[ "${KURASTORAGE_STORAGE_ROOT}" == /* ]] || die "Storage root must be absolute."
    [[ "${KURASTORAGE_STORAGE_MOUNT_PATH}" != "/" ]] ||
        die "Storage mount path must not be the OS root."
    [[ "$(readlink -m "${KURASTORAGE_STORAGE_MOUNT_PATH}")" == "${KURASTORAGE_STORAGE_MOUNT_PATH}" ]] ||
        die "Storage mount path must be normalized."
    [[ "$(readlink -m "${KURASTORAGE_STORAGE_ROOT}")" == "${KURASTORAGE_STORAGE_ROOT}" ]] ||
        die "Storage root must be normalized."
    [[ "${KURASTORAGE_STORAGE_ROOT}" == "${KURASTORAGE_STORAGE_MOUNT_PATH%/}/"* ]] ||
        die "Storage root must be below the storage mount path."
    [[ "${KURASTORAGE_STORAGE_ROOT}" != "${KURASTORAGE_STORAGE_MOUNT_PATH}" ]] ||
        die "Storage root and storage mount path must be different."
    [[ "${KURASTORAGE_STORAGE_DEVICE_UUID}" =~ ^[0-9A-Fa-f-]+$ ]] ||
        die "Invalid storage device UUID."
    # KURASTORAGE_STORAGE_ID is populated by the validated configuration loop above.
    # shellcheck disable=SC2153
    [[ "${KURASTORAGE_STORAGE_ID}" =~ ^[0-9A-Za-z._-]+$ ]] ||
        die "Invalid storage identity."
    [[ "${KURASTORAGE_STORAGE_MOUNT_UNIT}" =~ ^[0-9A-Za-z_.@\\-]+\.mount$ ]] ||
        die "Invalid storage mount unit."
    [[ "${KURASTORAGE_STORAGE_RESERVE_BYTES}" =~ ^[0-9]+$ ]] ||
        die "Invalid storage reserve."
    [[ "${KURASTORAGE_STORAGE_WARNING_BYTES}" =~ ^[0-9]+$ ]] ||
        die "Invalid storage warning threshold."
    ((KURASTORAGE_STORAGE_WARNING_BYTES >= KURASTORAGE_STORAGE_RESERVE_BYTES)) ||
        die "Storage warning threshold must be at least the storage reserve."
    [[ "${KURASTORAGE_MEDIA_DERIVATIVE_ROOT}" =~ ^[0-9A-Za-z._-]+$ ]] ||
        die "Invalid media derivative root."
    [[ "${KURASTORAGE_MEDIA_TEMPORARY_ROOT}" =~ ^[0-9A-Za-z._-]+$ ]] ||
        die "Invalid media temporary root."
    [[ "${KURASTORAGE_MEDIA_DERIVATIVE_ROOT}" != "${KURASTORAGE_MEDIA_TEMPORARY_ROOT}" ]] ||
        die "Media roots must be distinct."
    local media_number
    for media_number in \
        KURASTORAGE_MEDIA_IMAGE_WAIT_MILLISECONDS \
        KURASTORAGE_MEDIA_THUMBNAIL_PROFILE_VERSION \
        KURASTORAGE_MEDIA_IMAGE_PROFILE_VERSION \
        KURASTORAGE_MEDIA_VIDEO_PROFILE_VERSION \
        KURASTORAGE_MEDIA_THUMBNAIL_MAX_DIMENSION \
        KURASTORAGE_MEDIA_THUMBNAIL_WEBP_QUALITY \
        KURASTORAGE_MEDIA_JOB_POLL_MILLISECONDS \
        KURASTORAGE_MEDIA_JOB_HEARTBEAT_SECONDS \
        KURASTORAGE_MEDIA_STALE_JOB_SECONDS \
        KURASTORAGE_MEDIA_MAXIMUM_ATTEMPTS \
        KURASTORAGE_MEDIA_GENERATION_LEASE_SECONDS \
        KURASTORAGE_MEDIA_DELIVERY_LEASE_SECONDS \
        KURASTORAGE_MEDIA_DELIVERY_LEASE_RENEWAL_SECONDS \
        KURASTORAGE_MEDIA_CACHE_TTL_HOURS \
        KURASTORAGE_MEDIA_CACHE_HIGH_WATERMARK_BYTES \
        KURASTORAGE_MEDIA_CACHE_LOW_WATERMARK_BYTES \
        KURASTORAGE_MEDIA_CLEANUP_INTERVAL_MINUTES \
        KURASTORAGE_MEDIA_CLEANUP_BATCH_SIZE \
        KURASTORAGE_MEDIA_TERMINAL_JOB_RETENTION_DAYS \
        KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_MEDIA_JOBS \
        KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_VIDEO_JOBS; do
        [[ "${!media_number}" =~ ^[0-9]+$ ]] || die "Invalid media numeric setting: ${media_number}"
    done
    ((KURASTORAGE_MEDIA_JOB_POLL_MILLISECONDS <= KURASTORAGE_MEDIA_IMAGE_WAIT_MILLISECONDS)) ||
        die "Media polling must not exceed the image wait threshold."
    ((KURASTORAGE_MEDIA_JOB_HEARTBEAT_SECONDS < KURASTORAGE_MEDIA_STALE_JOB_SECONDS)) ||
        die "Media heartbeat must be shorter than the stale threshold."
    ((KURASTORAGE_MEDIA_DELIVERY_LEASE_RENEWAL_SECONDS < KURASTORAGE_MEDIA_DELIVERY_LEASE_SECONDS)) ||
        die "Media lease renewal must be shorter than the delivery lease."
    ((KURASTORAGE_MEDIA_CACHE_LOW_WATERMARK_BYTES < KURASTORAGE_MEDIA_CACHE_HIGH_WATERMARK_BYTES)) ||
        die "Media cache low watermark must be below the high watermark."
    ((KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_MEDIA_JOBS == 1)) ||
        die "The initial media concurrency must be one."
    ((KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_VIDEO_JOBS == 1)) ||
        die "The initial video concurrency must be one."
    local media_tool
    for media_tool in KURASTORAGE_MEDIA_VIPS_PATH KURASTORAGE_MEDIA_FFMPEG_PATH \
        KURASTORAGE_MEDIA_FFPROBE_PATH KURASTORAGE_MEDIA_PDFTOPPM_PATH; do
        [[ "${!media_tool}" == /* ]] || die "Media tool path must be absolute: ${media_tool}"
    done
    if ! [[ "${KURASTORAGE_TRASH_RETENTION_DAYS}" =~ ^[0-9]+$ ]] ||
        ((KURASTORAGE_TRASH_RETENTION_DAYS < 30)); then
        die "Trash retention must be at least 30 days."
    fi
    if ! [[ "${KURASTORAGE_TRASH_INTERVAL_HOURS}" =~ ^[0-9]+$ ]] ||
        ((KURASTORAGE_TRASH_INTERVAL_HOURS < 1 || KURASTORAGE_TRASH_INTERVAL_HOURS > 168)); then
        die "Trash purge interval must be between 1 and 168 hours."
    fi
    if ! [[ "${KURASTORAGE_TRASH_BATCH_SIZE}" =~ ^[0-9]+$ ]] ||
        ((KURASTORAGE_TRASH_BATCH_SIZE < 1 || KURASTORAGE_TRASH_BATCH_SIZE > 500)); then
        die "Trash purge batch size must be between 1 and 500."
    fi
    if ! [[ "${KURASTORAGE_TRASH_RETRY_DELAY_MINUTES}" =~ ^[0-9]+$ ]] ||
        ((KURASTORAGE_TRASH_RETRY_DELAY_MINUTES < 1 || KURASTORAGE_TRASH_RETRY_DELAY_MINUTES > 1440)); then
        die "Trash purge retry delay must be between 1 and 1440 minutes."
    fi
    [[ "${KURASTORAGE_STORAGE_ACCESS_GROUP}" =~ ^[a-z_][a-z0-9_-]*$ ]] ||
        die "Invalid storage access group."
    [[ "${KURASTORAGE_STORAGE_ACCESS_USER}" =~ ^[a-z_][a-z0-9_-]*$ ]] ||
        die "Invalid storage access user."
    [[ "${KURASTORAGE_POSTGRES_MAJOR}" == "17" ]] || die "PostgreSQL 17 is required."
    [[ "${KURASTORAGE_POSTGRES_DATABASE}" =~ ^[a-z][a-z0-9_]*$ ]] ||
        die "Invalid PostgreSQL database name."
    [[ "${KURASTORAGE_POSTGRES_ROLE}" =~ ^[a-z][a-z0-9-]*$ ]] ||
        die "Invalid PostgreSQL role name."

    local material
    for material in \
        "${KURASTORAGE_TLS_CERT_FILE}" \
        "${KURASTORAGE_TLS_KEY_FILE}" \
        "${KURASTORAGE_TLS_CA_CERT_FILE}" \
        "${KURASTORAGE_JWT_KEY_FILE}" \
        "${KURASTORAGE_ARTIFACT_FILE}"; do
        [[ -f "${material}" ]] || die "Required input file is missing: ${material}"
    done
}

render_template() {
    local source_file="$1"
    local destination_file="$2"
    local temporary_file
    temporary_file="$(mktemp)"
    # Keep Nginx runtime variables such as $host intact.
    # shellcheck disable=SC2016
    envsubst \
        '${KURASTORAGE_API_HOSTNAME} ${KURASTORAGE_LAN_API_IP} ${KURASTORAGE_LAN_CIDR} ${KURASTORAGE_ZEROTIER_API_IP} ${KURASTORAGE_ZEROTIER_CIDR} ${KURASTORAGE_ZEROTIER_INTERFACE} ${KURASTORAGE_STORAGE_MOUNT_PATH} ${KURASTORAGE_STORAGE_ROOT} ${KURASTORAGE_STORAGE_DEVICE_UUID} ${KURASTORAGE_STORAGE_ID} ${KURASTORAGE_STORAGE_RESERVE_BYTES} ${KURASTORAGE_STORAGE_WARNING_BYTES} ${KURASTORAGE_MEDIA_DERIVATIVE_ROOT} ${KURASTORAGE_MEDIA_TEMPORARY_ROOT} ${KURASTORAGE_MEDIA_IMAGE_WAIT_MILLISECONDS} ${KURASTORAGE_MEDIA_THUMBNAIL_PROFILE_VERSION} ${KURASTORAGE_MEDIA_IMAGE_PROFILE_VERSION} ${KURASTORAGE_MEDIA_VIDEO_PROFILE_VERSION} ${KURASTORAGE_MEDIA_THUMBNAIL_MAX_DIMENSION} ${KURASTORAGE_MEDIA_THUMBNAIL_WEBP_QUALITY} ${KURASTORAGE_MEDIA_JOB_POLL_MILLISECONDS} ${KURASTORAGE_MEDIA_JOB_HEARTBEAT_SECONDS} ${KURASTORAGE_MEDIA_STALE_JOB_SECONDS} ${KURASTORAGE_MEDIA_MAXIMUM_ATTEMPTS} ${KURASTORAGE_MEDIA_GENERATION_LEASE_SECONDS} ${KURASTORAGE_MEDIA_DELIVERY_LEASE_SECONDS} ${KURASTORAGE_MEDIA_DELIVERY_LEASE_RENEWAL_SECONDS} ${KURASTORAGE_MEDIA_CACHE_TTL_HOURS} ${KURASTORAGE_MEDIA_CACHE_HIGH_WATERMARK_BYTES} ${KURASTORAGE_MEDIA_CACHE_LOW_WATERMARK_BYTES} ${KURASTORAGE_MEDIA_CLEANUP_INTERVAL_MINUTES} ${KURASTORAGE_MEDIA_CLEANUP_BATCH_SIZE} ${KURASTORAGE_MEDIA_TERMINAL_JOB_RETENTION_DAYS} ${KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_MEDIA_JOBS} ${KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_VIDEO_JOBS} ${KURASTORAGE_MEDIA_VIPS_PATH} ${KURASTORAGE_MEDIA_FFMPEG_PATH} ${KURASTORAGE_MEDIA_FFPROBE_PATH} ${KURASTORAGE_MEDIA_PDFTOPPM_PATH} ${KURASTORAGE_TRASH_RETENTION_DAYS} ${KURASTORAGE_TRASH_INTERVAL_HOURS} ${KURASTORAGE_TRASH_BATCH_SIZE} ${KURASTORAGE_TRASH_RETRY_DELAY_MINUTES} ${KURASTORAGE_STORAGE_MOUNT_UNIT} ${KURASTORAGE_STORAGE_ACCESS_GROUP} ${KURASTORAGE_STORAGE_UID} ${KURASTORAGE_STORAGE_GID} ${KURASTORAGE_POSTGRES_DATABASE} ${KURASTORAGE_POSTGRES_ROLE}' \
        <"${source_file}" >"${temporary_file}"
    install -m 0640 "${temporary_file}" "${destination_file}"
    rm -f "${temporary_file}"
}

verify_storage_mount() {
    local expected_device="/dev/disk/by-uuid/${KURASTORAGE_STORAGE_DEVICE_UUID}"
    local actual_target actual_source actual_type actual_options expected_option

    [[ -n "${KURASTORAGE_STORAGE_UID:-}" && -n "${KURASTORAGE_STORAGE_GID:-}" ]] ||
        die "Storage UID and GID have not been resolved."
    mountpoint -q "${KURASTORAGE_STORAGE_MOUNT_PATH}" ||
        die "Storage mount path is not mounted: ${KURASTORAGE_STORAGE_MOUNT_PATH}"
    [[ ! -L "${KURASTORAGE_STORAGE_MOUNT_PATH}" ]] ||
        die "Storage mount path must not be a symlink."
    [[ ! -L "${KURASTORAGE_STORAGE_ROOT}" ]] || die "Storage root must not be a symlink."
    [[ -d "${KURASTORAGE_STORAGE_ROOT}" ]] || die "Storage root does not exist."
    [[ "$(readlink -f "${KURASTORAGE_STORAGE_MOUNT_PATH}")" == "${KURASTORAGE_STORAGE_MOUNT_PATH}" ]] ||
        die "Storage mount path contains a symbolic-link component."
    [[ "$(readlink -f "${KURASTORAGE_STORAGE_ROOT}")" == "${KURASTORAGE_STORAGE_ROOT}" ]] ||
        die "Storage root contains a symbolic-link component."

    actual_target="$(findmnt --noheadings --output TARGET --target \
        "${KURASTORAGE_STORAGE_ROOT}" | tr -d '[:space:]')"
    [[ "${actual_target}" == "${KURASTORAGE_STORAGE_MOUNT_PATH}" ]] ||
        die "Storage root is not on the configured mount path."
    actual_source="$(findmnt --noheadings --output SOURCE --target \
        "${KURASTORAGE_STORAGE_MOUNT_PATH}" | tr -d '[:space:]')"
    [[ "$(readlink -f "${actual_source}")" == "$(readlink -f "${expected_device}")" ]] ||
        die "Mounted storage device does not match the configured UUID."
    actual_type="$(findmnt --noheadings --output FSTYPE --target \
        "${KURASTORAGE_STORAGE_MOUNT_PATH}" | tr -d '[:space:]')"
    [[ "${actual_type,,}" == "exfat" ]] || die "Storage filesystem must be exFAT."
    actual_options="$(findmnt --noheadings --output OPTIONS --target \
        "${KURASTORAGE_STORAGE_MOUNT_PATH}" | tr -d '[:space:]')"
    for expected_option in rw nodev nosuid noexec noatime \
        "uid=${KURASTORAGE_STORAGE_UID}" "gid=${KURASTORAGE_STORAGE_GID}" \
        fmask=0007 dmask=0007 iocharset=utf8 errors=remount-ro; do
        [[ ",${actual_options}," == *",${expected_option},"* ]] ||
            die "Storage mount option is missing: ${expected_option}"
    done
    [[ "$(stat --format=%u "${KURASTORAGE_STORAGE_ROOT}")" == "${KURASTORAGE_STORAGE_UID}" ]] ||
        die "Storage root UID does not match the API user."
    [[ "$(stat --format=%g "${KURASTORAGE_STORAGE_ROOT}")" == "${KURASTORAGE_STORAGE_GID}" ]] ||
        die "Storage root GID does not match the API group."
}

ensure_upload_session_storage() {
    install -d -m 0770 \
        -o "${KURASTORAGE_STORAGE_UID}" \
        -g "${KURASTORAGE_STORAGE_GID}" \
        "${KURASTORAGE_STORAGE_ROOT}/upload-sessions"
}

set_storage_owner_variables() {
    KURASTORAGE_STORAGE_UID="$(id -u kurastorage-api)"
    KURASTORAGE_STORAGE_GID="$(getent group "${KURASTORAGE_STORAGE_ACCESS_GROUP}" | cut -d: -f3)"
    [[ "${KURASTORAGE_STORAGE_UID}" =~ ^[0-9]+$ ]] || die "Invalid storage UID."
    [[ "${KURASTORAGE_STORAGE_GID}" =~ ^[0-9]+$ ]] || die "Invalid storage GID."
    export KURASTORAGE_STORAGE_UID KURASTORAGE_STORAGE_GID
}

verify_storage_identity_file() {
    local identity_file="$1"
    python3 - "${identity_file}" "${KURASTORAGE_STORAGE_ID}" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected = sys.argv[2]
try:
    value = json.loads(path.read_text(encoding="utf-8"))
except (OSError, UnicodeDecodeError, json.JSONDecodeError):
    raise SystemExit(1)
if not isinstance(value, dict):
    raise SystemExit(1)
if value.get("storageId") != expected or value.get("formatVersion") != 1:
    raise SystemExit(1)
PY
}

verify_postgresql() {
    local actual_major
    actual_major="$(runuser -u postgres -- psql --no-psqlrc --tuples-only --command \
        "SHOW server_version_num;" | tr -d '[:space:]')"
    [[ "${actual_major}" =~ ^17[0-9]{4}$ ]] ||
        die "Running PostgreSQL server must be major version 17."
}

verify_systemd_service_unit() {
    local unit_file="$1"
    local output=""
    if output="$(systemd-analyze verify "${unit_file}" 2>&1)"; then
        return
    fi
    # During an initial versioned cutover, /opt/kurastorage/current can still
    # point at the legacy layout. Accept only that expected ExecStart lookup
    # failure; parser, directive, and dependency errors must still stop install.
    if grep -q 'Command .* is not executable' <<<"${output}" &&
        ! grep -qE 'Unknown key|Failed to parse|Missing dependency|Invalid argument' \
            <<<"${output}"; then
        printf '%s\n' "${output}"
        return
    fi
    printf '%s\n' "${output}" >&2
    die "Generated systemd service unit is invalid."
}

render_runtime_configuration() {
    local release_directory="$1"
    install -d -m 0750 -o root -g "${KURASTORAGE_STORAGE_ACCESS_GROUP}" "${CONFIG_ROOT}"
    render_template \
        "${DEPLOYMENT_DIR}/config/server/appsettings.Production.json.template" \
        "${release_directory}/appsettings.Production.json"
    chown "root:${KURASTORAGE_STORAGE_ACCESS_GROUP}" \
        "${release_directory}/appsettings.Production.json"
}

install_release_artifact() {
    local release_directory="${INSTALL_ROOT}/versions/${KURASTORAGE_VERSION}"
    [[ ! -e "${release_directory}" ]] || die "Release already exists: ${KURASTORAGE_VERSION}"
    install -d -m 0755 "${release_directory}"
    tar --extract --gzip --file "${KURASTORAGE_ARTIFACT_FILE}" --directory "${release_directory}"
    [[ -x "${release_directory}/KuraStorage.Api" ]] || die "API executable is absent from artifact."
    [[ -x "${release_directory}/KuraStorage.AdminCli" ]] ||
        die "Admin CLI executable is absent from artifact."
    [[ -x "${release_directory}/KuraStorage.Worker" ]] ||
        die "Worker executable is absent from artifact."
    chown -R root:root "${release_directory}"
    render_runtime_configuration "${release_directory}"
    printf '%s\n' "${release_directory}"
}

backup_database() {
    local backup_file="${BACKUP_ROOT}/pre-${KURASTORAGE_VERSION}.dump"
    install -d -m 0770 -o root -g postgres "${BACKUP_ROOT}"
    runuser -u postgres -- pg_dump \
        --format=custom \
        --file="${backup_file}" \
        "${KURASTORAGE_POSTGRES_DATABASE}" ||
        die "PostgreSQL pre-upgrade backup failed."
    [[ -s "${backup_file}" ]] || die "PostgreSQL pre-upgrade backup is empty."
    chown root:postgres "${backup_file}"
    chmod 0640 "${backup_file}"
    printf '%s\n' "${backup_file}"
}

apply_migrations() {
    local release_directory="$1"
    (
        cd "${release_directory}"
        runuser -u kurastorage-api -- \
            env \
            DOTNET_ENVIRONMENT=Production \
            KURASTORAGE_SECRETS_DIR="${CONFIG_ROOT}/secrets" \
            "${release_directory}/KuraStorage.AdminCli" database migrate
    )
}

verify_no_unfinished_purges() {
    local unfinished
    unfinished="$(runuser -u postgres -- psql --no-psqlrc --tuples-only \
        --dbname="${KURASTORAGE_POSTGRES_DATABASE}" --command \
        "SELECT count(*) FROM file_operations WHERE operation_type = 'PURGE' AND status <> 'COMPLETED';" |
        tr -d '[:space:]')"
    [[ "${unfinished}" == "0" ]] ||
        die "Rollback is blocked while unfinished PURGE operations exist."
}

verify_no_unfinished_upload_sessions() {
    local unfinished
    unfinished="$(runuser -u postgres -- psql --no-psqlrc --tuples-only \
        --dbname="${KURASTORAGE_POSTGRES_DATABASE}" --command \
        "SELECT count(*) FROM upload_sessions WHERE status IN ('ACTIVE', 'COMPLETING', 'RECOVERY_REQUIRED');" |
        tr -d '[:space:]')"
    [[ "${unfinished}" == "0" ]] ||
        die "Rollback is blocked while resumable upload sessions require the current server."
}

verify_no_active_media_jobs() {
    local active
    active="$(runuser -u postgres -- psql --no-psqlrc --tuples-only \
        --dbname="${KURASTORAGE_POSTGRES_DATABASE}" --command \
        "SELECT count(*) FROM media_jobs WHERE status IN ('QUEUED', 'RUNNING');" | \
        tr -d '[:space:]')"
    [[ "${active}" == "0" ]] ||
        die "Rollback is blocked while active Media Jobs require the current worker."
}

activate_release() {
    local release_directory="$1"
    local current_target=""
    if [[ -L "${INSTALL_ROOT}/current" ]]; then
        current_target="$(readlink -f "${INSTALL_ROOT}/current")"
    fi
    if [[ -n "${current_target}" ]]; then
        ln -sfn "${current_target}" "${INSTALL_ROOT}/previous"
    fi
    ln -sfn "${release_directory}" "${INSTALL_ROOT}/current"
    systemctl daemon-reload
    systemctl restart kurastorage-api.service
    systemctl restart kurastorage-worker.service
    systemctl reload nginx.service
}
