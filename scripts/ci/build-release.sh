#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
    printf 'Usage: %s VERSION OUTPUT_DIRECTORY\n' "$0" >&2
    exit 2
fi

version="$1"
output_directory="$2"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

[[ "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]] || {
    printf 'Version must use semantic version syntax.\n' >&2
    exit 2
}
[[ "${output_directory}" == /* ]] || {
    printf 'Output directory must be absolute.\n' >&2
    exit 2
}

required_environment=(
    ANDROID_SDK_ROOT
    JAVA_HOME
    KURASTORAGE_API_HOSTNAME
    KURASTORAGE_LAN_API_IP
    KURASTORAGE_ZEROTIER_API_IP
    KURASTORAGE_ROOT_CA_CERTIFICATE
    KURASTORAGE_RELEASE_KEYSTORE
    KURASTORAGE_RELEASE_KEY_ALIAS
    KURASTORAGE_ANDROID_SIGNING_CERT_SHA256
    KURASTORAGE_RELEASE_STORE_PASSWORD_FILE
    KURASTORAGE_RELEASE_KEY_PASSWORD_FILE
)
for name in "${required_environment[@]}"; do
    [[ -n "${!name:-}" ]] || {
        printf 'Required release input is missing: %s\n' "${name}" >&2
        exit 2
    }
done
[[ -x "${JAVA_HOME}/bin/java" ]] || {
    printf 'JAVA_HOME must reference a JDK 17 installation.\n' >&2
    exit 2
}
"${JAVA_HOME}/bin/java" -version 2>&1 | head -1 | grep -qE '"17(\.|")' || {
    printf 'Android release builds require JDK 17.\n' >&2
    exit 2
}
apksigner_binary="${ANDROID_SDK_ROOT}/build-tools/35.0.0/apksigner"
apkanalyzer_binary="${ANDROID_SDK_ROOT}/cmdline-tools/latest/bin/apkanalyzer"
[[ -x "${apksigner_binary}" && -x "${apkanalyzer_binary}" ]] || {
    printf 'Android SDK build-tools 35.0.0 and cmdline-tools are required.\n' >&2
    exit 2
}
for file in \
    "${KURASTORAGE_ROOT_CA_CERTIFICATE}" \
    "${KURASTORAGE_RELEASE_KEYSTORE}" \
    "${KURASTORAGE_RELEASE_STORE_PASSWORD_FILE}" \
    "${KURASTORAGE_RELEASE_KEY_PASSWORD_FILE}"; do
    [[ -f "${file}" ]] || {
        printf 'Required release input file is missing: %s\n' "${file}" >&2
        exit 2
    }
done
command -v openssl >/dev/null 2>&1 || {
    printf 'OpenSSL is required to validate the public Root CA certificate.\n' >&2
    exit 2
}
if grep -q 'PRIVATE KEY' "${KURASTORAGE_ROOT_CA_CERTIFICATE}" ||
    ! openssl x509 -in "${KURASTORAGE_ROOT_CA_CERTIFICATE}" -noout >/dev/null 2>&1 ||
    ! openssl x509 -in "${KURASTORAGE_ROOT_CA_CERTIFICATE}" -noout -text |
        grep -q 'CA:TRUE' ||
    ! openssl x509 -in "${KURASTORAGE_ROOT_CA_CERTIFICATE}" -noout -checkend 2592000; then
    printf 'Root CA input must be a public CA certificate valid for at least 30 days.\n' >&2
    exit 2
fi

server_publish="$(mktemp -d)"
staging_directory="$(mktemp -d)"
trap 'rm -rf "${server_publish}" "${staging_directory}"' EXIT
mkdir -p "${output_directory}"

dotnet restore "${repository_root}/server/KuraStorage.sln" --locked-mode
dotnet publish \
    "${repository_root}/server/src/KuraStorage.Api/KuraStorage.Api.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    --no-restore \
    --output "${server_publish}/api"
dotnet publish \
    "${repository_root}/server/src/KuraStorage.AdminCli/KuraStorage.AdminCli.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    --no-restore \
    --output "${server_publish}/cli"
dotnet publish \
    "${repository_root}/server/src/KuraStorage.Worker/KuraStorage.Worker.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    --no-restore \
    --output "${server_publish}/worker"
cp -a "${server_publish}/api/." "${staging_directory}/"
cp -a "${server_publish}/cli/." "${staging_directory}/"
cp -a "${server_publish}/worker/." "${staging_directory}/"
rm -f \
    "${staging_directory}/appsettings.json" \
    "${staging_directory}/appsettings.example.json"
if find "${staging_directory}" -maxdepth 1 -type f -name 'appsettings*.json' | grep -q .; then
    printf 'Server artifact must not contain appsettings files.\n' >&2
    exit 1
fi
for required_server_file in \
    KuraStorage.Api \
    KuraStorage.Api.dll \
    KuraStorage.AdminCli \
    KuraStorage.AdminCli.dll \
    KuraStorage.Worker \
    KuraStorage.Worker.dll; do
    [[ -f "${staging_directory}/${required_server_file}" ]] || {
        printf 'Server artifact input is missing: %s\n' "${required_server_file}" >&2
        exit 1
    }
done
tar --create --gzip \
    --file "${output_directory}/kurastorage-server-${version}-linux-arm64.tar.gz" \
    --directory "${staging_directory}" .

version_code="${KURASTORAGE_ANDROID_VERSION_CODE:-1}"
(
    cd "${repository_root}/apps/android"
    ./gradlew --no-daemon --no-configuration-cache --stacktrace \
        -Pkurastorage.apiHostname="${KURASTORAGE_API_HOSTNAME}" \
        -Pkurastorage.lanApiAddress="${KURASTORAGE_LAN_API_IP}" \
        -Pkurastorage.zerotierApiAddress="${KURASTORAGE_ZEROTIER_API_IP}" \
        -Pkurastorage.rootCaCertificate="${KURASTORAGE_ROOT_CA_CERTIFICATE}" \
        -Pkurastorage.versionName="${version}" \
        -Pkurastorage.versionCode="${version_code}" \
        :app:assembleRelease
)
cp "${repository_root}/apps/android/app/build/outputs/apk/release/app-release.apk" \
    "${output_directory}/kurastorage-android-${version}.apk"

apk_signing_output="$("${apksigner_binary}" verify --verbose --print-certs \
    "${output_directory}/kurastorage-android-${version}.apk")"
printf '%s\n' "${apk_signing_output}"
actual_signing_fingerprint="$(sed -n \
    's/^Signer #1 certificate SHA-256 digest: //p' <<<"${apk_signing_output}" | head -1)"
expected_signing_fingerprint="$(tr '[:upper:]' '[:lower:]' \
    <<<"${KURASTORAGE_ANDROID_SIGNING_CERT_SHA256}" | tr -d ':[:space:]')"
[[ "${actual_signing_fingerprint}" == "${expected_signing_fingerprint}" ]] || {
    printf 'Android signing certificate fingerprint does not match the expected value.\n' >&2
    exit 1
}
if "${apkanalyzer_binary}" manifest debuggable \
    "${output_directory}/kurastorage-android-${version}.apk" |
    grep -q '^true$'; then
    printf 'Release APK is debuggable.\n' >&2
    exit 1
fi
[[ "$("${apkanalyzer_binary}" manifest application-id \
    "${output_directory}/kurastorage-android-${version}.apk")" == "com.kurastorage.app" ]]
[[ "$("${apkanalyzer_binary}" manifest version-name \
    "${output_directory}/kurastorage-android-${version}.apk")" == "${version}" ]]
[[ "$("${apkanalyzer_binary}" manifest version-code \
    "${output_directory}/kurastorage-android-${version}.apk")" == "${version_code}" ]]

(
    cd "${output_directory}"
    sha256sum \
        "kurastorage-server-${version}-linux-arm64.tar.gz" \
        "kurastorage-android-${version}.apk" \
        >"SHA256SUMS-${version}"
)
printf 'Release artifacts generated in %s\n' "${output_directory}"
