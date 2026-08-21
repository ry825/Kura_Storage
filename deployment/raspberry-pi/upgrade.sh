#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deployment/raspberry-pi/lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

require_root
load_config
set_storage_owner_variables
verify_storage_mount
verify_postgresql

release_directory="$(install_release_artifact)"
backup_file="$(backup_database)"
printf 'Database backup created: %s\n' "${backup_file}"
render_template \
    "${DEPLOYMENT_DIR}/config/systemd/kurastorage-worker.service.template" \
    /etc/systemd/system/kurastorage-worker.service
chmod 0644 /etc/systemd/system/kurastorage-worker.service
verify_systemd_service_unit /etc/systemd/system/kurastorage-worker.service
systemctl daemon-reload
systemctl enable kurastorage-worker.service
systemctl stop kurastorage-worker.service
trap 'systemctl start kurastorage-worker.service || true' ERR
apply_migrations "${release_directory}"
activate_release "${release_directory}"
trap - ERR
"${SCRIPT_DIR}/verify.sh"
