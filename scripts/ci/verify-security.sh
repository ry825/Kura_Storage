#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

if git ls-files | rg -n '(^|/)(local\.properties|appsettings\.(Development|Production)\.json|environment-info\.md)$|\.(key|pem|p12|pfx|jks|keystore)$'; then
  echo "A local configuration or private key file is tracked." >&2
  exit 1
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
  '(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|password\s*[:=]\s*[^S][^E][^T])' \
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
