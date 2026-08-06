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
apply_migrations "${release_directory}"
activate_release "${release_directory}"
"${SCRIPT_DIR}/verify.sh"
