# Android UI PR4: Files, detail, transfer, Trash, and missing entries

## Scope

PR4 aligns the production file-management flow with references `010`, `021` through `023`, and `026` through `029`. Server-provided entry status, ownership, permission source, retention deadline, and transfer state remain authoritative; unknown states fail closed.

## Reference comparison

| Reference | Production implementation | State and interaction evidence |
| --- | --- | --- |
| `010-my-files.png` | Adds a safe-inset header, breadcrumb, Folder/File sections, List/Grid controls, server-backed Search navigation, entry actions, New folder, and Upload FAB. | Compose tests exercise callbacks, permission-controlled actions, pagination, empty/error recovery, stable entry keys, and both layouts. |
| `021-unsupported-file.png` | Shows file type, MIME, size, unsupported reason, and only the safe Download fallback. Unknown MIME or status does not enable content operations. | The unsupported-file fixture asserts the reason and absence of Open. |
| `022-file-details.png` | Separates file identity, metadata, and available-action summaries, including owner, sharing source, permission source, storage, status, and dates. | Detail/action tests cover active, shared, missing, candidate, and unknown states. |
| `023-folder-details.png` | Uses the same detail hierarchy for folders and exposes rename, move, sharing, Trash, or purge only when current capabilities allow them. | Owner/Manager/Viewer/Contributor/Editor/Unknown regression fixtures verify fail-closed controls and confirmations. |
| `026-server-folder-selection.png` | Shows full navigation context, current destination, owner and direct/inherited permission, writable status, folder creation, loading, and errors. | Move-picker tests cover disabled targets, subtree cycles, writable navigation, confirmation, recovery, and unknown results. The backup destination route uses the same permission guard. |
| `027-transfer-status.png` | Distinguishes waiting, hashing, upload, download, paused/resumable, completed, failed, and cancelled states with accessible state descriptions. | Compose and ViewModel tests cover progress boundaries, duplicate suppression, cancellation, retry, and authoritative session/offset reuse. |
| `028-trash.png` | Displays trashed time, server-calculated retention deadline, Restore, irreversible purge confirmation, and Admin capacity guidance. | Tests cover file/folder warnings, in-flight disabling, idempotency-key reuse, unknown outcomes, refresh, and authoritative errors. |
| `029-missing-files.png` | Separates checking, confirmed missing, and unknown status; shows detection timestamps without a physical path; confirmed deletion names its index-only scope. | Tests cover accessible status, duplicate recheck prevention, fail-closed actions, and index deletion without implying HDD deletion. |

## Adaptive and large-list evidence

- API 33 connected Compose tests rendered deterministic 360 dp, 200% font-scale, Dark-theme, and landscape fixtures with `captureToImage()` while keeping primary file actions reachable.
- Long names render within bounded list and grid entries. Missing thumbnails use a stable file-type icon without fetching original content.
- A deterministic 1,000-entry fixture verifies lazy List/Grid rendering, stable keys, and thumbnail composition limited to the visible window.
- Transfer status is exposed through semantics in addition to visible text and progress.

## Intentional differences from the references

- Folder child counts are not part of the current `FileEntry` or list API contract. The UI states that the count is unavailable instead of fabricating a value or adding server scope to this UI-only PR.
- Search opens the existing server-backed Search screen. Filtering only the currently loaded browser page would hide valid results.
- Decorative sample thumbnails and data are not bundled. The production thumbnail slot is used when available; otherwise the shared type icon is shown.
- Unsupported or unknown content is not automatically handed to another app. Download is offered only when the current permission allows it.
- Physical storage paths remain hidden. Missing-entry deletion removes only the server index record and explicitly says that it does not delete an HDD file.

## Android verification

| Check | Result |
| --- | --- |
| API 33 feature connected tests | Passed: 23 File browser/detail/transfer/Trash/missing tests on Android 13 (`kura_api33`, 320 x 640) |
| API 33 app connected tests | Passed: 8 navigation/application tests |
| JVM and focused quality checks | Passed: File ViewModel unit tests, app compilation, ktlint, and detekt |
| Compact/large-text/theme/orientation fixtures | Passed at 360 dp, 200% text, Dark theme, and landscape |
| Large-list fixture | Passed with 1,000 entries in List and Grid layouts |
| Full Android verification | Passed: 1,387 Gradle tasks, including unit tests, coverage gates, ktlint, detekt, Android Lint, SBOM generation, and debug/test builds |

The Android 13 physical device was not attached during this run. The API 33 emulator provides the PR4 Android platform and deterministic visual evidence; PR10 remains responsible for the final physical-device, TalkBack, rotation, and end-to-end transfer confirmation.
