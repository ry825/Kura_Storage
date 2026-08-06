#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
    printf 'Usage: %s OUTPUT_DIRECTORY API_HOSTNAME\n' "$0" >&2
    exit 2
fi

output_directory="$1"
api_hostname="$2"
passphrase_file="${KURASTORAGE_CA_PASSPHRASE_FILE:-}"

[[ "${output_directory}" == /* ]] || {
    printf 'Output directory must be absolute.\n' >&2
    exit 2
}
[[ "${api_hostname}" =~ ^[0-9A-Za-z.-]+$ ]] || {
    printf 'Invalid API hostname.\n' >&2
    exit 2
}
[[ -n "${passphrase_file}" && -f "${passphrase_file}" ]] || {
    printf 'KURASTORAGE_CA_PASSPHRASE_FILE must reference a protected file.\n' >&2
    exit 2
}

umask 077
mkdir -p "${output_directory}/root-ca" "${output_directory}/server"
for path in \
    "${output_directory}/root-ca/root-ca.key" \
    "${output_directory}/root-ca/root-ca.crt" \
    "${output_directory}/server/server.key" \
    "${output_directory}/server/server.crt"; do
    [[ ! -e "${path}" ]] || {
        printf 'Refusing to overwrite existing material: %s\n' "${path}" >&2
        exit 1
    }
done

root_config="$(mktemp)"
server_config="$(mktemp)"
trap 'rm -f "${root_config}" "${server_config}"' EXIT

cat >"${root_config}" <<'EOF'
[req]
distinguished_name = dn
x509_extensions = root_ext
prompt = no
[dn]
CN = KuraStorage Offline Root CA
[root_ext]
basicConstraints = critical,CA:TRUE,pathlen:0
keyUsage = critical,keyCertSign,cRLSign
subjectKeyIdentifier = hash
EOF

cat >"${server_config}" <<EOF
[req]
distinguished_name = dn
req_extensions = server_req_ext
prompt = no
[dn]
CN = ${api_hostname}
[server_req_ext]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = DNS:${api_hostname}
subjectKeyIdentifier = hash
[server_cert_ext]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = DNS:${api_hostname}
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer
EOF

openssl genpkey \
    -algorithm RSA \
    -pkeyopt rsa_keygen_bits:4096 \
    -aes-256-cbc \
    -pass "file:${passphrase_file}" \
    -out "${output_directory}/root-ca/root-ca.key"
openssl req \
    -new \
    -x509 \
    -sha256 \
    -days 3650 \
    -key "${output_directory}/root-ca/root-ca.key" \
    -passin "file:${passphrase_file}" \
    -config "${root_config}" \
    -out "${output_directory}/root-ca/root-ca.crt"

openssl genpkey \
    -algorithm RSA \
    -pkeyopt rsa_keygen_bits:3072 \
    -out "${output_directory}/server/server.key"
openssl req \
    -new \
    -sha256 \
    -key "${output_directory}/server/server.key" \
    -config "${server_config}" \
    -out "${output_directory}/server/server.csr"
openssl x509 \
    -req \
    -sha256 \
    -days 397 \
    -in "${output_directory}/server/server.csr" \
    -CA "${output_directory}/root-ca/root-ca.crt" \
    -CAkey "${output_directory}/root-ca/root-ca.key" \
    -passin "file:${passphrase_file}" \
    -CAcreateserial \
    -extfile "${server_config}" \
    -extensions server_cert_ext \
    -out "${output_directory}/server/server.crt"

rm -f \
    "${output_directory}/server/server.csr" \
    "${output_directory}/root-ca/root-ca.srl"
chmod 0600 "${output_directory}/root-ca/root-ca.key" "${output_directory}/server/server.key"
chmod 0644 "${output_directory}/root-ca/root-ca.crt" "${output_directory}/server/server.crt"

"$(dirname "$0")/verify-tls-certificates.sh" \
    "${output_directory}/root-ca/root-ca.crt" \
    "${output_directory}/server/server.crt" \
    "${output_directory}/server/server.key" \
    "${api_hostname}"

printf 'TLS material generated. Move root-ca.key to offline storage now.\n'
