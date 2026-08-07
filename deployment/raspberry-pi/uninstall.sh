#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deployment/raspberry-pi/lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

require_root
load_config
[[ "${KURASTORAGE_CONFIRM_UNINSTALL:-}" == "REMOVE_APPLICATION_KEEP_DATA" ]] ||
    die "Set KURASTORAGE_CONFIRM_UNINSTALL=REMOVE_APPLICATION_KEEP_DATA."

systemctl disable --now kurastorage-api.service || true
systemctl disable --now "${KURASTORAGE_STORAGE_MOUNT_UNIT}" || true
remove_ufw_coexistence
rm -f /etc/systemd/system/kurastorage-api.service
rm -f "/etc/systemd/system/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
rm -f /etc/systemd/system/nginx.service.d/kurastorage.conf
rm -f /usr/local/libexec/kurastorage/wait-for-api-addresses
rm -f /etc/nginx/sites-enabled/kurastorage.conf
rm -f /etc/nginx/sites-available/kurastorage.conf
rm -f /etc/nftables.d/kurastorage.conf
rm -f /etc/logrotate.d/kurastorage
systemctl daemon-reload
systemctl reload nginx.service || true

printf '%s\n' \
    "Application and exFAT mount units and generated configuration were removed." \
    "Releases, database, storage data, certificates, and keys were preserved."
