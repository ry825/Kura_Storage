# Android UI PR5: Discovery, sharing, organization, and activity

## Scope

PR5 aligns Recent, Shared, Category, Search, sharing settings, Favorites, Tags, entry organization, and Activity with references `011` through `014` and `024` through `025`. The screens reuse a core UI entry row while navigation continues to pass IDs through application callbacks, so feature modules do not depend on each other. Server-provided metadata, permissions, and entry status remain authoritative; unknown states fail closed.

## Reference comparison

| Reference | Production implementation | State and interaction evidence |
| --- | --- | --- |
| `011-recent-files.png` | Groups entries by opened time and displays type, owner, permission source, update time, status, pagination, refresh, empty, and error states. | ViewModel generation guards preserve the current request; Compose fixtures cover grouping, paging controls, and metadata. |
| `012-shared-files.png` | Adds owned/received and file/folder filters with the shared entry row, ownership, permission, source, update time, paging, and management navigation. | Tests cover filters, entry callbacks, unavailable states, and current permission-controlled management. |
| `013-category-browser.png` | Reuses the Search route and repository with Image, Video, Audio, and Document MIME categories selected from Home. | Navigation and Search fixtures verify category input without a new endpoint or a feature-to-feature dependency. |
| `014-search.png` | Organizes query, entry type, category, status, owner, shared source, tags, date, and size ranges with Search, Clear, Refresh, paging, and field-local validation. | Compose and ViewModel tests cover callbacks, invalid ranges, stale-result suppression, empty/error states, and compact large-text reachability. |
| `024-sharing-settings.png` | Shows the target, owner, file/folder scope, current members, inheritance context, permission changes, removal, and explicit impact confirmations. | Tests cover authoritative refresh, duplicate-submit prevention, folder-descendant/file-only wording, and global member removal. |
| `025-share-permissions.png` | Adds candidate search, selected-member state, permission descriptions, current value, progress, and errors. File shares omit Contributor and unknown permissions cannot be saved. | ViewModel and Compose tests cover permission constraints, candidate selection, validation, and pending-state control disabling. |

Favorites uses the same entry row and paging states. Tags follows the form/settings pattern with create, rename, delete, validation, and documented limits. Entry organization exposes favorite/tag state and pending reconciliation. Activity shows typed user-facing details and only provides navigation when a current target ID exists; raw audit values are not rendered.

## Adaptive and accessibility evidence

- API 33 connected Compose tests rendered deterministic 320 x 640, 200% font-scale, and Dark-theme fixtures with `captureToImage()` while retaining Search, sharing, retry, and navigation actions.
- Entry status is conveyed by text and semantics; inactive, missing, and unknown entries do not expose an enabled open action.
- Lazy lists use stable server IDs, and obsolete refresh/filter responses are ignored by request generation guards.

## Intentional differences from the references

- Category browsing uses the existing server-backed Search contract and common list row. It does not add a thumbnail-grid endpoint or load category results locally.
- Favorite is not a field in the formal Search API contract. Search therefore provides a server-backed Favorites destination instead of incorrectly filtering only the loaded result page.
- Decorative mock member photos and sample thumbnails are omitted. Real entry type icons and server metadata are shown.
- A share that has never been created has no authoritative owner response yet. The screen says ownership will be confirmed after sharing is loaded instead of inventing an owner.
- Shared-list source labels are derived from its owned/received server scopes; latest entry details and capabilities are fetched at the application navigation boundary before file/folder actions.

## Android verification

| Check | Result |
| --- | --- |
| API 33 Search connected tests | Passed: 11 tests |
| API 33 Sharing connected tests | Passed: 6 tests |
| API 33 Activity connected tests | Passed: 2 tests |
| API 33 app connected tests | Passed: 8 navigation/application tests |
| JVM and focused quality checks | Passed: Search, Sharing, and Activity unit tests; ktlint; detekt; app compilation |
| Compact/large-text/theme fixtures | Passed at 320 x 640, 200% text, and Dark theme |
| Full Android verification | Passed: 1,387 Gradle tasks, including unit tests, coverage gates, ktlint, detekt, Android Lint, SBOM generation, and debug/test builds |

The Android 13 physical device was not attached during this run. The API 33 emulator provides Android-platform and deterministic visual evidence for PR5; PR10 remains responsible for final physical-device, TalkBack, rotation, and end-to-end confirmation.
