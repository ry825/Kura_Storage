#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

if [[ -z "${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}" ]]; then
  echo "ANDROID_HOME or ANDROID_SDK_ROOT must point to an Android SDK containing API 36." >&2
  exit 1
fi

java_major="$(java -version 2>&1 | sed -n 's/.*version "\([0-9][0-9]*\).*/\1/p' | head -n 1)"
if [[ "$java_major" != "17" ]]; then
  echo "JDK 17 is required; detected: ${java_major:-unknown}." >&2
  exit 1
fi

./apps/android/gradlew -p apps/android \
  :app:assembleDebug \
  testDebugUnitTest \
  ktlintCheck \
  detekt \
  lint \
  --no-daemon \
  --no-configuration-cache

test -f apps/android/app/build/outputs/apk/debug/app-debug.apk

echo "Android verification passed."
