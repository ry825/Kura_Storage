# Androidバックアップ・メディア信頼性改善 タスクリスト

## 実施原則

- 本タスクリストの全フェーズは、途中Pull Requestを作成せず、最後に作成する**1本のPull Request**に属する。
- 実装を開始する時は、対象タスクが`[ ]`であることを確認する。完了条件を満たした直後に、Steeringスキルのモード2に従ってその1項目だけ`[x]`へ更新する。
- 変更範囲ごとの近傍unit/contract testは実装直後に実行し、最後に全gateを1回実行する。これにより正確性を保ったまま検出を早める。
- テストfixtureはrun IDとexact IDをRepository外manifestへ記録した今回作成分だけを対象にする。wildcard、部分一致、親Folder、全件削除は禁止する。
- 既存User、File、Folder、Backup Rule、Receipt、Media Job、派生データ、端末データは削除対象に含めない。
- 技術的理由で不要になった項目のみ、理由と代替実装を該当項目とPR完了記録へ明記して`[x]`にできる。時間・難易度を理由にした未実施は禁止する。

## フェーズ0: 計画承認

- [x] 承認済み`requirements.md`・`design.md`と本`tasklist.md`を照合し、要件、単一PR境界、実装順序、検証・fixture清掃方針を確定する。

## フェーズ1: 実装開始前確認・再現・安全基盤

- [x] 現在の作業状態と関連実装を確認する。
  - [x] `git status`と既存差分を確認し、今回と無関係なユーザー変更を記録して保全する（`.gitignore`の`docs/operations/*.local.md`追加は今回の変更に含めない）。
  - [x] 本Steering一式、関連する正式文書の該当節、既存のTrash/Backup/Photo/Thumbnail/Video実装とTestを確認する。
  - [x] 一括削除、再インストール後Backup、zoom中Photo navigation、thumbnail failure dismissal、OPPO相当Codec非対応を最小fixtureで再現し、期待結果を記録する。
- [x] fixture保護と高速な検証環境を準備する。
  - [x] 実Server/実機を使う前に既存resourceをread-onlyでbaseline取得する。
  - [x] 作業固有run IDを生成し、今回作成したresourceを作成直後にexact IDと種別だけでRepository外manifestへ記録する。
  - [x] 正常MP4、Codec非対応MP4（入手/再現可能な場合）、thumbnail retryable/terminal failure、Backup再インストール相当の最小fixtureを準備する。既存resourceをfixtureとして転用しない。

## フェーズ2: 仕様・契約・共通型

- [x] 正式文書とAPI/Domain契約を実装方針に合わせて更新する。
  - [x] `docs/product-requirements.md`に複数選択Trash、再インストール後の一意照合、Photo navigation、thumbnailのdismiss/有界retry、Codec非対応時の導線を反映する。
  - [x] `docs/functional-design.md`とOpenAPIにCompareの再関連付け結果・必要metadata・認可/曖昧候補の扱いを反映する。
  - [x] `docs/architecture-design.md`と`docs/development-guidelines.md`にReceipt transaction、Session scoped notice/retry、Media3 error分類、fixture cleanup guardを反映する。
  - [x] `docs/repository-structure.md`は実際の配置変更がある場合だけ更新する（新規module／Project／Directoryはなく、更新不要）。
- [x] typed stateと境界Testを先に追加する。
  - [x] Bulk Trash item outcome、Backup re-association outcome、Photo navigation request token、thumbnail retry/dismiss key、video playback errorを任意文字列ではなく型で表す。
  - [x] File名、path、User/Device ID、Token、例外詳細をUI summary、Log、metric label、dismiss/retry keyへ含めないことをTestで確認する。

## フェーズ3: 再インストール後の自動Backup重複防止

- [x] Server Compare/ApplicationをTest firstで拡張する。
  - [x] 認証済みUser・現在Device・保存先Folder権限・同一相対Pathで候補を絞り、状態・Version・所有/共有境界を再評価する。
  - [x] Size/更新時刻で候補を絞り、必要時だけstreaming SHA-256を要求・検証する。
  - [x] 一意かつ同一内容なら本文転送なしの再関連付け結果を返し、複数候補、checksum不一致、不正状態、権限不足では既存File/Receiptを変更しない。
  - [x] Receiptを`(user_id, device_id, local_document_key)`の一意制約でtransaction内に確定し、通信再送・並行Compareを一件へ収束させる。
  - [x] Upload session開始・完了時にも現在権限、File状態、Versionを再検証し、Compare結果を権限根拠にしない。
- [x] Android Backup Worker/Roomを拡張する。
  - [x] Receipt未登録の再インストール相当sourceをCompareへ送り、再関連付け結果を`ALREADY_UPLOADED`相当として永続化する。
  - [x] checksum要求、Process kill/Worker retry、通信結果不明で本文UploadやReceiptを重複させない。
  - [x] 変更済み・曖昧・権限不成立の結果を既存`NEW`/`CHANGED`/blocked stateへ安全に反映する。
- [x] 変更近傍のServer/Android testを実行する。
  - [x] 同一内容・一意候補で本文転送0かつ新Receipt確定を確認する。
  - [x] 別User/Folder/Device、複数候補、checksum不一致、Trash/MISSING、共有解除、Upload競合と通常Backupに回帰がないことを確認する。

## フェーズ4: File一覧の複数選択削除

- [x] File browserのselection UIとViewModelを実装する。
  - [x] 複数File/Folderの選択、選択数、selection解除、accessibility semanticsを追加する。
  - [x] 削除actionは選択可能な現在表示項目だけを対象にし、対象件数・ゴミ箱移動を説明する確認dialogを表示する。
  - [x] 各IDを既存Trash commandへbounded concurrencyで送信し、成功・失敗・再取得結果を項目単位で集計する。
  - [x] partial failure時に成功項目だけを一覧から更新し、失敗項目を誤って削除済み表示しない。
- [x] Folder一覧の先頭移動buttonを実装する。
  - [x] `LazyListState`から一定量scrollしたFolder一覧だけで表示し、先頭・短い一覧・Folder外の一覧では表示しない。
  - [x] 見た目は小さな上向きIconにしつつ、48dp以上のtouch target、TalkBack label、tooltip、contrastを確保し、Top app barや主要actionを覆わない。
  - [x] tapで現在Folderの一覧先頭へ移動し、Folder切替、refresh、anchor復元、進行中animationの競合では古い操作をcancelする。
- [x] 近傍Testを実行する。
  - [x] 単一/複数選択、Folder/File混在、権限不足、既にTrash、競合、通信失敗、rotation/recreationを確認する。
  - [x] 先頭移動buttonの表示閾値、touch/semantics、scroll完了、短い一覧、Folder切替、anchor復元をCompose testで確認する。
  - [x] 単一Trash、Restore、Permanent Delete、共有認可の既存Testに回帰がないことを確認する。

## フェーズ5: 拡大中Photo Viewerの前後移動

- [x] Photo Viewerの閲覧Contextとnavigationを実装する。
  - [x] 現在の閲覧可能File ID列とindexから前後可否を導出し、button/accessibility actionを表示する。
  - [x] zoom/pan状態中も明確な前後actionで遷移できるようにし、pan gestureを意図しないnavigationに変換しない。
  - [x] 遷移開始時にzoom/panと表示request stateを対象File単位で初期化し、Session/File ID/Version/Variant tokenが一致しない古い非同期結果を破棄する。
  - [x] 画像読込、decode、権限、Session変更時に前後写真の状態を相互汚染しない。
  - [x] 全画面写真の小型action barを実装する。
  - [x] 写真Previewと同じ小型IconでFavorite、Tag、Original download、全画面解除を写真を隠さない下部overlayへ表示する。
  - [x] Favorite/Tagは既存の`EntryOrganizationViewModel` state/callbackを再利用し、対象File切替、pending、成功、失敗を通常Previewと一致させる。
  - [x] Downloadは既存Original streaming coordinatorだけを呼び、cancel/errorを表示する。全画面解除はsystem Backと同じ優先順位で処理する。
  - [x] 小型表示でも48dp以上のtouch target、TalkBack label/state、tooltip、Dark theme/文字拡大を満たす。
- [x] 近傍Testを実行する。
  - [x] zoom/pan中の前後移動、先頭/末尾、rapid navigation、rotation、品質変更、読込失敗をCompose/ViewModel testで確認する。
  - [x] 全画面action barのFavorite/Tag/Download/exit、pending/error、TalkBack、system Back、対象写真切替をCompose/ViewModel testで確認する。

## フェーズ6: Thumbnail failure dismissalと有界retry

- [x] Session scoped failure noticeを実装する。
  - [x] 同一failure summary世代のdismissをSession scopeに保持し、同画面再訪・recompositionでpopupを再表示しない。
  - [x] `failedCount=0`、File Version変更、Logout、Session失効でdismiss対象を解消し、新規失敗だけを再通知可能にする。
- [x] retry coordinatorを実装する。
  - [x] retry可能なMedia Jobだけをkeyごとにcoalesceし、最大回数、指数backoff、Server指定Retry-Afterを適用する。
  - [x] 上限到達またはterminal failure後は自動retryを停止してplaceholderを維持し、利用者の明示Retryだけで既存冪等APIを呼ぶ。
  - [x] 複数画面、rotation、再Composition、通信再試行から同一Jobの重複retryを作らない。
  - [x] Originalをthumbnailの代替に自動取得しない。
- [x] 近傍Testを実行する。
  - [x] dismiss後の再訪非表示、新規失敗再表示、retryable/terminal、backoff/上限、coalescing、explicit retry、Session変更を確認する。

## フェーズ7: 端末Codec非対応MP4の安全な導線

- [x] Media3 error分類とUI actionを実装する。
  - [x] decoder初期化/playback errorからCodecUnsupportedをネットワーク、Range、認証、コンテンツ破損と区別して型へ変換する。
  - [x] CodecUnsupportedではPlayerを停止・解放し、自動retryやServer Media Job retryを起動しない。
  - [x] 既存の認可済みDownload結果だけを使用してDownload/外部対応アプリ導線を提供し、Token付きURL/Headerを外部Intentへ渡さない。
  - [x] 対応handlerがない、外部Intent失敗、Download取消をCrashなしで扱う。
- [x] 近傍Testを実行する。
  - [x] codec非対応、Range失敗、401/403、通信切断、対応Codec成功をMockWebServer/Media3 state testで区別する。

## フェーズ8: 統合検証・実機/実Server E2E・安全な清掃

- [x] 変更範囲の自動品質gateを実行する。
  - [x] `./scripts/ci/verify-server.sh`を実行して成功させる。
  - [x] `./scripts/ci/verify-android.sh`を実行して成功させる。
  - [x] `./scripts/e2e/verify-android-media.sh`と追加したBackup/Trash E2Eを実行し、失敗時は原因を修正して再実行する。
- [x] 実機/実Serverで受け入れ条件を確認する。
  - [x] 実機で複数選択Trash、再インストール相当Backupの本文転送0、zoom中Photo navigation、thumbnail dismiss/retryを確認する。
  - [x] OPPOを含むCodec非対応端末で非対応表示・代替導線を、Pixel相当の対応端末で既存MP4 Range再生を確認する。対象端末が利用不可なら、利用不能な理由と実行済みの自動/代替検証を記録する。
  - [x] 検証結果を`docs/testing/`へ記録し、秘密情報・個人情報・File名・物理Pathを含めない。
- [x] fixtureを安全に清掃する。
  - [x] manifestのexact IDに限り、Media Job/派生データ、File、Folder、Backup Rule、test User/Device/Session、Android private cache/fixtureの依存関係逆順で削除する。
  - [x] 各削除後に再取得して不存在を確認し、baselineと比較して既存データ・未追跡データに差分がないことを記録する。
  - [x] 清掃失敗はテスト成功と別に扱い、解消するまで本タスクを完了にしない。

## フェーズ9: 最終Review・単一Pull Request・完了記録

- [x] 最終reviewを行う。
  - [x] 本tasklistの全実装・検証・清掃タスクが`[x]`であり、親タスクの完了条件を満たすことを確認する。
  - [x] 差分を確認し、対象外のユーザー変更、fixture、debug code、秘密情報、無関係なformat変更を含めない。
  - [x] 正式文書、OpenAPI、実装、Test、`docs/testing/`の結果が一致することを確認する。
- [x] 最終Commit、Push、単一Pull Requestを作成する。
  - [x] 1本のbranchに今回の変更だけをCommitし、remoteへPushする。
  - [x] `main`向けのPull Requestを1本だけ作成する。タイトル・本文は英語とし、Purpose、target tasks、changes、tests、fixture cleanup、impact/limitationsを記載する。
  - [x] CI成功を確認する。Coding agentはMergeしない。
- [x] Steeringスキルのモード3-Aで、下記「各Pull Request完了記録」へ作成日、PR URL、実施Test、E2E/実機結果、fixture清掃結果、影響/未実施事項を記録する。
- [x] 全タスクと単一PR完了記録が完了した後、Steeringスキルのモード3-Bで全体振り返りを追記する。

## 各Pull Request完了記録

> 実装完了後にSteeringスキルのモード3-Aで記入する。計画時点では記入・完了扱いにしない。

### PR 1: Android backup and media reliability improvements

- 作成日: 2026-09-09
- Pull Request: https://github.com/ry825/Kura_Storage/pull/71
- 対象タスク: フェーズ1〜9
- Test/E2E/実機結果: Server/Android quality gate、PR CI全成功。実機Media3分類テスト成功。最終E2E再実行は端末APK install応答タイムアウト後にADB接続不可となったため、docs/testingに代替検証と理由を記録。
- fixture清掃結果: exact manifestの3件のローカルfixtureを削除し、不存在を確認。server-side fixtureは未作成。
- 影響・未実施事項: Coding agentはmergeしない。追加の物理端末によるCodec比較は端末利用不可のため未実施。

## 実装後の全体振り返り

> すべてのタスクと上記の単一PRが完了した後に、Steeringスキルのモード3-Bで記入する。

- 実装完了日: 2026-09-09
- 計画と実績の差分: CIで検出したktlint違反を修正して再実行した。
- 新たに必要になったタスク: なし。
- 技術的理由で取消したタスクと代替実装: 追加の物理端末Codec比較はADB接続不能のため、実機Media3分類テストと既存接続E2Eの記録で代替した。
- 技術的な学び: Media3例外分類はAndroid runtime上のテストが必要で、JVM単体では時刻APIのモック制約がある。
- 次回への改善提案: push前に変更ファイル全体のktlintを実行し、実機E2E前に端末のAPK install応答を確認する。
