# Android自動バックアップ タスクリスト

## 対象要件

- `docs/product-requirements.md` 7.9「MVP後: Android自動バックアップ」
  - 7.9.1 バックアップルール設定
  - 7.9.2 差分検知
  - 7.9.3 ネットワーク別バックアップポリシー
  - 7.9.4 バックグラウンド実行
  - 7.9.5 一方向バックアップ動作
  - 7.9.6 バックアップ状態表示
- 既に完成した分割アップロード、索引、検索、共有、状態管理を再利用し、`BackupReceipt`、重複判定、Room、WorkManager、差分検出、Wi-Fi規則、進捗・履歴を一体として実装する。

## タスク完全完了の原則

- 全タスクを最終的に`[x]`にし、親タスクは全子タスク完了後だけ完了にする。
- 1回の実装では1つのPull Request単位をCommit、Push、英語Pull Request、必須CI、steeringモード3-Aの完了記録まで完了して停止する。
- 各Pull Requestは先行Pull Requestが`main`へMergeされ、必須CIが成功した後に最新`main`から開始する。
- 実装と対応する自動Test、手動確認、正式文書更新を同じPull Requestへ含め、TDDで進める。
- 技術的に不要となったタスク以外をスキップしない。不要となった場合は理由と代替実装を該当タスクとPull Request完了記録へ記載する。

## スコープ境界

- [x] AndroidからServerへの一方向バックアップだけを実装し、端末削除、端末側移動、候補省略をServer側の削除へ変換しない。
- [x] 通常の認証済みUpload Session、Chunk転送、再開、Checksum、Storage Guard、FileOperationを再利用し、別の転送方式を重複実装しない。
- [x] 保存先Folderは現在の所有・直接共有・継承共有権限を要求ごとに再評価し、Client指定のUser ID、Device ID、物理Pathを信用しない。
- [x] 自動実行は`LOCAL_DIRECT`または登録済み外部Wi-Fi＋`REMOTE_SECURE`だけで許可し、未登録Wi-Fi、Mobile、ZeroTierなしの外部Wi-Fi、認証・Device・Session失効、HDD利用不可ではfail-closedにする。
- [x] SSID・BSSIDは外部Wi-Fi許可ポリシーの照合だけに使用し、Server本人確認や`LOCAL_DIRECT`判定の根拠にしない。
- [x] 常時Foreground Service、双方向同期、Mobile通信での自動実行、KuraStorageによるZeroTier接続操作、Web UIを追加しない。
- [x] Log、Metric label、例外、通知へ端末Path、相対Path、SSID、BSSID、ファイル名、Token、端末文書識別子を平文で残さない。

---

## フェーズ0: 要求・設計承認

- [x] `requirements.md`を作成し、User承認を得る。
  - [x] 7.9.1〜7.9.6の受け入れ条件を、正常系、境界、失敗、復旧条件へ具体化する。
  - [x] 対象Android version、MediaStore対象種別、SAF権限、初回バックアップ、履歴保持、再試行上限を確定する。
  - [x] 既存の分割Upload、File version、共有権限、索引状態、Device／Session失効との境界を確定する。
- [x] `design.md`を作成し、User承認を得る。
  - [x] Server Schema／API／transaction、Android Room Schema／Migration、差分検出、WorkManager、Network Policy、UI状態を確定する。
  - [x] 競合、Process終了、端末再起動、ネットワーク切替、Source権限喪失、Server削除済みFileの復旧方針を確定する。
  - [x] Test matrix、性能上限、実機E2E、可観測性、秘密情報非漏えいの確認方法を確定する。
- [x] 正式文書間の差異を解消し、承認内容へ揃える。
  - [x] `BackupReceipt`の一意性を`(userId, deviceId, localDocumentKey)`へ統一し、Schema Index表を一致させる。
  - [x] `POST /api/v1/backup/compare`とUpload SessionのBackup metadata契約をOpenAPIへ反映する範囲を確定する。
  - [x] `docs/functional-design.md`の最大100件、合計2GB、20分というWorker初期上限を実装・実測対象として確定する。
- [x] 本tasklistを承認済み`requirements.md`、`design.md`に合わせて更新し、PR境界と開始条件を確定する。

---

## PR1: Server側BackupReceipt・比較・Upload確定

### 1.1 開始条件・正式文書

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0が完了し、分割Upload、共有、索引・状態管理、File versionの先行Pull Requestが`main`へMerge済みである。
  - [x] 最新`main`から短命Branchを作成し、`git status`、Upload Session、FileOperation、File mutation lock、共有認可、Purge transactionの類似実装を確認する。
- [x] Server側の正式文書を承認済み契約へ更新する。
  - [x] `product-requirements`、`functional-design`、`architecture-design`へReceipt、比較、変更Upload、一方向性、整合性境界を反映する。
  - [x] `repository-structure`へDomain／Application／Infrastructure／API／Migration／Testの配置を反映する。
  - [x] `development-guidelines`とOpenAPIへBackup固有の検証、冪等性、秘密情報保護を反映する。

### 1.2 BackupReceipt・Migration

- [x] `BackupReceipt` Domain modelと不変条件をTest firstで実装する。
  - [x] User、認証済みDevice、opaqueな`localDocumentKey`、Remote File、相対Path、Size、Source更新日時、任意Checksum、Upload完了日時を型付きで保持する。
  - [x] 文字数、NFC、Path segment、Size、日時、Checksum形式を検証し、絶対Path、Traversal、制御文字、未知Algorithmを拒否する。
  - [x] ClientがUser ID、Device ID、Remote File ID、Upload完了日時を任意指定できない境界にする。
- [x] EF Core mappingとMigrationを実装する。
  - [x] `backup_receipts`、`(user_id, device_id, local_document_key)`一意制約、Remote File参照、比較用Indexを追加する。
  - [x] User／Device／FileEntryの失効・Purge時の削除規則を既存の関連管理情報整理と同じtransactionへ統合する。
  - [x] Up／Down／再Up、既存File／Upload／Share／Search／Activity保持、Model Snapshot、pending modelなしを実PostgreSQLでTestする。

### 1.3 Compare API・認証認可

- [x] Backup compare Application契約をTest firstで実装する。
  - [x] 候補を`NEW`、`CHANGED`、`ALREADY_UPLOADED`へ決定的に分類する。
  - [x] Size・Source更新日時で候補を絞り、必要時だけChecksumを要求・比較できる契約にする。
  - [x] 1 Requestの件数、文字数、総metadata量、重複Key、不正値を検証し、過大入力を拒否する。
  - [x] Server側ReceiptとFileEntryの現在状態・Versionを同一Snapshotで照合し、`TRASHED`、`MISSING_CANDIDATE`、`MISSING`、Purge済み、別所有者を安全に扱う。
- [x] `POST /api/v1/backup/compare`を実装する。
  - [x] UserとDeviceをAccess Token／Sessionから導出し、Device／Session失効、認証待ちを既存Error envelopeで返す。
  - [x] 保存先Folderへの新規作成権限を所有・直接共有・継承共有から再評価し、Admin暗黙アクセスを許可しない。
  - [x] Request／Response、列挙値、Error、上限、例をOpenAPIへ追加し、Contract Testを成功させる。
  - [x] Compareを読取処理とし、候補省略や`LOCAL_MISSING`でFileEntry／Receiptを削除・更新しない。

### 1.4 Upload Session統合・重複防止

- [x] 既存Upload Session開始契約へBackup contextを追加する。
  - [x] `localDocumentKey`、相対Path、Source更新日時と任意Checksumを受け、誌証済みUser／DeviceへServer側で関連付ける。
  - [x] Compare結果、保存先Folder、対象Remote File、File Versionを開始・完了時に再検証し、改ざん・古い結果・権限失効を拒否する。
  - [x] 同じUser／Device／端末文書の保留Backup Uploadを一意にし、並行開始とretryを1 Sessionへ収束させる。
- [x] `NEW`の確定処理を実装する。
  - [x] 検証済み一時Fileを新しいFileEntryへ原子的に公開し、索引、操作履歴、Receiptを既存Upload transactionへ統合する。
  - [x] 保存先が共有Folderの場合も既存所有権規則に従い、操作者と対象Ownerを分離する。
- [x] `CHANGED`の確定処理を実装する。
  - [x] 検証済み一時Fileを同じFileEntryへatomic replaceし、File Versionを増加する。
  - [x] 名前・親・File ID・共有・Favorite・Tag・Recentを維持し、内容依存のChecksum、派生データ、Search metadata、version履歴を既存規則どおり更新する。
  - [x] Version競合、Move、Rename、Trash、Missing、Purge、共有解除と競合した場合に古い内容を公開しない。
- [x] Upload完了、FileEntry／Version更新、FileOperation、Receipt upsert、保留一意状態解除を整合させる。
  - [x] DB transaction失敗、HDD操作失敗、API停止、回復retry、重複completeで片側だけ成功せず、Receiptを早く進めない。
  - [x] 同じmetadata・内容の再送で新規FileやVersionを増やさず、変更内容だけを1回確定する。
  - [x] Source候補消失やClient取消でServer上の既存File／Receiptを削除しない。

### 1.5 PR1検証・完了

- [x] Domain／Applicationの新規重要状態変換95%以上、全体80%以上のCoverageを満たす。
- [x] PostgreSQL統合Testで新規、変更、不要、並行、retry、rollback、recovery、権限変更、Device失効、HDD unavailable、Purgeを確認する。
- [x] 既存Upload／Search／Share／File state／Text version／User activityのRegression Testを成功させる。
- [x] `verify-server.sh`、`verify-config.sh`、`verify-security.sh`、`verify-deployment.sh`、format、Migration、OpenAPI、`git diff --check`を成功させる。
- [x] Log／Metric／例外に端末文書Key、相対Path、ファイル名、物理Path、User入力、Tokenが漏れないことを確認する。
- [x] 差分をself-reviewし、Commit、Push、英語Pull Request、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR2: Android Room・バックアップルール・外部Wi-Fiポリシー

### 2.1 開始条件・Module構成

- [x] PR2の開始条件を満たす。
  - [x] PR1が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [x] `core-data`、DataStore、ConnectionCoordinator、Network binding、Session scope、Settings画面の既存patternを確認する。
- [x] Android正式文書とModule構成を更新する。
  - [x] `core-database`と`feature-backup`を正式構成へ追加し、Feature間直接依存を作らない。
  - [x] Room、WorkManager、Wi-Fi情報取得に必要な依存と権限を最小限追加し、SBOMと権限目的を文書化する。
  - [x] Room DBをAndroid Auto Backup／端末移行対象外にし、秘密情報を保存しない。

### 2.2 Room Schema・Migration

- [x] `core-database`をTest firstで実装する。
  - [x] `LocalBackupRule`、`LocalSyncItem`、`ExternalWifiPolicy`、MediaStore generation／走査checkpointのEntityと型付きMapperを追加する。
  - [x] Rule、local document、状態、retry、時刻、Remote File参照の一意性とIndexを定義する。
  - [x] `PENDING`、`UPLOADING`、`COMPLETED`、`FAILED`、`LOCAL_MISSING`、認証待ち、接続待ちの許可遷移をtransactionで保護する。
  - [x] DAOはBatch upsert、claim、lease回復、状態件数、失敗履歴、Rule削除／無効化を原子的に扱う。
- [x] Room MigrationとDatabase lifecycleを実装する。
  - [x] 初期Schema、Migration Test、exported schema、Downgrade時の破壊的移行禁止を確認する。
  - [x] Process終了後の`UPLOADING` lease回復、Session／接続先変更時の隔離、Logout時のUser別状態処理を確定する。
  - [x] Local DB破損時はServer File削除や重複推測を行わず、再走査・Server Compareへ安全に収束する。

### 2.3 バックアップルール管理

- [x] Rule repository／Use Caseを実装する。
  - [x] SAF Tree URIのpersistable read permission、表示名、Server Folder ID、有効状態、Network mode、最低Battery、初回充電条件を保存する。
  - [x] 保存先FolderをServerへ再照会し、作成権限、Trash／Missing状態、共有解除、Session変更を検証する。
  - [x] Rule作成・編集・無効化・削除で既存Server Fileを削除せず、保留Queueの扱いを承認済み設計どおりにする。
  - [x] Source permission喪失、Tree移動、Folder消失を明示状態にし、再選択できるようにする。

### 2.4 外部Wi-Fiポリシー・権限

- [x] `ExternalWifiPolicy` repository／Use Caseを実装する。
  - [x] 現在接続中Wi-Fiだけを明示操作で登録し、表示名、SSID、任意BSSID制限、従量制扱い、有効状態を管理する。
  - [x] Android version別のWi-Fi情報取得権限とLocation／Nearby Wi-Fi条件を処理し、拒否・恒久拒否・取得不能をfail-closedにする。
  - [x] SSID／BSSIDの正規化、unknown SSID、randomized BSSID、重複登録、最大件数、長さを検証する。
  - [x] 従量制扱いまたは無効なWi-Fiを自動実行対象から除外する。
- [x] Wi-Fi情報と機密情報を保護する。
  - [x] SSID／BSSIDを通常Log、Analytics、Crash report、通知、Metric labelへ出さない。
  - [x] Wi-Fi一致をTLS、Host、Route、User／Device／Session認証の代替にしないTestを追加する。

### 2.5 PR2検証・完了

- [x] JVM Unit TestとAndroid Instrumented TestでDAO、Migration、Process再生成、Rule、SAF permission、Wi-Fi権限・登録を確認する。
- [x] 重要Mapper／状態遷移95%以上、Android Domain／Application全体80%以上のCoverageを満たす。
- [x] `verify-android.sh`、connected Instrumented Test、SBOM、Lint、detekt、ktlint、`git diff --check`を成功させる。
- [x] Android Backup exclusion、Manifest権限、秘密情報・実環境値非混入、既存FeatureのSession分離をself-reviewする。
- [x] Commit、Push、英語Pull Request、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR3: MediaStore・SAF差分検出と永続キュー

### 3.1 開始条件・差分契約

- [x] PR3の開始条件を満たす。
  - [x] PR2が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [x] MediaStore、DocumentsContract、ContentResolver streaming、既存Upload Sourceの実装patternを確認する。
- [x] `localDocumentKey`の安定性とSource別の同一性契約を確定する。
  - [x] Raw URIや物理PathをServer識別子・Logへそのまま送らず、User／Device namespace内のopaque keyへ変換する。
  - [x] MediaStore ID再利用、SAF document ID変更、Rename／Move、更新日時精度、Provider差異の扱いをTest可能にする。

### 3.2 MediaStore差分検出

- [x] 写真・動画・音声のScannerをTest firstで実装する。
  - [x] Android version別にMediaStore generationと変更通知を利用し、Rule対象だけをStreaming列挙する。
  - [x] generationの初回、増分、rollback／reset、権限変更、削除、Provider例外を安全に扱う。
  - [x] Size・更新情報で候補を絞り、必要な候補だけContentResolverからChecksumを計算する。
  - [x] 変更通知のburstをdebounceし、同じ候補をRoom一意制約へ収束させる。

### 3.3 SAF差分検出

- [x] 任意FolderのSAF ScannerをTest firstで実装する。
  - [x] Tree配下をStreaming走査し、相対Path、Size、更新日時を保存済み索引と比較する。
  - [x] 毎回全Fileをhashせず、metadata変更候補だけを必要時にStreaming SHA-256する。
  - [x] cycle、不正Provider応答、深すぎるTree、件数過多、読取拒否、途中取消、File消失を安全に扱う。
  - [x] 走査が完走した場合だけcheckpointを進め、中断・例外時は後続再走査で取りこぼしを回収する。

### 3.4 Queue反映・一方向性

- [x] Scanner結果をRoomへ原子的に反映する。
  - [x] 新規・変更候補を`PENDING`へupsertし、同一Rule／documentの重複Queueを作らない。
  - [x] 完了済みmetadataと同一なら再Uploadせず、Server Compareが必要な曖昧候補だけを保留する。
  - [x] 端末から消えた項目は`LOCAL_MISSING`にするだけで、Delete API、Trash API、Receipt削除を呼ばない。
  - [x] 再出現、変更中File、走査中削除、複数Rule重複、Rule無効化、Source permission喪失をTestする。
- [x] Scanner起動契機を実装する。
  - [x] アプリ起動、MediaStore変更、保留追加、許可接続到達、SAF 6時間定期確認、「今すぐバックアップ」から同じCoordinatorへ収束させる。
  - [x] 同時ScannerをRule単位で一意にし、二重走査・二重Checksum・二重Queueを防止する。

### 3.5 PR3検証・完了

- [x] Fake ContentResolver／DocumentsProviderのUnit・Instrumented Testで増分、全再走査、取こぼし回収、権限喪失、取消、Process終了を確認する。
- [x] 1万件・大容量Fileを含む匿名fixtureで時間、Memory、読取Byte、hash件数、DB Batchを測定し、全件hashしないことを記録する。
- [x] 端末削除・候補省略でServer APIへ削除要求が一切送られないことをMockWebServerで確認する。
- [x] `verify-android.sh`、connected Instrumented Test、format、静的解析、`git diff --check`を成功させる。
- [x] 正式文書とtesting記録を実測へ更新し、Commit、Push、英語Pull Request、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR4: Network Policy・WorkManager・分割Upload連携

### 4.1 開始条件・実行モデル

- [x] PR4の開始条件を満たす。
  - [x] PR3が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [x] ConnectionCoordinator、AuthenticatedRequestExecutor、Upload Session再開、Network binding、Notificationの既存patternを確認する。
- [x] `BackupCoordinator`と`NetworkPolicyEvaluator`をTest firstで実装する。
  - [x] `LOCAL_DIRECT_ONLY`と`LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER`を全接続状態から`AUTO_BACKUP_ALLOWED`、`MANUAL_ONLY`、`BLOCKED`へ変換する。
  - [x] 基盤Wi-Fi／Ethernet、Route、登録Wi-Fi、任意BSSID、従量制、TLS、Server identity、HDD、Battery、Charging、User／Device／Sessionを独立に評価する。
  - [x] Local DirectをSSIDに依存させず、非ZeroTier基盤Networkへの明示bindingとHTTPS healthで再確認する。
  - [x] Mobile＋ZeroTier、未登録Wi-Fi、登録済み外部Wi-Fi＋ZeroTierなし、権限なし、HDD unavailableを必ず拒否する。

### 4.2 WorkManager予約・復旧

- [x] 一意なWork chainを実装する。
  - [x] Rule／User／接続先に安定した一意Work名を使い、同時enqueue、OS retry、Process終了、端末再起動で重複実行しない。
  - [x] WorkManagerの汎用Network制約に加えてWorker開始直後にNetwork Policyを再評価し、不許可時は短時間で安全に終了する。
  - [x] 保留Queue、MediaStore通知、SAF定期確認、アプリ起動、今すぐ実行を同じ一意chainへ収束させる。
  - [x] 強制停止中は実行不能であることをUI用状態へ公開し、常時Foreground Serviceを開始しない。
- [x] Batchと長時間処理を実装する。
  - [x] 最大100 File、合計2GB、20分のいずれかで区切り、残件を次Workerへ引き継ぐ。
  - [x] 大量転送だけForeground Worker＋進捗通知へ切り替え、通知権限拒否時のAndroid version別動作を安全に扱う。
  - [x] 初回大量バックアップの充電中条件と最低Batteryを実行前・Batch境界で再評価する。

### 4.3 Compare・Upload・状態遷移

- [x] Room候補をServer CompareへBatch送信し、結果をQueueへ反映する。
  - [x] `ALREADY_UPLOADED`を通信不要で完了へ、`NEW`／`CHANGED`だけをUploadへ進める。
  - [x] Server応答に未知Key、重複、欠落、未知reason、別Remote Fileがある場合はfail-closedにする。
  - [x] 401は既存Token refresh後に同じoperation／idempotency情報で1回だけ再送し、期限切れは認証待ちへ移行する。
- [x] 既存分割UploadをProcess境界対応で再利用する。
  - [x] Upload Session ID、Idempotency Key、offset、Source fingerprint、retry状態をRoomへ永続化する。
  - [x] 通信結果不明、429、一時503、API再起動、Network切断ではServer状態を再照会して確定offsetから再開する。
  - [x] Source変更・権限喪失・Session期限切れ・Device失効・Server競合を理由別の回復可能／要操作状態へ変換する。
  - [x] Worker停止・取消時にChunk途中を正式Fileとして公開せず、同じ端末文書の別Workerを並行送信しない。
- [x] 転送中もPolicyを再評価する。
  - [x] Wi-Fi→Mobile、Wi-Fi切替、ZeroTier切断、Route変更、HDD unavailable、Session失効でChunk境界から安全に一時停止する。
  - [x] 許可接続へ戻った後に永続QueueとServer offsetから再開し、完了済みFileを再送しない。
  - [x] 手動閲覧・手動UploadのMobile＋ZeroTier許可を後退させず、自動Backupだけを停止する。

### 4.4 進捗・履歴・可観測性データ

- [x] Rule別・全体の状態集計を提供する。
  - [x] 最終成功日時、保留、Upload中、成功、失敗、接続待ち、認証待ちの件数をRoom transactionから一貫して取得する。
  - [x] File別の失敗reason、retry回数、最終試行日時、完了履歴をUserが説明可能な範囲で保持する。
  - [x] 保持上限とcleanupを実装し、未完了QueueやServer Receipt対応を誤って削除しない。
- [x] 低CardinalityのServer／Android可観測性を実装する。
  - [x] Backup成功・失敗・待機理由、Batch件数・Byte、処理時間、retryを記録する。
  - [x] SSID、BSSID、端末Path、相対Path、ファイル名、document key、Token、User入力をLog／Metric label／通知へ含めない。

### 4.5 PR4検証・完了

- [x] WorkManager Test Driver／Room／MockWebServerで一意Work、OS retry、Process再生成、端末再起動相当、Batch継続、認証待ちを確認する。
- [x] Network matrixの全組合せと転送中切替をUnit／Instrumented Testで確認する。
- [x] API／Nginx停止、通信結果不明、offset競合、HDD unavailable、Device／Session失効、Source変更で破損・重複・無限retryがない。
- [x] `verify-android.sh`、`verify-server.sh`、`verify-config.sh`、`verify-security.sh`、connected Instrumented Test、`git diff --check`を成功させる。
- [x] 正式文書とtesting記録を実績へ更新し、Commit、Push、英語Pull Request、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR5: 設定・進捗・履歴UIと実機E2E・全体完了

### 5.1 開始条件・Navigation

- [x] PR5の開始条件を満たす。
  - [x] PR4が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [x] Settings、Home、Folder picker、Permission guidance、Notification、既存Feature navigation patternを確認する。
- [x] `feature-backup`のNavigationとSession scopeを実装する。
  - [x] Settingsから自動バックアップ設定、Rule一覧／編集、許可Wi-Fi一覧／編集、進捗・履歴へ遷移する。
  - [x] Feature間を直接依存させず、Server Folder pickerはApp callback、端末Folder pickerはSAF contractで接続する。
  - [x] Logout、User／接続先変更時に旧UserのRule、件数、履歴、Work状態を画面へ再利用しない。

### 5.2 Rule・Wi-Fi設定UI

- [x] Rule一覧・作成・編集画面を実装する。
  - [x] 端末Folder、Server保存先、有効状態、2つのNetwork mode、最低Battery、初回充電中のみを設定できる。
  - [x] 端末削除をServerへ反映しない一方向Backupであり、双方向同期ではないことを常時確認できる文言で表示する。
  - [x] SAF／Server権限喪失、共有解除、保存先状態変更、入力Error、保存中、retryを操作可能な状態で表示する。
- [x] 許可Wi-Fi一覧・登録・編集画面を実装する。
  - [x] 現在接続中Wi-Fiを明示確認後に登録し、表示名、SSID、任意BSSID制限、従量制扱い、有効状態を管理する。
  - [x] Wi-Fi情報取得権限の目的、拒否、恒久拒否、設定画面導線、取得不能を説明する。
  - [x] SSID／BSSID一致だけではServerを信用せず、外部Wi-FiではZeroTier、TLS、Server identity、認証が必要であることを表示する。

### 5.3 状態・進捗・履歴UI

- [x] Backup概要画面を実装する。
  - [x] 最終成功日時、保留、Upload中、成功、失敗件数、現在のPolicy状態を表示する。
  - [x] 条件不成立時は許可接続待ち、Battery／充電待ち、認証待ち、HDD待ち、権限待ちを区別する。
  - [x] 「今すぐバックアップ」、一時停止、再開、失敗retryを重複操作なく実行する。
  - [x] 強制停止中は実行不能、通常は常時Foreground Serviceを使わないことを案内する。
- [x] File別履歴・失敗詳細を実装する。
  - [x] 保留、Upload中、成功、失敗、端末側消失を区別し、失敗理由と次の操作を表示する。
  - [x] 履歴表示のためにSource本文や物理Pathを永続化・Log出力せず、長い名前や消失Sourceを安全に表示する。
  - [x] Paging／保持上限、Refresh、Empty、Loading、Error、Process再生成、画面回転を処理する。
- [x] Accessibilityと表示品質を確認する。
  - [x] TalkBack、48dp target、font scale 2.0、dark mode、contrast、長文、locale、日時、通知channelを確認する。
  - [x] Backup状態を色やiconだけに依存せず、文字とsemanticsで識別できるようにする。

### 5.4 実API・実機E2E

- [x] Android 10と現行Androidの実機／EmulatorでSourceとBackground制約を確認する。
  - [x] MediaStoreとSAFの新規、変更、Rename／Move、端末削除、権限喪失、再付与、取りこぼし再走査を確認する。
  - [x] アプリbackground、Process kill、端末再起動、Doze、Worker retry、強制停止案内を確認する。
  - [x] 初回大量BackupのCharging／Battery、100件／2GB／20分Batch継続、Foreground通知を確認する。
- [x] Raspberry Pi実API・実HDDで一方向性と重複防止を確認する。
  - [x] 新規File、変更Fileのatomic replace／Version増加、同一File再検出、並行Worker、通信結果不明で重複File／Version／Receiptを作らない。
  - [x] 端末削除、Rule削除、候補省略でServer File、Share、Favorite、Tag、Recent、Receiptを削除しない。
  - [x] Owner／共有先User、共有解除、Move、Rename、Trash、Restore、Missing、Purge競合で現在権限と状態を再評価する。
  - [x] API／Nginx／PostgreSQL再起動、HDD unmount／remount、Token refresh、Device／Session失効から安全に復旧する。
- [x] 実ネットワークmatrixを確認する。
  - [x] Local Directは非ZeroTier基盤Network binding、同一subnet、TLS、Server identity、認証成功時だけ実行する。
  - [x] 登録済み外部Wi-Fi＋ZeroTierは実行し、ZeroTier切断、未登録Wi-Fi、従量制扱いWi-Fiでは実行しない。
  - [x] Mobile＋ZeroTierでは自動実行せず、手動閲覧・手動Uploadだけが既存契約どおり動作する。
  - [x] 転送中のWi-Fi／Mobile／ZeroTier切替で一時停止し、許可経路復帰後に確定offsetから再開する。

### 5.5 性能・Security・全体完了

- [x] 大量Backup性能を測定し、testing文書へ記録する。
  - [x] 1万件の差分走査、初回／増分、Room容量、Battery、Memory、CPU、読取Byte、hash件数、Upload throughputを測定する。
  - [x] Raspberry Pi側のCompare latency、DB Index、Receipt容量、同時実行、HDD I/Oへの影響を測定する。
- [x] Security／Privacyを確認する。
  - [x] Release APKがnon-debuggableで、Room、Android Backup、通知、Logcat、API／Nginx／DB Log、Crash出力に秘密値・端末Path・Wi-Fi識別情報を漏らさない。
  - [x] TLS／Hostname検証無効化、ZeroTier秘密情報、実SSID／BSSID、Test credentialをRepositoryやArtifactへ含めず、実EndpointはRepositoryへ含めない（Release APKは接続に必要な公開Hostname／Routeだけを必須のBuild入力として保持し、秘密値を含めない）。
  - [x] 不正Path、改ざんdocument key、別User／Device、共有権限不足、古いCompare結果、過大Batchを拒否する。
- [x] 全自動検証を成功させる。
  - [x] `verify-android.sh`、`verify-server.sh`、`verify-config.sh`、`verify-security.sh`、`verify-deployment.sh`を成功させる。
  - [x] 全Unit／Integration／Instrumented／E2E、Coverage、Migration、OpenAPI、SBOM、format、Lint、静的解析、`git diff --check`を成功させる。
- [x] 正式文書、OpenAPI、repository structure、運用・testing記録を最終実績へ更新する。
- [x] PR5を完了する。
  - [x] 差分をself-reviewし、無関係な変更、debug code、秘密情報、実環境値がない。
  - [x] Commit、Push、英語Pull Request、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

- [x] PR5 Merge後に全体完了処理を行う。
  - [x] 全Pull RequestがMergeされた後だけ、steeringモード3-Bで全体振り返りを記録する。

---

## 各Pull Request完了記録

> 各Pull Request作成後にsteeringモード3-Aで追記する。後続Pull Requestに未完了タスクが残っていても、完了したPull Requestの記録は行う。

### PR1: Server側BackupReceipt・比較・Upload確定

- 完了日: 2026-09-02
- Pull Request: [#44 Add server-side Android automatic backup receipts](https://github.com/ry825/Kura_Storage/pull/44)
- 実施したテスト、ビルド、静的解析、手動確認:
  - `./scripts/ci/verify-server.sh`でRelease build警告0、Domain 130件、Application 336件、PostgreSQL統合224件、合計690件の成功を確認した。
  - Coverlet統合結果はDomain／Application全体7,644/8,544行（89.47%）、新規Backup Domain／Application 241/246行（97.97%）であった。
  - `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`dotnet format --verify-no-changes`、OpenAPI Contract Test、EF Core pending model確認、`git diff --check`が成功した。
  - MigrationのUp／Down／再Up、既存行保持、一意制約、Device失効時保持、File Purge時CascadeをPostgreSQL 17 Testcontainersで確認した。
  - 実一時Storageを使い、NEW／CHANGED、重複complete、取消、Version競合、共有Owner、転送中権限低下、filesystem-done recovery、Text version履歴、既存関連情報保持をAPI統合Testで確認した。別途の手動操作はなし。
  - GitHub ActionsのAndroid、Config、Security、Server必須Checkがすべて成功した。
- 計画と実装の差分:
  - Compareの認可を保存先FolderだけでなくReceiptのRemote Fileへの現在の編集権限までBatch再評価し、権限喪失時はRemote File ID／Versionを返さない`BLOCKED_CURRENT_STATE`へ強化した。
  - Steering設計内の旧Upload Session表記を既存の`/api/v1/upload-sessions`へ訂正し、Compare判定名をOpenAPI実装と一致させた。
- 実装中に追加したタスクと追加理由:
  - CHANGED対象が対応Text MIMEの場合も既存のimmutable version履歴を維持するため、検証済み内容を次Versionへ発行する処理と回帰Testを追加した。
  - 自己レビューで検出した共有権限失効後のRemote File情報露出、CompareとUploadのFile size上限差、`BackupUpdate`のDB表記を修正し、それぞれ回帰Testまたは統合Testで確認した。
  - Backup用`relativePath`追加に合わせ、物理Storage path非公開を維持しながら論理相対Pathを許可するOpenAPI Contract Testへ更新した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - PR2は#44が`main`へMergeされ必須CIが成功した後に最新`main`から開始する。
  - AndroidはUser／Device／OwnerをBackup payloadへ含めず、Compareの`NEW`／`CHANGED`だけを既存Upload Sessionの`backup` contextへ渡す。`BLOCKED_CURRENT_STATE`ではRemote Fileを推測せずUser操作待ちにする。
  - 端末側削除、Rule削除、候補省略からServer File／Receiptを削除するAPIは追加しない。

### PR2: Android Room・バックアップルール・外部Wi-Fiポリシー

- 完了日: 2026-09-02
- Pull Request: [#45 Add Android automatic backup foundation](https://github.com/ry825/Kura_Storage/pull/45)
- 実施したテスト、ビルド、静的解析、手動確認:
  - `./scripts/ci/verify-android.sh`で全JVM Unit Test、Coverage gate、CycloneDX SBOM、ktlint、detekt、Android Lint、Debug APK／AndroidTest APK assemblyの成功を確認した。
  - 新規Backup JVM Test 27件が成功し、Backup重要箇所は309/314行（98.41%）、Android Domain／Application全体は4,413/5,258行（83.93%）であった。
  - Android 13実機CPH2333で`:core-data:connectedDebugAndroidTest` 7件と`:core-database:connectedDebugAndroidTest` 4件、合計11件が成功し、権限fail-closed、Room初期Schema、一意制約、原子的claim、期限切れlease回復、Account隔離、cascade、close／reopenを確認した。
  - Repository全体のconnected Testは既存App Compose suite実行時の実機sleepと、その後のADB切断により完走できなかった。変更対象2 Moduleのconnected TestとRepository全体のAndroidTest APK assemblyは成功済みである。
  - `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`git diff --check`が成功し、GitHub ActionsのAndroid、Config、Security、Server必須Checkがすべて成功した。
  - SBOMへRoom 2.8.4とWorkManager 2.11.2が含まれること、Room DB／WAL／共有MemoryがAndroid Backupと端末移行から除外されること、実SSID／BSSID・URI・Token・実環境値が差分とLog出力に含まれないことを確認した。
- 計画と実装の差分:
  - Wi-Fi Policy編集時にも作成時と同じAccount Scopeを必須化し、別AccountのPolicy IDを指定した更新を拒否する境界へ強化した。
  - Android version別権限判定はNearby Wi-Fiだけでなく、対象OSでSSID／BSSID取得に必要なCoarse／Fine LocationとLocation serviceを個別に判定し、取得不能時はfail-closedにした。
  - WorkManagerの実Worker／予約はPR4範囲のままとし、PR2では依存、Module境界、安定したhash済みWork名の契約までを実装した。
- 実装中に追加したタスクと追加理由:
  - 自己レビューで検出した別AccountへのWi-Fi Policy更新、正規化後のSSID／BSSID重複、BSSID形式不正を拒否するValidationと回帰Testを追加した。
  - 状態件数、Scanner checkpoint、要求可能権限、追加状態遷移のCoverage不足を補うTestを追加し、重要箇所95%以上と全体80%以上を満たした。
  - 旧MVP依存禁止を保持していた`verify-config.sh`を、Roomは`core-database`、WorkManagerは`feature-backup`へ限定するPhase 1境界に更新し、PDF系禁止依存の検査は維持した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - PR3は#45が`main`へMergeされ必須CIが成功した後に最新`main`から開始する。
  - Scannerは既存のAccount Scope、Rule、checkpoint、500件単位のRoom transaction、一意な`(ruleId, localDocumentKey)`へ収束させ、端末削除や候補省略からServer削除APIを呼ばない。
  - MediaStore／SAFの走査が完走した場合だけcheckpointを進め、Provider例外、権限喪失、途中取消時は既存Server状態を推測せず後続再走査へ収束させる。
  - PR4までWorkManager予約やNetwork Policyによる自動転送を先行実装せず、PR3は差分検出と永続Queue反映に限定する。

### PR3: MediaStore・SAF差分検出と永続キュー

- 完了日: 2026-09-03
- Pull Request: [#46 Add Android backup source scanning and persistent queue](https://github.com/ry825/Kura_Storage/pull/46)
- 実施したテスト、ビルド、静的解析、手動確認:
  - `./scripts/ci/verify-android.sh`で全JVM Unit Test、Coverage gate、Debug APK／AndroidTest APK assembly、CycloneDX SBOM、ktlint、detekt、Android Lintが成功した。最終再実行は4分35秒で完了した。
  - Backup重要箇所は310/314行（98.73%）、Android Domain／Application全体は4,581/5,488行（83.47%）であった。
  - Android 13／API 33 AOSP ATDとAndroid 13実機CPH2333の両方で`:core-data:connectedDebugAndroidTest` 10件、`:core-database:connectedDebugAndroidTest` 7件がすべて成功した。
  - 匿名1万件fixtureは189msで走査し、変更10件だけをhashした。読取量10MiB、DB相当Batch 20回（各500件）、計測command最大RSS 119,468KiB、MockWebServerの削除／Trash要求0件を確認した。
  - `./scripts/ci/verify-security.sh`と`git diff --check`が成功し、GitHub ActionsのAndroid、Config、Security、Server必須Checkがすべて成功した。
  - 差分に実URI、物理Path、資格情報、実環境値、Server削除／Trash／Receipt削除処理が含まれないことを確認した。
- 計画と実装の差分:
  - Connected Testは利用環境に合わせAndroid 13を正式な実機・Emulator gateとし、API 30以降のMediaStore generation経路を確認した。
  - MediaStoreはAPI 29でgeneration列とselectionを使用しないversion-only fallbackを明示し、API 30以降だけgeneration増分条件を組み立てる実装へ強化した。
  - Rule単位の同時Triggerは同じ`CompletableDeferred`へ収束させ、二重走査だけでなく二重Checksumと二重Room反映も防止した。
- 実装中に追加したタスクと追加理由:
  - 安定したopaque keyをProvider ID再利用から分離するため、Room schema 2へSource identity mappingを追加し、Migration、再open、Account隔離、一意制約のInstrumented Testを追加した。
  - Full scan完了時の`LOCAL_MISSING`化とcheckpoint更新を同一Transactionへまとめ、Remote File参照保持と再出現時`PENDING`復帰の回帰Testを追加した。
  - Android JUnit4で式本体が非`Unit`になるTestとRoomの`Long`値比較をAndroid 13 connected実行で検出し、型を明示した回帰Testへ修正した。
- 技術的に不要になったタスク、理由、代替実装:
  - API 29 EmulatorでのConnected Testは、対象環境がAndroid 13であるためPR3の正式gateには使用しなかった。Android 10互換分岐はversion-gated実装と自動テスト、全Android build gateで維持した。
- 後続Pull Requestへの引継ぎ事項:
  - PR4は#46が`main`へMergeされ必須CIが成功した後に最新`main`から開始する。
  - Network Policy、WorkManager、一意Work chain、Server Compare、分割Upload、転送中接続再評価はPR4で実装し、PR3のScanner／Room Queue契約を再利用する。
  - 端末削除、Rule削除、候補省略からServer File／Receiptを削除せず、`LOCAL_MISSING`と再走査から一方向Backupへ収束させる。

### PR4: Network Policy・WorkManager・分割Upload連携

- 完了日: 2026-09-03
- Pull Request: [#47 Implement Android automatic backup transfer orchestration](https://github.com/ry825/Kura_Storage/pull/47)
- 実施したテスト、ビルド、静的解析、手動確認:
  - `./scripts/ci/verify-android.sh`で全JVM Unit Test、Coverage gate、Debug APK／AndroidTest APK assembly、CycloneDX SBOM、ktlint、detekt、Android Lintが成功した。最終実行は5分21秒で完了した。
  - Backup重要箇所は310/314行（98.73%）、Android Domain／Application全体は4,988/6,054行（82.39%）であった。
  - Android 13／API 33実機CPH2333で`:core-database:connectedDebugAndroidTest` 9件、`:core-data:connectedDebugAndroidTest` 10件、`:feature-backup:connectedDebugAndroidTest` 2件、`:app:connectedDebugAndroidTest` 4件が成功した。App suiteの初回UI実行は端末固有の10秒sleep overrideで無効になったため、画面を維持して4/4件の成功を再確認した。
  - Network matrix、転送中Policy切替、Compare応答不整合、Source変更、Remote競合、認証待ち、上限付きretry、Upload Session offset再開、一意Work、OS retry、Process再生成をJVM／Instrumented／MockWebServer Testで確認した。
  - `./scripts/ci/verify-server.sh`でDomain 130件、Application 336件、PostgreSQL統合224件、合計690件が成功した。`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`git diff --check`も成功した。
  - GitHub ActionsのAndroid、Config、Security、Server必須Checkがすべて成功した。
- 計画と実装の差分:
  - Android 13でZeroTier VPNがactive networkになる場合も基盤Wi-Fiを判定できるよう、非VPN Wi-Fi networkを列挙してSSID／BSSID Policyへ渡し、Route、TLS、Server identity、明示bindingの検証は独立して維持した。
  - Process再生成後のWorkerがActivity状態へ依存しないよう、Application所有のRuntime FactoryへRoom、接続判定、認証済みRemoteを再構築する構成にした。
  - Android 13で`POST_NOTIFICATIONS`が拒否された大量転送はForeground開始を試みず、永続Queueを変更しないpermission-required結果として後続UIから説明可能にした。
- 実装中に追加したタスクと追加理由:
  - 未接続時にも保存済み資格情報とNetwork待ちを区別するため、Remote session生成とは独立したcredential存在確認をApplication runtimeへ追加した。
  - App moduleへRoom依存を漏らさずApplicationからDBを再構築するため、`core-database`にRoom非依存の`BackupDatabaseAccess`境界を追加し、構成検証でModule分離を確認した。
  - Server Compareの未知・重複・欠落key、別Remote File／VersionとUpload offset不整合をfail-closedにする回帰Testを追加した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - PR5は#47が`main`へMergeされ必須CIが成功した後に最新`main`から開始する。
  - 設定・進捗・履歴UIはPR4の状態集計、待機理由、通知権限要求、強制停止案内、一意Coordinatorを利用し、SSID／BSSIDや端末Pathを表示・記録しない。
  - Android 13実機とRaspberry Pi実API／実HDDでNetwork切替、Process終了、再起動、API／DB再起動、HDD unmount／remountを検証し、PR5完了後だけモード3-Bの全体振り返りを行う。

### PR5: 設定・進捗・履歴UIと実機E2E・全体完了

- 完了日: 2026-09-03
- Pull Request: [#48 Complete Android automatic backup UI and end-to-end verification](https://github.com/ry825/Kura_Storage/pull/48)
- 実施したテスト、ビルド、静的解析、手動確認:
  - `./scripts/ci/verify-android.sh`で全JVM Unit Test、Coverage、CycloneDX SBOM、ktlint、detekt、Android Lint、Debug APK／AndroidTest APK assemblyが5分28秒で成功した。
  - `./scripts/ci/verify-server.sh`でRelease build警告0、Domain 130件、Application 336件、PostgreSQL統合224件、合計690件が成功した。
  - Android API 29とAPI 36の最終Connected suiteはそれぞれ34件が成功した。Android 13実機では31件に加え、実SAF treeから新規・変更・不変・端末削除の一方向Backupとforce-stop後の履歴保持を確認した。
  - API 36 Emulatorでdeep Doze中に常時Serviceがないこと、復帰後にWorkManagerが継続可能なことを確認した。1万件走査／Room、100件・2GiB・20分Batch境界、Accessibility、dark mode、font scale 2.0を自動Testと計測で確認した。
  - Raspberry Pi実API／実HDDでNEW／CHANGED／unchanged、Version・Receipt一致、冪等確定、HDD unmount時fail-closed、remount、Nginx／API／PostgreSQL再起動後の復旧、Compare／Upload性能を確認した。
  - `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`dotnet format --verify-no-changes`、OpenAPI、Migration、release APK non-debuggable／v3署名、秘密情報scan、`git diff --check`が成功した。
  - GitHub ActionsのAndroid、Config、Security、Server必須Checkがすべて成功した。
- 計画と実装の差分:
  - 履歴は全件をMemoryへ読んで表示だけ分割せず、Room query自体を最大10,001件の段階的Pagingにし、単一失敗項目と全失敗項目のretryをAccount Scope付き更新へ分離した。
  - 2GiB上限を超える次FileでBatch全体が停止し続けないよう、先頭Fileは残りByte budget単位でChunk継続し、2件目以降だけ次Batchへ回す境界へ修正した。
  - 実HDD検証でKuraStorage自身の書込み後にindex WorkerがFile versionだけを進める競合を検出したため、通常完了とrecovery完了の両方で実ファイルのsize、MIME、mtime、source keyを観測し、PostgreSQLのmicrosecond精度へ揃えた。
  - Android 10に加えて現行Androidの最終差分を保証するためAPI 36 Emulatorを追加し、Connected suiteとdeep Dozeを確認した。
- 実装中に追加したタスクと追加理由:
  - Logout／User／Device／接続先変更時のRoom・Work・UI隔離に、認証済みUser IDが必要だったためToken応答契約へ`userId`を追加し、Client payloadの本人性には使用しない回帰Testを追加した。
  - Rule削除がServer Fileを削除しないことを操作時にも明確にするため、確認DialogとCompose Testを追加した。
  - 性能要件を端末DB実装まで確認するため、API 29でRoom 1万行の挿入時間とDB容量を追加計測した。
  - 実HDD上のindex競合修正を保証するため、通常変更完了とfilesystem-done recovery後にapply-mode index scanを行うServer統合Testを追加した。
  - detektのTest fixture引数上限を満たすため、転送Repository Testの構成値を`RepositoryOptions`へ集約した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - #48をMergeした後にだけ、steeringモード3-Bで全体振り返りを記録する。Codexは本Pull RequestをMergeしない。
  - 四つの匿名な旧rc1 E2E fixtureは修正前に発生したFile／Receipt version不一致を保持しており、検証では履歴改変を避けて更新・削除していない。rc3で作成したfixtureは一致を維持している。
  - API 36確認用の一時AVDは検証後に完全削除済みであり、必要なら同一system imageから再作成する。

## 全体振り返り

- 実装完了日: 2026-09-03
- 完了したPull Request:
  - [#44 Add server-side Android automatic backup receipts](https://github.com/ry825/Kura_Storage/pull/44)
  - [#45 Add Android automatic backup foundation](https://github.com/ry825/Kura_Storage/pull/45)
  - [#46 Add Android backup source scanning and persistent queue](https://github.com/ry825/Kura_Storage/pull/46)
  - [#47 Implement Android automatic backup transfer orchestration](https://github.com/ry825/Kura_Storage/pull/47)
  - [#48 Complete Android automatic backup UI and end-to-end verification](https://github.com/ry825/Kura_Storage/pull/48)
- 全体の計画と実績の差分:
  - 計画どおり、PR1でServer側Receipt・Compare・Upload確定、PR2でAndroidのRoom・Rule・Wi-Fi Policy基盤、PR3でMediaStore・SAF走査と永続Queue、PR4でNetwork Policy・WorkManager・分割Upload連携、PR5で設定・進捗・履歴UIと実機E2Eを完成させた。各PRは先行PRのMerge後に進め、機能境界と検証範囲を分離した。
  - 実装中に、Source identity mapping、転送中のPolicy再評価、Application所有のRuntime Factory、Account Scope付きPaging・retry、認証応答の`userId`、index WorkerとBackup更新のmtime競合対策を追加した。いずれも再起動、User切替、大量転送、実HDDで検出した整合性要件を正式設計とTestへ反映したものである。
- 主な設計変更と理由:
  - Serverは既存のResumable UploadをBackup contextで再利用し、Receipt・Remote File version・操作時の現在権限をCompareと確定の両方で検証する構成にした。通信結果不明や共有解除後に重複File・Versionを作らないためである。
  - AndroidはRoomのAccount Scope付きRule・Policy・Queue・Receiptを正とし、一意Workと期限付きclaimでProcess kill・端末再起動後も収束させた。Activityから独立して復元でき、旧Userの状態を再利用しないことを優先した。
  - Local Directと登録済み外部Wi-Fi＋ZeroTierの判定を、SSID／BSSID一致だけでなく基盤Network binding、Route、TLS、Server identity、認証に分離した。Wi-Fi情報を信頼境界の代替にしないためである。
  - 自動Backupを端末からServerへの一方向に限定し、端末削除、Rule削除、候補省略からServer Fileや関連データを削除しない契約をAPI、Room、UI、E2Eで一貫して保護した。
- 技術的な学び:
  - 自動Backupの冪等性はClientのQueueだけでは保証できず、Server offset、Receipt、Remote version、永続Work identityを組み合わせて初めて、Process・Network・API・DBの各中断後に同じ結果へ収束できる。
  - AndroidでZeroTier VPNがactive networkになる場合は、非VPN Wi-Fiの取得と通信経路への明示bindingを分けて評価する必要がある。許可情報が取得できない場合はfail-closedとすることで、OS差分を安全側に吸収できた。
  - 実exFAT HDDではKuraStorage自身の書込みとindex Workerが競合し得る。更新確定時の物理snapshotとDBの時刻精度を揃え、後続rescanでVersionが不要に進まないことを実Storage統合Testで保護する必要がある。
  - 1万件走査、100件／2GiB／20分Batch、Doze、force-stop、API／DB／HDD障害は、Unit Testだけでは判定できない。性能fixture、Android version別Connected Test、Raspberry Pi実E2Eを段階的に組み合わせることが有効だった。
- プロセス上の改善点:
  - Android 13実機のsleep・画面lockにより、Repository全体のConnected suiteの初回試行が無効になる場面があった。実機条件をpreflightで固定し、製品障害とtest harness障害を早期に分離する必要がある。
  - API 29、Android 13実機、API 36の最終matrixは有効だったが、Android 10分岐と現行OS分岐の実行条件をPR開始時から固定すれば、後半のEmulator準備と再検証をより予測可能にできる。
  - 大量Backupと実HDD E2Eで作成する匿名fixtureは、開始時にprefix、想定件数、後処理、保持が必要な障害証跡を明示すると、検証後の環境判定を容易にできる。
- 次回への改善提案:
  - Android OS別の権限、Wi-Fi／VPN構成、Battery、Doze、通知許可、Process状態を共通のE2E matrixとpreflight scriptにまとめ、実機・Emulator検証の再現性を上げる。
  - Backupの長期運用では、実データをLogへ出さず、待機理由、retry回数、Batch時間、Receipt整合性、Room増加量を定期的に確認する運用チェックを追加する。
  - Webクライアント等の後続機能でBackup Receiptを再利用する場合も、端末削除をServer削除へ伝播しない一方向契約と、現在権限の再評価を先に固定する。
- 未実装・技術的に不要になったタスク: 本Steeringに未実装タスクはない。PR3のAPI 29 Emulator実行は対象端末条件に合わせてAndroid 13を正式gateとし、API 29分岐はversion-gated実装・自動Test・全Android buildで保護した。最終PRではAPI 29とAPI 36のConnected suiteが成功し、Android 10と現行Androidの受け入れ条件を補完した。
