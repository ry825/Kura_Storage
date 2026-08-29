# サムネイル・派生データ・Worker基盤 タスクリスト

## 対象要件・設計

- `docs/product-requirements.md` 7.11「MVP後: 派生データとキャッシュ管理」
  - 低・中画質データの必要時生成、重複生成防止、元Versionとの整合を実現する。
  - 低・中画質キャッシュへ24時間TTL、10GB／6GBのLRU清掃、Lease保護を適用する。
  - サムネイルは通常キャッシュと分離し、元ファイルの完全削除まで保持する。
- `docs/functional-design.md` 5.4、6.2.7〜6.2.9、7.3〜7.5、8.5、18.2 Server Step 7
  - `FileDerivative`、永続ジョブ、`PreviewService`、独立Worker、派生データ配信APIを実装する。
  - 写真・動画・PDFサムネイル、写真・動画の低・中画質派生データを生成する。
- `docs/architecture-design.md` 8.6〜8.7、11.3〜11.4、15.2
  - 派生キー、PostgreSQL永続キュー、atomic rename後だけの公開、全件サムネイル保持を実装する。

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

- [x] 対象をServer側の独立Worker、PostgreSQL永続ジョブキュー、派生データ生成・配信、TTL／LRU清掃、Lease、元ファイル状態との連動に限定する。
- [x] Androidの写真・動画Viewer、品質選択UI、Media3／Coil統合、一覧サムネイル表示は後続Steeringとし、本作業へ含めない。
- [x] HLS、生成途中Segment配信、Web／iOS UI、音声専用変換、OCR、全文検索、AI分類、元ファイルの再圧縮・置換を追加しない。
- [x] 元ファイルを正とし、派生データの欠損・失敗・削除が元ファイル、`FileEntry`、共有、Tag、お気に入りへ影響しない境界を維持する。
- [x] 写真・動画・PDFサムネイルはTTL／10GB上限の対象外、写真・動画の低・中画質だけを通常キャッシュ清掃対象とする。
- [x] API Process内でMedia変換を実行せず、写真・動画・PDFの全生成を既存`KuraStorage.Worker` Generic Hostへ分離する。

---

## フェーズ0: 実装前の要求・設計確定

> 本タスクリストは正式文書から実装順序を作成したもの。専用の要求・設計文書を承認するまでPR1を開始しない。

- [x] `.steering/20260829-thumbnail-derivative-worker-infrastructure/requirements.md`を`steering`スキルのモード1で作成し、Userの承認を得る。
  - [x] 対象MIME、サムネイル寸法・形式・品質、写真低中画質Profile、動画Profile、PDF対象Pageを確定する。
  - [x] 写真・サムネイルの同期待機閾値と、閾値超過後に独立Workerへ引き継ぐ永続方式を確定する。
  - [x] 生成要求、状態照会、再試行、Range配信、認証・認可、HTTP Status、Error Codeを確定する。
  - [x] Retry上限、Backoff、Heartbeat、stale `RUNNING`回収、失敗分類、Job保持期間を確定する。
  - [x] Lease期間、更新間隔、Process異常終了時の失効、配信開始・終了と清掃の競合規則を確定する。
  - [x] Thumbnail容量監視、30万件時のHDD容量推定、Pi実機の生成時間・CPU・Memoryを測定・記録して運用上限を確定する受け入れ手順を定義する。
- [x] `.steering/20260829-thumbnail-derivative-worker-infrastructure/design.md`を`steering`スキルのモード1で作成し、Userの承認を得る。
  - [x] `FileDerivative`、`MediaJob`、`DerivativeLease`の状態遷移、不変条件、論理Key、Profile versioningを設計する。
  - [x] EF Core Schema、Index、制約、排他取得、Heartbeat、Retry、Migration Up／Downを設計する。
  - [x] API、Application、Infrastructure、Workerの依存境界とTransaction／advisory lock境界を設計する。
  - [x] HDD上の派生Root、決定的相対Path、一時Path、atomic rename、起動時回復、Symlink拒否を設計する。
  - [x] `vips`、FFmpeg／ffprobe、`pdftoppm`の引数境界、Timeout、Process終了、出力検証を設計する。
  - [x] TTL／LRU清掃、所有者別配信／生成Lease、Purge／Trash／Restore／`MISSING`／内容更新との連動を設計する。
  - [x] Test matrix、Test fixture、Pi実機性能、障害注入、配置、Rollback、運用監視を設計する。
- [x] 正式文書内の契約差分を解消し、同じ変更で関連文書を更新する。
  - [x] 正式文書のJob状態APIを`/api/v1/media-jobs/{jobId}`へ統一し、`/api/v1/transcode-jobs/{jobId}`を除去する。
  - [x] 正式文書の動画専用`TranscodeJob`／`transcode_jobs`を、全派生種別の`MediaJob`／`media_jobs`へ更新する。
  - [x] 正式文書へ`CANCELLED`、Retry、stale Job回収時のDerivative／Job状態対応を反映する。
  - [x] 正式文書へThumbnailの必要時生成後の完全削除まで保持、長辺512px、WebP品質75を反映する。
- [x] 承認済み`requirements.md`と`design.md`に合わせて本タスクリストを再確認する。
  - [x] API名、状態、制約値、Package、設定Key、確認コマンドを具体化する。
  - [x] PR1〜PR4の依存関係と完了条件が承認済み設計を過不足なく覆うことを確認する。
  - [x] 正式文書との矛盾が残っていないことを確認する。

---

## フェーズ1 / PR1: 派生データModel・永続ジョブキュー・Storage基盤

### 1.1 開始条件と既存実装確認

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0の全項目が`[x]`で、`requirements.md`と`design.md`が承認済みである。
  - [x] 先行Pull Requestが`main`へMerge済みで、必須CIが成功している。
  - [x] `git status`と既存差分を確認し、Userの変更を混在させない。
  - [x] `FileEntry.fileVersion`、Migration、Upload Session、Trash Purge、MISSING削除Participant、既存Workerの実装Patternを確認する。
  - [x] 最新`main`からPR1用Branchを作成する。

### 1.2 Domain modelと状態遷移

- [x] 派生データDomain modelをTest firstで実装する。
  - [x] `DerivativeType`へ写真・動画共通Thumbnail、PDF Thumbnail、写真Low／Medium、動画Low／Mediumを表現する。
  - [x] `FileDerivative`へSource File ID、Source Version、Type、Profile Version、相対Path、Size、Status、Access／Expiry／Lease、Error、UTC時刻を実装する。
  - [x] `(sourceFileId, sourceVersion, derivativeType, profileVersion)`を論理一意Keyとし、Rename／MoveをKeyへ含めない。
  - [x] `PENDING`、`RUNNING`、`READY`、`FAILED`、`BLOCKED_SOURCE_MISSING`、`DELETING`の許可遷移と不正遷移拒否をTestする。
  - [x] `READY`は検証済みSizeと正式相対Pathを必須とし、ThumbnailへExpiryを設定できない規則を実装する。
- [x] 永続Job Domain modelをTest firstで実装する。
  - [x] `MediaJob`へJob種別、`QUEUED`、`RUNNING`、`COMPLETED`、`FAILED`、`CANCELLED`、進捗、Queue順、試行、Heartbeat、Worker tokenを実装する。
  - [x] 同一Derivativeの有効Job重複、完了済みJobの再実行、不正な進捗・時刻遷移を拒否する。
  - [x] 初回を含む実行上限3回、30秒／2分Backoff、2分stale判定、terminal Job 7日保持をTestする。
  - [x] Server時刻とIDをServer側で生成し、Client指定User、Owner、Path、Profileを信頼しない。
- [x] 所有者別Lease Domain modelをTest firstで実装する。
  - [x] `DerivativeLease`へ`GENERATION`／`DELIVERY`、Owner token、Expiry、UTC時刻を実装する。
  - [x] 同一所有者Leaseの取得・更新・解放、複数配信Lease、期限切れ回収、不正所有者更新拒否をTestする。
  - [x] `FileDerivative.leaseUntil`をactive Lease最大期限の保守的投影として扱い、削除可否はLease行の存在で判定する。

### 1.3 PostgreSQL永続化と排他Queue

- [x] EF Core mappingとMigrationを実装する。
  - [x] `file_derivatives`へFK、状態Check、非負Size、論理Key unique、清掃・Lease・Source検索Indexを追加する。
  - [x] `media_jobs`へDerivative／要求Actor FK、状態・時刻・進捗・Retry・Worker token、Queue Index、有効Job partial uniqueを追加する。
  - [x] `derivative_leases`へDerivative FK、種別、Owner token、Expiry、一意制約、Cleanup Indexを追加する。
  - [x] `KuraStorageDbContext`へ3つの`DbSet`とmappingを追加し、既存Naming規約とUTC規約に従う。
  - [x] Migration Up、Down、再Up、model snapshot一致、既存データ非破壊を統合Testする。
- [x] PostgreSQL Queue repositoryをTest firstで実装する。
  - [x] `QUEUED`を`created_at`、IDの安定順で`FOR UPDATE SKIP LOCKED`取得し、同一Transactionで`RUNNING`へ変更する。
  - [x] 複数Worker競合時も同じJobを二重取得せず、Queue順と動画同時実行数1を守る。
  - [x] `LISTEN/NOTIFY`を使用する場合も通知消失後のPollingで必ず処理を再開できる。（PR1では`LISTEN/NOTIFY`を使用せず、PostgreSQL pollingを処理の正とする）
  - [x] Heartbeat更新、進捗更新、完了、失敗、Retry、stale `RUNNING`回収を条件付き更新で実装する。
  - [x] API／Worker再起動、DB接続断、更新結果不明、並行Retryで状態を壊さないTestを追加する。

### 1.4 派生Storageと設定

- [x] 派生データStorage境界をTest firstで実装する。
  - [x] `derivatives/<owner>/<source>/<version>/<profile>/<type>.<ext>`と`derivative-temp/<job>/<attempt>.part`だけをServer側で生成する。
  - [x] Path traversal、absolute path、Symlink、特殊File、Root外renameを拒否する。
  - [x] 一時File作成、Streaming read／write、Flush、出力検証、同一Filesystem内atomic rename、限定削除を実装する。
  - [x] HDD未Mount、Storage ID不一致、read-only、容量不足時にOS Rootへ書き込まず、元ファイルを変更しない。
  - [x] 同一正式Path競合、HDD成功後DB失敗、DB成功前Process停止の回復規則を統合Testする。
- [x] 型付きOptionsと起動時Validationを実装する。
  - [x] 派生Root、一時Root、2秒待機、500ms Polling、Profile version、10秒Heartbeat、3回Retry、2分stale／Lease、Cleanup周期、TTL、Watermark、7日Job保持を設定化する。
  - [x] 既定24時間、10GiB、6GiB、全Media直列・動画並列数1を表し、負値、Low≧High、危険なPath、不正な時間関係を起動時に拒否する。
  - [x] API、Worker、Production template、environment exampleへ同じ非秘密設定を追加する。

### 1.5 元ファイルLifecycle連携

- [x] 既存File操作との連動境界をTest firstで実装する。
  - [x] Rename／MoveではSource Versionを変えず、既存派生データとThumbnailを再利用する。
  - [x] 内容更新でSource Versionが増え、旧Versionを配信せず、新Versionを必要時生成する。
  - [x] Trash移動で低・中画質を削除対象にし、Thumbnailは保持する。
  - [x] Restoreで保持中Thumbnailを再利用し、低・中画質を必要時再生成する。
  - [x] `MISSING`確定で派生データを`BLOCKED_SOURCE_MISSING`へ変更し、正式ファイルの代替として配信しない。
  - [x] `MISSING`一覧削除とPermanent DeleteへParticipantを登録し、全派生物理Fileと管理行を対象Treeだけから削除する。
  - [x] Lifecycle操作と生成／配信Leaseの競合をadvisory lockと再読込で安全に直列化する。

### 1.6 PR1検証・文書・完了

- [x] PR1の自動検証を完了する。
  - [x] Domain／Applicationの状態遷移・Validation境界Line Coverage 95%以上、全体80%以上を満たす。（Media Domain 95.89%、Server全体94.77%）
  - [x] Queue競合、Migration、Storage境界、Lifecycle連動の統合Testが成功する。
  - [x] `./scripts/ci/verify-server.sh`、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`git diff --check`が成功する。
- [x] PR1に必要な正式文書を実装と同じ変更で更新する。
  - [x] Domain、Schema、Queue、Storage、設定、Lifecycleの確定内容を5つの正式文書と運用文書へ反映する。
  - [x] `docs/repository-structure.md`へ実際に追加した配置だけを反映する。
  - [x] Migration適用、Backup、Rollback、stale Job回収、派生Root保護を運用手順へ記載する。
- [x] PR1を完了する。
  - [x] フェーズ1の全項目が`[x]`であることを確認する。
  - [x] 差分に変換Engine本体、Android UI、不要Package、実環境値、Credentialがないことをセルフレビューする。
  - [x] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR1完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ2 / PR2: Thumbnail・写真派生生成・API配信・Lease

### 2.1 開始条件

- [x] PR2の開始条件を満たす。
  - [x] PR1が`main`へMerge済みで、必須CIが成功している。
  - [x] `git status`と既存差分を確認し、最新`main`からPR2用Branchを作成する。
  - [x] 既存File Content Range、Authorization、Storage Stream、OpenAPI Test、Worker ScopeのPatternを確認する。
  - [x] Pi／CIで`vips`、FFmpeg／ffprobe、`pdftoppm`の実行Path、Version、Loader／Encoder、arm64対応を確認する。（PiはDebian 12 arm64、FFmpeg／ffprobe 5.1.6、Poppler 22.12.0を確認。`libvips-tools`は未導入、CIはPopplerのみのためPR2配置更新で導入・検証する）

### 2.2 画像・PDF・動画Thumbnail生成

- [x] Thumbnail生成をTest firstで実装する。
  - [x] 承認済み対象MIMEだけをServer側で許可し、拡張子だけを信頼せずDecoder／Probe結果を検証する。
  - [x] `IMediaProcessRunner`で引数配列、absolute Binary、allow-list Environment、1MiB bounded出力、Timeout、Process tree終了を実装する。
  - [x] `vips`で写真をEXIF回転反映・Animated先頭Frame・長辺最大512px・WebP品質75・拡大なしに変換する。
  - [x] FFmpeg／ffprobeで動画Durationの10%かつ最大10秒地点からFrameを抽出し、Decode可能Frameへ安全にFallbackする。
  - [x] `pdftoppm`でPDF先頭PageだけをRaster化し、`vips`で長辺最大512px・WebP品質75へ変換する。
  - [x] Binary／Loader不足、暗号化PDF、巨大Document、Timeoutを`MEDIA_TOOL_UNAVAILABLE`または承認済み生成Errorへ変換する。
  - [x] Decompression bomb、巨大Dimension、破損入力、Unsupported codec、Timeout、取消、Process異常終了をTestする。
  - [x] 一時出力の検証とatomic rename後だけ`READY`にし、失敗・取消時に部分Fileを公開しない。
  - [x] 同一論理Keyへの並行要求を一意制約とJob取得で1回の生成へ収束させる。
  - [x] Thumbnailへ`expiresAt`を設定せず、通常Cache容量集計から除外する。

### 2.3 写真Low／Medium生成

- [x] 写真派生生成をTest firstで実装する。
  - [x] Lowを長辺最大1280px・WebP品質70、Mediumを長辺最大2560px・WebP品質82で生成し、小さい元画像を拡大しない。
  - [x] EXIF回転、色Profile、Alpha、Animated imageの承認済み規則を実装する。
  - [x] 全写真生成をWorkerで実行し、APIは500msごと最大4回・合計2秒だけDB状態を待機して、超過時に`202 Accepted`を返す。
  - [x] API要求取消や画面離脱後も永続Jobを取消さず、Workerが完了または失敗まで処理する。
  - [x] 完成時にSize、`lastAccessedAt`、`expiresAt = now + 24h`を保存する。
  - [x] 元Source VersionまたはProfile Versionが変わった結果を現在要求へ誤公開しない。

### 2.4 PreviewService・API・認可

- [x] `PreviewService`とAPI契約をTest firstで実装する。
  - [x] `GET /api/v1/files/{fileId}/content`の`thumbnail`、`image-low`、`image-medium`を実装する。
  - [x] `READY`かつ現Source／Profile一致・物理File検証済みの場合だけ`200`／単一Range `206`で配信する。
  - [x] 未生成・生成中は正式契約の`202 Accepted`、Job ID、状態URL、Retry-Afterを返し、元画質へ自動Fallbackしない。
  - [x] 不正Rangeを`416 RANGE_NOT_SATISFIABLE`、非対応Variant／MIME、失敗、Source missingを承認済みErrorへ変換する。
  - [x] 最新`FileEntry.name`からRFC 5987のDownload名を生成し、物理Pathや生成時名称を公開しない。
  - [x] Owner／直接共有／継承共有の`VIEWER`以上だけに許可し、AdminやID列挙へ暗黙の閲覧権限を与えない。
  - [x] Job状態照会・Retryで要求Actorではなく現在のFile閲覧権限とSource状態を再評価する。
  - [x] `GET /api/v1/media-jobs/{jobId}`と`POST /api/v1/media-jobs/{jobId}/retry`を実装し、他User／非認可Jobを`404`へ正規化する。
  - [x] OpenAPI、Request／Response DTO、Error、未知enumの契約Testを追加する。

### 2.5 配信・生成Lease

- [x] LeaseをTest firstで実装する。
  - [x] 生成権取得時に2分の所有者別`GENERATION` Leaseを設定し、Heartbeatで更新して完了・失敗時に解放する。
  - [x] `LeasedFileResult`で配信直前に2分の`DELIVERY` Leaseを取得し、Stream終了・取消・例外時に所有行だけを解放する。
  - [x] 64KiB Range配信中は30秒ごとに同じOwner tokenのLeaseを更新し、Cleanupが使用中Fileを削除できないことをTestする。
  - [x] Process異常終了後は期限切れLeaseだけが回収され、現行Leaseを別Processが奪わない。
  - [x] Lease取得後にSource状態・Version・権限・Derivative状態を再読込し、競合時はfail-closedにする。

### 2.6 PR2検証・文書・完了

- [x] PR2の自動・結合検証を完了する。
  - [x] 画像／PDF／動画Thumbnail、写真Low／Mediumのgolden／metadata／破損入力Testが成功する。
  - [x] 同時要求、待機閾値、API取消、Worker継続、Range、認可、Lease競合の統合Testが成功する。
  - [x] 実処理Library／実行Programを使用するTestと、失敗注入後の一時File・DB整合確認が成功する。
  - [x] Coverage基準と全Server／Config／Security／Deployment CI、`git diff --check`が成功する。
- [x] PR2に必要な正式文書、OpenAPI、依存関係、SBOM、運用設定を更新する。
  - [x] Debian 12の`libvips-tools`、`ffmpeg`、`poppler-utils`とBinary／Loader検証をinstall／upgrade／rollback／verify手順へ追加する。
  - [x] `Media`設定、`derivatives`／`derivative-temp`作成、systemd hardeningとStorage限定writeを配置Templateへ反映する。
- [x] PR2を完了する。
  - [x] フェーズ2の全項目が`[x]`であることを確認する。
  - [x] 差分にAndroid UI、動画Low／Medium変換本体、HLS、秘密情報、未使用Packageがないことを確認する。
  - [x] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR2完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ3 / PR3: 動画Low／Medium変換Workerと進捗・回復

### 3.1 開始条件

- [x] PR3の開始条件を満たす。
  - [x] PR2が`main`へMerge済みで、必須CIが成功している。
  - [x] `git status`と既存差分を確認し、最新`main`からPR3用Branchを作成する。
  - [x] PiのFFmpeg／ffprobe version、codec、実行User権限、HDD一時領域、systemd制約を確認する。（Debian 12.9 arm64、FFmpeg／ffprobe 5.1.6、libx264／AAC／`-progress`対応、`kurastorage-api`のTool実行／Storage書込権限、HDD空き約773GB、MemoryMax 3GB／CPUWeight 50／IOWeight 50／TasksMax 128／TimeoutStopSec 45秒を確認）

### 3.2 FFmpeg Process境界

- [x] FFmpeg／ffprobe AdapterをTest firstで実装する。
  - [x] Shell文字列連結を使わずArgument listへ固定Profileと検証済みPathだけを渡す。
  - [x] Lowを最大720p・H.264・約1.5Mbps・AAC 96kbps・最大30fpsで生成する。
  - [x] Mediumを最大1080p・H.264・約4Mbps・AAC 128kbps・最大30fpsで生成する。
  - [x] 縦横比を維持し、小さい元動画を拡大せず、完成済みMP4として出力する。
  - [x] Duration、codec、stream、dimension、container、終了Code、出力Sizeを検証する。
  - [x] 進捗出力から処理済み時間とPercentを安全に解析し、算出不能時はPercentを省略する。
  - [x] stdout／stderrをboundedに処理し、File名、物理Path、User情報を通常Log／Metric labelへ出さない。
  - [x] Timeout、取消、SIGTERM、異常終了時に子Processを終了し、部分出力を公開しない。

### 3.3 MediaGenerationWorkerの動画処理

- [x] 独立Workerの動画処理LoopをTest firstで実装する。
  - [x] PostgreSQL Queueから1件を排他取得し、Pi上の動画変換同時実行数を必ず1に制限する。
  - [x] Job取得後にSource `ACTIVE`、Version、権限不要のSystem処理条件、Storage状態、Derivative状態を再検証する。
  - [x] 元動画をRead-onlyで開き、同一Filesystemの一時MP4へ全体生成する。
  - [x] Heartbeatと進捗を条件付き更新し、古いWorkerが回収後のJob状態を上書きできないようにする。
  - [x] ffprobe検証、durable flush、atomic rename、`READY`／`COMPLETED`更新の順序を守る。
  - [x] Retry可能失敗を30秒／2分Backoff付き`QUEUED`、恒久失敗を`FAILED`へ移し、初回を含む3回上限を守る。
  - [x] Worker終了時に新規取得を停止し、猶予内終了または安全なstale回収へ移行する。

### 3.4 動画APIと状態管理

- [x] 動画派生APIをTest firstで実装する。
  - [x] `video-low`／`video-medium`要求で、存在しないDerivativeとJobを一意・冪等に作成する。
  - [x] `QUEUED`／`RUNNING`では`202`とJob状態URL、Queue位置、進捗、Retry待機を返す。
  - [x] 正式Job状態APIで`GENERATING`、`READY`、`FAILED`と承認済みErrorを返す。
  - [x] Retry APIは現在権限、Source、Version、Retry可否を再確認し、同時Retryを1件へ収束させる。
  - [x] `READY`動画だけを`200`／`206`でRange配信し、生成途中・検証前・失敗出力を配信しない。
  - [x] 元画質は`variant=original`の明示要求だけで配信し、Low／Medium要求から自動Fallbackしない。
  - [x] 画面離脱、HTTP取消、API再起動がWorker Jobを取消さないことを統合Testする。

### 3.5 回復・観測性・配置

- [x] Worker再起動と障害回復を実装する。
  - [x] 起動時と1分周期で2分staleの`RUNNING`、期限切れ生成Lease、DB候補に対応する一時Fileだけを回収する。
  - [x] DB停止、HDD切断、Storage read-only、容量不足、Process kill、Pi再起動、atomic rename後DB失敗を再現して安全に収束させる。
  - [x] Source内容更新、Trash、Purge、`MISSING`と変換完了の競合で古い結果を`READY`にしない。
- [x] Metricと構造化Logを追加する。
  - [x] Queue深さ、最古待機時間、実行中数、成功／失敗／Retry、変換時間、出力Bytes、stale回収を低Cardinalityで記録する。
  - [x] Job ID、File ID、Path、File名、User名をMetric labelへ含めず、必要な相関IDだけを機密情報なしで構造化Logへ記録する。
  - [x] Health／運用確認でAPIとは独立したWorker停止・Queue滞留・FFmpeg利用不可を識別できるようにする。
- [x] systemd／配置を更新する。
  - [x] Worker serviceの実行User、WorkingDirectory、EnvironmentFile、Restart、Timeout、Memory／CPU、Hardeningを更新する。
  - [x] FFmpeg／ffprobeの実Version、Codec、Profile、進捗出力をPiのinstall／verify／upgrade／rollback手順で確認する。
  - [x] API起動中にWorkerだけ停止・更新・Rollbackでき、WorkerへHTTP Listenerを追加していないことを確認する。

### 3.6 PR3検証・文書・完了

- [x] PR3の自動・実Process検証を完了する。
  - [x] 動画Profile、Probe、進捗、Queue順、単一並列、Retry、stale回収、Range、認可のTestが成功する。
  - [x] 短尺・長尺、縦動画、音声なし、複数音声、破損、unsupported codec、巨大metadataのFixtureを確認する。
  - [x] Worker／API／DB／HDDの各異常終了後に、部分出力非公開・元File不変・Job回復を確認する。
  - [x] Coverage基準と全Server／Config／Security／Deployment CI、`git diff --check`が成功する。
- [x] PR3に必要な正式文書、OpenAPI、配置、運用、Security、Test fixture出典を更新する。
- [x] PR3を完了する。
  - [x] フェーズ3の全項目が`[x]`であることを確認する。
  - [x] 差分にAndroid UI、HLS、任意Command実行、実環境Path、Credential、著作権不明Fixtureがないことを確認する。
  - [x] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR3完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ4 / PR4: TTL・LRU清掃、Pi性能・障害E2E、運用完成

### 4.1 開始条件

- [ ] PR4の開始条件を満たす。
  - [ ] PR3が`main`へMerge済みで、必須CIが成功している。
  - [ ] `git status`と既存差分を確認し、最新`main`からPR4用Branchを作成する。
  - [ ] Production相当DB／StorageのBackup、復元可能性、空き容量、Storage ID、Service状態を確認する。

### 4.2 TTL・LRU清掃

- [ ] `CacheCleanupService`をTest firstで実装する。
  - [ ] 30分ごとに100件Batchで期限切れの写真・動画Low／Mediumだけを検索する。
  - [ ] `READY`かつ`expiresAt <= now`、Lease失効済みの候補だけを安定したLRU順で処理する。
  - [ ] `PENDING`、`RUNNING`、`DELETING`、有効Lease、Thumbnail／PDF Thumbnailを除外する。
  - [ ] 配信時に`lastAccessedAt`と`expiresAt = now + 24h`を競合安全に更新する。
  - [ ] 通常Cache合計が10GBを超えた場合だけ追加LRU清掃し、6GB以下まで継続する。
  - [ ] 候補を`DELETING`へ条件付き遷移し、物理削除後に管理行を削除する。
  - [ ] 削除失敗は元ファイルへ影響させず、状態を再試行可能に戻して次回清掃で処理する。
  - [ ] 大量候補をMemoryへ全保持せず、Batchごとに容量を再計算し、Worker間競合で二重削除しない。
- [ ] Cleanup Workerを既存独立Workerへ追加する。
  - [ ] 起動時と設定周期で実行し、停止Token、失敗Backoff、Scope生成、構造化Logの既存Patternへ従う。
  - [ ] Cleanup同時実行を排除し、変換Worker、Trash Purge、MISSING削除、配信Leaseとの競合をTestする。
  - [ ] 清掃候補数、削除数／Bytes、残容量、失敗、所要時間を低Cardinality Metricへ記録する。
- [ ] Media Job履歴Cleanupを実装する。
  - [ ] 1日ごとに完了・失敗・取消から7日を超えたterminal Jobだけを100件ずつ削除する。
  - [ ] `QUEUED`、`RUNNING`、現行Retry参照中Jobを削除せず、Derivative状態と履歴削除を分離する。
  - [ ] Job履歴Cleanupの同時実行、Worker再起動、DB失敗後の再試行をTestする。

### 4.3 性能・容量・耐久E2E

- [ ] Raspberry Pi 4実機で生成性能とResource上限を測定する。
  - [ ] 写真Thumbnail／Low／Medium、動画Thumbnail／720p／1080p、PDF Thumbnailの代表Fixtureで時間、CPU、Memory、I/O、出力Sizeを記録する。
  - [ ] 動画変換が同時1件でQueue順を守り、API latencyと既存Worker処理を許容範囲に保つことを確認する。
  - [ ] 30万件のThumbnail推定容量と実測sampleを記録し、HDD容量計画・監視閾値を確定する。
- [ ] TTL／Watermark／Lease E2Eを完了する。
  - [ ] 23:59:59、24:00:00、時刻境界、Access更新、期限切れ削除をServer UTCで確認する。
  - [ ] 10GB以下では容量清掃せず、10GB超過時はLRU順で6GB以下まで削除する。
  - [ ] Thumbnailを保持し、配信中・生成中・削除中・有効LeaseのCacheを削除しない。
  - [ ] API／Worker再起動、DB切断、HDD切断、容量不足、削除失敗後に再試行して整合状態へ戻る。
- [ ] Lifecycle／認可／経路E2Eを完了する。
  - [ ] Owner、直接共有、継承共有、未共有、権限変更・解除で生成、状態照会、Retry、配信が正しく許可・拒否される。
  - [ ] Rename／Moveでは再利用し、内容更新では旧版を配信せず、Trash／Restore／Purge／`MISSING`で保持・削除規則を守る。
  - [ ] LANとZeroTierで同じHTTPS Hostname、TLS、認証、202／状態照会／Range契約が機能する。
  - [ ] 元画質への暗黙Fallback、生成途中公開、他User情報漏えい、Path漏えいがない。

### 4.4 回帰・Security・運用確認

- [ ] 既存機能の回帰を確認する。
  - [ ] 一覧、詳細、Search、Recent、Favorites／Tags、共有、Upload、Download、Rename、Move、Trash、Restore、Purge、MISSINGが従来どおり動作する。
  - [ ] 元FileのSize、SHA-256、File ID、Owner、`fileVersion`が派生生成・清掃だけでは変更されない。
  - [ ] API、Worker、Nginx、PostgreSQL LogへFile名、物理Path、User名、検索語、Token、変換Command全文が漏れない。
- [ ] Release／Rollbackを確認する。
  - [ ] `./scripts/ci/build-release.sh`でlinux-arm64 Server成果物とSBOMを生成する。
  - [ ] Migration、API、Worker、`libvips-tools`／FFmpeg／Poppler依存の適用順、サービス停止境界、Rollback、DB／Storage復元を確認する。
  - [ ] Worker停止中も元File APIが利用でき、再開後に永続Queueの処理を再開する。
  - [ ] 全必須CI、Migration、性能、障害注入、Pi実機E2Eが最終HEADで成功する。

### 4.5 文書・清掃・PR4完了

- [ ] 正式文書と実装を最終整合する。
  - [ ] 5つの正式文書、Steering、OpenAPI、Migration、Server、Worker、配置、運用・Test記録を一致させる。
  - [ ] Queue監視、stale回収、Cache容量、Thumbnail容量、Lease、FFmpeg失敗、HDD障害のRunbookを追加する。
  - [ ] 実測したProfile、性能、Resource消費、容量推定、障害注入、Rollback結果を`docs/testing/`へ機密情報なしで記録する。
- [ ] E2E環境を安全に清掃する。
  - [ ] 限定識別子で作成したTest File、Derivative、Job、一時Fileだけを削除する。
  - [ ] 実User、実File、実共有、Backup、資格情報、他機能の管理情報を削除しない。
  - [ ] 孤立Derivative／Job／一時File、stale Lease、未完了Test Jobが0件で、全ServiceとStorageが正常である。
- [ ] 全体差分をセルフレビューする。
  - [ ] N+1、要求単位HDD全走査、無制限Query／Memory保持、長期認可Cache、Client-only認可がない。
  - [ ] 元File変更、Root外Path、Shell injection、Symlink追跡、部分出力公開、Lease無視、Job二重実行がない。
  - [ ] Android UI、HLS、Web／iOS、OCR、AI分類、不要Package、生成物、実環境値、Credentialがない。
- [ ] PR4を完了する。
  - [ ] フェーズ4の全項目が`[x]`であることを確認する。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR4完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をUserへ報告して停止する。

---

## 各Pull Request完了記録

各Pull Request作成後に`steering`スキルのモード3-Aを使用して追記する。対象Pull Request内のタスクがすべて完了するまで記録しない。

### PR1: 派生データModel・永続ジョブキュー・Storage基盤

- 完了日: 2026-08-29
- Pull Request: [#29 Add media derivative persistence and storage foundation](https://github.com/ry825/Kura_Storage/pull/29)
- 実施したTest・Build・静的解析: `./scripts/ci/verify-server.sh`（Domain 81件、Application 185件、Integration 131件）、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`git diff --check`が成功した。CoverletによるMedia Domain Line Coverageは95.89%、Server全体Line Coverageは94.77%。GitHub必須CIのServer、Android、Config、Securityもすべて成功した。
- 手動確認・性能確認: Migration Up／Down／再Up、Queue競合・安定順・DB接続断後の再開、Storageの限定Path・atomic publish・HDD異常、Source Lifecycle連動をTestcontainersと実Filesystemの統合Testで確認した。PR1は変換Engineを実行しないためPi実機の変換性能確認はPR2以降へ引き継ぐ。
- 計画と実装の差分: Queueの正は計画どおりPostgreSQL pollingとし、任意だった`LISTEN/NOTIFY`は導入しなかった。Source Version、Trash、`MISSING`の派生状態連動は、既存File操作を個別改修する代わりに同一Transactionで必ず動作するPostgreSQL triggerへ集約した。Productionの直列実行数もenvironment templateから明示設定する形へ確定した。
- 実装中に追加したタスクと理由: Coverage計測で未到達だった不正Expiry、Access更新、Error code、Lease生成・解放後更新、3回目Backoff境界Testを追加し、95%基準を満たした。最終設定監査で全Media Optionsをenvironment exampleへ揃えるため、Media／動画同時実行数の展開・Shell Validationを追加した。
- 技術的に不要になったタスク・理由・代替実装: `LISTEN/NOTIFY`は待機短縮の任意機構であり、PR1では通知消失を考慮不要な500ms pollingを処理の正としたため不要。変換Binary実行、Hosted generation、HTTP API、配信・清掃はPR1の基盤範囲外で、後続PRの永続QueueとStorage境界から実装する。
- 後続Pull Requestへの引継ぎ事項: PR #29のMergeと必須CI成功をPR2開始条件とする。PR2ではPi／CI上の`vips`、FFmpeg／ffprobe、`pdftoppm`実体を先に検証し、Thumbnail・写真生成Runner、Media API、生成／配信Lease、認可、Range配信を今回のQueue・Storage・Lifecycle境界へ接続する。PR1のMigration DownはMedia行作成後に破壊的となるため、運用文書のBackup／Rollback手順を維持する。

### PR2: Thumbnail・写真派生生成・API配信・Lease

- 完了日: 2026-08-29
- Pull Request: [#30 Add media thumbnail generation and leased delivery](https://github.com/ry825/Kura_Storage/pull/30)
- 実施したTest・Build・静的解析: `./scripts/ci/verify-server.sh`（Build warning 0、Domain 81件、Application 219件、Integration 166件）、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`dotnet format --verify-no-changes`、OpenAPI YAML parse、`git diff --check`が成功した。CoverletのDomain／Application合算Line Coverageは88.51%（Domain 91.81%、Application 87.59%）、PR2追加境界は`MediaContracts.cs` 98.11%、`MediaJobRunner.cs` 99.02%、`PreviewService.cs` 95.18%。GitHub必須CIのServer、Android、Config、Securityもすべて成功した。
- 手動確認・性能確認: Piを読取専用で確認し、Debian 12 arm64、FFmpeg／ffprobe 5.1.6、Poppler 22.12.0、`libvips-tools` 8.14.1候補とWebP／H.264 codecを記録した。使い捨てUbuntu 26.04 containerで実`vips`・`ffmpeg`・`ffprobe`・`pdftoppm`を使い、画像・PDF・動画Thumbnailと破損入力を確認した。Config／Deployment文法は検証用containerでsystemd、nftables、Nginxまで確認し、一時container／imageは完了後に削除した。Pi実機の変換性能測定は計画どおりPR4で実施する。
- 計画と実装の差分: 公開契約とProfileは計画どおり。実libvips検証により、回転は`thumbnail`の既定動作を使い、出力Optionをファイル名に付与する正式CLI形式へ確定した。並行Delivery Leaseの最大期限projectionはEF追跡更新ではなく、所有者別行を競合安全に扱う限定SQLへ変更した。元Fileの省略時Download動作は後方互換を維持し、明示`disposition=inline`のみ新契約を適用した。
- 実装中に追加したタスクと理由: 実ツールのCLI互換性Test、配信StreamのDispose失敗後もLeaseを解放するTest、Workerの生成権喪失・保存障害・取消・予期しない例外・Heartbeat拒否Test、待機中READY遷移とJob終端状態Testを追加した。理由は実行Program差異、例外後のLease／部分File整合、95%追加境界Coverageを実際の失敗境界で保証するため。
- 技術的に不要になったタスク・理由・代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: PR #30のMergeと必須CI成功をPR3開始条件とする。PR3では今回の`IMediaProcessRunner`、永続Queue、GENERATION Lease、atomic publishを再利用して動画Low／Medium、進捗、Retry／stale回復を実装する。PR4へはThumbnailを除外した24時間TTL／LRU清掃、Pi実機性能・容量・障害E2E、パッケージインベントリの運用確認を引き継ぐ。

### PR3: 動画Low／Medium変換Workerと進捗・回復

- 完了日: 2026-08-29
- Pull Request: [#31 Add resilient video transcoding and recovery](https://github.com/ry825/Kura_Storage/pull/31)
- 実施したTest・Build・静的解析: `./scripts/ci/verify-server.sh`（Build warning 0、Domain 81件、Application 231件、Integration 175件）、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`dotnet format --verify-no-changes`、OpenAPI YAML parse、`git diff --check`が成功した。Coverlet Line CoverageはServer全体94.76%、PR3追加境界は`MediaContracts.cs` 96.83%、`MediaJobRunner.cs` 97.32%、`PreviewService.cs` 95.21%。GitHub必須CIのServer、Android、Config、Securityもすべて成功した。
- 手動確認・性能確認: Piを読取専用で確認し、Debian 12 arm64、FFmpeg／ffprobe 5.1.6、libx264／AAC／`-progress`対応、Worker実行UserのTool／HDD権限、HDD空き約773GB、systemd制約を記録した。開発Hostでは`/tmp`に展開したFFmpeg／ffprobe 8.0.1を使い、短尺・横・音声なしLowと長尺・縦・複数音声Mediumの実MP4変換・Probeを確認した。Pi実機性能・容量受け入れは計画どおりPR4で実施する。
- 計画と実装の差分: Retry仕様に「3回上限」と3つのBackoffが併記される矛盾があったため、現行実装に合わせて初回を含む最大3回、自動Retry 2回、30秒／2分に正式文書とSteeringを統一した。atomic rename後のDB更新結果不明で正式Fileを誤削除しないため、正式Pathの再検出と`MEDIA_COMPLETION_UNKNOWN`再試行を追加した。
- 実装中に追加したタスクと理由: 実FFmpegの縦動画・複数音声Profile Test、API再起動後のJob永続Test、5秒単位の進捗合流Test、Heartbeat例外時にWorker Loopを停止せずJobだけを再試行するTest、低Cardinality Metric Testを追加した。実行Program差、API／Worker生命周期、古いWorkerの上書き、運用監視、追加境界Coverage 95%を保証するため。
- 技術的に不要になったタスク・理由・代替実装: 8分Backoffは初回を含む3回上限では到達不可能であり、未使用分岐を残すと契約と実行回数が再び乖離するため除去した。代替は30秒／2分の2回の自動Retryと、権限・Source・Retry可否を再検証する明示Retry API。
- 後続Pull Requestへの引継ぎ事項: PR #31のMergeと必須CI成功をPR4開始条件とする。PR4ではThumbnailを除外した24時間TTL／10GiBから6GiBへのLRU清掃、Job履歴清掃、Pi実機の性能／容量／物理障害E2E、Package inventory・Backup／Rollback・運用受け入れを完成させる。

### PR4: TTL・LRU清掃、Pi性能・障害E2E、運用完成

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・性能確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク・理由・代替実装: 未記録
- 後続作業への引継ぎ事項: 未記録

---

## 全体振り返り

PR1〜PR4、本ファイルの全タスク、各Pull Request完了記録が完了した後にだけ、`steering`スキルのモード3-Bを使用して記録する。

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
