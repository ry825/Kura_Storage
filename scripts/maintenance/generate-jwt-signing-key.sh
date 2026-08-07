#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
    printf 'Usage: %s OUTPUT_KEY_FILE\n' "$0" >&2
    exit 2
fi

output_file="$1"
[[ "${output_file}" == /* ]] || {
    printf 'Output path must be absolute.\n' >&2
    exit 2
}
[[ ! -e "${output_file}" ]] || {
    printf 'Refusing to overwrite existing key.\n' >&2
    exit 1
}

umask 077
mkdir -p "$(dirname "${output_file}")"
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "${output_file}"
chmod 0600 "${output_file}"
openssl pkey -in "${output_file}" -check -noout
openssl pkey -in "${output_file}" -text -noout | grep -q 'ASN1 OID: prime256v1'
printf 'ES256 signing key generated: %s\n' "${output_file}"
