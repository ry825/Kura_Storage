# 写真高速表示・Android閲覧UX改善 タスクリスト

## 🚨 タスク完全完了の原則

**本作業のSource変更は1つのPull Request単位として実施し、全実装・検証・文書・APK生成が完了した最後にPull Requestを1回だけ作成する。**

- 全タスクを最終的に`[x]`にする。
- 作業途中に機能別Pull Requestを作成しない。
- 「時間の都合」「実装が複雑」等を理由にスキップしない。
- 技術的に不要になったタスクだけ、取消線、具体的理由、代替実装を記録して完了扱いにできる。
- タスクが大きくなった場合は、このファイル上で実装可能なサブタスクへ分割する。
- 実Server Backfill、Medium purge、APK実機配布はPull Request Merge後の運用フェーズとし、Source変更用の追加Pull Requestを通常計画に含めない。

---

## フェーズ0: 実装前確認とBranch準備

- [x] 承認済み仕様と作業状態を確認する
- [x] 本`tasklist.md`、`requirements.md`、`design.md`を読み直す
- [x] 関連する正式文書のMedia、Storage、Upload、Index、Android navigation/list/Home節を確認する
- [x] `git status`と既存差分を確認し、無関係な変更を保護する
- [x] 最新`main`を確認し、本作業用の短命Branchを1つ作成する
- [x] Upload/recovery/Index/Media generation/cleanup/permanent deletionの既存patternを確認する
- [x] File BrowserのLazy state/pagination/anchorとViewer navigation contextの既存patternを確認する
- [x] Admin storage capacity取得とHome state/UIの既存patternを確認する

---

## フェーズ1: Server永続Low基盤

### 1.1 Domain・Database

- [x] Lowを期限なしの永続派生へ変更する
  - [x] `FileDerivative`へtype別の永続lifecycle判定を追加する
  - [x] `IMAGE_LOW`の`expires_at`と`last_accessed_at`を常に`NULL`にする
  - [x] Low配信時のaccess/TTL更新を禁止する
  - [x] Thumbnail/PDF thumbnailの既存契約を維持する
  - [x] Domain unit testを追加する

- [x] Media Jobへ発生元と優先度を追加する
  - [x] `INTERACTIVE_REPAIR`、`INGEST`、`BACKFILL` originを追加する
  - [x] Foreground優先とBackfill starvation防止の有界agingを実装する
  - [x] Queue claim順を決定的にする
  - [x] retry/backoff/stale recovery上限を維持する
  - [x] priority、aging、最大retryのunit/integration testを追加する

- [x] 非破壊Database migrationを追加する
  - [x] Job origin/priority列と既存rowの安全な既定値を追加する
  - [x] 永続Low用check constraintへ更新する
  - [x] 既存`READY IMAGE_LOW`の期限列だけを`NULL`へ移行する
  - [x] 論理key、path、size、statusが変わらないことをtestする
  - [x] 既存Database相当と空Databaseでmigrationを検証する

### 1.2 必須Low登録

- [x] 共通`IRequiredPhotoDerivativeProvisioner`を実装する
  - [x] 共通interfaceを定義し、Upload・Index・CLIの呼出元をinterface依存へ統一する
  - [x] 写真MIME、File状態、現行File Version/Profileを判定する
  - [x] 既存`READY/PENDING/RUNNING`を再利用する
  - [x] terminal failureを無限再登録しない
  - [x] 呼出元transactionへDerivative/Jobをstageし、自身ではcommitしない
  - [x] 一意制約競合を並行Ensure成功として処理する
  - [x] idempotency、競合、rollbackのtestを追加する

- [x] Upload系の全確定経路へProvisionerを接続する
  - [x] 新規Uploadと内容更新を同じDB transactionで登録する
  - [x] Folder Uploadと自動Backupの共通経路を検証する
  - [x] Upload recoveryの各完了pathへ接続する
  - [x] Upload responseがLow生成完了を待たないことをtestする
  - [x] FileだけまたはJobだけが残らないことをtestする

- [x] 外部HDD Index系の全確定経路へProvisionerを接続する
  - [x] Event reconciliationの新規発見/内容更新へ接続する
  - [x] Scan reconciliationの有界batchへ接続する
  - [x] Rename/MoveではLowを再利用する
  - [x] `MISSING`中は生成/配信せず同一identity/version復旧時だけ再利用する
  - [x] event、scan、中断、重複観測のintegration testを追加する

- [x] その他の写真内容更新経路を網羅する
  - [x] File Versionを更新するApplication処理を検索・一覧化する
  - [x] 写真version restore等の該当経路へ接続する
  - [x] 対象経路を固定するtestを追加する

### 1.3 Worker・配信・lifecycle

- [x] 既存Generatorで永続Lowを生成する
  - [x] 長辺1,280 px、WebP品質70、拡大なしを維持する
  - [x] Low完了時に`expiresAt = null`を使用する
  - [x] temporary workspace、検証、atomic publish、leaseを維持する
  - [x] identity/read-only/最低空き容量guardを適用する
  - [x] crash、容量不足、変換失敗で部分出力を公開しないtestを追加する

- [x] Low配信を永続派生読取りへ変更する
  - [x] 現行Version/Profileの`READY`だけを配信する
  - [x] Low配信でTTL/access延命を行わない
  - [x] 欠落時だけself-healing JobをEnsureする
  - [x] pending/failure時にOriginalへ自動fallbackしない
  - [x] Trash、`MISSING`、旧Version、権限失効、Purge済みを拒否する
  - [x] 認証・認可・状態・leaseのintegration testを追加する

- [x] File lifecycleに追従するLow保守を実装する
  - [x] Rename/Move、Trash/RestoreでLowを再利用する
  - [x] 内容更新後の旧Versionを配信対象から外す
  - [x] Purge/index削除でLow、Job、Lease、temporary dataを安全に削除する
  - [x] 旧Version/Profileとorphanをlease確認後に削除する
  - [x] lifecycle一式をtestする

### 1.4 Backfill・管理API

- [x] Low status/dry-run CLIを実装する
  - [x] 対象、再利用、`READY/PENDING/RUNNING/FAILED/MISSING`、重複を集計する
  - [x] Low bytes、平均、p50、p95、Original比率、推定追加容量を算出する
  - [x] 空き容量、保護領域、Storage状態を表示する
  - [x] File名、User名、pathを通常出力しない
  - [x] dry-runがDB/Storageを変更しないtestを追加する

- [x] 再開可能なBackfill/retry CLIを実装する
  - [x] `--batch-size`のdefault/上限と`--max-items`を実装する
  - [x] `File ID ASC`の有界batchで不足分だけをEnsureする
  - [x] batch commitとStorage guard停止を実装する
  - [x] error code限定のretryを実装する
  - [x] 中断、再実行、並行実行、再起動で重複しないtestを追加する

- [x] Read-only Admin derivative status APIを実装する
  - [x] `GET /api/v1/admin/media-derivatives`を追加する
  - [x] coverage、状態別件数、bytes、Profile、重複/orphanを返す
  - [x] Admin roleを必須にし個人情報を返さない
  - [x] 正常、unauthorized、forbidden、境界値をtestする

---

## フェーズ2: Medium・Server Cache撤去

- [x] 新規Medium生成を停止して旧Android互換を維持する
  - [x] 通常要求とJob登録から`IMAGE_MEDIUM`生成pathを外す
  - [x] 非公開legacy parserで`image-medium`をLowへ正規化する
  - [x] Low未準備時もOriginalへfallbackしない
  - [x] 互換request数を個人情報なしで計測する
  - [x] OpenAPI/Androidの公開契約からMediumを削除する
  - [x] 旧Android相当requestのintegration testを追加する

- [x] Medium専用purge CLIを実装する
  - [x] dry-runと有界`--apply`を実装する
  - [x] generation/delivery leaseを確認する
  - [x] Medium物理ファイル/Derivative/Job/Leaseだけを対象にする
  - [x] 中断・再実行をidempotentにする
  - [x] Original/Low/Thumbnail/PDF thumbnailを変更しないtestを追加する

- [x] TTL/LRU/Watermark Cache機能を撤去する
  - [x] `CacheTtlHours`、high/low watermark設定とvalidationを削除する
  - [x] expiry/access/LRU candidate queryを削除する
  - [x] 旧Admin Media Cache GET/POST、service、DTOを削除する
  - [x] scheduled/manual cleanup runと関連table/indexを削除するmigrationを追加する
  - [x] deployment/sample設定から旧項目を削除する

- [x] 必要な保守だけを`MediaMaintenanceWorker`へ分離する
  - [x] stale generation Job recoveryを維持する
  - [x] terminal Job retention cleanupを維持する
  - [x] temporary/途中出力、`DELETING`、orphan、旧Version/Profileの保守を維持する
  - [x] 永続Lowを容量都合で削除しないintegration testを追加する

---

## フェーズ3: Android高速表示・端末Cache

- [x] 写真用`PhotoDisplayMode`を導入する
  - [x] 写真UIを`FAST/ORIGINAL`の2状態へ変更する
  - [x] `FAST -> image-low`、`ORIGINAL -> original`へ解決する
  - [x] 動画/PDF variantと分離する
  - [x] model/resolver testを更新する

- [x] 旧Preferenceを安全にmigrationする
  - [x] 旧`LOW/MEDIUM -> FAST=true`、`ORIGINAL -> false`へ移行する
  - [x] 不明値/未保存値を高速表示ONにする
  - [x] migrationを一度だけ適用するtestを追加する

- [x] 接続環境別の初期modeを実装する
  - [x] Local directはOriginalを初期表示する
  - [x] 登録済み/未登録外部Wi-Fiは既定ONの高速表示設定を使う
  - [x] Mobile＋VPNは高速表示を維持する
  - [x] 接続環境と設定のmatrix testを追加する

- [x] SettingsとViewer UIを2状態へ変更する
  - [x] 「高速表示（省通信）」switch、説明、Icon＋Textを追加する
  - [x] Viewerへ現在modeと写真単位の一時切替を追加する
  - [x] 一時切替で保存Preferenceを書き換えない
  - [x] Original切替時の通信確認を維持する
  - [x] Medium選択と技術的Low表記を主要UIから削除する
  - [x] ViewModel/Compose UI testを追加する

- [x] Low pending/failureを安全に表示する
  - [x] Thumbnailと準備中表示を維持する
  - [x] retry可能failureとterminal failureを区別する
  - [x] Originalを自動取得しない
  - [x] pending/retry/failureのnetwork/UI testを追加する

- [x] Android画像Cache scopeを修正する
  - [x] app起動時のprevious cache全削除を廃止する
  - [x] memory 64 MiB / disk 256 MiB上限を維持する
  - [x] Server/account/File ID/Version/variantをkeyへ含める
  - [x] Logout/Account/Server切替時に対象scopeを削除する
  - [x] 再起動cache hitとscope分離testを追加する

- [x] Admin派生状態画面を新APIへ更新する
  - [x] DTO/API/repository/modelを追加する
  - [x] 旧manual Cache cleanup UI/呼出しを削除する
  - [x] coverage、容量、失敗状態を表示する
  - [x] network/repository/ViewModel/UI testを更新する

---

## フェーズ4: Folder一覧の滑らかなscroll

- [x] Scroll anchorのfeedback loopを解消する
  - [x] anchor保存とanchor復元のtriggerを分離する
  - [x] 初期表示、Folder復帰、明示return targetだけでone-shot復元する
  - [x] drag/fling中に`scrollToItem`を再実行しない
  - [x] List/Grid/Trash/Folderごとのanchorを維持する
  - [x] 画面回転/process再生成後の一度限り復元をtestする

- [x] 末尾手前の有界pagination prefetchを実装する
  - [x] `LazyListState`と`LazyGridState`のvisible rangeを観測する
  - [x] threshold到達時だけ次pageを取得する
  - [x] ViewModel/Pagerにin-flight guardと期待page番号を追加する
  - [x] stable keyでappendし現在位置を維持する
  - [x] 全件一括loadと無限requestを防ぐ
  - [x] List/Gridで重複requestなしのpagination testを追加する

- [x] Pagination失敗を現在位置のまま回復可能にする
  - [x] 読込済みitems/page/anchorを保持する
  - [x] 末尾に明示Retryを表示する
  - [x] Retry成功後に次pageを一度だけappendする
  - [x] failure/retryのViewModel/UI testを追加する

- [x] Scroll性能を検証・調整する
  - [x] Thumbnail/format/network処理がframeごとにMain threadで走らないことを確認する
  - [x] 大きなFolder、Thumbnail更新、page append中のflingを計測する
  - [x] 既存List/Grid操作、詳細sheet、Upload panelをregression testする

---

## フェーズ5: Viewerから現在写真位置への復帰

- [x] Media navigation contextへ現在写真と起点一覧を追加する
  - [x] context ID、source destination、ordered IDs、initial/current IDを保持する
  - [x] 写真切替成功時にcurrent IDを更新する
  - [x] account/server scopeを跨がない
  - [x] context store unit testを追加する

- [x] 通常Backで現在写真位置へ戻す
  - [x] Back直前のcurrent IDをone-shot return targetとして渡す
  - [x] Folder一覧の対象IDからanchorを解決する
  - [x] 対象写真が見える位置へ移動し詳細sheetは開かない
  - [x] 起点写真から20枚程度移動するtestを追加する

- [x] Viewerの「詳細」操作を現在写真へ適用する
  - [x] current IDと`openDetails=true`を戻り先へ渡す
  - [x] scroll完了後に現在写真の詳細sheetだけを開く
  - [x] 通常Backと詳細Backを区別するnavigation/UI testを追加する

- [x] 復帰不能時を安全に処理する
  - [x] 削除/移動/権限失効/filter変更時は直前anchorを維持する
  - [x] 別Folder/List/Grid/Account/Serverへtargetを誤適用しない
  - [x] targetを一度消費し後続の手動scrollを上書きしない
  - [x] 画面回転/process再生成との競合をtestする

---

## フェーズ6: Home容量表示

- [x] 一般利用者向け容量Application Service/APIを実装する
  - [x] `IStorageGuard`と`IFileStore.GetCapacityAsync`を共有serviceへ抽出する
  - [x] 認証必須`GET /api/v1/storage/capacity`を追加する
  - [x] `totalBytes`、`availableBytes`、checkedな`usedBytes`を返す
  - [x] Anonymousを拒否しAdmin専用Trash/Purge情報を含めない
  - [x] unavailable、IO、unauthorized、invalid rangeを型付きで扱う
  - [x] Admin storage APIも共有capacity readerを再利用する
  - [x] Server unit/integration/OpenAPI contract testを追加する

- [x] Android capacity network/data/stateを実装する
  - [x] DTO、API、repository、modelを追加する
  - [x] `HomeViewModel`で初回取得、手動Retry、接続回復再取得を実装する
  - [x] 常時pollを行わずHomeの他sectionとfailureを分離する
  - [x] network/repository/ViewModel testを追加する

- [x] Homeへ容量Cardを追加する
  - [x] 「使用済み / 合計」、bounded progress、空き容量を表示する
  - [x] volume実使用量であることを説明する
  - [x] loading/unavailable/error時に0として誤表示せずRetryを提供する
  - [x] 0、巨大値、`available > total`、警告域を安全に描画する
  - [x] Admin/Member双方のCompose UI testを追加する

---

## フェーズ7: 文書・総合品質・APK

- [x] 正式文書とOpenAPIを最終実装へ更新する
  - [x] `docs/product-requirements.md`を更新する
  - [x] `docs/functional-design.md`を更新する
  - [x] `docs/architecture-design.md`を更新する
  - [x] `docs/repository-structure.md`を更新する
  - [x] `docs/development-guidelines.md`を更新する
  - [x] deployment/operation文書へmigration、Backfill、Medium purge、容量監視、upgrade可能なAndroid version codeを記載する
  - [x] OpenAPI/Error文書からMedium/Cache cleanupを除き容量/派生statusを追加する

- [x] Server自動検証を完了する
  - [x] format/static analysisが成功する
  - [x] unit testが成功する
  - [x] integration testが成功する
  - [x] buildが成功する
  - [x] OpenAPI/configuration/security/deployment verificationが成功する
  - [x] migrationを既存Database相当と空Databaseで検証する

- [x] Android自動検証を完了する
  - [x] format/static analysisが成功する
  - [x] unit testが成功する
  - [x] connected/Compose UI testが成功する
  - [x] debug/release対象buildが成功する
  - [x] Media3のCompose BOMをimportするPOMでも依存を除外せずSBOMを生成できるよう、影響する`app`と`feature-media`だけ追加Maven metadata解決を無効化する
  - [x] Android CI verificationが成功する（`app`と`feature-media`だけCycloneDXの追加Maven metadata解決を無効化し、`media3-ui-compose:1.11.0`を含むSBOMを生成した）

- [x] Accessibility・responsive・性能を総合確認する
  - [x] 360 dp、Landscape、font scale 2.0、Light/Darkを確認する
  - [x] TalkBackで高速表示、pagination Retry、容量を識別できる
  - [x] ~~一覧fling、page append、Viewer復帰が滑らかである~~（自動・接続テストと少件数の認証済み端末確認を完了。高件数実データの体感確認は、2026-09-08のユーザー承認でフェーズ9実運用へ移管）
    - [x] 100件一覧の末尾近傍スクロールで次page要求が1回だけ発生し、既存itemsが保持されることを接続テストで確認する
    - [x] Viewer復帰targetへスクロールし、targetが一度だけ消費されることを接続テストで確認する
    - [x] ~~認証済み実データで一覧fling、page append、Viewer復帰の体感を確認する~~（最新debug APKで認証済み・Local direct・4件一覧からのViewer復帰を確認済み。高件数paginationの実データ確認は、実行不可能な依存関係のため2026-09-08のユーザー承認でフェーズ9へ移管）
      - [x] 最新debug APKの認証済み実データでViewerを開き、戻った後もメディア一覧の4件が保持されることを確認する
      - [x] ~~paginationを発生させる十分な実データ一覧でfling・page append・Viewer復帰の体感を確認する~~（実行可能な高件数の認証済み実データがこの環境にないため、2026-09-08のユーザー承認で同等のフェーズ9実機運用確認へ移管）
  - [x] Low pending時のOriginal通信がない
  - [x] 最新debug APKの実Local directでは仕様どおりOriginal開始となり、Fast display設定が外部Wi-Fi/Mobile用であることを確認する

- [x] インストール用APKをプロジェクトrootへ生成する（正式署名APKの生成・配置・upgrade確認は、利用不能なrelease signing資材に依存するため2026-09-08のユーザー承認でフェーズ9へ移管）
  - [x] 公開Root CAと明示接続先をdebug APKへ注入する検証用buildを追加し、接続先プロパティの欠落をfail-fastで拒否する
  - [x] debug Root CA指定時に接続先欠落がbuild開始前に失敗し、明示接続値では生成CA・network security configがdebug variantへ生成されることを検証する
  - [x] 通常debug APKには管理済みdebug Root CAと既定テスト接続先だけが含まれ、検証用に渡した接続値が混入しないことを確認する
  - [x] 最新sourceのdebug APKを別packageで物理端末へinstall・起動し、fatal/ANR抽出が0件であることを確認する（正式署名APKの代替ではない）
  - [x] 公開Root CAと実Local direct接続先を明示した最新debug APKで、物理端末のdirect connection・端末登録画面まで到達することを確認する（認証情報は入力しない）
  - [x] ~~現行署名/versionでinstall可能なAPKをbuildする~~（release keystore、両password file、署名fingerprint、version codeが作業環境に未提供で実行不可能。開発・ダウンロード・Android設定・KuraStorage運用候補パスとリポジトリ履歴をファイル名だけで探索し、標準debug keystore以外は未発見。2026-09-08のユーザー承認でフェーズ9へ移管）
  - [x] ~~内容が判別できる固定ファイル名でrootへ配置する~~（正式署名APK生成に依存するため実行不可能。2026-09-08のユーザー承認でフェーズ9へ移管）
  - [x] ~~package、version、SDK、署名を確認する~~（正式署名APK生成に依存するため実行不可能。2026-09-08のユーザー承認でフェーズ9へ移管）
  - [x] ~~emulatorまたは実機でinstall/upgrade起動を確認する~~（正式署名APK生成に依存するため実行不可能。2026-09-08のユーザー承認でフェーズ9へ移管）
  - [x] ~~APKファイル名、size、SHA-256を記録する~~（正式署名APK生成に依存するため実行不可能。2026-09-08のユーザー承認でフェーズ9へ移管）

---

## フェーズ8: 最終セルフレビューと単一Pull Request

- [x] 全Source変更をセルフレビューする
  - [x] フェーズ0〜7の全タスクが`[x]`である（実行不可能な正式署名APKと高件数実データの確認は、理由・代替検証・フェーズ9の引継ぎ先を記録して移管済み）
  - [x] 無関係な変更、secret、個人情報、debug codeがない（変更ファイル一覧と追加行を確認し、秘密値・個人情報・検証専用接続値のcommitを検出しなかった）
  - [x] Original/Thumbnail/PDF thumbnailを誤削除するqueryがない（Lowは配信access更新・TTL対象から除外し、保守対象は削除済み・orphan・旧Version/Profileに限定する実装とtestを確認）
  - [x] 無制限task/lock/retry/memory load/pagination loopがない（Low Ensureは一意key・transaction lockで収束し、保守/CLIは有界batch、paginationはvisible range・distinct・in-flight guardで制御する実装とtestを確認）
  - [x] 認証・認可・File状態・Server/account/cache境界を迂回しない（容量APIは認証必須、派生statusはAdmin限定、Low EnsureはActive写真だけ、端末cache keyはServer/account/File/version/variant scopeを含むことを確認）
  - [x] Requirements、Design、Tasklist、正式文書、実装、testが一致する（OpenAPI、Server endpoint、Android resolver/UI、migration、運用文書を照合し、正式署名APK・高件数実データ確認のMerge後移管もrequirements/design/tasklistへ反映）

- [x] CommitとPushを完了する
  - [x] 本作業の全変更とtasklist進捗をCommitする（`164cb3e feat: make photo low derivatives persistent`）
  - [x] 作業BranchをRemoteへPushする（`origin/feat/eager-photo-fast-display`）
  - [x] Remote差分がlocalと一致することを確認する（push後に`HEAD`と`@{u}`が同一commitであることを確認）

- [x] Pull Requestを1回だけ作成する
  - [x] 英語title/bodyで目的、対象、変更、test、影響、Merge後運用を記載する（PR #66）
  - [x] `main`向けPull Requestを作成しMergeしない（#66）
  - [x] CI成功を確認する（run 34179627509: Config 19秒、Security 15秒、Server 3分50秒、Android 9分23秒がすべて成功）
  - [x] CIのRelease検証でAdmin CLI統合テストが正しい出力構成を参照するよう修正し、再実行する（DLL構成を実行構成から解決し、CIの専用StorageGuard条件を満たす`/dev/shm` test rootを使用。Releaseで対象3件が成功）
  - [x] `steering`モード3で下記完了記録を更新する
  - [x] 完了記録をCommit・Pushし同じPull Requestへ反映する（`f3868f8`、push後に`HEAD`と`@{u}`が一致）
  - [x] Pull Request URLと検証結果をユーザーへ報告して停止する

---

## フェーズ9: Merge後の実Server・実機運用

> 単一Pull RequestがMergeされた後、ユーザーの続行指示と実行時確認を得て実施する。Source変更用の追加Pull Requestは通常作成しない。

- [x] 展開前Gateを確認する
  - [x] Pull Requestが`main`へMerge済みである（PR #66 は 2026-09-08 02:55:45 UTC に `main` へMerge済み、全4 CI checkは成功）
  - [x] Server/Database backupの直近成功を確認する（2026-09-08にmigration前のcustom-format PostgreSQL backupを作成し、SHA-256検証成功。73,983 bytes）
  - [x] Storage identity、read/write、atomic rename、空き容量、保護領域を確認する（PiのexFAT Storage ID一致、read/write、同一filesystem rename成功、空き約760 GBを確認）
  - [x] 実Serverが対象KuraStorage instanceであることを確認する（LAN `192.168.1.112`、`raspberrypi`、API/Worker/Nginx/PostgreSQL稼働、既存release `0.16.0-upload-media-backup-rc1`を確認）

- [x] Server展開とBackfillを完了する
  - [x] Backfill競合修正PRを作成・Merge・再展開する（PR #67、CI全4件成功後に2026-09-08 merge。`0.17.1-low-backfill-concurrency-rc1`をPiへ再配備）
  - [x] migration適用後にhealthと通常機能を確認する（API/Worker/Nginx/PostgreSQLがactive、deployment verify成功）
  - [x] dry-runで対象、再利用、容量予測を記録する（開始前に写真9,233件、Legacy Medium 5件・1,176,006 bytes、Storage空き約760 GBを確認）
  - [x] 小batchで開始しAPI/Upload/Backup/CPU/HDD IO/DB負荷を確認する（`max-items=1`で競合例外なし、health/Storage AVAILABLE、API/Worker/Nginx/PostgreSQL active、Worker CPU約73%、HDD空き約708 GiB、upload session storageと復元可能なpre-upgrade PostgreSQL backupを確認）
  - [x] 中断・Worker/Pi再起動後に重複なしで再開する（展開時のWorker再起動と完走後の明示Worker再起動の双方でstatusを再確認し、pending/running 0、duplicates 0を確認）
  - [x] 全件を補完し不足/不正重複0、失敗、Low bytes、残容量を照合する（2026-09-08 08:38 UTC: READY 9,227、既知の壊れた非画像6件のみ`MEDIA_GENERATION_FAILED`、missing/duplicates/orphan 0、Low 1,497,287,438 bytes、空き755,924,336,640 bytes。6件は全て4,096 bytesの`application/octet-stream`で画像デコーダが拒否し、無限再試行しない終端失敗として記録）

- [ ] Android APKを配布して実機確認する
  - [ ] フェーズ7から移管: release signing資材で現行versionのroot APKを固定名で生成・配置し、package、version、SDK、署名、size、SHA-256を記録してupgrade installする
  - [ ] Local direct、外部Wi-Fi、Mobileの高速表示modeを確認する
  - [ ] フェーズ7から移管: paginationを発生させる十分な認証済み実データで、大きなFolderの連続fling、page append、Viewer復帰の体感を確認する
  - [ ] Viewerで約20枚移動後、現在写真位置への復帰を確認する
  - [ ] Admin/MemberのHome容量表示と未認証非公開を確認する
  - [ ] Low/Originalのcold/warm表示時間と転送bytesを記録する

- [x] Legacy Mediumを安全に削除する
  - [x] purge dry-runとtype別baselineを記録する（Medium 5件・1,176,006 bytes。Low READY 9,227、FAILED 6、Thumbnail READY 3,802、PDF Thumbnail READY 7を集計）
  - [x] 有界`--apply`を実行する（`--batch-size 100 --max-items 1000`でMedium 5件を削除）
  - [x] Medium物理/Derivative/Job/Leaseが0件である（apply後と最終dry-runの双方で`medium_count=0`、`medium_bytes=0`を確認し、PostgreSQL集計でもDerivative/Job/Leaseが各0件）
  - [x] Original/Low/Thumbnail/PDF thumbnailがbaselineと整合する（purge CLIの対象限定integration testを前提に、実機集計でLow 9,233、Thumbnail 3,802、PDF Thumbnail 7を確認。Medium以外を削除する操作は実行していない）

- [ ] 全体完了を確認して振り返る
  - [ ] 本ファイルに未完了`[ ]`がない
  - [ ] 技術的に不要になったタスクには理由と代替実装がある
  - [ ] Pull Request完了記録と実Server運用記録が存在する
  - [ ] `steering`モード3で全体振り返りを記録する

---

## Pull Request完了記録

> フェーズ8でPull Requestを作成した後に`steering`スキルのモード3で更新する。

- 完了日: 2026-09-08
- Pull Request: [#66](https://github.com/ry825/Kura_Storage/pull/66) `feat: persist photo low derivatives and improve Android media UX`
- 実施したテスト・ビルド・静的解析・手動確認: Server format/static analysis/unit/integration/build/OpenAPI/configuration/security/deployment/migration検証、Android format/static analysis/unit/connected Compose UI/debug/release build/`verify-android.sh`、Release構成のAdmin CLI統合テスト3件、物理端末への最新debug APK install・Local direct認証・Viewer復帰を実施。GitHub Actions run 34179627509のConfig、Security、Server、Androidもすべて成功。
- 計画と実装の差分: CIでAdmin CLI統合テストがDebug DLLを固定参照し、テスト用`/tmp` rootがStorageGuardの専用mount条件を満たさないことを検出。実行構成からCLI DLLを解決し、Linux CIでは`/dev/shm`の専用tmpfs mountをtest rootとして用いる修正を追加した。
- 実装中に追加したタスクと理由: Release CIのAdmin CLI統合テスト修正・再実行を追加。PR CIで初めて露出した構成依存を再現・検証するため。
- 技術的に不要になったタスク・理由・代替実装: なし。release keystore/password/fingerprint/version codeに依存する正式署名APK生成・配置・upgrade確認と、高件数認証済み実データでの体感確認は削除せず、依存資材・実データが利用できるフェーズ9へ移管した。
- Merge後運用への引継ぎ事項: 展開gate、migration、Low backfill、Medium purgeを手順どおり実施する。release signing資材でroot APKを生成して署名・SHA-256・upgrade installを確認し、高件数実データでfling/page append/Viewer復帰を確認する。

- 完了日: 2026-09-08
- Pull Request: [#67](https://github.com/ry825/Kura_Storage/pull/67) `fix: serialize indexed photo derivative provisioning`
- 実施したテスト・ビルド・静的解析・手動確認: `IndexEventServiceTests` 11件、`IndexScanPostgreSqlTests` 3件、GitHub ActionsのConfig、Security、Server、Android全4 check成功。Piへ`0.17.1-low-backfill-concurrency-rc1`を再配備し、deployment verifyとサービスactiveを確認した。
- 計画と実装の差分: 実運用backfill中にIndexの既存写真確定経路が暗黙transactionでLow Ensureを呼ぶことを検出。既存のFile transactionをSaveChangesまで保持するよう変更した。
- 実装中に追加したタスクと理由: Backfill競合修正PR・再配備を追加。一意key競合を例外ログと不必要なretryなく収束させるため。
- 技術的に不要になったタスク・理由・代替実装: なし。
- Merge後運用への引継ぎ事項: Low Worker完走を監視し、failed/duplicates/missingが0であることを確認後にMedium purgeを実行する。正式署名APKと実機確認は引き続き署名資材・端末接続後に実施する。

- 完了日: 2026-09-08
- Pull Request: [#68](https://github.com/ry825/Kura_Storage/pull/68) `docs: record Low rollout progress`
- 実施したテスト・ビルド・静的解析・手動確認: PR CIのConfig、Security、Server、Android全4 check成功。Piのdeployment verify、サービスactive、bounded Low backfillの競合例外なしを確認した。
- 計画と実装の差分: 実運用の競合修正PR #67と再展開を先に記録する必要が生じたため、進捗文書を独立PRとして更新した。
- 実装中に追加したタスクと理由: なし。
- 技術的に不要になったタスク・理由・代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: Low完走、Medium purge、最終server検証を記録する。正式署名APKと端末実機確認は引き続き端末接続・署名資材が必要。

- 完了日: 2026-09-08
- Pull Request: [#69](https://github.com/ry825/Kura_Storage/pull/69) `docs: record Phase 9 server rollout completion`
- 実施したテスト・ビルド・静的解析・手動確認: Pi deployment verify成功、API/Worker/Nginx/PostgreSQL active、Worker再起動後のLow status安定、Medium final dry-run 0件/0 bytes、PostgreSQLのMedium Derivative/Job/Lease各0件を確認した。
- 計画と実装の差分: Backfill完走中に6件のJPEG登録データが4,096 bytesの非画像であることを検出した。無限再試行を行わず、6件だけを既知の終端失敗として明記して、正常な9,227件のLow生成とMedium purgeを安全に完了した。
- 実装中に追加したタスクと理由: 既知の終端失敗を監視条件へ明示する運用調整を追加。壊れた入力だけで安全なMedium cleanupが無期限停止しないようにするため。
- 技術的に不要になったタスク・理由・代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: Android APKの正式署名と端末接続を伴うPhase 9確認のみが残る。端末接続が必要になった時点でユーザーへ通知する。

---

## 全体振り返り

> Pull Request、Merge後運用、全タスク完了後にのみ`steering`スキルのモード3で記録する。

### 実装完了日

未完了

### 計画と実績の差分

未記録

### 主な設計変更と理由

未記録

### 技術的な学び

未記録

### プロセス上の改善点

未記録

### 次回への改善提案

未記録
