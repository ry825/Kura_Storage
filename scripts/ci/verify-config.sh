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

shellcheck scripts/ci/*.sh

if ! rg -q '^openapi: 3\.[01]\.' contracts/openapi/kurastorage-api.yaml; then
  echo "The OpenAPI contract must declare OpenAPI 3.0 or 3.1." >&2
  exit 1
fi

if rg -n --glob '!**/build/**' --glob '!**/.gradle/**' \
  '(androidx\.room|androidx\.work|androidx\.media3|io\.coil-kt|pdfbox|barteksc)' apps/android; then
  echo "MVP-excluded Android dependency found." >&2
  exit 1
fi

feature_dependency_pattern='project\(":feature-[^"]+"\)'
if rg -n "$feature_dependency_pattern" apps/android/feature-*/build.gradle.kts; then
  echo "A feature module directly depends on another feature module." >&2
  exit 1
fi

echo "Configuration verification passed."
