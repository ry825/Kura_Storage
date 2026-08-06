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
template_variables='${KURASTORAGE_API_HOSTNAME} ${KURASTORAGE_LAN_API_IP} ${KURASTORAGE_LAN_CIDR} ${KURASTORAGE_ZEROTIER_API_IP} ${KURASTORAGE_ZEROTIER_CIDR} ${KURASTORAGE_ZEROTIER_INTERFACE} ${KURASTORAGE_STORAGE_MOUNT_PATH} ${KURASTORAGE_STORAGE_ROOT} ${KURASTORAGE_STORAGE_DEVICE_UUID} ${KURASTORAGE_STORAGE_ID} ${KURASTORAGE_STORAGE_RESERVE_BYTES} ${KURASTORAGE_STORAGE_MOUNT_UNIT} ${KURASTORAGE_STORAGE_ACCESS_GROUP} ${KURASTORAGE_STORAGE_UID} ${KURASTORAGE_STORAGE_GID} ${KURASTORAGE_POSTGRES_DATABASE} ${KURASTORAGE_POSTGRES_ROLE}'

envsubst "${template_variables}" \
    <deployment/config/server/appsettings.Production.json.template \
    >"${validation_root}/appsettings.Production.json"
python3 -m json.tool "${validation_root}/appsettings.Production.json" >/dev/null

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
    <deployment/config/systemd/storage.mount.template \
    >"${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
verify_systemd_unit "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
grep -q '^Type=exfat$' "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
grep -q 'fmask=0007,dmask=0007' "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
grep -q "^Where=${KURASTORAGE_STORAGE_MOUNT_PATH}$" \
    "${validation_root}/${KURASTORAGE_STORAGE_MOUNT_UNIT}"

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
if ! nginx_output="$(nginx -t -p "${validation_root}" \
    -c "${validation_root}/nginx.conf" 2>&1)"; then
    if grep -q 'syntax is ok' <<<"${nginx_output}" &&
        grep -q 'socket().*Operation not permitted' <<<"${nginx_output}"; then
        printf 'Nginx syntax parsed; listen socket access is unavailable in this environment.\n'
    else
        printf '%s\n' "${nginx_output}" >&2
        exit 1
    fi
fi

printf 'Deployment configuration verification passed.\n'
