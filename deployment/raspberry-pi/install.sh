#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deployment/raspberry-pi/lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

require_root
load_config
for command_name in apt-get blkid createuser createdb dpkg dpkg-query envsubst findmnt getent grep groupadd id ip \
    install mkdir mountpoint nginx nft openssl pg_conftool psql python3 readlink \
    runuser stat systemctl systemd-analyze systemd-escape tar useradd usermod; do
    require_command "${command_name}"
done

getent group kurastorage-secrets >/dev/null ||
    groupadd --system kurastorage-secrets
getent group "${KURASTORAGE_STORAGE_ACCESS_GROUP}" >/dev/null ||
    groupadd --system "${KURASTORAGE_STORAGE_ACCESS_GROUP}"
id "${KURASTORAGE_STORAGE_ACCESS_USER}" >/dev/null 2>&1 ||
    die "Storage access user does not exist: ${KURASTORAGE_STORAGE_ACCESS_USER}"
id kurastorage-api >/dev/null 2>&1 ||
    useradd --system --gid "${KURASTORAGE_STORAGE_ACCESS_GROUP}" \
        --groups kurastorage-secrets \
        --home-dir /var/lib/kurastorage --shell /usr/sbin/nologin kurastorage-api
usermod --append --groups "${KURASTORAGE_STORAGE_ACCESS_GROUP}" kurastorage-api
usermod --append --groups kurastorage-secrets kurastorage-api
usermod --append --groups "${KURASTORAGE_STORAGE_ACCESS_GROUP}" \
    "${KURASTORAGE_STORAGE_ACCESS_USER}"
set_storage_owner_variables

install -d -m 0755 "${INSTALL_ROOT}/versions"
install -d -m 0750 -o kurastorage-api \
    -g "${KURASTORAGE_STORAGE_ACCESS_GROUP}" "${STATE_ROOT}"
install -d -m 0750 -o root -g kurastorage-secrets "${CONFIG_ROOT}/secrets"
install -d -m 0750 -o root -g www-data "${CONFIG_ROOT}/tls"
install -d -m 0750 -o kurastorage-api -g adm /var/log/kurastorage
install_media_dependencies

expected_mount_unit="$(systemd-escape --path --suffix=mount "${KURASTORAGE_STORAGE_MOUNT_PATH}")"
[[ "${expected_mount_unit}" == "${KURASTORAGE_STORAGE_MOUNT_UNIT}" ]] ||
    die "Storage mount unit does not match the storage mount path."
storage_device="/dev/disk/by-uuid/${KURASTORAGE_STORAGE_DEVICE_UUID}"
[[ -b "${storage_device}" ]] || die "Configured HDD UUID does not resolve to a block device."
[[ "${KURASTORAGE_STORAGE_MOUNT_PATH}" != *$'\n'* ]] || die "Invalid storage mount path."
[[ "$(blkid -o value -s TYPE "${storage_device}")" == "exfat" ]] ||
    die "The shared HDD must use exFAT."
install -d -m 0755 "${KURASTORAGE_STORAGE_MOUNT_PATH}"
render_template \
    "${DEPLOYMENT_DIR}/config/systemd/storage.mount.template" \
    "/etc/systemd/system/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
chmod 0644 "/etc/systemd/system/${KURASTORAGE_STORAGE_MOUNT_UNIT}"
systemctl daemon-reload
systemctl enable "${KURASTORAGE_STORAGE_MOUNT_UNIT}"
systemctl restart "${KURASTORAGE_STORAGE_MOUNT_UNIT}"
mkdir -p "${KURASTORAGE_STORAGE_ROOT}"
verify_storage_mount
identity_file="${KURASTORAGE_STORAGE_ROOT}/.storage-identity"
# KURASTORAGE_STORAGE_ID is loaded dynamically from the protected config file.
# shellcheck disable=SC2153
if [[ -e "${identity_file}" ]]; then
    verify_storage_identity_file "${identity_file}" ||
        die "Storage identity does not match deployment configuration."
else
    printf '{"storageId":"%s","formatVersion":1}\n' \
        "${KURASTORAGE_STORAGE_ID}" >"${identity_file}"
fi
mkdir -p \
    "${KURASTORAGE_STORAGE_ROOT}/users" \
    "${KURASTORAGE_STORAGE_ROOT}/upload-temp" \
    "${KURASTORAGE_STORAGE_ROOT}/upload-sessions"
ensure_media_storage

install -m 0640 -o root -g www-data "${KURASTORAGE_TLS_CERT_FILE}" \
    "${CONFIG_ROOT}/tls/server.crt"
install -m 0640 -o root -g www-data "${KURASTORAGE_TLS_KEY_FILE}" \
    "${CONFIG_ROOT}/tls/server.key"
install -m 0644 -o root -g root "${KURASTORAGE_TLS_CA_CERT_FILE}" \
    "${CONFIG_ROOT}/tls/root-ca.crt"
install -m 0640 -o root -g kurastorage-secrets "${KURASTORAGE_JWT_KEY_FILE}" \
    "${CONFIG_ROOT}/secrets/jwt-signing-key.pem"

verify_postgresql
pg_conftool 17 main set listen_addresses localhost
pg_conftool 17 main set unix_socket_directories /var/run/postgresql
systemctl restart postgresql.service
if ! runuser -u postgres -- psql --no-psqlrc --tuples-only --command \
    "SELECT 1 FROM pg_roles WHERE rolname = '${KURASTORAGE_POSTGRES_ROLE}'" |
    grep -q 1; then
    runuser -u postgres -- createuser --no-createdb --no-createrole --no-superuser \
        "${KURASTORAGE_POSTGRES_ROLE}"
fi
runuser -u postgres -- psql --no-psqlrc --set=ON_ERROR_STOP=1 --command \
    "ALTER ROLE \"${KURASTORAGE_POSTGRES_ROLE}\" NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION CONNECTION LIMIT 20;"
if ! runuser -u postgres -- psql --no-psqlrc --tuples-only --command \
    "SELECT 1 FROM pg_database WHERE datname = '${KURASTORAGE_POSTGRES_DATABASE}'" |
    grep -q 1; then
    runuser -u postgres -- createdb --owner="${KURASTORAGE_POSTGRES_ROLE}" \
        "${KURASTORAGE_POSTGRES_DATABASE}"
fi
runuser -u postgres -- psql --no-psqlrc --set=ON_ERROR_STOP=1 --command \
    "REVOKE CONNECT ON DATABASE \"${KURASTORAGE_POSTGRES_DATABASE}\" FROM PUBLIC;"
runuser -u postgres -- psql --no-psqlrc --set=ON_ERROR_STOP=1 --command \
    "GRANT CONNECT ON DATABASE \"${KURASTORAGE_POSTGRES_DATABASE}\" TO \"${KURASTORAGE_POSTGRES_ROLE}\";"

render_template \
    "${DEPLOYMENT_DIR}/config/systemd/kurastorage-api.service.template" \
    /etc/systemd/system/kurastorage-api.service
chmod 0644 /etc/systemd/system/kurastorage-api.service
render_template \
    "${DEPLOYMENT_DIR}/config/systemd/kurastorage-worker.service.template" \
    /etc/systemd/system/kurastorage-worker.service
chmod 0644 /etc/systemd/system/kurastorage-worker.service
render_template \
    "${DEPLOYMENT_DIR}/config/nginx/kurastorage.conf.template" \
    /etc/nginx/sites-available/kurastorage.conf
install -d -m 0755 /usr/local/libexec/kurastorage /etc/systemd/system/nginx.service.d
install -m 0755 "${SCRIPT_DIR}/wait-for-api-addresses.sh" \
    /usr/local/libexec/kurastorage/wait-for-api-addresses
render_template \
    "${DEPLOYMENT_DIR}/config/systemd/nginx-kurastorage.conf.template" \
    /etc/systemd/system/nginx.service.d/kurastorage.conf
chmod 0644 /etc/systemd/system/nginx.service.d/kurastorage.conf
rm -f /etc/nginx/sites-enabled/default
ln -sfn /etc/nginx/sites-available/kurastorage.conf /etc/nginx/sites-enabled/kurastorage.conf
install -d -m 0755 /etc/nftables.d
render_template \
    "${DEPLOYMENT_DIR}/config/firewall/nftables.conf.template" \
    /etc/nftables.d/kurastorage.conf
grep -qxF 'include "/etc/nftables.d/*.conf"' /etc/nftables.conf ||
    printf '%s\n' 'include "/etc/nftables.d/*.conf"' >>/etc/nftables.conf
install -m 0644 "${DEPLOYMENT_DIR}/config/logrotate/kurastorage" \
    /etc/logrotate.d/kurastorage

nginx -t
nft --check --file /etc/nftables.d/kurastorage.conf
verify_systemd_service_unit /etc/systemd/system/kurastorage-api.service
verify_systemd_service_unit /etc/systemd/system/kurastorage-worker.service
systemctl daemon-reload
systemd-analyze verify nginx.service
systemctl enable nftables.service
systemctl reload-or-restart nftables.service
configure_ufw_coexistence

release_directory="$(install_release_artifact)"
apply_migrations "${release_directory}"
activate_release "${release_directory}"
systemctl enable kurastorage-api.service kurastorage-worker.service nginx.service postgresql.service

"${SCRIPT_DIR}/verify.sh"
