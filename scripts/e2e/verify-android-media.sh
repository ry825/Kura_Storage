#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mode="${1:-all}"
package_name="${KURASTORAGE_E2E_PACKAGE:-com.kurastorage.app}"
adb_command="${ADB:-${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}/platform-tools/adb}"
evidence_root="${KURASTORAGE_E2E_EVIDENCE_DIR:-/tmp/kurastorage-android-media-e2e-$(date -u +%Y%m%dT%H%M%SZ)}"

if [[ ! -x "$adb_command" ]]; then
  echo "ADB or ANDROID_HOME/ANDROID_SDK_ROOT must identify an Android SDK platform-tools installation." >&2
  exit 1
fi

mapfile -t connected_serials < <("$adb_command" devices | awk 'NR > 1 && $2 == "device" { print $1 }')
device_serial="${ANDROID_SERIAL:-}"
if [[ -z "$device_serial" ]]; then
  if [[ "${#connected_serials[@]}" -ne 1 ]]; then
    echo "Set ANDROID_SERIAL when zero or multiple Android devices are connected." >&2
    exit 1
  fi
  device_serial="${connected_serials[0]}"
fi
adb=("$adb_command" -s "$device_serial")

capture_evidence() {
  mkdir -p "$evidence_root"
  local model sdk release fingerprint package_uid process_id
  model="$("${adb[@]}" shell getprop ro.product.model | tr -d '\r')"
  sdk="$("${adb[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
  release="$("${adb[@]}" shell getprop ro.build.version.release | tr -d '\r')"
  fingerprint="$("${adb[@]}" shell getprop ro.build.fingerprint | tr -d '\r')"
  package_uid="$("${adb[@]}" shell dumpsys package "$package_name" | sed -n 's/.*userId=\([0-9]*\).*/\1/p' | head -1)"
  process_id="$("${adb[@]}" shell pidof "$package_name" | tr -d '\r' || true)"

  if [[ -z "$package_uid" ]]; then
    echo "$package_name is not installed on $device_serial." >&2
    exit 1
  fi
  if [[ "$device_serial" == emulator-* || "$fingerprint" == *generic* ]]; then
    echo "Physical-device evidence is required; detected emulator-like device $device_serial." >&2
    exit 1
  fi

  {
    echo "serial=$device_serial"
    echo "model=$model"
    echo "android_release=$release"
    echo "sdk=$sdk"
    echo "package=$package_name"
    "${adb[@]}" shell dumpsys package "$package_name" |
      sed -n '/versionCode=/p;/versionName=/p;/flags=/p'
  } >"$evidence_root/environment.txt"

  "${adb[@]}" shell dumpsys meminfo "$package_name" >"$evidence_root/meminfo.txt"
  "${adb[@]}" shell dumpsys gfxinfo "$package_name" framestats >"$evidence_root/gfxinfo.txt"
  "${adb[@]}" shell dumpsys netstats detail |
    awk -v uid="$package_uid" '
      /mAppUidStatsMap:/ { in_map = 1; next }
      in_map && $1 == uid { print "uid rxBytes rxPackets txBytes txPackets"; print; exit }
    ' >"$evidence_root/network-bytes.txt"

  if [[ -n "$process_id" ]]; then
    "${adb[@]}" logcat -d --pid="$process_id" -v brief |
      awk '/FATAL EXCEPTION|ANR in |Player is closed|Too many open files/' >"$evidence_root/fatal-events.txt"
  else
    : >"$evidence_root/fatal-events.txt"
  fi
  if [[ -s "$evidence_root/fatal-events.txt" ]]; then
    echo "A fatal application event was detected; see $evidence_root/fatal-events.txt." >&2
    exit 1
  fi

  echo "Physical-device evidence captured in $evidence_root."
}

run_connected_tests() {
  if [[ -z "${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}" ]]; then
    echo "ANDROID_HOME or ANDROID_SDK_ROOT is required for connected tests." >&2
    exit 1
  fi
  if [[ -z "${JAVA_HOME:-}" || ! -x "$JAVA_HOME/bin/java" ]]; then
    echo "JAVA_HOME must identify JDK 17 for connected tests." >&2
    exit 1
  fi
  local java_major
  java_major="$("$JAVA_HOME"/bin/java -version 2>&1 | sed -n 's/.*version "\([0-9][0-9]*\).*/\1/p' | head -1)"
  if [[ "$java_major" != "17" ]]; then
    echo "JDK 17 is required; detected ${java_major:-unknown}." >&2
    exit 1
  fi

  ANDROID_SERIAL="$device_serial" "$repository_root/apps/android/gradlew" -p "$repository_root/apps/android" \
    :app:connectedDebugAndroidTest \
    :core-data:connectedDebugAndroidTest \
    :core-database:connectedDebugAndroidTest \
    :core-ui:connectedDebugAndroidTest \
    :feature-activity:connectedDebugAndroidTest \
    :feature-auth:connectedDebugAndroidTest \
    :feature-backup:connectedDebugAndroidTest \
    :feature-connection:connectedDebugAndroidTest \
    :feature-files:connectedDebugAndroidTest \
    :feature-media:connectedDebugAndroidTest \
    :feature-search:connectedDebugAndroidTest \
    :feature-settings:connectedDebugAndroidTest \
    :feature-sharing:connectedDebugAndroidTest \
    :feature-text:connectedDebugAndroidTest \
    --no-daemon \
    --no-configuration-cache \
    --max-workers=1
}

case "$mode" in
  all)
    run_connected_tests
    capture_evidence
    ;;
  connected)
    run_connected_tests
    ;;
  capture)
    capture_evidence
    ;;
  *)
    echo "Usage: $0 [all|connected|capture]" >&2
    exit 2
    ;;
esac
