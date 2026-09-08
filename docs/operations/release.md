# Release build and validation

## Inputs

Release builds require the fixed API hostname, LAN and ZeroTier API addresses,
the public Root CA certificate, and an Android signing keystore. Keystore and
key passwords are supplied through protected files, not command-line values.

```bash
export ANDROID_SDK_ROOT=/absolute/path/to/android-sdk
export JAVA_HOME=/absolute/path/to/jdk-17
export KURASTORAGE_API_HOSTNAME=api.kurastorage.home.arpa
export KURASTORAGE_LAN_API_IP=192.0.2.10
export KURASTORAGE_ZEROTIER_API_IP=198.51.100.10
export KURASTORAGE_ROOT_CA_CERTIFICATE=/protected/root-ca.crt
export KURASTORAGE_RELEASE_KEYSTORE=/protected/kurastorage-release.jks
export KURASTORAGE_RELEASE_KEY_ALIAS=kurastorage
export KURASTORAGE_ANDROID_SIGNING_CERT_SHA256=SET_FROM_KEYTOOL_OUTPUT
export KURASTORAGE_RELEASE_STORE_PASSWORD_FILE=/protected/store-password
export KURASTORAGE_RELEASE_KEY_PASSWORD_FILE=/protected/key-password
# Use a positive integer greater than every installed production APK's versionCode.
# For example, devices with versionCode 29 require at least 30 for an upgrade install.
export KURASTORAGE_ANDROID_VERSION_CODE=30
```

Use real environment values locally; the documentation addresses above are
IANA examples. Generate artifacts into a Git-ignored absolute directory:

```bash
./scripts/ci/build-release.sh 0.1.0 /absolute/path/to/artifacts
```

`KURASTORAGE_ANDROID_VERSION_CODE` is required. Choose a value greater than the
currently distributed APK's `versionCode`; Android rejects downgrade installs.

## Physical-device debug verification

The debug variant normally embeds the repository's test endpoint and test Root
CA. To verify a current source build against a real Local direct server without
using the production signing key, explicitly supply the production **public**
Root CA and all three network values. This produces `com.kurastorage.app.debug`;
it does not replace or upgrade the production application.

```bash
./apps/android/gradlew -p apps/android \
  -Pkurastorage.apiHostname=api.kurastorage.home.arpa \
  -Pkurastorage.lanApiAddress=192.0.2.10 \
  -Pkurastorage.zerotierApiAddress=198.51.100.10 \
  -Pkurastorage.debugRootCaCertificate=/protected/root-ca.crt \
  :app:assembleDebug
```

Do not commit the certificate or any production environment values. A debug APK
is only for local verification and cannot substitute for the signed release APK.

The command produces:

- `kurastorage-server-<version>-linux-arm64.tar.gz`
- `kurastorage-android-<version>.apk`
- `SHA256SUMS-<version>`

The server archive contains the API, Admin CLI, and Worker ARM64 executables.
The build verifies all three publish outputs, APK signatures, application ID,
non-debuggable state, and creates SHA-256 checksums. Verify checksums again after copying artifacts:

```bash
sha256sum --check SHA256SUMS-0.1.0
```

Private keys, password files, environment files, and production
`appsettings` must not be present in release artifacts.

The managed .NET dependency inventory and checksums cover the built release.
Media conversion also depends on Debian packages installed on the target. Each
install or upgrade records `libvips-tools`, `libvips42`, `ffmpeg`, and
`poppler-utils` with resolved version and architecture in
`/var/lib/kurastorage/media-runtime-packages.sbom`. Copy that inventory into
the protected release evidence directory together with the checksum file and
Pi verification record; do not place target-specific paths or secrets in the
public artifact.
