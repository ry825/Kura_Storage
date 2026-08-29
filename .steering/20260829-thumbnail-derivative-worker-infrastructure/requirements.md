# サムネイル・派生データ・Worker基盤 要求内容

## 概要

写真・動画・PDFを快適に閲覧する後続UIの前提として、サーバー側にサムネイル、写真・動画の低中画質派生データ、PostgreSQL永続ジョブキュー、独立Worker、配信Lease、TTL／LRU清掃を実装する。

元ファイルを唯一の正として保護し、派生データは必要時に安全に生成・再生成できるCacheとして扱う。生成途中または検証未完了のFileは公開せず、API、Worker、PostgreSQL、HDDの再起動や一時障害後も処理を回復できるようにする。

## 背景

現在のKuraStorageは元ファイルのRange配信まで実装済みだが、一覧表示のたびに元画像・動画を取得する構成では通信量、Decode時間、Memory使用量が大きくなる。特にRaspberry Pi 4とAndroid端末をLAN／ZeroTier経由で利用する環境では、一覧サムネイル、写真の品質別表示、動画の再生開始を効率化するサーバー基盤が必要である。

長時間のMedia処理をAPI Processで実行すると、HTTP要求の取消やAPI再起動が変換処理へ波及し、APIの応答性と障害分離も損なう。このため、生成要求をPostgreSQLへ永続化し、HTTP Listenerを持たない既存の`KuraStorage.Worker`で処理する。

また、低・中画質Cacheを無制限に保存するとHDD容量を圧迫する。一方、一覧表示に不可欠なThumbnailは通常Cacheと同じ規則で削除すると閲覧体験が不安定になる。Thumbnailは元ファイルの完全削除まで保持し、低・中画質だけをTTLと容量上限の対象にする必要がある。

## 承認対象となる仕様決定案

正式文書には実装前に解消すべき記載差分がある。本要求文書では次を推奨案とし、User承認後に正式仕様として`docs/`へ反映する。

1. 公開Job APIは、写真・Thumbnail・動画を同じ永続Job基盤で扱える`/api/v1/media-jobs/{jobId}`へ統一する。
   - `docs/functional-design.md`に併記されている`/api/v1/transcode-jobs/{jobId}`は使用しない。
   - 動画変換だけでなく、同期待機閾値を超えた写真処理とThumbnail生成も同じ状態照会契約を使用する。
2. 内部永続Jobは汎用Media Jobとして管理し、動画だけに限定した`TranscodeJob`という概念名を置き換える。
   - Job種別で写真Thumbnail、動画Thumbnail、PDF Thumbnail、写真Low／Medium、動画Low／Mediumを区別する。
   - `FileDerivative`は生成結果、Media Jobは実行履歴とRetry状態を表す。
3. Thumbnailの「全件保持」は、全Fileを事前Batch生成する意味ではなく、最初の要求時に必要時生成し、生成済みThumbnailを元ファイルの完全削除まで保持する意味とする。
4. Thumbnailの初期Profileは、長辺最大512px、WebP品質75、縦横比維持、拡大なしとする。
   - 写真はEXIF回転を反映する。
   - Animated imageは先頭Frameを使用する。
   - 動画はDurationの10%地点を基本とし、10%地点が10秒を超える場合は10秒地点、短すぎる場合は先頭からDecode可能なFrameを使用する。
   - PDFは先頭Pageを使用する。
5. 写真生成のAPI同期待機閾値は既定2秒とし、起動時検証される設定値として変更可能にする。

## 実装対象の機能

### 1. 派生データ管理

- 元ファイルから生成される次の派生種別を`FileDerivative`として管理する。
  - 写真・動画共通Thumbnail
  - PDF Thumbnail
  - 写真Low／Medium
  - 動画Low／Medium
- 派生データの論理Keyを`sourceFileId + sourceVersion + derivativeType + profileVersion`とする。
- Rename／Moveでは`sourceVersion`を変更せず、内容が同じ派生データを再利用する。
- 元ファイルの内容変更時は`sourceVersion`を増加させ、旧Versionの派生データを現在のFileとして配信しない。
- 変換設定変更時は`profileVersion`を増加させ、旧Profileの結果を新Profileとして使用しない。
- 派生データの状態を`PENDING`、`RUNNING`、`READY`、`FAILED`、`BLOCKED_SOURCE_MISSING`、`DELETING`として永続化する。
- 一時Fileへの全体生成、出力検証、durable flush、同一Filesystem内のatomic renameが完了した後だけ`READY`へ変更する。

### 2. PostgreSQL永続ジョブキュー

- 生成要求をPostgreSQLへ永続化し、Process Memory内Queueや`LISTEN/NOTIFY`だけを処理の正にしない。
- `QUEUED` Jobを作成時刻とIDの安定順で取得する。
- Workerは`FOR UPDATE SKIP LOCKED`で1件を排他取得し、同じTransaction内で`RUNNING`へ変更する。
- 同一File、Source Version、Derivative Type、Profile Versionの有効な生成を一意制約で1件へ収束させる。
- Jobへ種別、Derivative ID、要求User、状態、進捗、Queue位置算出情報、試行回数、Heartbeat、開始・完了時刻、Error Codeを記録する。
- Retry可能失敗は上限付き指数Backoffで再Queueし、恒久的失敗または上限到達を`FAILED`とする。
- Heartbeatが途絶えた`RUNNING` Jobは、別Workerが即座に奪わず、設定済み期限超過後だけ再実行可能状態へ戻す。
- APIまたはWorkerが再起動しても、未完了JobをDBから再取得して処理を継続する。
- `LISTEN/NOTIFY`を使用する場合は待機時間短縮だけに限定し、通知消失後もPollingで処理する。

### 3. 独立Media Worker

- 既存`KuraStorage.Worker`を拡張し、Media生成とCache清掃をAPIとは独立して実行する。
- WorkerへHTTP Listenerを追加しない。
- API要求の終了、Client切断、画面離脱によって永続Jobを取消さない。
- Raspberry Pi 4 Model B（8GB RAM）では動画変換を同時1件に制限する。
- Worker停止時は新規Job取得を停止し、処理中Jobを猶予内に完了させるか、Heartbeat期限後に安全に再取得できる状態を残す。
- Job取得後に元ファイル状態、Source Version、Storage状態、Derivative状態を再確認し、古いまたは無効な生成を正式公開しない。
- API、DB、HDD、外部変換Processの一時障害を分類し、元ファイルを変更せずRetryまたは恒久失敗へ移行する。

### 4. Thumbnail生成

- 写真、動画、PDFのThumbnailを必要時に生成する。
- 対象の拡張子だけで安全と判断せず、MIME、Decoder／Probe結果、実データを検証する。
- 縦横比を維持し、元画像または抽出FrameがProfileより小さい場合は拡大しない。
- 生成途中、検証失敗、Timeout、取消、Decoder／Process異常終了の出力を配信しない。
- 同じ論理Keyへの同時要求では生成を重複実行せず、既存のJobまたは`READY`結果を返す。
- ThumbnailへTTLと通常Cache容量上限を適用せず、元ファイルの完全削除まで保持する。
- Thumbnail容量はMetricと運用確認の対象とし、30万File規模のHDD容量計画へ含める。

### 5. 写真Low／Medium生成

- Lowは長辺最大1280px、WebP品質70で生成する。
- Mediumは長辺最大2560px、WebP品質82で生成する。
- 縦横比とEXIF回転を反映し、元画像が対象長辺より小さい場合は拡大しない。
- 要求処理内で既定2秒まで生成完了を待機する。
- 待機閾値内に完成した場合は派生画像を返し、閾値を超えた場合は`202 Accepted`とJob状態URLを返す。
- HTTP要求が終了しても、永続JobはWorkerで完了または失敗まで継続する。
- Low／Mediumが未準備の場合、元画質へ自動Fallbackしない。
- 完成したLow／Mediumへ`lastAccessedAt`と`expiresAt = lastAccessedAt + 24時間`を保存する。

### 6. 動画Low／Medium生成

- Lowは最大720p、H.264、約1.5Mbps、AAC 96kbps、最大30fpsの完成済みMP4として生成する。
- Mediumは最大1080p、H.264、約4Mbps、AAC 128kbps、最大30fpsの完成済みMP4として生成する。
- 縦横比を維持し、元動画が対象解像度より小さい場合は拡大しない。
- FFmpegで一時MP4へ動画全体を変換し、ffprobeでContainer、Stream、Codec、Duration、Dimension、出力Sizeを検証する。
- 可能な場合は処理済み時間と元Durationから進捗率を算出し、Job状態として公開する。
- 出力全体の検証とatomic renameが完了するまで動画をRange配信しない。
- 動画Low／Mediumが未準備の場合、元画質へ自動Fallbackしない。
- HLS、生成途中Segment、部分生成済みMP4の配信は行わない。

### 7. 派生データAPI

- `GET /api/v1/files/{fileId}/content`の`variant`として`original`、`thumbnail`、`image-low`、`image-medium`、`video-low`、`video-medium`を扱う。
- `READY`かつ現在のSource Version／Profile Versionに一致し、物理Fileが検証できる派生データだけを`200 OK`または単一Rangeの`206 Partial Content`で配信する。
- 不正または範囲外のRangeへ`416 RANGE_NOT_SATISFIABLE`を返す。
- 未生成または生成中では`202 Accepted`、Job ID、状態URL、Retry待機秒を返す。
- `GET /api/v1/media-jobs/{jobId}`で`GENERATING`、`READY`、`FAILED`、進捗、Queue位置、Retry待機秒を返す。
- `POST /api/v1/media-jobs/{jobId}/retry`でRetry可能な失敗だけを冪等に再Queueする。
- Job状態照会とRetryはJobを作成したUserだけに固定せず、現在その元Fileを閲覧できるUserだけへ許可する。
- Owner、直接共有、継承共有の`VIEWER`以上に生成・状態照会・配信を許可し、Admin Roleへ暗黙の他User File権限を与えない。
- Sourceが`TRASHED`、`MISSING_CANDIDATE`、`MISSING`、未完了File操作中、またはPurge対象の場合は通常の生成・配信を拒否する。
- `original`以外のDownload名は要求時点の`FileEntry.name`を基に品質識別子と実派生拡張子を付け、RFC 5987 `filename*`で返す。
- 派生相対Path、物理File名、生成時の元File名をAPI、Error、Logへ公開しない。

### 8. Lease制御

- 生成中および配信中の派生データをCache清掃やLifecycle削除との競合から保護する。
- 生成開始時に生成Leaseを取得し、Worker Heartbeatとともに期限を更新する。
- 配信開始直前に配信Leaseを取得し、Stream終了、取消、例外時に解放する。
- Leaseは所有Tokenまたは同等の世代識別子を持ち、期限切れ後に旧Processが新しい所有者の状態を上書きできないようにする。
- 長時間のRange配信ではLeaseを更新する。
- Process異常終了で明示解放できない場合も、設定済み期限後にだけ回収可能とする。
- Lease取得後に元File状態、Source Version、権限、Derivative状態を再確認し、不整合時は配信または生成を開始しない。

### 9. TTL・LRU清掃

- 写真・動画Low／Mediumだけを通常Cache清掃対象とする。
- Cache利用時に`lastAccessedAt`と24時間後の`expiresAt`を更新する。
- 30分ごとに期限切れCacheをBatch検索して削除する。
- 通常Cache合計が10GB以下の場合は容量超過清掃を行わない。
- 10GBを超えた場合は最終Access日時が古い順で削除し、合計が6GB以下になるまで継続する。
- `PENDING`、`RUNNING`、`DELETING`、有効Leaseを持つ派生データを削除しない。
- 写真・動画ThumbnailとPDF ThumbnailをTTL、10GB集計、6GBまでのLRU清掃から除外する。
- Cleanupは大量候補をMemoryへ全保持せず、安定順のBatchで処理する。
- 削除前に`DELETING`へ条件付き遷移し、物理削除後に管理情報を削除する。
- 削除失敗時は元ファイルを変更せず、後続Cleanupで再試行する。

### 10. 元ファイルLifecycleとの連動

- Rename／MoveではFile IDと`fileVersion`を維持し、既存ThumbnailとLow／Mediumを再利用する。
- 元ファイルの内容更新時は`fileVersion`を増加させ、旧Versionの派生データを配信しない。
- `ACTIVE`からTrashへ移動するときはLow／Mediumを削除し、Thumbnailをゴミ箱表示とRestoreのため保持する。
- Restore時は保持中Thumbnailを再利用し、Low／Mediumは必要時に再生成する。
- `MISSING`確定時は派生データを`BLOCKED_SOURCE_MISSING`として配信停止し、元ファイルの代替として提供しない。
- `MISSING`項目の一覧削除時はHDD上の元ファイル操作を行わず、対象の全派生データと管理情報を削除する。
- Permanent Deleteでは対象Treeの元ファイル、Thumbnail、Low／Medium、Job管理情報を既存の完全削除境界に参加させて削除する。
- 元ファイルが同じ場所へ復旧した場合も、旧Cacheをそのまま有効化せず、現在のSource Versionに対して必要時再生成する。
- Lifecycle変更と生成完了が競合した場合、Lock取得後の再読込で古い派生データを`READY`へしない。

### 11. Security・観測性・運用

- APIとWorkerはrootで実行しない。
- 外部ProcessのCommandをShell文字列連結で構築せず、固定実行Programと引数配列を使用する。
- Client指定の物理Path、User ID、Owner ID、Profile、FFmpeg引数を信頼しない。
- 派生Rootと一時Rootは`StorageGuard`で検証済みのStorage Root配下に限定する。
- Path traversal、absolute path、Symlink、特殊File、Root外renameを拒否する。
- 画像の巨大Dimension、Decompression bomb、破損Input、PDF暗号化／巨大文書、動画Probe失敗、Process TimeoutをResource上限内で失敗させる。
- stdout／stderrをboundedに読み、子Process deadlockと無制限Memory使用を防止する。
- MetricへFile ID、Job ID、File名、Path、User名をLabelとして含めない。
- 通常Logへ物理Path、File名、User名、Token、変換Command全文を含めない。
- Queue深さ、最古待機時間、実行中数、成功／失敗／Retry、処理時間、出力Bytes、Cleanup削除Bytes、stale回収を低Cardinality Metricとして記録する。
- Worker停止、Queue滞留、FFmpeg／ffprobe利用不可、HDD利用不可、Cleanup失敗を運用上区別できるようにする。

## 受け入れ条件

### 派生データと永続Queue

- [ ] 論理Keyが同じ同時要求100件で、`READY`派生データと有効Jobがそれぞれ最大1件になる。
- [ ] 複数Workerが同時取得しても同じJobを二重実行せず、Queue順が安定する。
- [ ] API再起動、Worker再起動、DB一時切断後も未完了Jobを失わず、完了、Retry、または説明可能な`FAILED`へ収束する。
- [ ] stale `RUNNING`はHeartbeat期限前に回収されず、期限後にだけRetry規則へ従って回収される。
- [ ] Rename／Moveで派生Keyを変更せず、内容更新で旧Versionを配信しない。

### Thumbnail・写真生成

- [ ] 写真・動画・PDF Thumbnailが承認済みProfileのWebPとして生成され、縦横比を維持し、拡大しない。
- [ ] 写真Lowが最大1280px／品質70、Mediumが最大2560px／品質82で生成される。
- [ ] 写真生成が2秒以内に完了すれば派生データを返し、超過時は1秒以内に`202 Accepted`と状態URLを返す。
- [ ] 同期HTTP要求を取消しても、永続化済み生成Jobが失われない。
- [ ] 破損、巨大、非対応、TimeoutのInputが元ファイルまたは別の派生データへ影響しない。
- [ ] ThumbnailにExpiryが設定されず、TTL／LRU清掃後も元ファイルが存在する限り保持される。

### 動画生成

- [ ] Pi上で動画変換が同時1件だけ実行される。
- [ ] Low／Mediumが指定Profileの完成済みMP4として生成され、ffprobe検証後だけ`READY`になる。
- [ ] Queue待ち、実行中、進捗あり／なし、完了、Retry可能失敗、恒久失敗をAPIで区別できる。
- [ ] 生成途中、検証前、失敗、stale Workerの出力を`200`／`206`で取得できない。
- [ ] HTTP取消、画面離脱、API再起動で動画Jobが取消されない。
- [ ] Low／Medium要求が元動画の自動配信へFallbackしない。

### API・認可・Lease

- [ ] `READY`派生データはRangeなしで`200`、有効な単一Rangeで`206`、不正Rangeで`416`を返す。
- [ ] 未生成または生成中は`202`とJob ID、状態URL、Retry待機秒を返す。
- [ ] Ownerと`VIEWER`以上の共有Userだけが生成、状態照会、Retry、配信できる。
- [ ] 未共有User、権限失効User、Admin RoleだけのUserへ対象またはJobの存在情報を過度に公開しない。
- [ ] 配信中・生成中・有効Leaseの派生データをCleanup、Trash連動、Purgeが途中で削除しない。
- [ ] Process異常終了後は期限切れLeaseだけが回収され、古い所有者が新しい状態を上書きしない。

### TTL・LRU・Lifecycle

- [ ] Low／Mediumは最終Accessから24時間未満ではTTL削除されず、期限到達後に削除候補となる。
- [ ] 通常Cacheが10GB以下では容量清掃せず、10GB超過時にLRU順で6GB以下まで削除する。
- [ ] Thumbnailは通常Cache容量集計と削除対象へ含まれない。
- [ ] Cache削除失敗後も元ファイルが同一内容で取得でき、次回Cleanupで再試行される。
- [ ] TrashでLow／Mediumだけを削除し、RestoreでThumbnailを再利用できる。
- [ ] Purgeと`MISSING`一覧削除で対象の派生データとJob管理情報が残らない。
- [ ] `MISSING`状態の残存派生データを正式ファイルの代替として取得できない。

### 品質・性能・運用

- [ ] Domain／Application全体Line Coverageが80%以上、今回追加する状態遷移・Validation・認可境界が95%以上である。
- [ ] 30万File規模でもQueue取得、Job状態照会、Cache候補Batch取得が無制限ScanまたはMemory全保持を行わない。
- [ ] Raspberry Pi 4実機で各Profileの生成時間、CPU、Memory、I/O、出力Sizeを測定し、承認済み上限を満たす。
- [ ] 30万件Thumbnailの推定容量と実測Sampleを記録し、HDD容量計画へ反映する。
- [ ] DB切断、HDD切断、read-only、容量不足、Worker kill、Pi再起動、atomic rename後DB失敗を注入し、元ファイル不変・部分出力非公開・Job回復を確認する。
- [ ] Worker停止中も元ファイルAPIが利用でき、Worker再開後に永続Queue処理が再開する。
- [ ] LANとZeroTierで同じHTTPS Hostname、TLS、認証、202／状態照会／Range契約が機能する。
- [ ] 既存の一覧、詳細、Search、Recent、Favorites／Tags、共有、Upload、Download、Rename、Move、Trash、Restore、Purge、MISSINGに回帰がない。

## 成功指標

- 生成途中または検証未完了の派生データ公開件数: 0件。
- 同一論理Keyの重複有効Jobおよび重複`READY`行: 0件。
- Pi上の同時動画変換数: 常に1件以下。
- TTL／LRU清掃によるThumbnail削除件数: 0件。
- 派生処理による元ファイルのSize、SHA-256、File ID、Owner、`fileVersion`の意図しない変更: 0件。
- WorkerまたはAPI再起動による永続Job消失: 0件。
- Root外書込み、Symlink追跡、Shell injection、機密情報Log／Metric label漏えい: 0件。
- Cache合計が10GBを超えて容量清掃が完了した後の通常Cache容量: 6GB以下。
- 未生成の写真派生要求: 同期待機閾値超過後、通常1秒以内に`202 Accepted`を返す。
- 未生成の動画派生要求: 通常1秒以内にQueue待ちまたは生成状態を返す。

## スコープ外

以下はこの作業では実装しない。

- Androidの一覧Thumbnail表示、写真Viewer、動画Player、品質選択、進捗画面。
- AndroidのMedia3、Coil、画質Preference、再生位置維持。
- Web UI、iOS UI、共有Link用Public Preview。
- HLS、DASH、生成途中Segment配信、Progressive MP4生成中配信。
- 音声専用派生データ、波形、字幕生成、OCR、全文検索、AI分類・顔認識。
- 元ファイルの置換、上書き圧縮、元動画の削除。
- Low／Mediumの事前全件生成および永久保持。
- ThumbnailのTTL／10GB容量上限への組込み。
- Client指定Profile、任意FFmpeg引数、任意物理Pathによる生成。
- Workerから外部Internet ServiceへのUploadまたは変換委譲。

## 依存関係・前提

- 既存`FileEntry.fileVersion`、`AuthorizationService`、`StorageGuard`、Range配信、Trash／Restore／Purge、`MISSING`管理を再利用する。
- 既存`KuraStorage.Worker`、systemd Worker unit、PostgreSQL、Storage Rootを拡張する。
- Image／PDF処理LibraryとFFmpeg／ffprobeの採用Version、License、arm64対応、Security更新方針は`design.md`で確定する。
- PRは`tasklist.md`記載のPR1〜PR4順に実施し、依存元が`main`へMergeされるまで後続PRを開始しない。
- Android写真・動画UIのSteeringは、本作業のServer APIとWorker基盤が完成した後に開始する。

## 参照ドキュメント

- `docs/product-requirements.md` 7.11「MVP後: 派生データとキャッシュ管理」
- `docs/functional-design.md` 5.4「MVP後: 派生データ・キャッシュ」
- `docs/functional-design.md` 6.2.7〜6.2.9「PreviewService／MediaTranscodeWorker／CacheCleanupService」
- `docs/functional-design.md` 7.3〜7.5「写真生成／動画生成／Cache清掃」
- `docs/functional-design.md` 8.5「派生データ配信」
- `docs/functional-design.md` 18.2 Server Step 7
- `docs/architecture-design.md` 8.6〜8.7「派生データの識別／PostgreSQL永続ジョブキュー」
- `docs/architecture-design.md` 11.3〜11.4「写真／動画派生データ」
- `docs/architecture-design.md` 15.2「サムネイル」
- `docs/repository-structure.md`
- `docs/development-guidelines.md`
- `.steering/20260829-thumbnail-derivative-worker-infrastructure/tasklist.md`
