# User activity foundation PR1 verification

## Scope

This record covers the PR1 `UserActivity` domain model, PostgreSQL persistence,
and recording integration for successful Upload, Move, Text Edit / Version
Restore, Share, Trash, and Purge state changes. The user-facing query API,
administrator search CLI, and Android UI remain outside PR1.

No production user, file, device, path, credential, token, request identifier,
or file content was used or recorded. Migration and capacity tests used only
generated identifiers and synthetic snapshots in disposable PostgreSQL 17
containers.

## Automated verification

The final Release verification completed with zero build warnings and the
following test totals:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 119 | 0 | 0 |
| Application | 318 | 0 | 0 |
| Integration | 208 | 0 | 0 |

The tests cover typed detail invariants, NFC/control/length snapshot rejection,
server-derived actor/device/owner/recipient snapshots, matching and conflicting
operation IDs, no-op suppression, rollback behavior, journal recovery,
concurrent Move/Trash serialization, manual and retention Purge, User/File
deletion with retained snapshots, and separation from Security Audit events.

Merged Coverlet line coverage for Domain and Application is 6,806/7,620
(89.32%). The new critical activity model and recording factory are 293/296
(98.99%) combined: `UserActivity.cs` 125/128 (97.66%),
`UserActivityDetails.cs` 19/19 (100%), and `UserActivityFactory.cs` 149/149
(100%). Transaction and recovery behavior is additionally exercised through the
PostgreSQL/API integration suite.

## Migration and capacity

`AddUserActivities` passed Up, Down, and re-Up against PostgreSQL 17. The test
verifies the operation ID uniqueness constraint, typed detail shape constraint,
keyset/admin indexes, nullable `SET NULL` references, retained display
snapshots, existing Audit/File/Share rows, and the new FileOperation actor
reference. `dotnet ef migrations has-pending-model-changes` reported no pending
model changes.

The opt-in one-million-row capacity test produced this redacted local result:

| Measurement | Result |
| --- | ---: |
| Seed duration | 128,398 ms |
| Sample insert overhead | 75.6 microseconds/row |
| Table bytes | 221,495,296 |
| Index bytes | 507,682,816 |
| Total relation bytes | 729,178,112 |
| Logical backup estimate | 210,750,000 bytes |

The test is gated by `KURASTORAGE_RUN_USER_ACTIVITY_PERF=1` so the million-row
seed is explicit rather than part of every normal test run.

## Repository verification

- `./scripts/ci/verify-server.sh` passed formatting, Release build, all 645
  tests, and required Server artifact checks.
- `./scripts/ci/verify-config.sh`, `./scripts/ci/verify-security.sh`, and
  `./scripts/ci/verify-deployment.sh` passed.
- Configuration/deployment checks used temporary extracted ShellCheck and Nginx
  binaries under `/tmp`; no package was installed into the development host.
- In the restricted environment, systemd user lookup, netlink/nftables kernel
  access, and Nginx listen socket access were unavailable after their grammar
  and syntax checks had passed; the verification scripts accepted their
  documented restricted-environment paths.
- `git diff --check` passed. Review found no activity HTTP endpoint,
  administrator activity CLI, Android activity feature, secret, or production
  environment value in the PR1 diff.
