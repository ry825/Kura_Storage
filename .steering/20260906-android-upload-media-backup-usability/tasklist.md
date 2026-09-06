# Androidアップロード・メディア・バックアップ操作性改善 タスクリスト

## 対象

- `requirements.md`で定義したTransfer status、複数File/Folder Upload、パンくず、一覧位置復元、写真、Search Navigation、Thumbnail状況/並列生成、動画、Trusted Wi-Fi、Settings、Backup、PDF、File browser Headerの改善を実装する。
- `design.md`のSAF Upload計画、ID基準Navigation、Original動画、通信確認、Job集計、制御付き並列処理、段階的Test、manifest限定清掃に従う。
- 本タスクリストの実装、検証、正式文書、清掃、記録は中間Pull Requestを作らず、最後に1本のPull Requestへまとめる。

## 🚨 タスク完全完了の原則

### 必須ルール

- 本タスクリストの全項目は同じPull Request単位に属する。
- 実装開始後は全フェーズを完了し、Pull Requestを作成して完了記録を反映するまで作業を継続する。
- 実装、Test、実機検証、清掃、文書更新が完了した項目だけを`[x]`へ変更する。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ変更する。
- 「時間の都合」「難しい」「Testが重い」を理由に未完了タスクを残さない。
- タスクが大きい場合は同じPull Request内の小さいサブタスクへ分割し、本ファイルへ追加して完了させる。
- 技術的に不要になった項目だけ、不要理由と代替実装を該当項目とPull Request完了記録へ明記して取消可能とする。
- 実装中に仕様または設計を変更した場合は、対象`docs/`、`requirements.md`、`design.md`、本ファイルを同じ変更で更新する。

### 進捗更新

- 各タスク開始前に本ファイルを読み、対象項目が`[ ]`であることを確認する。
- 対象タスクの完了条件を満たした直後に、その項目だけを`[x]`へ更新する。
- 5タスクごとに本ファイルを再読込し、実装と進捗が一致していることを確認する。
- 自動Testに成功しても必要な実機確認が未完了なら、実機確認項目を完了扱いにしない。

## Pull Request構成

### PR 1: Improve Android uploads, media playback, backup throughput, and navigation

- 本タスクリストのフェーズ0〜13をすべて含む。
- 変更目的は「Androidアプリで報告されたUpload、File browser、Media/PDF、Settings、Trusted Wi-Fi、Backupの不具合と操作性を修正し、進行状況と通信負荷を利用者が把握できるようにする」とする。
- 実装、Test、Build/Lint、API契約、正式文書、実機・実Server検証、性能測定、今回作成したテストデータの清掃を完了してからPull Requestを作成する。
- Pull Requestは英語のTitle/Bodyで作成し、Mergeせず停止する。

---

## フェーズ0: 計画承認

- [x] `requirements.md`を作成し、User承認を得る。
- [x] `design.md`を作成し、User承認を得る。
- [x] 本`tasklist.md`について、1本のPull Request境界、実装順、検証、清掃方針のUser承認を得る。

---

## フェーズ1: 実装開始前確認・再現・安全基盤

### 1.1 Branchと既存差分の確定

- [x] PR 1の開始状態を確定する。
  - Rootおよび対象Directoryの`AGENTS.md`、本Steering 3文書、`steering`スキルのモード2を再確認する。
  - 関連する`docs/product-requirements.md`、`docs/functional-design.md`、`docs/architecture-design.md`、`docs/repository-structure.md`、`docs/development-guidelines.md`の節を確認する。
  - 文書間の矛盾を確認し、矛盾があれば実装前に影響と修正対象を明記する。
  - `git status`と既存差分を確認し、Userの変更を識別して保持する。
  - 先行Pull Requestと現在BranchのMerge/CI状態を確認し、最新`main`を基点とするPR 1専用Branchを作成する。
  - Upload、File browser、Navigation、Media/PDF、Settings/Wi-Fi、Backup、Media Job集計の既存実装とTest patternを検索する。

### 1.2 変更前baselineと原因の記録

- [x] 報告された全18項目の変更前状態を再現または既存証跡で確認する。
  - Upload成功後も`Transfer status`が残るstate遷移を特定する。
  - File pickerが単一選択だけであることと、Folder pickerがないことを確認する。
  - パンくずが非Linkであることと、現在のFolder遷移競合対策を確認する。
  - 写真の全画面導線、intrinsic size、aspect ratio、zoom基準値を確認する。
  - SearchでBottom navigationのHomeが機能しないback stackを再現する。
  - Thumbnail Jobの現行状態・集計Query・Android表示有無を確認する。
  - Thumbnail Workerの現在の同時実行数、claim/Lease、Job別処理時間、CPU/memory/I/Oを確認する。
  - 動画品質選択によるLow/Medium Job生成とServer負荷、現在の通信確認を確認する。
  - Settings下位画面の背景色と文字色をLight/Darkで記録する。
  - Trusted Wi-Fiで現在接続情報を取得できないAndroid version・権限・OS状態を切り分ける。
  - 通常表示の動画操作位置、表示切替、Player再作成、startup/rebuffer/frame dropを記録する。
  - Backup Queueの直列処理箇所、claim、retry、server limiterを確認する。
  - PDFを開けないFile・応答・一時File・Renderer段階を特定する。
  - File browser Headerの構成要素、高さ、inset、一覧の利用可能高さを測定する。
  - scrollした一覧から詳細/Viewerを開いてBackした際に先頭へ戻る状態所有とNavigation lifecycleを特定する。
  - 秘密情報・個人情報を含めないbaseline証跡を作業用evidenceへ記録する。

### 1.3 テストデータmanifestと既存データ保護

- [x] 実機・実Server E2E用の安全な追跡と清掃手順を用意する。
  - 作業prefixを`ks-20260906-ux-`とし、実行ごとのrun IDを生成する。
  - User、File、Folder、Upload session、Backup run/item、Media job/derivative、Tag、Favorite、Share、Recent、Activity、端末一時Fileをexact IDで追跡するmanifestをRepository外の作業用領域へ作る。
  - Token、Password、SSID/BSSID、個人的なFile名、物理Pathをmanifestと証跡へ保存しない。
  - Fixture作成前に既存データの保護対象ID、件数、必要なchecksumをread-onlyで記録する。
  - 作成成功応答の実IDだけを直後にmanifestへ追記し、推測IDや名前一致を使用しない。
  - 削除前にmanifest membershipと作業prefix/作成metadataの両方を要求するguardを用意する。
  - wildcard、部分一致、親Folder再帰削除、Database全件削除、Storage directory一括削除をguardで拒否する。

---

## フェーズ2: Upload QueueとTransfer status

### 2.1 複数Transfer state

- [x] 複数Transferを独立追跡できるstateをTest firstで実装する。
  - Queue itemに一意なID、対象名、状態、進捗、retry情報、operation IDを持たせる。
  - `ACTIVE`、`NEEDS_ATTENTION`、`COMPLETED_NOTICE`、`IDLE`の表示状態を実装する。
  - 複数項目の進捗更新を項目ID単位で反映し、別項目のstateを上書きしない。
  - 一部失敗時は成功済み項目を維持し、失敗項目だけを再試行できるようにする。
  - 全項目成功時は完了eventを1回だけ発行し、永続的な完了stateを残さない。
  - 項目数、同時進捗、成功/失敗混在、retry、取消のUnit Testを追加する。

### 2.2 Transfer statusの表示終了

- [x] Upload完了後に`Transfer status`が自動的に消えるよう修正する。
  - activeまたは要対応failureがある場合だけstatus領域をCompositionする。
  - 最後のTransfer成功時に短時間のSnackbarを表示する。
  - Snackbar消費後または画面復帰時に成功済みTransferを再表示しない。
  - failureは利用者がretryまたはdismissするまで識別可能にする。
  - 単一成功、複数成功、一部失敗、retry成功、rotation/recreationのViewModel/UI Testを追加する。

### 2.3 複数File選択

- [x] 複数File pickerとQueue投入を実装する。
  - 単一File用launcherを`OpenMultipleDocuments`へ移行し、取消と空結果を扱う。
  - URIを安定順で重複除去し、各Fileのdisplay name、MIME、Size、read permissionを検証する。
  - 選択した各Fileへ独立したoperation IDを割り当て、既存resumable uploadへ投入する。
  - 同名FileのServer競合、permission喪失、metadata不正を項目別errorへ変換する。
  - 1件、複数件、重複URI、取消、一部読取不能、一部Upload失敗をTestする。

---

## フェーズ3: Folder Upload

### 3.1 SAF Folder tree取得

- [x] Folder pickerと安全なtree walkerをTest firstで実装する。
  - `OpenDocumentTree`を追加し、選択取消とURI permission取得失敗を扱う。
  - Root配下のFolder/Fileを走査し、Document IDと親子関係を保持する。
  - 空FolderをUpload計画に保持する。
  - 空名、`.`、`..`、区切り文字、不正Document ID、Root外参照、同一Document再訪を拒否する。
  - 深い階層と大量項目でMain threadを塞がず、cancel可能な走査にする。
  - 入れ子、空Folder、重複、読取不能、循環相当、取消、上限境界のUnit/Instrumented Testを追加する。

### 3.2 Server Folder構造の作成

- [x] Folder treeを親優先でServerへ反映する。
  - 相対Pathをsegment列として扱い、文字列連結によるServer Path生成を行わない。
  - 親Folder作成結果のIDを子Folder/Fileへ引き渡す。
  - 既存同名Folder/Fileとの競合を既存API契約に従って処理する。
  - 作成済みFolder、空Folder、部分失敗、retryで重複Folderを作らない。
  - Path traversalと選択Root外取込みがServer側validationでも拒否されることを確認する。

### 3.3 Folder内FileのQueue Upload

- [x] Folder配下のFileを既存Transfer Queueへ投入する。
  - 親Folder IDの確定後にFile Uploadを開始する。
  - 個々のFileの進捗、成功、失敗、retryをTransfer statusへ反映する。
  - 1件のFile失敗で独立した残りFileをcancelしない。
  - Folder traversalまたはRoot permissionの全体errorと、子File固有errorを分ける。
  - 入れ子Folderの構造・内容・Size/checksumをUpload後に検証するIntegration Testを追加する。

---

## フェーズ4: File browserとTop-level Navigation

### 4.1 Link化したパンくず

- [x] 確定済みFolder IDに基づくパンくずNavigationを実装する。
  - 各`BrowserBreadcrumb`をLinkまたはButtonとして表示し、対象IDをViewModelへ渡す。
  - 現在地は非活性表示とし、祖先要素だけを操作可能にする。
  - 対象IDが現在の確定chainにない場合は遷移を拒否する。
  - 祖先押下時に後続chainを切り、対象Folderを1回だけ読込む。
  - 連打、別祖先の連続tap、読込中Back、stale response、権限失効、削除済みFolderをTestする。
  - 横幅不足時のscroll/省略とTalkBack label、48dp touch targetを確認する。

#### 4.1.1 パンくず全階層の表示保証

- [x] 深いFolderでパンくずが2件だけ見える状態を修正し、すべての階層名とLinkへ到達できるようにする。
  - 現在地表示後も祖先階層が意図せず切り捨てられない表示方法に修正する。
  - 360dp幅、長いFolder名、深い階層、fontScale 2.0で全項目を確認・操作できることをTestと実機で確認する。

### 4.2 SearchからHomeへの復帰

- [x] Bottom navigationのHome選択をTop-level Navigationとして修正する。
  - Search routeとnested stateを含む現行back stackの原因を回帰Testへ固定する。
  - Home選択へ`popUpTo`、`launchSingleTop`、state restorationの正しい組合せを適用する。
  - Search→Home、Search結果→Viewer→戻る→Home、Home再選択をNavigation Testする。
  - Files、Favorites、Tags等の既存Bottom navigation挙動を回帰確認する。

### 4.3 File browser Header省スペース化

- [x] Headerの縦領域を縮め、一覧の表示領域を広げる。
  - Top app bar、画面Title、Path、説明、Actionの重複と余白を整理する。
  - Upload、検索、並べ替え等の主要Actionを維持する。
  - status bar insetと48dp touch targetを維持する。
  - 修正前後のHeader高さと一覧利用可能高さを同一viewportで測定・記録する。
  - 360dp幅、Landscape、fontScale 2.0、Light/Dark、TalkBackで重なり・切れを確認する。

### 4.4 File一覧のscroll位置保存・復元

- [x] 詳細/Viewerから戻ったときに同じ一覧位置を復元する。
  - Folder ID、Sort、Filterを含むcontext keyごとに先頭可視File ID、index、pixel offsetを保存する。
  - File/Folderを開く直前と一覧離脱時にanchorを更新する。
  - 同じcontextへのBackでは安定File IDを優先して最新一覧上の位置を解決し、保存offsetへ復元する。
  - anchor Fileが削除・非表示なら保存indexを現在範囲へclampし、近傍へ安全に戻す。
  - 別Folder、Sort、Filterの位置を混同せず、Refreshや再Compositionだけで先頭へ移動しない。
  - rotation/process recreationでもSavedStateHandle等から小さいanchor stateを復元する。
  - 詳細/各Viewerとの往復、Refresh、項目挿入/削除、Sort/Filter変更、大量一覧をUnit/Compose/Navigation Testする。

---

## フェーズ5: Thumbnail生成状況

### 5.1 Server集計Query

- [x] Thumbnail Job状態の権限制御付き集計をTest firstで実装する。
  - `THUMBNAIL`と`PDF_THUMBNAIL`だけを対象にする。
  - `QUEUED`、`RUNNING`、`FAILED`を別々に集計する。
  - 現在Userがread可能なFileのJobだけを含める。
  - 非`ACTIVE`、権限失効、削除済みFile、他User専用Fileを除外する。
  - Jobの同時更新中にも負数・重複・不整合countを返さない。
  - Query planと既存indexを確認し、必要な場合だけMigration/indexを追加する。

### 5.2 APIとOpenAPI契約

- [x] `GET /api/v1/media/thumbnail-jobs/summary`を実装する。
  - `queuedCount`、`runningCount`、`failedCount`、`observedAt`を返すContractを追加する。
  - 認証、Session失効、Server errorを既存Error envelopeへ合わせる。
  - File ID、File名、他UserのJob情報をResponseへ含めない。
  - OpenAPI、API互換性/Error文書、Server Contract/Integration Testを更新する。

### 5.3 Android表示とPolling

- [x] Thumbnail生成状況をAndroidから確認できるようにする。
  - DTO validation、Repository、UiStateを追加する。
  - 待機中数と生成中数を常に区別し、失敗数は0より大きい場合に案内する。
  - 対象画面表示中だけ下限付きPollingを行い、全件0では頻度を下げるか停止する。
  - request generationと`observedAt`で古い応答を破棄する。
  - 取得失敗時もFile browserと既存Thumbnail表示を利用可能にする。
  - 0件、待機/実行混在、完了、retry、stale response、API失敗をUnit/UI Testする。

#### 5.3.1 Thumbnail generation failedバナーの残留防止

- [x] File画面の`Thumbnail generation failed`バナーが消えずに残る原因を、Server集計とAndroidのUI state/Pollingの両方で特定し修正する。
  - 実際に再試行対象の失敗が残る場合と、再取得・画面遷移後のstale表示を切り分ける。
  - 失敗解消後にバナーが消え、未解消失敗では必要な案内を維持するUnit/UI/API/E2E Testを追加する。

### 5.4 Thumbnail生成の制御付き並列化

- [x] Thumbnail Workerを安全な上限内で並列実行できるようにする。
  - `THUMBNAIL`と`PDF_THUMBNAIL`専用の型付き同時実行設定を追加し、最小1・安全な最大値を起動時検証する。
  - 設定された実行枠数まで安定順と`FOR UPDATE SKIP LOCKED`でclaimし、Jobごとにworker tokenとLeaseを分離する。
  - 同一File/version/typeの重複claim・重複生成・重複READY公開を防ぐ。
  - Jobごとのtemporary outputへ生成し、元versionとLeaseを再確認した後だけatomic publishする。
  - 1件の失敗で別Jobをcancelせず、失敗Jobだけを既存retry/stale recovery契約へ渡す。
  - Host停止、Lease失効、元File変更/削除、partial outputをTestする。
  - 動画Low/Medium派生Jobを並列枠へ含めず、新規作成しない。

### 5.5 Thumbnail並列性能Test

- [x] Thumbnail並列数の正確性と性能を測定して正式値を確定する。
  - 写真・動画・PDFを並列数1・2・4で実行し、4でも改善が続いて資源に余裕があれば6・8も実行する。
  - 最大同時実行数、queued/running count、成功/失敗、生成物Size/形式を検証する。
  - 直列baselineと並列結果の最初のThumbnail待ち時間と全件所要時間を比較する。
  - Raspberry PiのCPU、memory、I/OとWorker heartbeat/Lease更新を測定する。
  - 手動/自動Upload、一覧API、動画Range再生を同時実行し、Foreground応答と動画rebufferへの影響を測定する。
  - 継続CPU余力25%以上、swap増加/OOM/thermal throttlingなし、I/O waitの継続20%未満、一覧API p95悪化20%未満を採用目安として確認する。
  - 安全域を超えた場合は同時数または処理資源を調整して再測定する。
  - 確定した既定値、測定条件、結果を`docs/testing/`と正式設計へ記録する。

---

## フェーズ6: 動画Original再生・通信確認・操作・性能

### 6.1 正式仕様とServer variant policyの先行更新

- [x] 動画をOriginalのみとする契約を正式文書とServerへ反映する。
  - `docs/product-requirements.md`から動画Low/Medium選択要求を変更し、写真品質選択との違いを明記する。
  - `docs/functional-design.md`へ動画Original、Cellular確認、動画派生Job停止を反映する。
  - `docs/architecture-design.md`へvariant policy、Range再生、Buffer、既存派生データの扱いを反映する。
  - `docs/development-guidelines.md`へ動画Content取得前確認とPlayer lifecycle規約を反映する。
  - 動画Low/Mediumの新規Job要求を既存Error envelopeの4xxで拒否する。
  - 写真Low/Mediumと全Thumbnail生成が維持されるServer Testを追加する。
  - 既存動画派生データを今回の変更で一括削除しないことを確認する。

### 6.2 Android動画品質選択の無効化

- [x] 動画ViewModelとUIをOriginal固定へ変更する。
  - 動画画面から品質選択UIと品質変更actionを除く。
  - 動画に対する`MediaVariantResolver`をOriginal固定とし、写真の品質解決から分離する。
  - 動画Low/Medium派生Jobのcreate/poll/retry経路を呼ばない。
  - Original metadataとRange sourceだけをPlayerへ渡す。
  - 写真のLow/Medium/Original選択が回帰していないことをTestする。

### 6.3 Network transportと1 MiB警告

- [x] Cellularでの大容量動画確認をTest firstで実装する。
  - active networkをWi-Fi、Ethernet、Cellular、Other/Unknownへ型付き変換するobserverを追加する。
  - OriginalのSizeをContent GET前にHEAD/metadataで取得する。
  - CellularかつSizeが1 MiB以上の場合にSize付き確認Dialogを表示する。
  - CellularかつSizeが1 MiB未満の場合はDialogなしで再生準備する。
  - Size不明かつCellularの場合はSize不明の通信警告を表示する。
  - CancelではSourceをPlayerへ渡さず、GET/Range requestを開始しない。
  - `元動画を再生`選択時だけOriginalをprepareする。
  - Wi-Fi/Ethernetではモバイル通信Dialogを表示せずOriginalをprepareする。
  - 1 MiBの前後、network切替、file/version切替、rotation、stale metadataをUnit/UI/MockWebServer Testする。

### 6.4 通常/全画面共通Player Overlay

- [x] 動画上へ半透明の共通操作Overlayを実装する。
  - 通常表示と全画面表示の両方で動画metadataの元の縦横比を固定し、余白を許容する`Fit`表示にする。
  - Crop、`FillBounds`、縦横別倍率を使用せず、画面回転やsystem bar変更後も引き伸ばさない。
  - 再生・一時停止、巻き戻し、早送り、seek、現在/総時間、速度、全画面切替を配置する。
  - 通常表示と全画面表示で同じComposableとstateを使用する。
  - surface tapで表示、再tapで非表示、一定時間の無操作で自動非表示にする。
  - seek/速度変更/Accessibility focus中は意図せず自動非表示にしない。
  - system Backは全画面中なら全画面解除を優先する。
  - 縦長・横長・正方形に近い動画について、通常/全画面、Portrait/Landscape、rotation時の元縦横比と余白をScreenshot/Instrumented Testする。
  - background/foregroundとTalkBackをCompose/Instrumented Testする。

### 6.5 動画カクつきの原因修正

- [x] Playerと配信経路の計測結果に基づいてカクつきを改善する。
  - Player instance、MediaItem、authenticated route-bound DataSourceの生成回数を計測する。
  - 初回Range、seek Range、`Content-Range`、Network切断・再開のServer/Android挙動を確認する。
  - Compose再Compositionで同じSourceを再prepareしないようidentityを安定化する。
  - Main thread上のmetadata、File、Bitmap、Network処理を除去する。
  - Wi-Fi/Cellular別のBufferとCacheを既存memory上限内で調整する。
  - codec非対応または端末decode限界をNetworkカクつきと区別して表示・記録する。
  - 基準動画でstartup time、rebuffer count/time、dropped frames、PSS、Server CPU/networkを修正前後比較する。
  - 改善値と残る端末依存制約を個人情報なしで`docs/testing/`へ記録する。

---

## フェーズ7: 写真Viewer

### 7.1 写真の比率と既定Scale

- [x] 写真を歪ませず、不必要に拡大しない表示をTest firstで実装する。
  - intrinsic width/heightとContainer constraintから縦横同一の基準Scaleを計算する。
  - 通常表示と全画面表示を`ContentScale.Fit`へ固定し、余白を許容してCropと`FillBounds`を禁止する。
  - 小さい画像を元pixel相当以上へ自動拡大しない。
  - 明示的なpinch zoomだけ基準Scaleを超える拡大を許可し、zoom中も縦横へ同一倍率を適用する。
  - zoom/pan boundaryを画像Sizeとviewportから計算し、stretchする非等方scaleを禁止する。
  - 縦長、横長、正方形、小画像、大画像、EXIF rotationについて通常/全画面の元縦横比をUnit/Screenshot Testする。

### 7.2 写真全画面表示

- [x] 写真Viewerに全画面表示を実装する。
  - 通常表示から全画面へ切り替える操作を追加する。
  - 全画面ではApp chromeを隠し、利用可能領域全体を画像surfaceへ使う。
  - 画面を埋めるために元の縦横比を変更せず、必要な上下または左右の余白を維持する。
  - tapまたは明示操作とsystem Backで通常表示へ戻れるようにする。
  - file ID/version/variant変更時だけzoom/panをresetし、再Compositionでは維持する。
  - Portrait/Landscape、system bars、rotation、background/foreground、TalkBackをInstrumented Testする。

---

## フェーズ8: SettingsとTrusted Wi-Fi

### 8.1 Settings下位画面の色修正

- [x] Settings全画面の背景・文字色をTheme tokenへ統一する。
  - hard-coded color、同一background/content color、透明Surface上のText色を検索する。
  - 見出し、項目名、現在値、説明、入力、Dialog、DropdownをMaterial color schemeへ合わせる。
  - enabled、disabled、selected、focused、error状態の文字を判読可能にする。
  - Light/Darkで通常文字4.5:1以上、大文字と必要UI要素3:1以上を確認する。
  - 360dp、Landscape、fontScale 2.0、TalkBackのPreview/Screenshot/UI Testを追加する。

### 8.2 現在接続中Wi-Fiの検出

- [x] Android version別の現在Wi-Fi取得をTest firstで修正する。
  - Wi-Fi transport、Android version、`NEARBY_WIFI_DEVICES`、必要なlocation permission、位置情報Service状態を確認する。
  - `Available`、`PermissionRequired`、`LocationServicesDisabled`、`NotConnected`、`Unavailable`を型付き結果として返す。
  - `UNKNOWN_SSID`、空SSID、masked/invalid BSSIDを登録候補から除く。
  - 権限request後とSettings復帰後に状態を再取得する。
  - Android 12以前、Android 13以降、permission拒否、位置情報無効、Wi-Fi未接続、取得不能をUnit/Instrumented Testする。

### 8.3 Trusted Wi-Fi登録Form

- [x] 検出したWi-Fiを登録Formへ安全に反映する。
  - `現在のWi-Fiを使用`操作または画面初期取得でSSIDと利用可能なBSSIDを自動入力する。
  - 自動入力後も利用者の明示的SaveまでRepositoryへ登録しない。
  - permission/Service/接続状態ごとに解決可能な案内とactionを表示する。
  - 既存登録との重複、SSID quote除去、BSSID任意制限、metered設定を検証する。
  - 登録判定をTLS、Server identity、User/Device/Session認証、`LOCAL_DIRECT`判定の代替にしていないことを回帰Testする。

---

## フェーズ9: PDF Viewer

### 9.1 PDF不具合の回帰Testと取得修正

- [x] baselineで特定したPDF open failureをTest firstで修正する。
  - metadata、Content-Type parameter、Content-Length、Range対応を正規化・検証する。
  - 通信量確認後にprivate一時領域へ完全取得し、Download保存操作を要求せずViewerを開く。
  - 取得完了前、Size不一致、空File、cancel済みFileをRendererへ渡さない。
  - 正常、認証失敗、Network失敗、HTTP失敗、256 MiB境界、空き容量不足、途中切断をTestする。

### 9.2 Renderer lifecycleとtyped error

- [x] PDF Rendererと一時resourceを安全に管理する。
  - seekableな完全Fileだけを`PdfRenderer`へ渡す。
  - Corrupt、Encrypted、RenderUnsupportedを取得Errorと区別する。
  - page切替、retry、Viewer離脱、logout、route変更でPage、PFD、未公開Bitmapを解放する。
  - `.part`、Session一時File、lease、TTL、512 MiB/Session契約を維持する。
  - 正常PDF、破損PDF、暗号化PDF、複数page、rapid open/closeをInstrumented Testする。

---

## フェーズ10: 自動Backupの制御付き並列化

### 10.1 Dispatcherと設定

- [x] 既存Backup run内へ上限付きdispatcherをTest firstで実装する。
  - WorkManagerをFile件数分生成せず、1 run内の固定worker poolまたは`Semaphore`で並列化する。
  - 手動Uploadと自動Backupを同じ端末内の優先度付き共通転送枠へ接続し、合計同時数を制御する。
  - 待機Queueでは手動Uploadを優先し、すでに実行中のBackupは途中で破棄しない。
  - 同時実行数を型付き設定とし、最小1、運用上限、起動時validationを定義する。
  - 並列数1・2・4をServer limiterと実測値に照らし、4でも改善が続いて余裕があれば6・8も比較する。
  - 2を超える値を採用する場合はServer全体Upload limiterも同じ混合負荷Testに基づいて調整する。
  - 設定値を超えるUploadが同時実行されないdeterministic Testを追加する。

### 10.2 Queue claim・冪等性・再開

- [x] 並列実行時のQueue整合性を保証する。
  - Queue claimをTransactionで確定し、同一itemを複数coroutineが処理しない。
  - Fileごとにupload session、operation ID、expected version、receiptを分離する。
  - 同一Fileの重複候補を既存identity/checkpoint規則で1件へ収束させる。
  - retryableな1件失敗で他の独立itemをcancelしない。
  - 認証失効、Storage不足等のrun停止Errorだけを全workerへ伝播する。
  - Network constraint喪失とWorker cancellationで新規claimを止め、実行中itemを再開可能な状態へ確定する。
  - Process再起動後に未完了itemだけを再開し、成功済みFileを再Uploadしない。

### 10.3 Backup正確性・性能Test

- [x] 並列Backupの正確性と改善量を測定する。
  - 並列数1・2・4、必要なら6・8について、大量小File、大File混在、並列上限超過をTestする。
  - 手動Uploadと自動Backupの混在時に合計上限を守り、待機中の手動Uploadが優先されることをTestする。
  - 部分失敗、429、timeout、Network切替、cancel、process restartをTestする。
  - Upload後のFile count、Folder、Size、checksum、receipt、Queue最終状態を検証する。
  - 同じfixtureの直列baselineと並列結果について総所要時間を比較する。
  - Android CPU/memory/network、Server CPU/memory/I/O、429数を測定する。
  - Thumbnail生成、一覧API、動画Range再生を同時実行し、継続CPU余力25%以上、swap増加/OOM/thermal throttlingなし、I/O waitの継続20%未満、一覧API p95悪化20%未満、動画rebuffer増加なしを確認する。
  - 安全域を超えた場合は並列数またはBufferを調整し、再測定して正式値を文書化する。

---

## フェーズ11: 正式文書・契約・対象自動検証

### 11.1 正式文書の最終整合

- [x] 最終実装を正式文書へ反映する。
  - `docs/product-requirements.md`へUpload、パンくず、一覧位置復元、写真、Search、Thumbnail状況/並列生成、動画Original/Cellular確認、Wi-Fi、Settings、Backup、PDF、Headerの受入条件を反映する。
  - `docs/functional-design.md`へSAF Folder計画、Transfer state、一覧anchor、Job summary/並列生成、動画確認、Network transport、Backup dispatcher、PDF flowを反映する。
  - `docs/architecture-design.md`へAPI/authorization、Thumbnail dispatcher/同時数、動画variant policy、Player/Buffer、Backup同時数、resource上限を反映する。
  - `docs/repository-structure.md`へ実際に追加・変更したComponentとTest配置を反映する。
  - `docs/development-guidelines.md`へFolder traversal、通信確認、並列処理、manifest清掃規約を反映する。
  - `contracts/openapi/kurastorage-api.yaml`とAPI Error/互換性文書を最終実装へ一致させる。
  - 文書間で動画品質、1 MiB境界、Backup/Thumbnail並列数、Thumbnail count scope、一覧anchor、清掃条件に矛盾がないことを検索確認する。

### 11.2 変更直近の自動Test

- [x] 変更箇所に近いTestを実行し、すべて成功させる。
  - Transfer state、Upload selection、Folder walker、パンくず、一覧anchor、NavigationのJVM Unit Testを実行する。
  - Thumbnail summaryと並列dispatcherのApplication/Infrastructure/API Contract Testを実行する。
  - Photo/Video/PDF、Network transport、Wi-Fi、SettingsのJVM/Compose Testを実行する。
  - Backup dispatcher、claim、retry/restartのUnit/Integration Testを実行する。
  - 失敗修正後は失敗した最小scopeから再実行し、関係する上位scopeを再確認する。

### 11.3 Module単位のBuild・Lint・Test

- [x] 関連Module単位の品質確認を完了する。
  - 変更したAndroid moduleの`test`を実行する。
  - 変更したAndroid moduleのCompose/Instrumented TestをEmulatorで実行する。
  - 変更したAndroid moduleのcompile/lintを実行する。
  - 変更したServer projectのbuild/testを実行する。
  - Server format verificationとOpenAPI validationを実行する。
  - 追加依存がある場合はversion catalog、license、依存方向、不要依存がないことを確認する。

### 11.4 Repository標準検証

- [x] 変更が安定した後にRepository標準検証を実行する。
  - `./scripts/ci/verify-server.sh`が成功する。
  - `./scripts/ci/verify-android.sh`が成功する。
  - `./scripts/ci/verify-config.sh`が成功する。
  - API/permission/Path traversal/manifest guard変更に関係する`./scripts/ci/verify-security.sh`が成功する。
  - Deployment設定に変更が生じた場合だけ`./scripts/ci/verify-deployment.sh`を実行する。
  - 成功済みの重い検証は、関連Sourceまたは設定が再変更された場合だけ再実行する。

---

## フェーズ12: 実機・実Server E2E、性能確認、安全な清掃

### 12.1 Upload・File browser・Navigation実機確認

- [x] Android実機と実ServerでUploadとNavigationを確認する。
  - 単一Fileと複数Fileを選択し、内容・Size/checksum・結果表示を確認する。
  - 入れ子Folder、空Folder、同名項目、一部読取不能をFolder Uploadし、親子構造と部分結果を確認する。
  - 全Upload完了後に`Transfer status`が消え、失敗時だけ必要な情報が残ることを確認する。
  - パンくずの各祖先へ移動し、連打やBackでも架空Pathが出ないことを確認する。
  - 一覧をscrollしてFile/各Viewerを開き、Back後に同じFile位置とoffsetへ戻ることを確認する。
  - 一覧Refresh、項目追加/削除、Sort/Filter変更、rotation後のanchor復元とcontext分離を確認する。
  - SearchからHomeへ1回で戻り、他のBottom navigationも回帰していないことを確認する。
  - Header縮小前後の一覧領域と主要Actionを確認する。

### 12.2 Thumbnail・写真・動画・PDF実機確認

- [x] Media/PDF機能をAndroid実機と実Serverで確認する。
  - Thumbnailの待機数と生成中数をDB/Server状態と照合し、完了・失敗・retryで更新されることを確認する。
  - 複数Thumbnail Jobが設定上限内で並列生成され、重複/部分生成物が公開されないことを確認する。
  - Thumbnailの直列/並列所要時間とRaspberry PiのCPU、memory、I/Oを比較し、安全な既定値を確認する。
  - 写真を通常/全画面で表示し、縦横比、既定Size、zoom、rotationを確認する。
  - 動画に品質選択がなく、Low/Medium動画Jobが新規生成されないことを確認する。
  - Wi-FiでOriginal動画を再生・停止・seek・速度変更し、tap overlayを通常/全画面で確認する。
  - 縦長・横長・正方形に近い写真と動画で、通常/全画面・回転後も元の縦横比が変わらず、Cropや引き伸ばしがないことを確認する。
  - Cellularで1 MiB未満、1 MiB以上、Size不明を確認し、Cancel前にContent GET/RangeがないことをServer log等で確認する。
  - 基準動画のstartup、rebuffer、dropped frame、端末/Server負荷を測定し、baselineと比較する。
  - 正常、複数page、破損、暗号化PDFを開き、表示/error/一時File清掃を確認する。

### 12.3 Settings・Trusted Wi-Fi実機確認

- [x] Settingsと現在Wi-Fi登録を実機確認する。
  - Settings全下位画面をLight/Dark、Portrait/Landscape、fontScale 2.0で確認する。
  - 対象Android versionで現在接続中SSIDと利用可能なBSSIDがFormへ反映されることを確認する。
  - permission拒否、位置情報Service無効、Wi-Fi未接続、取得不能の案内を確認する。
  - 検出直後は未登録で、Save後だけ登録されることを確認する。
  - 登録済み外部Wi-FiでもZeroTier、TLS、Server identity、User/Device/Session認証を満たさなければBackupを開始しないことを確認する。

### 12.4 Backup並列E2E

- [x] 実機・実ServerでBackupの正確性と性能を確認する。
  - 並列上限より多い作業prefix付きFileをBackup対象にする。
  - 同時実行数が設定上限を超えず、複数Fileが並列に進むことを確認する。
  - 手動Uploadと自動Backupを同時に開始し、合計上限と手動Uploadの待機優先を確認する。
  - 直列baselineより総所要時間が改善することを確認する。
  - Network切替、1件失敗、Worker停止/再開後に未完了項目だけを処理することを確認する。
  - Server/端末のCPU、memory、I/O、429が安全域内であることを確認する。
  - Thumbnail生成・一覧操作・動画再生との混合負荷でもForeground応答と再生品質を維持し、上限超過分がQueueで待機することを確認する。
  - File count、Folder、Size、checksum、Queue、receiptに重複・欠落・破損がないことを確認する。

### 12.5 今回作成したテストデータだけの清掃

- [x] manifestに記録した今回作成分だけを清掃する。
  - 削除候補ごとにexact ID、manifest membership、作業prefix/作成metadataをread-onlyで再確認する。
  - 照合できない候補は削除せず、原因を調査して対象を確定する。
  - 今回作成したShare、Favorite、Tag、Recent、Activity等の参照をID指定で解除・削除する。
  - 今回作成したUpload session、Backup run/item、Media job/derivativeを既存契約に従ってID指定で清掃する。
  - 今回作成したFileとFolderを子から親の順にID指定で完全削除する。
  - 今回作成したUserだけをID指定で削除する。
  - 今回作成したAndroid一時Fileと検証用local artifactだけを清掃する。
  - manifest全IDが残っていないことを再照会する。
  - 作業前baselineの既存ID、件数、checksum/spot-check対象が維持されていることを確認する。
  - wildcard、名前部分一致、親Folder一括、全件削除を実行していないことを操作記録で確認する。
  - 清掃対象、結果、既存データ維持確認を秘密情報なしでevidenceへ記録する。

---

## フェーズ13: 最終Review・Commit・Pull Request・記録

### 13.1 差分と完了条件のSelf-review

- [x] Pull Request前の最終reviewを完了する。
  - フェーズ0〜12に未完了項目がないことを確認する。
  - `requirements.md`の全受け入れ条件とTest/evidenceを対応付ける。
  - 実装と`design.md`、正式文書、OpenAPIが一致することを確認する。
  - `git diff --check`を実行する。
  - 差分へUserの既存変更、無関係なrefactor、生成物、秘密情報、個人情報、debug code、テストデータが混在していないことを確認する。
  - Upload/Folder traversal、authorization、Thumbnail/Backup並列、一覧anchor、Cellular確認、Wi-Fi identity、cleanup guardを重点reviewする。
  - Test結果、性能値、未実施事項が事実どおり記録されていることを確認する。

### 13.2 Commit・Push・英語Pull Request

- [ ] PR 1をCommit・Pushして英語Pull Requestを作成する。
  - 全実装、Test、正式文書、Steering進捗、秘密情報を含まないevidenceをCommitする。
  - Commit前に`git status`とstaged diffをreviewする。
  - PR 1 BranchをremoteへPushする。
  - 英語Titleを`Improve Android uploads, media playback, backup throughput, and navigation`を基準に最終差分へ合わせる。
  - 英語BodyへPurpose、target tasks、changes、tests、performance results、cleanup results、impact/limitationsを記載する。
  - CIを監視し、失敗を修正して必要な検証と進捗更新を行う。
  - Pull RequestはMergeしない。

### 13.3 Pull Request完了記録

- [ ] `steering`スキルのモード3-Aで、本ファイルの「各Pull Request完了記録」へPR 1を記録する。
  - 完了日とPull Request番号/URLを記録する。
  - Test、Build、Lint、API/E2E、性能測定、実機確認、清掃結果を記録する。
  - 計画と実装の差分、追加タスクと理由、技術的取消と代替実装、引継ぎを記録する。
  - 該当事項がない項目は「なし」と記載する。
  - 完了記録を同じBranchへCommit・Pushし、Pull Requestへ反映されたことを確認する。

### 13.4 全体振り返り

- [ ] `steering`スキルのモード3-Bで全体振り返りを記録する。
  - フェーズ0〜13.3に未完了タスク`[ ]`が残っていないことを確認してから本文を書く。
  - 実装完了日、計画と実績、主な設計変更、技術的な学び、プロセス改善、次回提案を記録する。
  - 振り返り更新を同じBranchへCommit・Pushし、Pull Requestへ反映されたことを確認する。
  - Pull Request URL、主な変更、検証、性能、清掃、完了記録・振り返りをUserへ報告し、Mergeせず停止する。

---

## 各Pull Request完了記録

全タスク完了後、Pull Request作成時に`steering`スキルのモード3-Aで記録する。PR作成前には記録しない。

---

## 全体振り返り

全タスク、PR 1、Pull Request完了記録が完了した後にだけ、`steering`スキルのモード3-Bで記録する。
