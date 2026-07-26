#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

if ! command -v rg >/dev/null 2>&1; then
  echo "rg is required for repository security verification." >&2
  exit 1
fi

public_test_ca="apps/android/app/src/debug/res/raw/kurastorage_root_ca.pem"
tracked_sensitive_files="$(
  git ls-files |
    rg '(^|/)(local\.properties|appsettings\.(Development|Production)\.json|environment-info\.md)$|\.(key|pem|p12|pfx|jks|keystore)$' |
    rg -v "^${public_test_ca}$" || true
)"
if [[ -n "$tracked_sensitive_files" ]]; then
  printf '%s\n' "$tracked_sensitive_files"
  echo "A local configuration or private key file is tracked." >&2
  exit 1
fi

if git ls-files --error-unmatch "$public_test_ca" >/dev/null 2>&1; then
  if rg -q 'BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY' "$public_test_ca" ||
    ! openssl x509 -in "$public_test_ca" -noout >/dev/null 2>&1; then
    echo "The Android debug test CA must contain only a valid public X.509 certificate." >&2
    exit 1
  fi
fi

scan_paths=(
  .github
  apps/android
  contracts
  scripts
  server
)

if rg -n --hidden --glob '!**/build/**' --glob '!**/bin/**' --glob '!**/obj/**' \
  --glob '!**/.gradle/**' --glob '!**/gradle-wrapper.jar' \
  '(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|password["'\'']?\s*[:=]\s*["'\''][^"'\'']+["'\''])' \
  "${scan_paths[@]}"; then
  echo "Potential secret material found." >&2
  exit 1
fi

if rg -n --glob '*.example.*' --glob '*.example' \
  '(^|[^0-9])(10\.|127\.|169\.254\.|172\.(1[6-9]|2[0-9]|3[01])\.|192\.168\.)[0-9.]+' \
  apps server deployment 2>/dev/null; then
  echo "A private or loopback IP address was found in an example file." >&2
  exit 1
fi

echo "Security verification passed."
