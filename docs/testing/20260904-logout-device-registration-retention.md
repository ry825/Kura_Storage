# Android Logout Device Registration Retention Verification

## Scope

This verification covers the Android authentication-state change that retains
non-secret device registration metadata after a normal logout while removing
all session credentials. It also verifies that a server-side device revocation
still removes the retained registration and returns the app to device
registration.

No real username, password, token, User ID, Device ID, server address, physical
storage path, or file content is recorded here.

## Environment

- Android device: OPPO CPH2333, Android 13
- Connection: verified `LOCAL_DIRECT` route
- Server: physical Raspberry Pi KuraStorage deployment with PostgreSQL and the
  dedicated storage mount available
- Android build: versionCode 17, release-equivalent client code
- E2E isolation: a separate debug application ID and two dedicated temporary
  fixture users; the installed normal application and unrelated device data
  were not cleared

The normal application was first updated with the same-signature versionCode 17
APK by using `adb install -r`, which retained its existing application data. The
end-to-end account and device lifecycle was then exercised in the isolated
application ID so the normal application's account and settings were not used.

## Automated verification

| Gate | Result |
| --- | --- |
| `./scripts/ci/verify-android.sh` | Passed: 1,387 actionable tasks; build, JVM tests, coverage, ktlint, Detekt, Lint, APKs, and SBOM |
| `./scripts/ci/verify-server.sh` | Passed: Domain 135, Application 353, Integration 229 |
| `./scripts/ci/verify-security.sh` | Passed |
| Targeted Android JVM and contract tests | Passed |
| `:core-data:connectedDebugAndroidTest` | Passed: 14 tests on the physical device |
| `:feature-auth:connectedDebugAndroidTest` | Passed: 8 tests on the physical device |
| `:app:connectedDebugAndroidTest` | Passed: 9 tests on the physical device |
| `git diff --check` | Passed |

The connected suite was run with the repository JDK and SDK configuration:

```bash
JAVA_HOME=<jdk-17> ANDROID_HOME=<android-sdk> \
  ./apps/android/gradlew -p apps/android \
  :core-data:connectedDebugAndroidTest \
  :feature-auth:connectedDebugAndroidTest \
  :app:connectedDebugAndroidTest \
  --no-daemon --no-configuration-cache
```

## Physical Android and server result

1. A dedicated temporary member was created and one device was registered over
   `LOCAL_DIRECT`. PostgreSQL showed one device, one active device, and one open
   refresh session.
2. A normal logout changed the open refresh-session count from one to zero. The
   device count remained one, its status remained active, and an anonymized
   comparison confirmed the Device ID was unchanged.
3. Immediately after logout and again after force-stop plus cold start, the app
   showed `Sign in`, prefilled the previous username, left Password empty, and
   did not show `Register this device`.
4. Back navigation after logout exited the authentication activity and did not
   reopen Home, Files, Media, Backup, or another protected destination.
   Instrumented app-shell tests separately confirmed that the protected back
   stack, media navigation state, and backup UI state are cleared.
5. Password login succeeded with the retained Device ID. Home, Files, Shared,
   and Settings were accessible.
6. Logout, cold start, and password login were completed three times. Each
   cycle retained one active device and the same anonymized Device ID comparison;
   logout had zero open sessions and login created one current session.
7. After a final normal logout, the dedicated device was revoked on the server.
   The next login attempt returned the app to `Register this device`; `Sign in`
   and protected Home content were absent.

The API contract tests additionally confirm that login and logout send the
retained Device ID, logout sends the current refresh token for server-side
revocation, and refresh-token reuse or authentication-required errors clear only
session state. Device revocation clears registration and session state.

## Cleanup

Both temporary fixture users, the dedicated device, all refresh sessions, the
generated root file-entry row, related audit rows, and the dedicated physical
user directory were removed. PostgreSQL verification returned zero users,
devices, sessions, file entries, and related audit rows for the fixtures. Both
physical user-directory checks returned absent. The isolated debug application,
device-side UI dumps, local password file, copied source tree, and other E2E
temporary files were also removed. The normal versionCode 17 application remains
installed.

## Compatibility constraint

An older application version deleted the Device ID during logout. A device that
was already logged out by that version has no registration metadata that the new
version can recover, so it must complete device registration once after upgrade.
After that registration, normal logout uses the retained-registration flow
verified above.
