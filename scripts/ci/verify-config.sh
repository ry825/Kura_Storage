#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

for required_command in rg shellcheck; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "$required_command is required for repository configuration verification." >&2
    exit 1
  fi
done

required_files=(
  .editorconfig
  .gitattributes
  .gitignore
  global.json
  server/KuraStorage.sln
  server/Directory.Packages.props
  apps/android/gradlew
  apps/android/gradle/libs.versions.toml
  apps/android/local.properties.example
  apps/android/release.properties.example
  contracts/openapi/kurastorage-api.yaml
  contracts/fixtures/system-health-response.json
  contracts/fixtures/error-response.json
  deployment/config/server/environment.example
  deployment/config/server/appsettings.Production.json.template
  deployment/config/nginx/kurastorage.conf.template
  deployment/config/systemd/kurastorage-api.service.template
  deployment/config/systemd/kurastorage-worker.service.template
  deployment/config/systemd/storage.mount.template
  deployment/config/firewall/nftables.conf.template
  deployment/raspberry-pi/install.sh
  deployment/raspberry-pi/upgrade.sh
  deployment/raspberry-pi/verify.sh
  deployment/raspberry-pi/rollback.sh
  deployment/raspberry-pi/uninstall.sh
  scripts/ci/build-release.sh
  scripts/ci/verify-deployment.sh
  scripts/maintenance/generate-tls-certificates.sh
  scripts/maintenance/verify-tls-certificates.sh
  scripts/maintenance/generate-jwt-signing-key.sh
)

for required_file in "${required_files[@]}"; do
  if [[ ! -f "$required_file" ]]; then
    echo "Missing required file: $required_file" >&2
    exit 1
  fi
done

python3 -m json.tool global.json >/dev/null
python3 -m json.tool server/src/KuraStorage.Api/appsettings.example.json >/dev/null
python3 -m json.tool contracts/fixtures/system-health-response.json >/dev/null
python3 -m json.tool contracts/fixtures/error-response.json >/dev/null

./scripts/ci/verify-deployment.sh

if ! rg -q '^openapi: 3\.[01]\.' contracts/openapi/kurastorage-api.yaml; then
  echo "The OpenAPI contract must declare OpenAPI 3.0 or 3.1." >&2
  exit 1
fi

nginx_template="deployment/config/nginx/kurastorage.conf.template"
if ! rg -q '^log_format kurastorage_safe ' "$nginx_template" ||
  [[ "$(rg -c '^    access_log .+ kurastorage_safe;$' "$nginx_template")" -ne 2 ]] ||
  rg -n '^log_format .*\$(request|request_uri|args)([^A-Za-z_]|$)' "$nginx_template"; then
  echo "LAN and ZeroTier must use the query-free kurastorage_safe access log format." >&2
  exit 1
fi

for logging_config in \
  server/src/KuraStorage.Api/appsettings.json \
  server/src/KuraStorage.Api/appsettings.example.json \
  deployment/config/server/appsettings.Production.json.template; do
  if ! rg -q '"Microsoft\.AspNetCore\.Hosting\.Diagnostics": "Warning"' "$logging_config"; then
    echo "API request-start logging must be suppressed to keep query strings out of logs: $logging_config" >&2
    exit 1
  fi
done

if rg -n --glob '!**/build/**' --glob '!**/.gradle/**' \
  '(pdfbox|barteksc)' apps/android; then
  echo "MVP-excluded Android dependency found." >&2
  exit 1
fi

if rg -n --glob '!**/build/**' --glob '!**/.gradle/**' --glob '!**/gradle.lockfile' \
  --glob '!apps/android/core-database/**' \
  --glob '!apps/android/gradle/libs.versions.toml' \
  'androidx\.room' apps/android; then
  echo "Room must remain isolated to the core-database module." >&2
  exit 1
fi

if rg -n --glob '!**/build/**' --glob '!**/.gradle/**' --glob '!**/gradle.lockfile' \
  --glob '!apps/android/feature-backup/**' \
  --glob '!apps/android/gradle/libs.versions.toml' \
  'androidx\.work' apps/android; then
  echo "WorkManager must remain isolated to the feature-backup module." >&2
  exit 1
fi

feature_dependency_pattern='project\(":feature-[^"]+"\)'
if rg -n "$feature_dependency_pattern" apps/android/feature-*/build.gradle.kts; then
  echo "A feature module directly depends on another feature module." >&2
  exit 1
fi

echo "Configuration verification passed."
