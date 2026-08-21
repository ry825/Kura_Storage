#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deployment/raspberry-pi/lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

require_root
load_config
for command_name in curl findmnt getent id mountpoint nginx nft openssl python3 readlink stat systemctl; do
    require_command "${command_name}"
done

set_storage_owner_variables
verify_storage_mount
verify_storage_identity_file "${KURASTORAGE_STORAGE_ROOT}/.storage-identity" ||
    die "Storage identity mismatch."
systemctl is-active --quiet postgresql.service
systemctl is-active --quiet kurastorage-api.service
systemctl is-active --quiet nginx.service
nginx -t
nft --check --file /etc/nftables.d/kurastorage.conf
verify_ufw_coexistence
[[ "$(systemctl show kurastorage-api.service --property=User --value)" == "kurastorage-api" ]] ||
    die "API is not configured to run as kurastorage-api."
if [[ -x "${INSTALL_ROOT}/current/KuraStorage.Worker" ]]; then
    systemctl is-active --quiet kurastorage-worker.service
    [[ "$(systemctl show kurastorage-worker.service --property=User --value)" == "kurastorage-api" ]] ||
        die "Worker is not configured to run as kurastorage-api."
    [[ "$(systemctl show kurastorage-worker.service --property=Group --value)" == "${KURASTORAGE_STORAGE_ACCESS_GROUP}" ]] ||
        die "Worker does not use the shared storage group."
else
    ! systemctl is-active --quiet kurastorage-worker.service ||
        die "Worker must be inactive when the current release does not contain it."
fi
socket_available=false
for _ in {1..60}; do
    if [[ -S /run/kurastorage/api.sock ]]; then
        socket_available=true
        break
    fi
    sleep 1
done
[[ "${socket_available}" == true ]] || die "API Unix socket is unavailable."

health_json=""
for _ in {1..60}; do
    if health_json="$(curl --fail --silent \
        --cacert "${CONFIG_ROOT}/tls/root-ca.crt" \
        --resolve "${KURASTORAGE_API_HOSTNAME}:443:${KURASTORAGE_LAN_API_IP}" \
        "https://${KURASTORAGE_API_HOSTNAME}/api/v1/system/health" 2>/dev/null)"; then
        break
    fi
    sleep 1
done
grep -q '"api":"AVAILABLE"' <<<"${health_json}" || die "API health is unavailable."
grep -q '"storage":"AVAILABLE"' <<<"${health_json}" || die "Storage health is unavailable."

printf 'KuraStorage deployment verified: %s\n' "${KURASTORAGE_VERSION}"
