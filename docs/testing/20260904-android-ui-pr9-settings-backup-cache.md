# Android UI PR9 Settings・Backup・Cache 検証記録

## 対象と環境

- 検証日: 2026-09-04
- 対象: `015-settings.png`、`030-backup-status.png`〜`036-cache-management.png`
- 端末: Android 13 / API 33 `kura_pr9_api33` Emulator
- 表示条件: Light/Dark、標準文字、200%文字、800 x 360 dp横画面を決定的Compose fixtureで検証
- データ: 固定の非識別fixture。実SSID/BSSID、User名、Token、File名、Path、Idempotency keyはCaptureや本記録に残していない。

物理Android 13端末が未接続のため、変更後の表示・操作は同一API levelのEmulatorとContract/Repository/ViewModel/Composeの決定的fixtureで代替した。実Serverと物理端末を使う最終E2EはPR10で行う。

## 参考UIとCapture検証

| 参考 | 検証した表示・操作 | 決定的fixture / Capture |
| --- | --- | --- |
| `015` Settings | Account、Connection、Backup、Trusted Wi-Fi、Quality/Data usage、Activity、LogoutをSection化。CacheはAdminのみ表示し、Memberに管理値や操作を見せない。 | Admin/Member、Dark、200%文字、横画面を`SettingsHubScreenTest`でCapture。 |
| `030` Backup status | 最終成功、Pending/Uploading/Succeeded/Failed件数、Task progress、待機理由、今すぐ実行、一時停止/再開、失敗retryを表示。 | 件数と状態を文字/semanticsの両方で検証し、非識別fixtureをCapture。 |
| `031` Backup rules | Source、Server保存先、有効状態、Network/Battery、Permission、最終状態、Add/Edit/Toggle/Deleteを表示。 | Deleteの確認でServer File非削除を検証。fixtureのURIとFolder IDは非識別値。 |
| `032` Rule editor | MediaStore/SAF Source、Server Folder picker、二つのNetwork mode、Battery、初回充電、Enabled、一方向Backupの説明を表示。 | Editor下部までscrollし、主要Formと端末削除非反映の説明をCapture。 |
| `033` Trusted Wi-Fi | 現在接続中とPolicyの一致、SSID、任意BSSID、従量制、Enabled、権限失敗をfail-closedで表示。 | 権限必要状態と固定の`Fixture Wi-Fi`を検証。端末設定への導線も確認。 |
| `034` Wi-Fi editor | 現在Wi-Fi、表示名、SSID/BSSID制限、従量制、Enabled、別Dialogの明示確定を表示。 | 登録は確定前にcallbackされず、確定後のみ1回実行されることを検証。 |
| `035` Quality/Data usage | LOCAL_DIRECT、登録済み/未登録外部Wi-Fi + ZeroTier、Mobile + ZeroTierのLow/Medium/Original、Save、Reset、saving/errorを表示。 | 明示Save/Resetのcallback、Dark、200%文字、800 x 360 dpで主操作へscroll可能なことをCapture。 |
| `036` Cache management | READYのImage/Video Low/Medium内訳、10 GiB上限、6 GiB目標、生成中/失敗、最新Runとpending/running/completed/failedをServer応答から表示。 | Thumbnail非合算、清掃確認、個別失敗retry非表示、Member 403、通信結果不明、polling終了を検証しCapture。 |

## API・状態・安全境界

- Cache DTOは非負数、Low/High watermark順序、4区分と合計の完全一致、UUID、UTC時刻、Run件数をstrict mappingし、不整合応答を無効にした。未知のenumは成功扱いせず`UNKNOWN`とした。
- `GET /api/v1/admin/media-cache`と`POST /api/v1/admin/media-cache/cleanup-requests`のAuthorization、401 refresh、403、UUID `Idempotency-Key`をContract testで確認した。
- POSTの通信結果不明時はGETでServer状態を再取得し、retry時は同じkeyを再送する。受理後はpending/runningをpollし、completed/failed/unknownで停止する。
- Cache画面はSession serviceとRouteに所有され、Session/User切替えやRoute離脱でViewModelとpollingが破棄される。MemberがRouteを直接開いた場合もServer 403を権威とし、状態と管理操作を表示しない。
- SSID/BSSIDは端末のBackup policy選択にだけ使用し、Server identityの代替にしない。Mobile通信の自動Backup禁止は変更可能な設定にしていない。

## 意図的な差分

- 参考UIの固定User名、Server名、件数、時刻、SSID/BSSIDは取り込まず、認証済みSession、正式Repository/API、端末の現在状態を表示する。
- VPN表記は正式接続設計に合わせ`ZeroTier`とした。Backup editorとWi-Fi editorは既存のApp callbackとstate所有を保つため、各一覧内のscroll可能な明示Formとした。
- `036`のThumbnail表示は正式10 GiB対象外なので内訳に含めない。Serverが個別失敗の再実行契約を持たないため、参考UIの一括失敗retryは提供しない。
- 自動Backupの一意Work、Room transaction、一方向意味、強制停止後の再起動条件は既存の正式実装を維持し、UIの解釈で変更していない。

## 自動検証結果

次の検証が成功した。

- 関連JVM/Contract test: `:core-network:testDebugUnitTest`、`:core-data:testDebugUnitTest`、`:feature-settings:testDebugUnitTest`、`:feature-backup:testDebugUnitTest`、`:app:testDebugUnitTest`
- API 33 Compose test: `:feature-settings:connectedDebugAndroidTest` 10/10、`:feature-backup:connectedDebugAndroidTest` 11/11、`:app:connectedDebugAndroidTest` 8/8
- 総合検証: `./scripts/ci/verify-android.sh`成功（1,387 tasks）、`git diff --check`成功

CycloneDX生成時に`androidx.media3:media3-ui-compose:1.11.0`のeffective-POM warningが出るが、既存の非fatal警告であり、SBOM生成を含む総合検証は成功した。
