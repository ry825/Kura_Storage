#!/usr/bin/env bash
set -euo pipefail

[[ "$#" -eq 4 ]] || {
    printf 'Usage: %s LAN_IP ZEROTIER_IP API_SOCKET TIMEOUT_SECONDS\n' "$0" >&2
    exit 2
}
lan_address="$1"
zerotier_address="$2"
api_socket="$3"
timeout_seconds="$4"
[[ "${api_socket}" == /* ]] || {
    printf 'API socket path must be absolute.\n' >&2
    exit 2
}
[[ "${timeout_seconds}" =~ ^[0-9]+$ && "${timeout_seconds}" -gt 0 ]] || {
    printf 'Timeout must be a positive integer.\n' >&2
    exit 2
}

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
    if ip -o address show to "${lan_address}" | grep -q . &&
        ip -o address show to "${zerotier_address}" | grep -q . &&
        [[ -S "${api_socket}" ]]; then
        printf 'KuraStorage API addresses and Unix socket are available.\n'
        exit 0
    fi
    sleep 1
done

printf 'Timed out waiting for KuraStorage API addresses.\n' >&2
exit 1
