# Android UI PR7: Text editor and version history

## Scope

PR7 aligns the existing Android text editor with reference `020` and applies the KuraStorage design system to version history, preview, comparison, and restore. The existing authenticated repositories, 1 MiB UTF-8 limit, 64 KiB process-recreation draft limit, optimistic version checks, and session/file/request generation guards remain authoritative.

## Reference comparison

| Reference area | Production implementation | Verification evidence |
| --- | --- | --- |
| Header and modes | Shows Back, the production file name, History, and an adaptive View/Edit selector. Edit is unavailable for read-only access. | Compose tests verify readable content, edit permission, history action, and the absence of a force-overwrite action. |
| Status and metadata | Shows unsaved, saving, saved, read-only, conflict, and typed error states separately. Encoding, version, character count, and line count use real document state. | ViewModel tests cover dirty state, successful save, size rejection, permission loss, network failure, and same-operation retry behavior. |
| Content area | Uses a high-contrast monospaced viewer/editor inside the common card hierarchy. The screen scrolls as one reachable surface in compact, landscape, 200% text, and IME-constrained layouts. | API 33 Compose fixtures verify long content and that status and Save remain reachable at 200% font scale. |
| Unsaved exit | Back with a dirty draft offers Save and leave, Discard, and Cancel. Navigation occurs only after a successful save; failed saves retain the draft. | JVM and Compose tests verify the three actions, bounded `SavedStateHandle` persistence, and post-save navigation state. |
| Conflict | A 409 loads the latest version, renders a bounded line comparison, and offers Reload latest or Save as copy. It never offers force overwrite or automatic merge. | Tests cover conflict reload, 400-line/512-character bounds, explicit truncation guidance, latest-version retrieval failure, and copy upload routing. |
| History and restore | Loads newest-first metadata in 50-item pages, previews only the selected body, cancels preview, and confirms restore after permission/current-version revalidation. | Tests cover paging, preview generation, cancellation, read-only history, restore conflict, permission loss, and stale response rejection. |

## State and safety evidence

- `FILE_NOT_FOUND` after a save/restore permission check disables mutation controls and requires an authoritative reload; stale edit permission is not reused.
- Comparison-limit overflow is explicit. The display compares at most 400 lines and 512 characters per line, while the full draft and selected preview remain unchanged.
- Failure to retrieve the latest conflicting version keeps the local draft and explains that the user should retry when connectivity returns.
- Preview, list refresh, file/session replacement, and editor load use cancellation plus independent generation checks so late responses cannot overwrite the active state.
- Restore confirmation disables repeat submission while restoration is in progress. The server remains responsible for the final permission and expected-version decision.

## Adaptive and accessibility evidence

- Common `KuraAppScaffold`, top bar, cards, status panels/badges, confirmation dialog, and adaptive action layout provide consistent color-independent state labels and 48 dp actions.
- The editor root is vertically scrollable; the content surface uses a reduced minimum height in short or high-font-scale windows so metadata, errors, conflict actions, and Save remain reachable above or around the IME.
- Headings, content descriptions, selected mode semantics, progress semantics, visible status labels, and disabled controls are exposed without relying on color alone.
- Deterministic Compose capture fixtures cover conflict and history. No production screenshot hook, sample file body, user name, or decorative background was added.

## Intentional differences from the reference

- The Japanese sample file name, content, counts, and timestamps are never bundled. Production values come from the selected file and text/version APIs.
- The reference's generic conflict banner is expanded into typed recovery actions and a bounded comparison because the formal specification prohibits force overwrite.
- Version history and restore are separate authenticated views rather than an overlay embedded in the editor. This preserves route restoration, cancellation, and permission revalidation boundaries.
- Decorative wave patterns are omitted in accordance with the approved UI scope. Dark theme and system font scale are supported instead of reproducing one fixed screenshot size.

## Verification

| Check | Result |
| --- | --- |
| API 33 feature-text connected tests | Passed: 8 tests |
| API 33 app connected tests | Passed: 8 tests |
| Focused JVM tests | Passed: feature-text and app unit tests, including editor/history generation, conflict, permission, draft, paging, preview, and restore coverage |
| Static and full Android verification | Passed: `./scripts/ci/verify-android.sh` (1,387 tasks), including build, JVM tests, coverage, ktlint, detekt, and Android lint |
| Diff hygiene | Passed: `git diff --check` |

No Android 13 physical device was attached for this UI-only pass. The same API level was exercised on an Emulator with deterministic fixtures; the final post-alignment physical-device, TalkBack, rotation, IME, connectivity, and real-server pass remains assigned to PR10.
