# Android写真・PDFビューアー／動画・音声プレイヤー タスクリスト

## 対象要件・設計

- `docs/product-requirements.md` 4.6、7.6.1〜7.6.3
  - 一覧サムネイル、写真の拡大・縮小と前後移動、PDF閲覧、動画・音声のRange再生を実現する。
  - 写真・動画の低画質／中画質／元画質選択、元画質取得前のサイズまたは推定通信量確認、生成状態表示を実現する。
- `docs/functional-design.md` 6.1.5、7.3〜7.4、8.5〜8.6、11.3、11.5〜11.6、11.10、Android Step 8
  - `MediaViewerController`、認証付き派生データ取得、Media Job状態照会、完成済みMP4のRange再生をAndroidへ接続する。
  - 接続環境は初期品質の決定だけに使用し、手動の品質選択を制限しない。
- `docs/architecture-design.md` 5.2、11.3〜11.4、14.2、21.3〜21.4
  - Coil、Media3 ExoPlayer、Android `PdfRenderer`を必要になった段階で導入し、既存の認証・Network binding境界を再利用する。
- `.steering/20260829-thumbnail-derivative-worker-infrastructure/`
  - 完成済みのThumbnail、写真Low／Medium、動画Low／Medium、Media Job、Lease付き単一Range配信を前提とする。

## タスク完全完了の原則

**このファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止してよい。**

### 必須ルール

- 全タスクを最終的に`[x]`にする。
- 「時間の都合」「実装が複雑」などを理由にタスクを後回しまたは省略しない。
- 選択したPull Request単位に未完了タスクを残したまま作業を終了しない。
- 後続Pull Requestのタスクは、先行Pull Request完了時点では`[ ]`のままでよい。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ更新する。
- 実装時はTDDのRed、Green、Refactor、Verifyを各変更単位で行う。

### Pull Request運用

- 各Pull Requestは原則として最新の`main`から短命Branchを作成する。
- 未Mergeの先行Pull Requestに依存する作業は、先行Pull Requestが`main`へMergeされ、必須CIが成功してから開始する。
- 実装、Test、文書、tasklist更新、Commit、Push、英語のPull Request作成までを同じPull Request単位で完了する。
- Pull RequestはMergeしない。作成後は`steering`スキルのモード3-Aで完了記録を追記し、Commit・Pushして停止する。

## スコープ境界

- [x] Androidの一覧サムネイル、写真Viewer、PDF Viewer、動画・音声Player、品質・通信量設定を対象とする。
- [x] Server側は既存の派生データ・Media Job・Range配信契約を利用し、契約不備の修正が必要な場合だけ最小限のServer変更を同じ関連PRへ含める。
- [x] 写真と動画は低画質／中画質／元画質を対象とし、ユーザー操作なしで元画質へFallbackしない。
- [x] 音声は元ファイルのRange再生だけを対象とし、音声Low／Mediumと音声変換Jobを追加しない。
- [x] HLS、DASH、生成途中Segment再生、次動画の自動再生、端末への恒久Media保存、音声波形表示、字幕編集、DRM、Picture-in-Picture、Castを追加しない。
- [x] PDF Thumbnailは一覧表示だけに使用し、PDF本文は元PDFを安全なApp private一時領域へ取得して`PdfRenderer`で表示する。
- [x] 物理Path、Access Token、内部Job情報、未検証の派生データをUI、Log、Analyticsへ公開しない。

---

## フェーズ0: 実装前の要求・設計確定

> 本タスクリストは正式文書から実装順序を作成したもの。専用の要求・設計文書を承認し、先行Media基盤が`main`へMergeされるまでPR1を開始しない。

- [x] `.steering/20260829-android-media-viewers-players/requirements.md`を`steering`スキルのモード1で作成し、Userの承認を得る。
  - [x] 一覧サムネイル、写真、PDF、動画、音声ごとの対象MIME、正常状態、Loading状態、Empty状態、Error状態、非対応形式を確定する。
  - [x] 写真・動画の品質初期値、手動切替、元画質確認、推定通信量の算出・表示・再確認条件を確定する。
  - [x] `docs/product-requirements.md` 7.6.3が動画・音声を一括して品質選択対象とする一方、既存Server派生契約が動画だけをLow／Medium対象とする差を解消する。
  - [x] 音声を元画質Range再生だけにする場合は正式文書へ明記し、音声Low／Mediumを必要とする場合はServer派生生成を先行Steeringとして分離する。
  - [x] PDF取得上限、空き容量不足、Download取消、一時File保持期間、画面離脱・Process再生成時の扱いを確定する。
  - [x] 生成待ち、Queue待ち、進捗なし／あり、失敗、Retry可能／不可、通信切断、認証失効のユーザー操作と完了条件を確定する。
  - [x] Android 10以降の実機、対応Codec、巨大写真・長大PDF・長時間Media、LOCAL_DIRECT／REMOTE_SECURE／Mobileの受け入れ条件を確定する。
- [x] `.steering/20260829-android-media-viewers-players/design.md`を`steering`スキルのモード1で作成し、Userの承認を得る。
  - [x] `feature-media`と`feature-settings`、`core-model`、`core-network`、`core-data`、`app`の依存境界とNavigationを設計する。
  - [x] Coilの認証付きImageLoader、Media3のDataSource／認証Header、Token refresh、接続先変更時のClient再生成を設計する。
  - [x] `MediaViewerController`の品質決定、状態遷移、Request取消、Job polling、Retry、画面再訪時の状態復元を設計する。
  - [x] `PdfRenderer`用App private一時FileのStreaming取得、Size上限、容量確認、FileDescriptor lifecycle、清掃を設計する。
  - [x] Media3 Player lifecycle、Range、Seek、再生速度、品質切替時の位置維持、Buffer設定、音声Focus、画面回転を設計する。
  - [x] 一覧Thumbnailの並行数、Memory／Disk cache、再利用、取消、Placeholder、Error表示とScroll性能を設計する。
  - [x] JVM Unit Test、MockWebServer、Compose Instrumented Test、実Server／実Android E2E、性能・通信量測定Matrixを設計する。
- [x] 正式文書と承認済み要求・設計の差分を同じ変更で解消する。
  - [x] 音声品質、PDF通信量確認、推定通信量算出、品質変更時の再確認規則を正式文書へ反映する。
  - [x] `feature-media`、`feature-settings`と追加するSource／Test配置を`docs/repository-structure.md`へ反映する。
  - [x] Coil、Media3、`PdfRenderer`の確定Version、認証境界、Cache方針を`docs/architecture-design.md`へ反映する。
  - [x] 実機受け入れ条件とCI対象を`docs/development-guidelines.md`へ反映する。
- [x] 承認済み`requirements.md`と`design.md`に合わせて本タスクリストを再確認する。
  - [x] API名、DTO、enum、状態、Module、依存Version、確認Commandを具体化する。
  - [x] PR1〜PR4の依存関係と完了条件が承認済み設計を過不足なく覆うことを確認する。
  - [x] 正式文書との矛盾と未決定Placeholderが残っていないことを確認する。

---

## フェーズ1 / PR1: Media契約・品質設定・共通Controller基盤

### 1.1 開始条件と既存実装確認

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0の全項目が`[x]`で、`requirements.md`と`design.md`が承認済みである。
  - [x] サムネイル・派生データ・Worker基盤の全PRが`main`へMerge済みで、必須CIが成功している。
  - [x] `git status`と既存差分を確認し、Userの変更を混在させない。
  - [x] `FileRepository`、`KuraStorageApi`、Token refresh、接続先切替、Navigation、ViewModel、DataStoreの既存Patternを確認する。
  - [x] 最新`main`からPR1用Branchを作成する。

### 1.2 Module・依存関係

  - [x] Mediaと品質設定のModule基盤を実装する。
  - [x] 承認済み設計どおり`feature-media`と必要な`feature-settings`を追加し、Feature間を直接依存させない。
  - [x] Version CatalogへCoil 3.5.0（`coil-compose`、`coil-gif`）とMedia3 1.11.0（`media3-exoplayer`、`media3-datasource-okhttp`、`media3-ui-compose`）を固定し、Gradle dependency lockを更新する。
  - [x] Android標準`PdfRenderer`は外部PDF Libraryを重複導入せず利用する。
  - [x] Convention Pluginを再利用し、Module固有Build設定だけを各`build.gradle.kts`へ記載する。
  - [x] `app`のServiceContainer／ViewModelFactory／NavigationへInterface経由で配線し、認証Sessionまたは接続先変更時にMedia状態を破棄する。

### 1.3 型付きMedia契約とAPI Client

  - [x] Media Domain modelとAPI DTOをTest firstで実装する。
  - [x] `MediaQuality`、`MediaVariant`、`MediaKind`、`MediaJobStatus`、`PlaybackState`、Byte size、Durationを型付きで表現する。
  - [x] Serverの`thumbnail`、`image-low`、`image-medium`、`video-low`、`video-medium`、`original`を誤変換しないMapperを実装する。
  - [x] `202 GENERATING`、`READY`、`FAILED`、Queue位置、進捗、省略可能値、Retry-After、未知Statusをfail-closedなUI状態へ変換する。
  - [x] `HEAD .../content?variant=original`のSize、MIME、Range対応を型付き結果へ変換する。
  - [x] 他User情報、物理Path、Server Process出力をDTOへ追加しない。
  - [x] 認証付きMedia APIをMockWebServerでTest firstに実装する。
  - [x] Thumbnail／写真／動画／音声／PDFのContent URLをServer契約どおり構築し、任意のPathやHostを受け入れない。
  - [x] Media Job状態照会と明示Retryを実装し、`Retry-After`に従ってbounded pollingする。
  - [x] `200`、`202`、`206`、`401`、`403`、`404`、`409`、`416`、`429`、`5xx`と共通Error codeを状態へ変換する。
  - [x] Access TokenをQueryへ付けずHeaderで送信し、401 refreshの単一Flightと再試行上限を既存Client境界で守る。
  - [x] Range、`Content-Range`、`Accept-Ranges`、取消、途中切断、短いBody、不一致Content-LengthをTestする。

### 1.4 品質・通信量設定

  - [x] 接続環境別の初期品質設定をTest firstで実装する。
  - [x] LOCAL_DIRECTは元、登録済み外部Wi-Fi＋ZeroTierは中、未登録Wi-Fi＋ZeroTierとMobile＋ZeroTierは低を初期値とする。
  - [x] 端末共通の変更を`media_quality_preferences` DataStoreへ保存し、未知値・破損値を安全な既定値へ戻す。
  - [x] 許可Wi-Fi機能が未導入のProductionではREMOTE_SECUREなWi-Fiを未登録として扱い、`RegisteredWifiSource`を将来差替え可能にする。
  - [x] 接続種別はViewer起動時の初期値だけに使い、低／中／元の選択肢を削除または無効化しない。
  - [x] Sessionまたは接続先をまたいでFile固有状態、Job ID、認証済みURLを再利用しない。
  - [x] 元画質の通信量確認をTest firstで実装する。
  - [x] HEADで取得した元Sizeと、承認済み規則で算出した推定通信量を人間向け単位で表示する。
  - [x] Size不明、HEAD失敗、File Version変更、品質再選択時の確認規則を実装する。
  - [x] 確認前に元Contentをprefetchせず、取消時に元画質Requestを開始しない。
  - [x] 低／中品質でも取得予定Sizeを取得できる場合は通信量表示へ反映し、表示値を課金保証として扱わない文言を用意する。

### 1.5 MediaViewerController

  - [x] 共通ControllerをTest firstで実装する。
  - [x] 初期品質決定、品質変更、元画質確認、Loading、Generating、Ready、Failed、Disconnectedを単方向状態遷移で表現する。
  - [x] 同じFile／Version／品質の重複Requestと重複Pollingを抑止し、最後に選択した品質だけを画面へ反映する。
  - [x] 写真の2秒超過`202`と動画の即時`202`を同じ生成状態へ潰さず、Retry-Afterと画面lifecycleに従う。
  - [x] 画面離脱でHTTP／Pollingは取消してもServer Media Jobを取消さない。
  - [x] 再訪時はJob IDだけを無条件に信頼せず、現在File／Version／権限から状態を再取得する。
  - [x] 低／中品質失敗時に元画質へ自動Fallbackせず、明示選択だけを許可する。

### 1.6 PR1検証・文書・完了

- [x] PR1の検証を完了する。
  - [x] Media model、Mapper、API、品質設定、ControllerのJVM Unit TestとMockWebServer Testが成功する。
  - [x] `./scripts/ci/verify-android.sh`、`./scripts/ci/verify-config.sh`、`git diff --check`が成功する。
  - [x] dependency lock、License、不要依存、秘密情報、認証URL／TokenのLog出力がないことを確認する。
- [x] PR1に必要な正式文書を実装と同じ変更で更新する。
- [x] PR1を完了する。
  - [x] フェーズ1の全項目が`[x]`であることを確認する。
  - [x] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR1完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ2 / PR2: 一覧サムネイル・写真Viewer・PDF Viewer

### 2.1 開始条件

- [x] PR2の開始条件を満たす。
  - [x] PR1が`main`へMerge済みで、必須CIが成功している。
  - [x] `git status`と既存差分を確認し、最新`main`からPR2用Branchを作成する。
  - [x] `FileBrowserScreen`、File詳細／Download、Compose状態保存、List／Grid、MockWebServerの既存Patternを確認する。

### 2.2 一覧サムネイル

- [x] 認証付きThumbnail取得をTest firstで実装する。
  - [x] Coil ImageLoaderを既存OkHttp／認証／Network binding境界へ接続し、別の未認証Clientを作らない。
  - [x] File ID、Version、`thumbnail` VariantをCache keyへ含め、Rename／Moveだけでは画像内容Cacheを無効化しない。
  - [x] Session scopeをCache keyとDirectoryへ含め、MemoryをHeap 10%かつ最大64MiB、DiskをSession最大256MiBに制限する。
  - [x] `202`時はPlaceholderとbounded再取得を行い、元ファイルを代替取得しない。
  - [x] `FAILED`、非対応MIME、権限消失、MISSING、通信切断を種類Iconまたは操作可能なErrorへ変換する。
  - [x] Scrollで画面外になったRequestを取消し、同一Thumbnailの重複取得と無制限並列Requestを防止する。
  - [x] Thumbnail取得を最大8並列とし、Session終了時とProcess起動時に旧Session cacheを清掃する。
- [x] File一覧UIへThumbnailを統合する。
  - [x] GridではThumbnailを表示し、Listでは承認済み表示規則に従ってThumbnailまたは種類Iconを表示する。
  - [x] Folder、Thumbnail非対象File、生成中、Error、MISSING、共有Badgeを視覚的・Accessibility上で区別する。
  - [x] 1,000件Folderの段階表示でMain thread decode、過剰Recomposition、元画質通信が発生しないことを計測する。

### 2.3 写真Viewer

- [x] 写真表示と操作をTest firstで実装する。
  - [x] 対象MIMEだけを写真ViewerへRoutingし、Server宣言とDecode結果が不正な場合は非対応表示にする。
  - [x] Coilの認証付き取得で選択品質だけをDecodeし、Loading／Generating／Ready／Failedを表示する。
  - [x] 画面fit、ピンチZoom、Pan、Double tap復帰、回転後の安全な状態復元を実装する。
  - [x] Decodeを長辺4096px、Bitmap見積り32MiB、Zoom 1〜4倍に制限し、Main threadでDecodeしない。
  - [x] 同じ閲覧Context内の前後写真へ移動し、Folder／非画像／閲覧不可／MISSINGを候補から除外する。
  - [x] 前後移動時の先読みを承認済み上限内にし、モバイル通信で元画質を先読みしない。
- [x] 写真の品質・通信量操作を実装する。
  - [x] 低／中／元の現在値、接続種別、元Size、生成状態を表示する。
  - [x] 低／中切替では選択Variantだけを要求し、旧Request完了が新しい画像を上書きしない。
  - [x] 元画質はサイズ／推定通信量Confirm後だけ取得し、File Version変更時は再確認する。
  - [x] Viewerから詳細と品質指定Downloadへ移動し、最新File名と選択品質を引き渡す。

### 2.4 PDF Viewer

- [x] PDF取得・一時File管理をTest firstで実装する。
  - [x] HEADでSize／MIME／Range対応を確認し、1 File 256MiBと空き容量`Content-Length + 64MiB`を満たす場合だけ取得する。
  - [x] 元PDFの通信量を表示して確認後に、Streamingで一時Fileへ書き、全体をMemoryへ保持しない。
  - [x] 取消、途中切断、Content-Length不一致、空き容量不足、破損PDFで部分Fileを閉じて限定削除する。
  - [x] File ID／VersionからServer制御の安全な一時名を生成し、元File名をPathとして使用しない。
  - [x] 画面終了、Process再生成、期限切れ、Logout、接続先変更時のFileDescriptorと一時File清掃を実装する。
  - [x] PDF一時FileをSession合計512MiB、未参照TTL 1時間のLRUで清掃し、active FileDescriptor付きFileを除外する。
- [x] `PdfRenderer` ViewerをTest firstで実装する。
  - [x] Pageを必要時に1枚ずつRenderし、現在Page以外のBitmapとPageをboundedに解放する。
  - [x] PDF Bitmapを長辺4096px、1枚32MiB、Zoom 1〜4倍に制限し、Main threadでRenderしない。
  - [x] 前後Page移動、Page指定、現在Page／総Page数、Pinch zoom、Pan、画面fitを実装する。
  - [x] 0 Page、暗号化、破損、巨大Page、Renderer例外をCrashさせずError表示へ変換する。
  - [x] Download操作へ移動でき、Viewer一時Fileを公開Storageや他Appへ直接公開しない。

### 2.5 Navigation・UI検証

- [x] File一覧／詳細から写真・PDF ViewerへのNavigationを実装する。
  - [x] MIMEとFile状態に応じて正しいViewerへ遷移し、不明MIMEを誤って開かない。
  - [x] Back、画面回転、Process再生成、Logout、Token失効、接続先変更で別Userの表示状態を残さない。
- [x] Compose Instrumented Testを追加する。
  - [x] ThumbnailのPlaceholder／Ready／Error、写真Zoom／品質Confirm、PDF Page移動／ErrorをSemantics経由で確認する。
  - [x] Accessibility label、Touch target、文字拡大、Dark theme、縦横画面で主要操作が利用できることを確認する。

### 2.6 PR2検証・文書・完了

- [x] PR2の自動・手動検証を完了する。
  - [x] JVM Unit Test、MockWebServer Test、Compose Instrumented Testが成功する。
  - [x] `./scripts/ci/verify-android.sh`、`./scripts/ci/verify-config.sh`、`git diff --check`が成功する。
  - [x] 実機E2Eで検出した元ContentのHEAD契約不備をServerに最小追加し、Size／MIME／Range応答をTest firstで確認する。
  - [x] 実機E2Eで検出した長いFile名のViewer Header崩れとPDF Page操作の画面外配置をTest firstで修正する。
  - [x] 現行Android（Android 13）実機で一覧Thumbnail、写真Low／Medium／Original、PDF複数Pageを確認する。Android 10実機は今回の受け入れ対象外とする。
  - [x] Network inspectionで一覧が元画質を取得せず、写真が選択品質だけを取得することを確認する。
  - [x] Leak／StrictMode／Memory profilerでBitmap、PDF Page、FileDescriptor、一時Fileの解放を確認する。
  - 実機検証記録（2026-08-30）: OPPO CPH2333 / Android 13で一覧Thumbnail、写真Low／Medium／Original、PDF 3 Page、長いFile名を確認し、`feature-media` Instrumented Test 8件が成功した。
  - 通信／Resource検証記録（2026-08-30）: Server側の派生File更新時刻で選択Variantのみの取得を確認し、StrictModeで検出したPDFのMain thread I/Oを修正後、KuraStorage由来の違反、CloseGuard、OOM、Activity／View増加がないことを確認した。
- [x] PR2に必要な正式文書とUI mockup対応を実装と同じ変更で更新する。
- [x] PR2を完了する。
  - [x] フェーズ2の全項目が`[x]`であることを確認する。
  - [x] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR2完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ3 / PR3: 動画・音声Playerと動画変換状態

### 3.1 開始条件

- [x] PR3の開始条件を満たす。
  - [x] PR2が`main`へMerge済みで、必須CIが成功している。
  - [x] `git status`と既存差分を確認し、最新`main`からPR3用Branchを作成する。
  - [x] 既存OkHttp認証、Media Job契約、完成済みMP4配信、Range挙動、Android audio focus／Codec対応を確認する。

### 3.2 Media3認証・Range再生基盤

- [x] Media3 Player adapterをTest firstで実装する。
  - [x] 承認済みMedia3 DataSourceへ認証Headerと既存Network bindingを渡し、TokenをURLへ埋め込まない。
  - [x] `Accept-Ranges`、`206`、`Content-Range`、Seek後のRange再要求、Server `416`を正しく処理する。
  - [x] Player、DataSource、Coroutineを画面lifecycleに従って停止・解放し、Server Media Jobは取消しない。
  - [x] Token refresh後に現在位置からbounded再開し、認証失効、権限消失、File Version変更時は停止する。
  - [x] Network切断、Timeout、短い応答、Codec非対応、Decoder errorを再試行loopにせず操作可能な状態へ変換する。
  - [x] Mobileでは承認済みBuffer上限を使用し、次動画を自動準備・自動再生しない。
  - [x] `DefaultLoadControl`をWi-Fi 15〜50秒、Mobile 5〜15秒、再生開始1.5秒、再Buffer 3秒の初期値で構成する。

### 3.3 動画品質・変換Job

- [x] 動画品質選択と生成状態をTest firstで実装する。
  - [x] 低／中は`video-low`／`video-medium`、元は明示確認後の`original`だけを要求する。
  - [x] `READY`の完成・検証済みMP4だけをPlayerへ渡し、`202`や生成途中Fileを再生しない。
  - [x] QUEUED、Queue位置あり／なし、RUNNING、進捗あり／なし、Retry待ち、FAILED、READYを区別して表示する。
  - [x] 「完了まで待つ」は画面内でbounded pollingし、完了時に選択品質を自動再取得する。
  - [x] 「バックグラウンドで続ける」は画面を離れ、再訪時にServerから状態を再取得する。
  - [x] 「元画質で再生」はSize／推定通信量Confirm後だけ開始し、低／中失敗から自動実行しない。
  - [x] Retry可能な失敗だけ明示Retryを許可し、重複TapでJobを多重登録しない。
- [x] 品質変更時の再生位置維持をTest firstで実装する。
  - [x] 現在位置と再生／一時停止状態を保存し、新品質がReadyになった後にDuration内へclampして復元する。
  - [x] 新品質準備失敗または取消時は旧品質を勝手に停止・破棄せず、承認済み状態へ戻す。
  - [x] Duration差、動画末尾、Liveでないこと、Seek不能File、回転／Process再生成の境界をTestする。

### 3.4 動画・音声Player UI

- [x] 共通Player操作を実装する。
  - [x] 再生／一時停止、Seek bar、動画・音声共通の3秒戻る／進むと10秒戻る／進む、現在時間／総時間を実装する。
  - [x] 0.5、0.75、1.0、1.25、1.5、1.75、2.0、2.5、3.0倍で速度を変更し、選択値とAccessibility説明を表示する。
  - [x] Buffering、再接続中、再生終了、非対応Codec、認証失効、通信Errorを区別して表示する。
  - [x] Android audio focus、Headset切断、通話割込み、App background時の承認済みPause規則を実装する。
  - [x] 画面回転後も現在位置、速度、選択品質、再生状態を安全に復元する。
- [x] 動画固有UIを実装する。
  - [x] 映像Surface、Controller表示／非表示、Aspect ratio、全画面の承認済み挙動を実装する。
  - [x] 低／中／元品質、変換進捗、Queue、通信量ConfirmをPlayer操作と競合しないUIへ統合する。
- [x] 音声固有UIを実装する。
  - [x] Artworkがない場合の種類表示、File名、再生操作、時間、速度を表示する。
  - [x] フェーズ0で確定した音声品質契約だけを表示し、Server未対応Variantを要求しない。

### 3.5 Navigation・UI Test

- [x] File一覧／詳細から動画・音声PlayerへのNavigationを実装する。
  - [x] 対象MIMEとCodec検査結果に応じて動画／音声UIまたは非対応表示へ遷移する。
  - [x] Back、Logout、Token失効、接続先変更で再生と認証済みDataSourceを停止・破棄する。
- [x] PlayerのUnit／Instrumented Testを追加する。
  - [x] Fake Playerで再生、一時停止、Seek、動画・音声共通の±3秒と±10秒、速度、終了、Error状態をTestする。
  - [x] MockWebServerで初回Range、Seek Range、切断再開、401 refresh、416、品質変更をTestする。
  - [x] Compose Testで品質Confirm、Job状態、Retry、再接続、Codec非対応、Accessibilityを確認する。

### 3.6 PR3検証・文書・完了

- [x] PR3の自動・手動検証を完了する。
  - [x] JVM Unit Test、MockWebServer Test、Compose／Media3 Instrumented Testが成功する。
  - [x] `./scripts/ci/verify-android.sh`、`./scripts/ci/verify-config.sh`、`git diff --check`が成功する。
  - [x] 現行Android（Android 13）実機で対象動画・音声MIME、Range再生、Seek、速度、回転、background／foregroundを確認する。PR2で承認済みの端末範囲を継続し、利用可能な端末／EmulatorがないAndroid 10は今回の受け入れ対象外とする。
  - [x] Network inspectionとMockWebServer TestでSeekのRange要求を確認し、選択品質以外と生成途中MP4をPlayerへ渡さないことを確認する。
  - [x] Mobile設定の自動Testと実機回線切断／復帰でBuffer上限、切断、復帰、元画質Confirm、次動画非自動再生を確認する。Cellular＋ZeroTierの実Server到達は外部経路不通のためfail-closed表示まで確認した。
  - 実機検証記録（2026-08-30）: OPPO CPH2333 / Android 13、署名済み非Debuggable Release `0.9.0-pr3-test`で動画Low／Medium／Original、音声Original、Range再生、Seek、速度、品質変更、回転、background／foreground、Replay、回線切断／復帰を確認した。
  - UI修正記録（2026-08-30）: 初回Original確認取消後のLoading停滞、回転時の空画面、終了時のPause表示、`+10s`の縦長表示を修正し、Player操作5 Buttonが同じ48dp高であることを実機とCompose Testで確認した。
  - 詳細記録: `docs/testing/20260830-android-media-players-pr3.md`
- [x] PR3に必要な正式文書とUI mockup対応を実装と同じ変更で更新する。
- [ ] PR3を完了する。
  - [ ] フェーズ3の全項目が`[x]`であることを確認する。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR3完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ4 / PR4: 統合E2E・性能・運用仕上げ

### 4.1 開始条件

- [ ] PR4の開始条件を満たす。
  - [ ] PR3が`main`へMerge済みで、必須CIが成功している。
  - [ ] `git status`と既存差分を確認し、最新`main`からPR4用Branchを作成する。
  - [ ] PR1〜PR3の完了記録、未解決Review、実機測定結果を確認する。

### 4.2 統合E2E

- [ ] 実ServerとAndroid端末のE2Eを自動化または再現可能なScriptへ記録する。
  - [ ] 一覧で写真・動画・PDF Thumbnailを必要時生成し、再表示で再利用する。
  - [ ] 写真Low／Medium／Originalを切り替え、生成中、完了、失敗、Retry、通信量Confirmを確認する。
  - [ ] PDF複数Page、Zoom、途中切断、再取得、一時File清掃、Download遷移を確認する。
  - [ ] 動画Low／MediumのQueue、進捗、完成後自動再生、background継続、再訪、Retryを確認する。
  - [ ] 動画Originalと音声をRange再生し、Seek、速度、品質変更時位置維持、通信切断復帰を確認する。
  - [ ] Owner、直接共有、継承共有、権限消失、MISSING、Trash、File Version更新で古いMediaを表示・再生しない。
  - [ ] Logout、Device失効、Session失効、接続先変更後に前UserのThumbnail、PDF、Player、Job状態を再利用しない。

### 4.3 性能・Resource・通信量検証

- [ ] 基準環境で性能目標を測定する。
  - [ ] 1,000件Folderの一覧を段階表示し、Thumbnail request数、Scroll frame、Memory、Cache hit率を記録する。
  - [ ] Cache済み写真は通常2秒以内、Cache済み動画・音声は通常3秒以内に表示／再生開始することを測定する。
  - [ ] 未生成写真・動画が通常1秒以内に生成状態を返し、UIをblockしないことを測定する。
  - [ ] 巨大写真、256MiB境界PDF、長時間動画でOOM、ANR、Main thread I/O、FileDescriptor leakがないことを確認する。
  - [ ] LOCAL_DIRECT、REMOTE_SECURE、Mobile相当で選択品質ごとの実通信Byteと表示推定値を比較し、差の理由を記録する。
  - [ ] Battery、Data usage、Player buffer、Coil cache、PDF一時容量の運用値を承認済み設計へ反映する。

### 4.4 回帰・Security・Accessibility

- [ ] 回帰とSecurity確認を完了する。
  - [ ] File一覧、検索、最近使用、お気に入り、Tag、共有、Download、Trash、Restoreの既存導線が壊れていない。
  - [ ] URL／Header／Log／Crash report／一時File名にToken、物理Path、他User情報が含まれない。
  - [ ] 悪意あるMIME、巨大寸法、破損PDF、異常Duration、Range異常、Redirect先Host変更をfail-closedに拒否する。
  - [ ] Screenshot／外部Backup／FileProviderへの一時Media露出を承認済みSecurity方針どおり制御する。
- [ ] Accessibilityと端末差確認を完了する。
  - [ ] TalkBack、文字拡大、Dark theme、縦横画面、Gesture navigationで主要操作が利用できる。
  - [ ] Android 10、基準端末、現行Androidで画像Decoder、PDF Renderer、MediaCodec差を確認する。
  - [ ] 非対応Codecを明示し、Crashまたは無限Retryにならない。

### 4.5 最終品質ゲート・文書・完了

- [ ] 全体の品質ゲートを完了する。
  - [ ] `./scripts/ci/verify-android.sh`、`./scripts/ci/verify-server.sh`、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`git diff --check`が成功する。
  - [ ] 専用Emulator／実機Jobで全対象`connectedDebugAndroidTest`が成功する。
  - [ ] PR1〜PR4で追加した状態変換・ControllerのLine Coverage 95%以上、Android全体80%以上を満たす。
  - [ ] Release Debuggable、Network security、Dependency vulnerability、License、SBOMへの新規依存反映を確認する。
- [ ] 正式文書と運用文書を最終実装へ一致させる。
  - [ ] 5つの正式文書、対象Mockup対応表、E2E手順、対応MIME／Codec、性能測定値、既知制約を更新する。
  - [ ] 実装していない将来機能や存在しないFile／Moduleを`docs/repository-structure.md`へ記載しない。
- [ ] PR4を完了する。
  - [ ] フェーズ4の全項目が`[x]`であることを確認する。
  - [ ] 本タスクリスト全体に未完了項目がないことを確認する。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR4完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をUserへ報告して停止する。

---

## 各Pull Request完了記録

> 各Pull Request作成後に`steering`スキルのモード3-Aで追記する。後続Pull Requestに未完了タスクがあっても、完了したPull Requestの記録は行う。

### PR1: Media契約・品質設定・共通Controller基盤

- 完了日: 2026-08-30
- Pull Request: [#33 Add Android media contract and quality foundations](https://github.com/ry825/Kura_Storage/pull/33)
- 実施したTest／Build／静的解析／手動確認: `./scripts/ci/verify-android.sh`（Unit Test、MockWebServer Test、ktlint、detekt、lint、Debug APK、Appと新規FeatureのAndroidTest APK）、`./scripts/ci/verify-server.sh`（Domain 81件、Application 236件、Integration 177件）、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`git diff --check`を実施して成功。Core Dataで短いBody、上限超過、途中切断を追加確認。Coil 3.5.0とMedia3 1.11.0のPOMがApache-2.0であること、依存Lock、認証URL／TokenのLog出力がないことを手動確認。GitHub必須CIはAndroid、Config、Security、Serverの4件が成功。実機実行は後続Viewer／Player PRの受け入れ範囲のため未実施。
- 計画と実装の差分: AndroidがRetry操作の表示可否をfail-closedに判定できるよう、既存Server Media Job応答に`retryable`を最小追加し、OpenAPIとServer Testを同時更新した。その他は承認済みPR1設計どおり。
- 実装中に追加したタスクと理由: `retryable`のServer／OpenAPI契約更新とIntegration Testを追加。理由は失敗JobのRetry可否をAndroid側で推測しないため。また、新規FeatureのAndroidTest APKをCIコンパイル対象へ追加し、承認済みCoil／Media3を旧MVP依存禁止Guardから除外した。`core-data`単体lintのため`ACCESS_NETWORK_STATE`をLibrary Manifestに宣言した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: PR #33の`main`へのMergeと必須CI成功をPR2開始条件とする。PR2では本PRの`MediaRepository`、`MediaViewerController`、品質設定、通信量確認を使用し、Coil認証Fetcher／Cache、一覧Thumbnail、写真Viewer、PDF Viewerを実装する。実Android 10／現行Androidでの実行、通信量、Memory／Leak確認はPR2以降の受け入れ項目で実施する。

### PR2: 一覧サムネイル・写真Viewer・PDF Viewer

- 完了日: 2026-08-30
- Pull Request: [#35 Add Android thumbnails, photo viewer, and PDF viewer](https://github.com/ry825/Kura_Storage/pull/35)
- 実施したTest／Build／静的解析／手動確認: `./scripts/ci/verify-android.sh`（960 tasks、Unit Test、MockWebServer Test、ktlint、detekt、lint、Debug APK）、`./scripts/ci/verify-server.sh`（Domain 81件、Application 236件、Integration 178件）、`./scripts/ci/verify-config.sh`、`git diff --check`が成功。OPPO CPH2333 / Android 13で`feature-media` Instrumented Test 8件が成功。一覧Thumbnail、写真Low／Medium／Original、Original通信量Confirm、PDF 3 Page、長いFile名、選択Variantのみの取得、StrictMode、Memory、CloseGuard／OOMがないことを実機で確認。GitHub必須CIはAndroid、Config、Security、Serverの4件が成功。
- 計画と実装の差分: 実機E2EでServerのOriginal ContentにHEAD対応がないことを検出し、GETと同じEndpointにSize／MIME／RangeのHEAD応答を追加した。長いFile名でViewer HeaderとPDF Page操作が崩れる問題、PDFのFile／Renderer操作がMain threadで実行される問題を実機検証から追加修正した。Userの受け入れ変更により、Android 13を今回の現行Android実機とし、Android 10実機は対象外とした。
- 実装中に追加したタスクと理由: Original ContentのHEAD契約修正、長いFile名のViewer Layout修正、debug StrictMode導入、PDFのFile lease／`PdfRenderer`生成をI/O dispatcherへ移動するタスクを追加。理由は通信量Confirm、主要操作、Main thread I/Oの実機E2E不具合を受け入れ条件まで修正するため。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: PR #35の`main`へのMergeと必須CI成功をPR3開始条件とする。PR3では本PRの認証付きMedia Repository、品質選択、通信量Confirm、Session scope、debug StrictModeを維持して動画／音声Playerを実装する。

### PR3: 動画・音声Playerと動画変換状態

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest／Build／静的解析／手動確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク、理由、代替実装: 未記録
- 後続Pull Requestへの引継ぎ事項: 未記録

### PR4: 統合E2E・性能・運用仕上げ

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest／Build／静的解析／手動確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク、理由、代替実装: 未記録
- 後続Pull Requestへの引継ぎ事項: 未記録

---

## 全体振り返り

> すべてのPull Requestとタスクが完了した後にだけ、`steering`スキルのモード3-Bで記録する。

### 実装完了日

未完了

### 全体の計画と実績の差分

未記録

### 主な設計変更と理由

未記録

### 技術的な学び

未記録

### プロセス上の改善点

未記録

### 次回への改善提案

未記録
