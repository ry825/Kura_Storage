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
export KURASTORAGE_ANDROID_VERSION_CODE=1
```

Use real environment values locally; the documentation addresses above are
IANA examples. Generate artifacts into a Git-ignored absolute directory:

```bash
./scripts/ci/build-release.sh 0.1.0 /absolute/path/to/artifacts
```

The command produces:

- `kurastorage-server-<version>-linux-arm64.tar.gz`
- `kurastorage-android-<version>.apk`
- `SHA256SUMS-<version>`

It verifies APK signatures, application ID, non-debuggable state, and creates
SHA-256 checksums. Verify checksums again after copying artifacts:

```bash
sha256sum --check SHA256SUMS-0.1.0
```

Private keys, password files, environment files, and production
`appsettings` must not be present in release artifacts.
