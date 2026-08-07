#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 4 ]]; then
    printf 'Usage: %s ROOT_CA_CERT SERVER_CERT SERVER_KEY API_HOSTNAME\n' "$0" >&2
    exit 2
fi

root_ca_cert="$1"
server_cert="$2"
server_key="$3"
api_hostname="$4"

openssl verify -CAfile "${root_ca_cert}" "${server_cert}"
openssl x509 -in "${root_ca_cert}" -noout -checkend 2592000
openssl x509 -in "${server_cert}" -noout -checkend 2592000

root_text="$(openssl x509 -in "${root_ca_cert}" -noout -text)"
server_text="$(openssl x509 -in "${server_cert}" -noout -text)"
grep -q 'CA:TRUE, pathlen:0' <<<"${root_text}"
grep -q 'Certificate Sign' <<<"${root_text}"
grep -q 'CRL Sign' <<<"${root_text}"
grep -q 'CA:FALSE' <<<"${server_text}"
grep -q 'TLS Web Server Authentication' <<<"${server_text}"
grep -q "DNS:${api_hostname}" <<<"${server_text}"
grep -q 'sha256' <<<"$(openssl x509 -in "${server_cert}" -noout -text |
    grep 'Signature Algorithm' | head -1)"

root_bits="$(openssl x509 -in "${root_ca_cert}" -noout -text |
    sed -n 's/.*Public-Key: (\([0-9][0-9]*\) bit).*/\1/p' | head -1)"
server_bits="$(openssl x509 -in "${server_cert}" -noout -text |
    sed -n 's/.*Public-Key: (\([0-9][0-9]*\) bit).*/\1/p' | head -1)"
[[ "${root_bits}" -ge 4096 ]]
[[ "${server_bits}" -ge 3072 ]]

certificate_public_key="$(mktemp)"
private_public_key="$(mktemp)"
trap 'rm -f "${certificate_public_key}" "${private_public_key}"' EXIT
openssl x509 -in "${server_cert}" -pubkey -noout |
    openssl pkey -pubin -outform DER >"${certificate_public_key}"
openssl pkey -in "${server_key}" -pubout -outform DER >"${private_public_key}"
cmp --silent "${certificate_public_key}" "${private_public_key}"

printf 'TLS certificate chain and key properties verified.\n'
