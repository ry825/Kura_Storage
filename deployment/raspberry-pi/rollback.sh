#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deployment/raspberry-pi/lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

require_root
load_config
previous_target="$(readlink -f "${INSTALL_ROOT}/previous" 2>/dev/null || true)"
[[ -n "${previous_target}" && -x "${previous_target}/KuraStorage.Api" ]] ||
    die "No valid previous release is available."

current_target="$(readlink -f "${INSTALL_ROOT}/current")"
systemctl stop kurastorage-worker.service
verify_no_unfinished_purges
ln -sfn "${current_target}" "${INSTALL_ROOT}/previous"
ln -sfn "${previous_target}" "${INSTALL_ROOT}/current"
systemctl restart kurastorage-api.service
if [[ -x "${INSTALL_ROOT}/current/KuraStorage.Worker" ]]; then
    systemctl enable --now kurastorage-worker.service
else
    systemctl disable --now kurastorage-worker.service
fi
"${SCRIPT_DIR}/verify.sh"

printf '%s\n' \
    "Application rollback completed." \
    "Database restoration is intentionally manual: inspect the migration compatibility" \
    "and use docs/operations/backup-recovery.md only when a database rollback is required."
