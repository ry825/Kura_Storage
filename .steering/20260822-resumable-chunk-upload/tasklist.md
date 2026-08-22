# 分割アップロード・中断再開 タスクリスト

## タスク完全完了の原則

**本ファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止する。**

### 必須ルール

- 全タスクを最終的に`[x]`にする。
- 実装開始時は対象タスクを`[ ]`のままにし、実装と検証が完了した直後に個別に`[x]`へ更新する。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ更新する。
- 「時間の都合」「実装が難しい」「別タスクで対応」などを理由に、選択したPull Request単位のタスクを残さない。
- 技術的に不要になったタスクだけを、取消理由と代替実装を明記して完了扱いにできる。
- 実装中に必要性が判明した作業は、開始前に本ファイルの対象Pull Requestへ追加する。
- 各Pull Request作成後は、そのPull Requestの完了記録を本ファイルへ追加し、同じBranchへCommit・Pushして停止する。
- 後続Pull Requestのタスクは、先行Pull Request完了時に`[ ]`のままでよい。
- 全体振り返りは、PR1、PR2および全タスクの完了記録が揃った後にだけ記入する。

### 今回の実装境界

- 既存の`POST /api/v1/files/upload`と、その全体再試行契約は後方互換のため維持する。
- 新しいUpload Session契約は、手動アップロード、将来の動画・Webアプリ・自動バックアップから再利用できるTransfer機能として実装する。
- 今回のClient実装対象はAndroidの手動アップロードとする。Webアプリ、自動バックアップ、Room、WorkManagerは今回追加しない。
- 初回実装では連続したOffset順のChunk転送を採用し、並列・順不同Chunk Uploadは追加しない。
- Chunkごとに範囲、長さ、Sessionの期待Offset、任意または正式設計で必須化したChunk Checksumを検証する。
- Session完了まではFileEntryを公開せず、全体Sizeと任意SHA-256の一致、atomic rename、DB確定後にだけ一覧へ表示する。
- 期限切れ、明示取消、Device失効、復旧不能状態ではSessionを再開できないようにし、一時ファイルを安全に清掃する。

### Pull Request順序

1. PR1: Resumable Upload Server・API契約・期限切れSession清掃
2. PR2: Android中断再開・大容量実機E2E・運用文書

各PRは前段PRが`main`へMergeされたことを確認してから、最新の`main`を基点に短命Branchを作成する。

### 共通Pull Request完了手順

各PRの末尾で次をすべて実施する。

- 対象PR内の子タスクがすべて`[x]`であることを確認する。
- `git status --short`と全差分を確認し、対象外変更、秘密情報、物理絶対Path、生成物、Debug用コードが含まれないことを確認する。
- `git diff --check`を成功させる。
- 対象PRに必要な自動Test、Build、静的解析、Migration、手動確認をすべて成功させる。
- 変更と検証結果を含むCommitを作成し、作業BranchをRemoteへPushする。
- 英語のTitleとBodyでPull Requestを作成する。BodyにPurpose、Tasks、Changes、Tests、Impact、Not performedを記載する。
- GitHub ActionsのConfig、Server、Security、Androidのうち対象PRに必要な必須Jobを最終HEADで成功させる。
- `steering`スキルのモード3-Aで、完了日、PR URL、検証、計画差分、追加タスク、取消タスク、引継ぎを「各Pull Request完了記録」へ追記する。
- 完了記録を同じBranchへCommit・Pushし、作成済みPull Requestへ反映されたことを確認する。
- Pull RequestをMergeせず、URLと検証結果をユーザーへ報告して停止する。

---

## 実装前Steering文書

- [x] `requirements.md`が作成・承認されている。
  - [x] Upload Session、Chunk検証、中断再開、完了、取消、期限切れ清掃の要求と受け入れ条件を定義する。
  - [x] 既存Streaming Multipart Uploadとの互換性と移行境界を定義する。
  - [x] Android手動アップロードを今回のClient範囲とし、Webアプリと自動バックアップ本体を対象外にする。
  - [x] 文書作成後にユーザーの承認を得る。（2026-08-22承認）
- [x] `design.md`が作成・承認されている。
  - [x] Endpoint、Request/Response、状態遷移、Offset・Checksum規則、冪等性、Session期限、Error Codeを確定する。
  - [x] DB Schema、Storage一時ファイル、Lock、完了Transaction、Recovery、Cleanupの実装方針を確定する。
  - [x] AndroidのSession保持範囲、SAF再読込、再試行、取消、画面状態を確定する。
  - [x] 文書作成後にユーザーの承認を得る。（2026-08-22承認）
- [x] 承認済みSteering文書と正式文書に矛盾がないことを確認し、矛盾があれば実装開始前に解消する。

---

## PR1: Resumable Upload Server・API契約・期限切れSession清掃

### 1.1 作業開始

- [x] PR1の作業準備が完了している。
  - [x] `requirements.md`、`design.md`、本ファイルが承認済みであることを確認する。
  - [x] 最新の`main`を取得し、PR1用の短命Branchを作成する。（`feat/resumable-chunk-upload-server`）
  - [x] 作業に直接関係する5つの正式文書のUpload、Transfer、Recovery、Security、Testの節を再確認する。
  - [x] `git status`と既存差分を確認し、ユーザーの変更を保護する。（今回作成した未追跡Steering文書だけを保持）
  - [x] 既存`POST /api/v1/files/upload`、`FileService.UploadAsync`、`FileStore`、`FileOperationRecoveryService`、Hosted Service、OpenAPI、Integration Testの実装パターンを再確認する。
  - [x] 現行DB Schema、Migration、Upload一時ファイル配置、FileOperation状態を棚卸しし、移行時に保持すべき既存データと一時ファイルを記録する。（現行SchemaにUploadSessionはなく、既存`upload-temp`と`FileOperation(UPLOAD)`は後方互換のため維持）
  - [x] PostgreSQL Testcontainers、一時Storage、Server検証Scriptを実行できることを確認する。（開始時`verify-server.sh`: Domain 20件、Application 35件、Integration 51件成功）

### 1.2 正式文書・API契約

- [x] Resumable Uploadの正式仕様を更新する。
  - [x] `docs/product-requirements.md`へSession作成、中断再開、Chunk検証、完了、取消、期限切れ、Device失効の受け入れ条件を反映する。
  - [x] `docs/functional-design.md`へUpload Session API、状態遷移、Error、既存Uploadとの共存、将来のWeb・Backup再利用境界を反映する。
  - [x] `docs/architecture-design.md`へDB・HDD整合性、Lock、atomic publish、Recovery、Cleanup、資源制限を反映する。
  - [x] `docs/repository-structure.md`へTransfer Domain、Application、Infrastructure、Endpoint、Testの実配置を反映する。
  - [x] `docs/development-guidelines.md`へChunk Streaming、Offset・Checksum検証、Session冪等性、期限切れ清掃の実装・Test規則を反映する。
  - [x] MVP記述を履歴として壊さず、Resumable Uploadが今回のMVP後拡張であることを明確にする。
- [x] OpenAPIとFixtureを更新する。
  - [x] Session作成、状態取得、Chunk送信、完了、取消のEndpointと認証を定義する。
  - [x] `UploadSession`のID、状態、期待Size、受信済みByte、次Offset、Chunk上限、期限、完了Fileを定義する。
  - [x] `Idempotency-Key`の適用範囲と、同一Payload再送・異なるPayload再利用の応答を定義する。
  - [x] ChunkのOffset、Content Length、Checksum、重複再送、範囲外、順不同、期限切れの応答を定義する。
  - [x] `400`、`401`、`404`、`409`、`413`または設計で確定した上限Error、`422`、`429`、`507`、`503`を共通Error契約と整合させる。
  - [x] 既存`POST /api/v1/files/upload`のOpenAPI契約とFixtureを変更せず回帰Testで保護する。

### 1.3 UploadSession Domain・Persistence・Migration

- [x] Upload Session Domainを実装する。
  - [x] `CREATED`、`UPLOADING`、`COMPLETING`、`COMPLETED`、`CANCELLED`、`EXPIRED`、`RECOVERY_REQUIRED`または承認済み設計の状態を定義する。
  - [x] 状態ごとに許可する作成、Chunk受付、完了、取消、期限切れ、復旧の遷移を制約する。
  - [x] User、認証済みDevice、保存先Folder、File名、Content Type、期待Size、全体Checksum、Idempotency Key、受信済みByte、期限を保持する。
  - [x] Client指定の`userId`、`deviceId`、物理Pathを受け付けない。
  - [x] SessionのMetadata一致判定を定義し、同一Key・同一Metadataを冪等に扱う。
  - [x] 完了・取消・期限切れ後の不正な再遷移を拒否する。
- [x] Upload Session PersistenceとMigrationを実装する。
  - [x] `upload_sessions`と承認済み設計で必要な関連Table、Column、Check Constraint、Foreign Keyを追加する。
  - [x] UserとIdempotency Keyの一意性、未完了SessionのDevice・期限検索、Cleanup Batch取得に必要なIndexを追加する。
  - [x] 同一保存先・同名の並行確定を既存File操作と整合する一意性またはLockで保護する。
  - [x] `received_bytes`等の進捗を競合更新から保護するConcurrency Tokenまたは条件付き更新を実装する。
  - [x] Migration Up/DownとModel Snapshotを整合させ、既存Upload・FileOperationデータを破壊しない。
  - [x] Migration適用前に作成された既存Upload一時ファイルの扱いをRecovery方針と整合させる。

### 1.4 Session作成・照会・取消

- [x] Session作成Use Caseを実装する。
  - [x] 認証User・Device、保存先Folder、名前、Size、Checksum、Content Type、Idempotency Keyを検証する。
  - [x] 保存先の所有権・状態・同名競合・Storage利用可否をHDD書込み前に検証する。
  - [x] 安全余裕を差し引いた利用可能容量を検証し、不足時に`STORAGE_CAPACITY_INSUFFICIENT`を返す。
  - [x] 設定された最大File Size、Chunk Size、同時Session数、User・Device単位制限を検証する。
  - [x] 同一User・Key・同一Metadataの再送に既存Sessionまたは完了Fileを返す。
  - [x] 同一User・Keyを異なるMetadataで再利用した場合に`IDEMPOTENCY_CONFLICT`を返す。
  - [x] Session IDから推測可能な物理Pathを生成せず、検証済み相対一時Pathだけを保存する。
- [x] Session状態取得Use Caseを実装する。
  - [x] 所有Userと認証済みDeviceの規則に基づきSessionへのアクセスを制限する。
  - [x] 状態、受信済みByte、次Offset、期限、再開可否、完了File IDだけを返す。
  - [x] 他User・許可されないDevice・存在しないSessionを情報漏えいしないErrorへ統一する。
  - [x] Session照会が期限を延長するか否かを承認済み設計どおりに固定する。
- [x] Session取消Use Caseを実装する。
  - [x] 未完了Sessionだけを取消可能にし、同一取消再送を冪等成功にする。
  - [x] Chunk受付・完了処理と同じSession Lockで直列化する。
  - [x] 一時ファイルをStorage Root配下だけで冪等削除する。
  - [x] DB状態と一時ファイル削除の途中失敗をRecovery対象として記録する。

### 1.5 Chunk Streaming・検証・冪等性

- [x] Chunk受付Use Caseを実装する。
  - [x] Request Bodyを全体Bufferingせず一時ファイルへStreamingする。
  - [x] Session Lock内で状態、所有者、Device、期限、期待Offsetを再検証する。
  - [x] Offsetが現在の受信済みByteと連続する場合だけ新規Chunkを受け付ける。
  - [x] Chunkの宣言長、実受信長、設定上限、File全体Size超過を検証する。
  - [x] Chunk Checksumが指定または必須の場合、受信中に計算して一致を検証する。
  - [x] 不正Chunkでは進捗を進めず、部分書込みを切り戻すか安全に再試行可能な長さへtruncateする。
  - [x] 同一Offsetの再送は、既に確定した長さとChecksumが一致する場合だけ冪等成功にする。
  - [x] 同一Offsetで内容、長さ、Checksumが異なる再送をConflictとして拒否する。
  - [x] 未来Offset、負Offset、Gap、Overlap、範囲外、空Chunkを承認済みErrorへ変換する。
  - [x] Client切断、Cancellation、Disk full、read-only、I/O Errorで不完全Chunkを確定しない。
- [x] FileStoreの安全境界を拡張する。
  - [x] Session専用一時ファイル作成、指定Offset追記、truncate、範囲Checksum、削除、完了検証をApplication境界から呼べるようにする。
  - [x] Storage Root外、絶対Path、Path Traversal、Symbolic Link、管理Rootへの書込みを拒否する。
  - [x] Chunk確定前後のflush・fsync方針を承認済み設計どおり実装する。
  - [x] 同一Sessionへの二重書込みをProcess内Lockだけに依存せず、複数API Processでも直列化する。
  - [x] Log、Audit、Exception Responseへ物理Path、File名、内容、Checksum全文を不要に出力しない。

### 1.6 完了・公開・整合性

- [x] Session完了Use Caseを実装する。
  - [x] Session Lock内で状態、期限、受信済みByteと期待Sizeの一致を再検証する。
  - [x] 一時ファイルの実Sizeを確認し、指定された全体SHA-256をStreaming計算して検証する。
  - [x] SizeまたはChecksum不一致時にFileEntryを作成せず、再送可能性を承認済み状態へ反映する。
  - [x] 保存先Folder、同名競合、Storage状態を公開直前に再検証する。
  - [x] `FileOperation(PENDING)`、atomic rename、`FILESYSTEM_DONE`、FileEntry作成、Session完了、Audit、`COMPLETED`を既存整合性規則に従って確定する。
  - [x] 完了済みSessionへの同一完了再送で同じFile結果を返し、二重FileEntryや重複Auditを作成しない。
  - [x] 完了前の一時ファイルを一覧、詳細、Download、検索対象へ公開しない。
  - [x] 完了後のFileが既存Range Download、Trash、Restore、Rename、Move、Purgeで通常Fileと同様に扱える。
- [x] 並行操作を制御する。
  - [x] Chunk、完了、取消、期限切れ、Device失効が同じSession Lockへ整合する。
  - [x] 同じ保存先・同名のSession完了と既存Multipart Upload、Folder作成等の競合を既存Lock規則で直列化する。
  - [x] 無関係なUser、Session、保存先を不要に直列化しない。
  - [x] API Process停止後もDB状態と実ファイルから確定状態を判断できる。

### 1.7 期限切れ・Device失効・Recovery

- [x] Session期限と設定を実装する。
  - [x] Session有効期間、Cleanup間隔、Batch Size、最大Chunk Size、同時Session数を型付きOptionsとして定義する。
  - [x] 既定値と安全な上下限を設定し、API起動時とConfig検証で不正値を拒否する。
  - [x] 時刻はServer UTCを正とし、Client時刻を期限判定に使用しない。
  - [x] Chunk成功時に期限を延長する場合、その上限と更新規則を承認済み設計どおり実装する。
- [x] 期限切れSession CleanupをAPI内Hosted Serviceへ実装する。
  - [x] 起動時と設定周期に、期限順・ID順でBatch取得する。
  - [x] Global Cleanup Lockまたは同等の仕組みで複数Processの重複Runを防ぐ。
  - [x] Session Lock内で期限と状態を再検証し、転送・完了中Sessionを誤清掃しない。
  - [x] 一時ファイルを冪等削除し、Sessionを`EXPIRED`へ確定する。
  - [x] 1件の失敗でBatch全体を停止せず、再試行可能な状態と最小限の観測情報を残す。
  - [x] Cleanupが長時間のHDD全走査を行わず、DB候補から対象Pathだけを処理する。
- [x] Device失効時のSession処理を実装する。
  - [x] 失効Deviceの未完了Sessionを新規Chunk、照会、完了から拒否する。
  - [x] 失効処理とSession取消・期限切れを競合安全に実行する。
  - [x] 一時ファイル削除に失敗したSessionをRecoveryまたは次回Cleanup対象として残す。
- [x] Upload Session Recoveryを実装する。
  - [x] DBの受信済みByteと一時ファイル長が一致する正常中断を再開可能にする。
  - [x] 一時ファイルが長い場合、安全に確定Offsetへtruncateできるケースだけ自動復旧する。
  - [x] 一時ファイルが短い、欠落、Symbolic Link、Storage ID不一致、完了状態矛盾を安全側で分類する。
  - [x] atomic rename後・DB確定前のSessionを既存FileOperation Recoveryと整合して完了または`RECOVERY_REQUIRED`へ移行する。
  - [x] Storage未利用時に物理状態を推測せず、次回復旧へ延期する。
  - [x] RecoveryとCleanupが同じSessionを同時処理しても二重公開・誤削除しない。

### 1.8 API Endpoint・Security・資源制御

- [x] Upload Session Endpointを実装する。
  - [x] EndpointからRepository、DbContext、FileStoreを直接呼ばずApplication Use Caseへ委譲する。
  - [x] `sub`、`device_id`、Request IDを認証Contextから取得する。
  - [x] Request Body Size LimitとNginx buffering設定をChunk契約に合わせ、Client・Nginx・APIで全体FileをBufferingしない。
  - [x] Chunk Bodyを1回だけ読み取り、Model BindingやLog MiddlewareによるBody複製を避ける。
  - [x] 共通Error Response、Retry-After、Request IDをOpenAPIどおり返す。
  - [x] Session ID、File ID以外の物理情報をResponseへ含めない。
- [x] Upload資源制御と観測性を実装する。
  - [x] 既存Upload同時受付上限とResumable Chunk受付上限の関係を定義し、過負荷時に`429`と`Retry-After`を返す。
  - [x] Session作成数、Chunk Byte、再開回数、完了・取消・期限切れ・Recovery件数、処理時間を低Cardinalityで計測する。
  - [x] User ID、Device ID、Session IDを必要以上にMetric Labelへ使用しない。
  - [x] 成功、拒否、Recovery Requiredを既存Audit規則に従い記録し、File名、Path、内容、Checksum、Tokenを含めない。
  - [x] Rate Limit、Storage容量、Session上限が既存Multipart Uploadを不必要に停止させない。

### 1.9 PR1自動Test

- [x] Domain・Application Testが完了している。
  - [x] 全状態遷移、禁止遷移、期限境界、Idempotency、Metadata ConflictをTestする。
  - [x] 正常Chunk、重複再送、Checksum不一致、短いBody、長いBody、Gap、Overlap、未来Offset、Size超過をTestする。
  - [x] 完了時のSize・全体Checksum・同名競合・容量不足・二重完了をTestする。
  - [x] Chunk、完了、取消、失効、Cleanupの並行処理をTestする。
  - [x] AuditとMetricへFile名、Path、内容、Secretが入らないことをTestする。
- [x] Infrastructure・Integration Testが完了している。
  - [x] Migration Up/Down、Constraint、Index、ConcurrencyをPostgreSQLでTestする。
  - [x] 実Filesystemで複数Chunk、中断、再開、完了、取消、期限切れ清掃をTestする。
  - [x] API再起動、Chunk書込み途中停止、atomic rename後停止、DB確定失敗から復旧する。
  - [x] Path Traversal、絶対Path、Symbolic Link、Storage Root外、read-only、Disk full相当を拒否する。
  - [x] 他User、他Device、失効Device、期限切れSession、無効Tokenを拒否する。
  - [x] Session完了前は一覧・詳細・Downloadへ出ず、完了後だけ公開される。
  - [x] 同名Session、既存Multipart Upload、Rename、Move、Trash、Restore、Purgeとの競合をTestする。
  - [x] OpenAPI、Fixture、API Response、共通Errorが一致する。
  - [x] 既存Streaming Multipart Uploadと全File操作へ回帰がない。
- [x] 大容量・資源Testが完了している。
  - [x] Test用大容量Fileを全体Byte配列化せず生成・送信し、Client Test HarnessとServerのMemoryがFile Size比例で増えないことを確認する。
  - [x] 設定最大Chunk境界、最大File Size境界、同時Session・Chunk上限をTestする。
  - [x] 低速送信、切断、再接続、Retry-After後再試行でSessionを継続できる。
  - [x] Cleanup Batchが大量期限切れSessionを上限件数ずつ処理し、API応答を長時間停止させない。
- [x] PR1の標準検証が成功している。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [x] `git diff --check`が成功する。

### 1.10 PR1手動確認・セルフレビュー

- [x] API Clientと実FilesystemでServer機能を確認する。
  - [x] Session作成後に複数Chunkを送信し、状態照会のOffsetと一時ファイル長が一致する。
  - [x] 転送を切断してAPIを再起動し、受信済みOffsetから再開して元Fileと同じChecksumで完了する。
  - [x] Chunk破損、範囲不正、期限切れ、取消、Device失効を再現し、不完全Fileが公開されない。
  - [x] 期限切れSessionをCleanupし、一時ファイルとDB状態を照合する。
  - [x] 完了直前・公開直後の障害を再現し、Recovery後にFileが0件または1件だけ存在する。
  - [x] 既存Multipart Uploadが従来どおり全体再試行で動作する。
- [x] PR1差分をセルフレビューする。
  - [x] 承認済み`requirements.md`と`design.md`のPR1範囲に対応する実装・Testがある。
  - [x] Web・Backup固有ロジック、Room、WorkManager、未使用Workerを先行追加していない。
  - [x] API・Application・Infrastructureの依存方向と物理Path非公開境界を維持している。
  - [x] Migration、Rollback、旧Client互換性、未完了Sessionの扱いが文書化されている。
  - [x] 不要なPackage、生成物、実環境情報、Credentialが差分にない。

### 1.11 Pull Request完了

- [x] PR1が完了している。
  - [x] 1.1〜1.10がすべて`[x]`である。
  - [x] 共通Pull Request完了手順をすべて実施する。
  - [x] PR1完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [x] 完了記録CommitがPR1へ反映されている。
  - [x] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR2: Android中断再開・大容量実機E2E・運用文書

### 2.1 作業開始

- [ ] PR2の作業準備が完了している。
  - [ ] PR1が`main`へMerge済みであることを確認する。
  - [ ] 最新の`main`を取得し、PR2用の短命Branchを作成する。
  - [ ] `requirements.md`、`design.md`、本ファイル、PR1完了記録を確認する。
  - [ ] `git status`と既存差分を確認する。
  - [ ] Androidの`KuraStorageApi`、`TransferRepository`、`UploadOperation`、SAF Stream、ViewModel、Compose UI、Testの既存パターンを再確認する。
  - [ ] Raspberry Pi、PostgreSQL、共有exFAT HDD、Android実機、LAN、ZeroTier、Release署名入力を利用できることを確認する。

### 2.2 Android Network・Model契約

- [ ] Upload Session Network契約を実装する。
  - [ ] Session作成、状態取得、Chunk送信、完了、取消を`KuraStorageApi`へ追加する。
  - [ ] OpenAPIの状態、Offset、Size、Checksum、期限、ErrorをKotlin DTOへ欠落なく変換する。
  - [ ] Chunk Request Bodyを全体ByteArray化せず、SAF InputStreamの指定範囲からStreamingする。
  - [ ] 401 Refresh後、通信結果不明、Retry-After後の再送でも同じSession、Idempotency Key、Offset、Chunk内容を維持する。
  - [ ] Session Conflict、Offset Conflict、Chunk Checksum、期限切れ、取消、Device失効、容量不足、Storage異常を既存Error分類へ追加する。
  - [ ] 既存Multipart Upload APIを残し、旧ServerまたはProtocol Versionの扱いを承認済み設計どおり実装する。
- [ ] Android Upload Modelを拡張する。
  - [ ] Source URI、Metadata、Idempotency Key、Session ID、受信済みOffset、期限、状態を表現する。
  - [ ] File内容や物理Pathを永続化せず、必要な`content://` URIだけを扱う。
  - [ ] 同じPayloadの再開と、別File・別名・別保存先として開始する操作を区別する。
  - [ ] Session完了後または明示取消後に再利用できない状態へ遷移する。

### 2.3 Android TransferRepository・中断再開

- [ ] Resumable Upload処理を実装する。
  - [ ] Upload開始時にSessionを作成し、Serverが返したChunk上限内で送信単位を決定する。
  - [ ] SAF InputStreamをServerの次Offsetまで安全に進め、全体FileをMemoryや一時複製へ保持しない。
  - [ ] 各ChunkのChecksumをStreaming計算し、送信Byteと一致させる。
  - [ ] Chunk成功後にServer応答の確定Offsetを進捗の正として採用する。
  - [ ] 通信切断・Response不明時はSession状態を再照会し、Server確定Offsetから再開する。
  - [ ] 期待Offset Conflict時はServer状態を再照会し、内容不一致を推測で上書きしない。
  - [ ] 全Chunk送信後に完了APIを呼び、完了FileをServerから取得して結果を確定する。
  - [ ] Coroutine Cancellationと画面上の取消を区別し、明示取消時だけ取消APIを呼ぶ。
  - [ ] Source URIを再度開けない、権限喪失、Source Size・内容変更を検出し、新しいSessionなしに継続しない。
- [ ] 進捗と再試行状態を実装する。
  - [ ] Server確定Byteを基準に0〜100%の進捗を表示する。
  - [ ] `CREATING`、`UPLOADING`、`PAUSED`、`VERIFYING`、`COMPLETED`、`CANCELLED`、`FAILED`をUI向け状態へ変換する。
  - [ ] 再試行可能な通信・429・一時503と、ユーザー対応が必要な期限切れ・権限喪失・内容変更を区別する。
  - [ ] Retryで同じSessionとIdempotency Keyを維持し、`Retry-After`を尊重する。
  - [ ] 完了後にFile一覧をServerから再取得し、Client側で未確定Fileを合成しない。

### 2.4 ViewModel・Compose UI

- [ ] FileBrowser ViewModelへ中断再開状態を実装する。
  - [ ] 同時二重開始、二重完了、同じ操作への並行Retryを防ぐ。
  - [ ] 通信切断時に受信済みByteと再開可能状態を保持する。
  - [ ] Retry、取消、一覧へ戻る操作を状態ごとに制御する。
  - [ ] Session期限切れ時は新しいSessionで最初から開始する必要があることを示し、暗黙に別Fileを開始しない。
  - [ ] 画面再生成で実行中Coroutineと表示状態を不必要に重複させない。
  - [ ] Process終了をまたぐ永続再開は、Room・WorkManagerを追加しない今回の範囲に従って扱う。
- [ ] Upload UIを更新する。
  - [ ] 送信済み容量、全体容量、進捗、再開中、検証中を表示する。
  - [ ] 通信中断時に「受信済み位置から再開」できることを案内する。
  - [ ] Retry可能、期限切れ、Source権限喪失、Source変更、容量不足、Storage異常を区別して表示する。
  - [ ] Cancel確認後にSession取消を実行し、送信中の誤操作を防ぐ。
  - [ ] File名、保存先変更、別File選択では新しいSessionとIdempotency Keyを使用する。
  - [ ] アクセシビリティ、画面回転、Back操作、長いFile名、0%・100%境界を確認する。

### 2.5 Android自動Test

- [ ] Network・Repository Testが完了している。
  - [ ] Session APIのRequest、Header、Body、DTO、Error MappingをTestする。
  - [ ] SAF Streamを複数Chunkへ分けても元内容と一致し、全体ByteArray化しないことをTestする。
  - [ ] 中断後の状態照会、確定Offset再開、重複Response、Offset Conflict、Retry-AfterをTestする。
  - [ ] 401 Refresh後もSession、Key、Offset、Chunk内容を維持する。
  - [ ] Source Size・内容変更、URI権限喪失、期限切れ、取消、完了をTestする。
  - [ ] 既存Download、Range、File操作、旧Multipart Uploadの契約へ回帰がない。
- [ ] ViewModel・UI Testが完了している。
  - [ ] 状態遷移、進捗、Retry、取消、二重Tap、一覧再取得をTestする。
  - [ ] 再開可能Errorとユーザー対応必須Errorの文言・ActionをTestする。
  - [ ] 画面回転、Back、Cancel確認、完了後表示をCompose Testする。
  - [ ] File名変更・別File選択で古いSessionとKeyを再利用しないことをTestする。
- [ ] PR2の標準検証が成功している。
  - [ ] `./scripts/ci/verify-config.sh`が成功する。
  - [ ] `./scripts/ci/verify-server.sh`が成功する。
  - [ ] `./scripts/ci/verify-security.sh`が成功する。
  - [ ] `./scripts/ci/verify-deployment.sh`が成功する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] `./apps/android/gradlew -p apps/android connectedDebugAndroidTest --max-workers=1`が成功する。
  - [ ] `git diff --check`が成功する。

### 2.6 Raspberry Pi・Android実機E2E

- [ ] 実環境相当で中断再開を確認する。
  - [ ] 配置前にPostgreSQLとStorage RootのBackupを取得する。
  - [ ] Migrationを適用し、API、Nginx、設定を既存手順で配置する。
  - [ ] `deployment/raspberry-pi/verify.sh`でAPI、Nginx、PostgreSQL、HDD、Storage IDを確認する。
  - [ ] Android実機から小容量Fileと大容量動画FileをLAN経路でアップロードし、元FileとServer FileのSize・SHA-256が一致する。
  - [ ] Upload中に通信を切断し、同じSessionの受信済みOffsetから再開して完了する。
  - [ ] LANから`REMOTE_SECURE`経路へ切り替え可能な条件では、認証・TLS境界を維持して再開する。
  - [ ] Android画面回転、Background遷移、API再起動、Nginx再起動の各条件で、設計どおり再開または明確な失敗になる。
  - [ ] Chunk破損、Server容量不足、HDD未Mount、read-only、Device失効、Session期限切れで不完全Fileを公開しない。
  - [ ] 明示取消と期限切れCleanup後に一時ファイルが残らず、DB、Audit、Metricと一致する。
  - [ ] 既存Multipart UploadとRange Downloadも実機で回帰確認する。
- [ ] 資源と性能を実機確認する。
  - [ ] 大容量動画Upload中のAndroid HeapとServer RSSがFile Size比例で増加しない。
  - [ ] Chunk Size、同時Upload上限、Retry間隔がRaspberry PiとAndroidの操作性を損なわない。
  - [ ] Upload中も一覧、Health、認証更新、Range Downloadが許容範囲で応答する。
  - [ ] Cleanup実行中も有効SessionのChunk受付と既存File操作が不必要に停止しない。
  - [ ] 測定条件、File Size、経路、Chunk Size、中断位置、再開時間、Memory最大値を`docs/testing/`へ記録する。

### 2.7 配置・運用・将来Consumer境界

- [ ] 配置と設定を更新する。
  - [ ] `appsettings.example.json`とdeployment ConfigへSession期限、Chunk Size、同時数、Cleanup間隔・Batchを追加する。
  - [ ] NginxのRequest Body制限、buffering、timeoutをChunk APIと既存Uploadの両方に適用できるよう更新する。
  - [ ] Install、Upgrade、Rollback、Verify ScriptへMigrationと設定検証を追加する。
  - [ ] 実環境値、File名、端末情報、SecretをGit管理対象へ追加しない。
- [ ] 運用文書を更新する。
  - [ ] Session状態、期限切れ件数、Cleanup失敗、`RECOVERY_REQUIRED`、一時容量を確認する手順を記載する。
  - [ ] 安全な再試行、明示取消、Device失効、手動復旧、未完了Session確認を記載する。
  - [ ] Migration順序、旧Client互換、Rollback前の未完了Session処置を記載する。
  - [ ] 物理一時PathやChecksum全文を通常の運用Logへ出さない手順にする。
- [ ] 将来Consumer向け境界を確認する。
  - [ ] WebアプリがBrowser File Streamから同じSession APIを利用でき、Android固有Contractへ依存していない。
  - [ ] 自動バックアップが認証済みDeviceとBackup Metadataを追加でき、TransferのChunk処理を再利用できる。
  - [ ] 動画などContent TypeやFile Sizeだけを理由に拒否せず、設定上限と容量の範囲で処理できる。
  - [ ] 今回はWeb UI、Backup Compare、Receipt、Room、WorkManagerを実装していないことを文書上明確にする。

### 2.8 文書整合・セルフレビュー

- [ ] PR2実装と文書を整合する。
  - [ ] `requirements.md`のAndroid中断再開と大容量受け入れ条件に対応する実装・検証がある。
  - [ ] `design.md`と実装差分がある場合、理由と確定設計を反映する。
  - [ ] 5つの正式文書、OpenAPI、Config、Migration、Server、Android、運用手順の名称、状態、既定値が一致する。
  - [ ] 実測結果を基に既定Chunk Sizeや同時数を変更した場合、根拠と影響を記録する。
- [ ] PR2差分をセルフレビューする。
  - [ ] Androidが物理Path変換、全体ByteArray化、不要な永続URI権限取得をしていない。
  - [ ] 通信結果不明時にUpload完了や受信Offsetを推測していない。
  - [ ] Server確定OffsetとIdempotencyを全再試行経路で維持している。
  - [ ] Web・Backup本体、Room、WorkManager、不要なPackage・Moduleを追加していない。
  - [ ] Credential、実環境情報、File内容、生成物が差分にない。

### 2.9 Pull Request完了

- [ ] PR2が完了している。
  - [ ] 2.1〜2.8がすべて`[x]`である。
  - [ ] 共通Pull Request完了手順をすべて実施する。
  - [ ] PR2完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [ ] 完了記録CommitがPR2へ反映されている。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## 各Pull Request完了記録

各Pull Request作成後に`steering`スキルのモード3-Aを使用して追記する。対象Pull Request内のタスクがすべて完了するまで記録しない。

### PR1: Resumable Upload Server・API契約・期限切れSession清掃

- 完了日: 2026-08-22
- Pull Request: [#14 Add resumable chunk upload server flow](https://github.com/ry825/Kura_Storage/pull/14)
- 実施したTest・Build・静的解析:
  - `./scripts/ci/verify-config.sh`: 成功
  - `./scripts/ci/verify-server.sh`: 成功（Domain 28件、Application 51件、Integration 63件）
  - `./scripts/ci/verify-security.sh`: 成功
  - `./scripts/ci/verify-deployment.sh`: 成功
  - `./scripts/ci/verify-android.sh`: 成功
  - `git diff --check`: 成功
  - GitHub Actions最終HEAD: Config、Server、Security、Androidの全Job成功
- 手動確認・実機確認:
  - API Test Clientと一時実Filesystemを使用し、Session作成、複数Chunk、中断後の状態照会・再開、完了、取消、期限切れ清掃、Device失効、Recoveryを確認した。
  - 完了前のFile非公開、完了後の既存File操作、既存Multipart Uploadの回帰をIntegration Testで確認した。
  - Raspberry PiおよびAndroid実機による大容量E2EはPR2の対象であり、PR1では実施していない。
- 計画と実装の差分:
  - SHA-256 Headerは相互運用性のため大文字・小文字の16進表記を受理し、内部で小文字へ正規化する仕様に確定した。
  - 保存先FolderのPurgeでUploadSessionまでCascade削除されないよう、`destination_folder_id`をnullableかつ`ON DELETE SET NULL`とした。公開直前に保存先を再検証し、消失時は完了を拒否して期限切れ清掃へ移行する。
- 実装中に追加したタスクと理由:
  - 保存先FolderのPurgeと未完了Sessionの相互作用を確認するMigration・Integration Testを追加した。SessionのCleanup情報を保持し、一時ファイルを孤児化させないため。
  - Cleanup Batch上限と低Cardinality MetricのIntegration Testを追加した。運用時の資源制御と個人識別子非露出を回帰から保護するため。
- 技術的に不要になったタスク・理由・代替実装: なし
- PR2への引継ぎ事項:
  - PR1 Merge後、最新`main`からAndroid用Branchを作成し、OpenAPI契約に基づくSession Client、SAF範囲Streaming、再開状態、ViewModel・Compose UIを実装する。
  - Raspberry Pi、共有exFAT HDD、Android実機で大容量・LAN切断・再接続・ZeroTier・Release BuildのE2Eを完了する。
  - Server側のSession API、設定、Cleanup、Recovery、既存Multipart互換契約はPR1で利用可能になっている。

### PR2: Android中断再開・大容量実機E2E・運用文書

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・実機確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク・理由・代替実装: 未記録
- 後続作業への引継ぎ事項: 未記録

---

## 全体振り返り

PR1、PR2および本ファイルの全タスクが完了した後にだけ、`steering`スキルのモード3-Bを使用して記録する。

### 実装完了日

未完了

### 計画と実績の差分

- 未記録

### 主な設計変更と理由

- 未記録

### 技術的な学び

- 未記録

### プロセス上の改善点

- 未記録

### 次回への改善提案

- 未記録
