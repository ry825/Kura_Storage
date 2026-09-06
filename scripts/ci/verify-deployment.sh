#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repository_root}"

for required_command in envsubst ip nginx nft openssl python3 shellcheck systemd-analyze systemd-escape; do
    command -v "${required_command}" >/dev/null 2>&1 || {
        printf '%s is required for deployment verification.\n' "${required_command}" >&2
        exit 1
    }
done

verify_systemd_unit() {
    local unit_file="$1"
    local output=""
    if ! output="$(systemd-analyze verify "${unit_file}" 2>&1)"; then
        if grep -qE 'Unknown key|Failed to parse|Missing|not executable|No such file' \
            <<<"${output}"; then
            printf '%s\n' "${output}" >&2
            exit 1
        fi
        if grep -qF 'Unit postgresql.service not found' <<<"${output}"; then
            printf 'systemd unit parsed; PostgreSQL service metadata is unavailable in this environment.\n'
            return
        fi
        grep -qE 'SO_PASSRIGHTS|SO_PASSCRED' <<<"${output}" || {
            printf '%s\n' "${output}" >&2
            exit 1
        }
        printf 'systemd unit parsed; user lookup socket access is unavailable in this environment.\n'
    fi
}

mapfile -t shell_scripts < <(find deployment scripts -type f -name '*.sh' -print | sort)
shellcheck "${shell_scripts[@]}"

validation_root="$(mktemp -d)"
trap 'rm -rf "${validation_root}"' EXIT
export KURASTORAGE_VERSION=0.1.0
export KURASTORAGE_API_HOSTNAME=api.kurastorage.example
export KURASTORAGE_LAN_API_IP=192.0.2.10
export KURASTORAGE_LAN_CIDR=192.0.2.0/24
export KURASTORAGE_ZEROTIER_API_IP=198.51.100.10
export KURASTORAGE_ZEROTIER_CIDR=198.51.100.0/24
export KURASTORAGE_ZEROTIER_INTERFACE=zt-test
export KURASTORAGE_STORAGE_MOUNT_PATH="${validation_root}/storage-mount"
export KURASTORAGE_STORAGE_ROOT="${KURASTORAGE_STORAGE_MOUNT_PATH}/KuraStorage"
export KURASTORAGE_STORAGE_DEVICE_UUID=ABCD-1234
export KURASTORAGE_STORAGE_ID=00000000-0000-0000-0000-000000000000
export KURASTORAGE_STORAGE_RESERVE_BYTES=1073741824
export KURASTORAGE_STORAGE_WARNING_BYTES=10737418240
export KURASTORAGE_MEDIA_DERIVATIVE_ROOT=derivatives
export KURASTORAGE_MEDIA_TEMPORARY_ROOT=derivative-temp
export KURASTORAGE_MEDIA_IMAGE_WAIT_MILLISECONDS=2000
export KURASTORAGE_MEDIA_THUMBNAIL_PROFILE_VERSION=1
export KURASTORAGE_MEDIA_IMAGE_PROFILE_VERSION=1
export KURASTORAGE_MEDIA_VIDEO_PROFILE_VERSION=1
export KURASTORAGE_MEDIA_THUMBNAIL_MAX_DIMENSION=512
export KURASTORAGE_MEDIA_THUMBNAIL_WEBP_QUALITY=75
export KURASTORAGE_MEDIA_JOB_POLL_MILLISECONDS=500
export KURASTORAGE_MEDIA_JOB_HEARTBEAT_SECONDS=10
export KURASTORAGE_MEDIA_STALE_JOB_SECONDS=120
export KURASTORAGE_MEDIA_MAXIMUM_ATTEMPTS=3
export KURASTORAGE_MEDIA_GENERATION_LEASE_SECONDS=120
export KURASTORAGE_MEDIA_DELIVERY_LEASE_SECONDS=120
export KURASTORAGE_MEDIA_DELIVERY_LEASE_RENEWAL_SECONDS=30
export KURASTORAGE_MEDIA_CACHE_TTL_HOURS=24
export KURASTORAGE_MEDIA_CACHE_HIGH_WATERMARK_BYTES=10737418240
export KURASTORAGE_MEDIA_CACHE_LOW_WATERMARK_BYTES=6442450944
export KURASTORAGE_MEDIA_CLEANUP_INTERVAL_MINUTES=30
export KURASTORAGE_MEDIA_CLEANUP_MANUAL_POLL_SECONDS=5
export KURASTORAGE_MEDIA_CLEANUP_RUN_LEASE_MINUTES=15
export KURASTORAGE_MEDIA_CLEANUP_BATCH_SIZE=100
export KURASTORAGE_MEDIA_TERMINAL_JOB_RETENTION_DAYS=7
export KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_MEDIA_JOBS=1
export KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_THUMBNAIL_JOBS=2
export KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_VIDEO_JOBS=1
export KURASTORAGE_MEDIA_VIPS_PATH=/usr/bin/vips
export KURASTORAGE_MEDIA_FFMPEG_PATH=/usr/bin/ffmpeg
export KURASTORAGE_MEDIA_FFPROBE_PATH=/usr/bin/ffprobe
export KURASTORAGE_MEDIA_PDFTOPPM_PATH=/usr/bin/pdftoppm
export KURASTORAGE_TRASH_RETENTION_DAYS=30
export KURASTORAGE_TRASH_INTERVAL_HOURS=24
export KURASTORAGE_TRASH_BATCH_SIZE=100
export KURASTORAGE_TRASH_RETRY_DELAY_MINUTES=15
export KURASTORAGE_STORAGE_MOUNT_UNIT
KURASTORAGE_STORAGE_MOUNT_UNIT="$(systemd-escape --path --suffix=mount \
    "${KURASTORAGE_STORAGE_MOUNT_PATH}")"
export KURASTORAGE_STORAGE_UID
KURASTORAGE_STORAGE_UID="$(id -u)"
export KURASTORAGE_STORAGE_GID
KURASTORAGE_STORAGE_GID="$(id -g)"
KURASTORAGE_STORAGE_ACCESS_GROUP="$(id -gn)"
export KURASTORAGE_STORAGE_ACCESS_GROUP
mkdir -p "${KURASTORAGE_STORAGE_ROOT}"
export KURASTORAGE_POSTGRES_DATABASE=kurastorage
export KURASTORAGE_POSTGRES_ROLE=kurastorage-api

fake_bin="${validation_root}/fake-bin"
fake_ufw_log="${validation_root}/ufw-added.log"
mkdir -p "${fake_bin}"
cat >"${fake_bin}/ufw" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
    status)
        printf 'Status: active\n'
        ;;
    show)
        [[ "${2:-}" == "added" ]]
        cat "${KURASTORAGE_FAKE_UFW_LOG}"
        ;;
    allow)
        printf 'ufw %s\n' "$*" >>"${KURASTORAGE_FAKE_UFW_LOG}"
        ;;
    --force)
        printf 'ufw %s\n' "$*" >>"${KURASTORAGE_FAKE_UFW_LOG}"
        ;;
    *)
        exit 1
        ;;
esac
EOF
chmod 0755 "${fake_bin}/ufw"
: >"${fake_ufw_log}"
export KURASTORAGE_FAKE_UFW_LOG="${fake_ufw_log}"
original_path="${PATH}"
PATH="${fake_bin}:${PATH}"
# shellcheck source=deployment/raspberry-pi/lib/common.sh
source deployment/raspberry-pi/lib/common.sh
ensure_upload_session_storage
[[ -d "${KURASTORAGE_STORAGE_ROOT}/upload-sessions" ]]
[[ "$(stat --format=%a "${KURASTORAGE_STORAGE_ROOT}/upload-sessions")" == "770" ]]
configure_ufw_coexistence
verify_ufw_coexistence
remove_ufw_coexistence
PATH="${original_path}"
grep -Fq \
    "ufw allow from ${KURASTORAGE_LAN_CIDR} to ${KURASTORAGE_LAN_API_IP} port 22 proto tcp comment KuraStorage LAN SSH" \
    "${fake_ufw_log}"
grep -Fq \
    "ufw allow from ${KURASTORAGE_LAN_CIDR} to ${KURASTORAGE_LAN_API_IP} port 443 proto tcp comment KuraStorage LAN HTTPS" \
    "${fake_ufw_log}"
grep -Fq \
    "ufw allow in on ${KURASTORAGE_ZEROTIER_INTERFACE} from ${KURASTORAGE_ZEROTIER_CIDR} to ${KURASTORAGE_ZEROTIER_API_IP} port 443 proto tcp comment KuraStorage ZeroTier HTTPS" \
    "${fake_ufw_log}"
grep -Fq \
    "ufw --force delete allow in on ${KURASTORAGE_ZEROTIER_INTERFACE} from ${KURASTORAGE_ZEROTIER_CIDR} to ${KURASTORAGE_ZEROTIER_API_IP} port 443 proto tcp" \
    "${fake_ufw_log}"
# shellcheck disable=SC2016
template_variables='${KURASTORAGE_API_HOSTNAME} ${KURASTORAGE_LAN_API_IP} ${KURASTORAGE_LAN_CIDR} ${KURASTORAGE_ZEROTIER_API_IP} ${KURASTORAGE_ZEROTIER_CIDR} ${KURASTORAGE_ZEROTIER_INTERFACE} ${KURASTORAGE_STORAGE_MOUNT_PATH} ${KURASTORAGE_STORAGE_ROOT} ${KURASTORAGE_STORAGE_DEVICE_UUID} ${KURASTORAGE_STORAGE_ID} ${KURASTORAGE_STORAGE_RESERVE_BYTES} ${KURASTORAGE_STORAGE_WARNING_BYTES} ${KURASTORAGE_MEDIA_DERIVATIVE_ROOT} ${KURASTORAGE_MEDIA_TEMPORARY_ROOT} ${KURASTORAGE_MEDIA_IMAGE_WAIT_MILLISECONDS} ${KURASTORAGE_MEDIA_THUMBNAIL_PROFILE_VERSION} ${KURASTORAGE_MEDIA_IMAGE_PROFILE_VERSION} ${KURASTORAGE_MEDIA_VIDEO_PROFILE_VERSION} ${KURASTORAGE_MEDIA_THUMBNAIL_MAX_DIMENSION} ${KURASTORAGE_MEDIA_THUMBNAIL_WEBP_QUALITY} ${KURASTORAGE_MEDIA_JOB_POLL_MILLISECONDS} ${KURASTORAGE_MEDIA_JOB_HEARTBEAT_SECONDS} ${KURASTORAGE_MEDIA_STALE_JOB_SECONDS} ${KURASTORAGE_MEDIA_MAXIMUM_ATTEMPTS} ${KURASTORAGE_MEDIA_GENERATION_LEASE_SECONDS} ${KURASTORAGE_MEDIA_DELIVERY_LEASE_SECONDS} ${KURASTORAGE_MEDIA_DELIVERY_LEASE_RENEWAL_SECONDS} ${KURASTORAGE_MEDIA_CACHE_TTL_HOURS} ${KURASTORAGE_MEDIA_CACHE_HIGH_WATERMARK_BYTES} ${KURASTORAGE_MEDIA_CACHE_LOW_WATERMARK_BYTES} ${KURASTORAGE_MEDIA_CLEANUP_INTERVAL_MINUTES} ${KURASTORAGE_MEDIA_CLEANUP_MANUAL_POLL_SECONDS} ${KURASTORAGE_MEDIA_CLEANUP_RUN_LEASE_MINUTES} ${KURASTORAGE_MEDIA_CLEANUP_BATCH_SIZE} ${KURASTORAGE_MEDIA_TERMINAL_JOB_RETENTION_DAYS} ${KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_MEDIA_JOBS} ${KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_THUMBNAIL_JOBS} ${KURASTORAGE_MEDIA_MAXIMUM_CONCURRENT_VIDEO_JOBS} ${KURASTORAGE_MEDIA_VIPS_PATH} ${KURASTORAGE_MEDIA_FFMPEG_PATH} ${KURASTORAGE_MEDIA_FFPROBE_PATH} ${KURASTORAGE_MEDIA_PDFTOPPM_PATH} ${KURASTORAGE_TRASH_RETENTION_DAYS} ${KURASTORAGE_TRASH_INTERVAL_HOURS} ${KURASTORAGE_TRASH_BATCH_SIZE} ${KURASTORAGE_TRASH_RETRY_DELAY_MINUTES} ${KURASTORAGE_STORAGE_MOUNT_UNIT} ${KURASTORAGE_STORAGE_ACCESS_GROUP} ${KURASTORAGE_STORAGE_UID} ${KURASTORAGE_STORAGE_GID} ${KURASTORAGE_POSTGRES_DATABASE} ${KURASTORAGE_POSTGRES_ROLE}'

envsubst "${template_variables}" \
    <deployment/config/server/appsettings.Production.json.template \
    >"${validation_root}/appsettings.Production.json"
python3 -m json.tool "${validation_root}/appsettings.Production.json" >/dev/null
python3 - "${validation_root}/appsettings.Production.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    config = json.load(source)
storage = config["Storage"]
purge = config["TrashPurge"]
upload = config["UploadSession"]
indexing = config["Indexing"]
media = config["Media"]
log_levels = config["Logging"]["LogLevel"]
assert storage["CapacityWarningFreeBytes"] >= storage["MinimumFreeBytes"] > 0
assert media["MaximumConcurrentMediaJobs"] == 1
assert media["MaximumConcurrentThumbnailJobs"] == 2
assert media["MaximumConcurrentVideoJobs"] == 1
assert 1 <= media["CleanupManualPollSeconds"] <= 60
assert 1 <= media["CleanupRunLeaseMinutes"] <= 1440
assert purge["RetentionDays"] >= 30
assert 1 <= purge["IntervalHours"] <= 168
assert 1 <= purge["BatchSize"] <= 500
assert 1 <= purge["RetryDelayMinutes"] <= 1440
assert 262144 <= upload["PreferredChunkBytes"] <= upload["MaximumChunkBytes"]
assert upload["MaximumChunkBytes"] == 8388608
assert upload["IdleExpirationHours"] <= upload["AbsoluteExpirationHours"]
assert 1 <= upload["CleanupBatchSize"] <= 500
assert upload["MaximumActiveSessionsPerDevice"] <= upload["MaximumActiveSessionsPerUser"]
assert 1 <= upload["MaximumConcurrentChunkWrites"] <= 16
assert indexing["Enabled"] is False
assert 10 <= indexing["BatchSize"] <= 5000
assert 1 <= indexing["MissingConfirmationDelayMinutes"] <= 1440
assert 1 <= indexing["StagingRetentionHours"] <= 168
assert 5 <= indexing["FullRescanIntervalMinutes"] <= 10080
assert indexing["RunOnStartup"] is True
assert 50 <= indexing["EventDebounceMilliseconds"] <= 10000
assert 100 <= indexing["MovePairingWindowMilliseconds"] <= 30000
assert 128 <= indexing["EventQueueCapacity"] <= 65536
assert 1 <= indexing["RetryBackoffSeconds"] <= 3600
assert log_levels["Microsoft.AspNetCore.Http.Result.FileStreamResult"] == "Warning"
PY

envsubst "${template_variables}" \
    <deployment/config/systemd/storage.mount.template \
    >"${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
verify_systemd_unit "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
grep -q '^Type=exfat$' "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
grep -q 'fmask=0007,dmask=0007' "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
grep -q "^Where=${KURASTORAGE_STORAGE_MOUNT_PATH}$" \
    "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"

envsubst "${template_variables}" \
    <deployment/config/systemd/kurastorage-api.service.template \
    >"${validation_root}/kurastorage-api.service"
sed -i \
    -e "s/^User=.*/User=$(id -un)/" \
    -e "s/^Group=.*/Group=$(id -gn)/" \
    -e '/^SupplementaryGroups=/d' \
    -e 's#^ExecStart=.*#ExecStart=/bin/true#' \
    -e "s#^ReadWritePaths=.*#ReadWritePaths=${validation_root}#" \
    "${validation_root}/kurastorage-api.service"
verify_systemd_unit "${validation_root}/kurastorage-api.service"

envsubst "${template_variables}" \
    <deployment/config/systemd/kurastorage-worker.service.template \
    >"${validation_root}/kurastorage-worker.service"
sed -i \
    -e "s/^User=.*/User=$(id -un)/" \
    -e "s/^Group=.*/Group=$(id -gn)/" \
    -e '/^SupplementaryGroups=/d' \
    -e 's#^ExecStart=.*#ExecStart=/bin/true#' \
    -e "s#^ReadWritePaths=.*#ReadWritePaths=${validation_root}#" \
    "${validation_root}/kurastorage-worker.service"
verify_systemd_unit "${validation_root}/kurastorage-worker.service"
grep -q '^PrivateNetwork=true$' "${validation_root}/kurastorage-worker.service"
grep -q '^RestrictAddressFamilies=AF_UNIX$' "${validation_root}/kurastorage-worker.service"
grep -q '^TimeoutStopSec=45s$' "${validation_root}/kurastorage-worker.service"
grep -q '^KillMode=control-group$' "${validation_root}/kurastorage-worker.service"
grep -q '^OOMPolicy=stop$' "${validation_root}/kurastorage-worker.service"
grep -q '^LimitNOFILE=65536$' "${validation_root}/kurastorage-worker.service"
grep -q '^CPUQuota=125%$' "${validation_root}/kurastorage-worker.service"
# shellcheck disable=SC2016
grep -Fq '[[ -x "${INSTALL_ROOT}/current/KuraStorage.Worker" ]]' deployment/raspberry-pi/rollback.sh
grep -Fq 'systemctl disable --now kurastorage-worker.service' deployment/raspberry-pi/rollback.sh
grep -Fq 'verify_no_unfinished_upload_sessions' deployment/raspberry-pi/rollback.sh
grep -Fq 'verify_no_active_media_jobs' deployment/raspberry-pi/rollback.sh
grep -Fq 'ensure_upload_session_storage' deployment/raspberry-pi/upgrade.sh
grep -Fq 'install_media_dependencies' deployment/raspberry-pi/install.sh
grep -Fq 'install_media_dependencies' deployment/raspberry-pi/upgrade.sh
grep -Fq 'ensure_media_storage' deployment/raspberry-pi/install.sh
grep -Fq 'ensure_media_storage' deployment/raspberry-pi/upgrade.sh
grep -Fq 'verify_media_dependencies' deployment/raspberry-pi/verify.sh
grep -Fq 'media-runtime-packages.sbom' deployment/raspberry-pi/verify.sh
# shellcheck disable=SC2016
grep -Fq '[[ -x "${INSTALL_ROOT}/current/KuraStorage.Worker" ]]' deployment/raspberry-pi/verify.sh

envsubst "${template_variables}" \
    <deployment/config/systemd/nginx-kurastorage.conf.template \
    >"${validation_root}/nginx-kurastorage.conf"
grep -q "wait-for-api-addresses ${KURASTORAGE_LAN_API_IP} ${KURASTORAGE_ZEROTIER_API_IP} /run/kurastorage/api.sock 120" \
    "${validation_root}/nginx-kurastorage.conf"
if ip -o address show to 127.0.0.1 >/dev/null 2>&1; then
    socket_path="${validation_root}/api.sock"
    python3 - "${socket_path}" <<'PY' &
import socket
import sys
import time

server = socket.socket(socket.AF_UNIX)
server.bind(sys.argv[1])
time.sleep(5)
PY
    socket_pid=$!
    deployment/raspberry-pi/wait-for-api-addresses.sh \
        127.0.0.1 127.0.0.1 "${socket_path}" 2 >/dev/null
    kill "${socket_pid}" 2>/dev/null || true
    wait "${socket_pid}" 2>/dev/null || true
else
    printf 'Address wait script syntax verified; netlink access is unavailable.\n'
fi

envsubst "${template_variables}" \
    <deployment/config/firewall/nftables.conf.template \
    >"${validation_root}/nftables.conf"
nft_output=""
nft_command=(nft)
if [[ "${CI:-}" == "true" ]]; then
    nft_command=(sudo nft)
fi
if ! nft_output="$("${nft_command[@]}" --check \
    --file "${validation_root}/nftables.conf" 2>&1)"; then
    if grep -qE 'syntax error|unexpected|No such file or directory' <<<"${nft_output}"; then
        printf '%s\n' "${nft_output}" >&2
        exit 1
    fi
    grep -qE 'cache initialization failed: Operation not permitted|Unable to initialize Netlink socket' \
        <<<"${nft_output}" || {
        printf '%s\n' "${nft_output}" >&2
        exit 1
    }
    printf 'nftables grammar parsed; kernel ruleset access is unavailable in this environment.\n'
fi

openssl req -x509 -newkey rsa:2048 -nodes -days 1 \
    -subj "/CN=${KURASTORAGE_API_HOSTNAME}" \
    -keyout "${validation_root}/server.key" \
    -out "${validation_root}/server.crt" >/dev/null 2>&1
envsubst "${template_variables}" \
    <deployment/config/nginx/kurastorage.conf.template |
    sed \
        -e "s#/etc/kurastorage/tls/server.crt#${validation_root}/server.crt#g" \
        -e "s#/etc/kurastorage/tls/server.key#${validation_root}/server.key#g" \
        -e "s#/var/log/nginx/kurastorage-access.log#${validation_root}/kurastorage-access.log#g" \
        >"${validation_root}/kurastorage-site.conf"
cat >"${validation_root}/nginx.conf" <<EOF
pid ${validation_root}/nginx.pid;
error_log ${validation_root}/error.log;
events {}
http {
    access_log off;
    client_body_temp_path ${validation_root}/client-body;
    fastcgi_temp_path ${validation_root}/fastcgi;
    proxy_temp_path ${validation_root}/proxy;
    scgi_temp_path ${validation_root}/scgi;
    uwsgi_temp_path ${validation_root}/uwsgi;
    include ${KURASTORAGE_NGINX_MIME_TYPES:-/etc/nginx/mime.types};
    include ${validation_root}/kurastorage-site.conf;
}
EOF
nginx_output=""
[[ "$(grep -c 'client_max_body_size 8m;' "${validation_root}/kurastorage-site.conf")" == "2" ]]
[[ "$(grep -c 'proxy_request_buffering off;' "${validation_root}/kurastorage-site.conf")" -ge "2" ]]
if ! nginx_output="$(nginx -t -p "${validation_root}" \
    -c "${validation_root}/nginx.conf" 2>&1)"; then
    if grep -q 'syntax is ok' <<<"${nginx_output}" &&
        grep -qE 'socket\(\).*Operation not permitted|bind\(\).*Cannot assign requested address' \
            <<<"${nginx_output}"; then
        printf 'Nginx syntax parsed; listen socket access is unavailable in this environment.\n'
    else
        printf '%s\n' "${nginx_output}" >&2
        exit 1
    fi
fi

printf 'Deployment configuration verification passed.\n'
