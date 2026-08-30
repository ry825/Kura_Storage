#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

if [[ -z "${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}" ]]; then
  echo "ANDROID_HOME or ANDROID_SDK_ROOT must point to the Android SDK." >&2
  exit 1
fi
if [[ -z "${JAVA_HOME:-}" || ! -x "$JAVA_HOME/bin/java" ]]; then
  echo "JAVA_HOME must identify JDK 17." >&2
  exit 1
fi
java_major="$("$JAVA_HOME"/bin/java -version 2>&1 | sed -n 's/.*version "\([0-9][0-9]*\).*/\1/p' | head -1)"
if [[ "$java_major" != "17" ]]; then
  echo "JDK 17 is required; detected ${java_major:-unknown}." >&2
  exit 1
fi

./apps/android/gradlew -p apps/android :app:cyclonedxDirectBom --no-daemon --no-configuration-cache
sbom_root="apps/android/app/build/reports/cyclonedx-direct"
test -s "$sbom_root/bom.json"
test -s "$sbom_root/bom.xml"
rg -q '"bomFormat" : "CycloneDX"' "$sbom_root/bom.json"
rg -q '"group" : "androidx.media3"' "$sbom_root/bom.json"
rg -q '"version" : "1.11.0"' "$sbom_root/bom.json"
rg -q '"group" : "io.coil-kt.coil3"' "$sbom_root/bom.json"
rg -q '"version" : "3.5.0"' "$sbom_root/bom.json"

echo "Android app CycloneDX SBOM generated in $sbom_root/."
