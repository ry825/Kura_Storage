# 分割アップロード・中断再開 要求仕様

## 概要

大容量ファイルを複数のChunkへ分けて送信し、通信中断後にServerが確定した位置から再開できるUpload Sessionを追加する。Session作成、Chunk検証、完了時の全体検証と原子的公開、取消、Device失効、期限切れSession清掃を一貫して管理し、Androidの手動アップロードと将来の動画・Webアプリ・自動バックアップで再利用できる転送基盤とする。

## 背景

現行の`POST /api/v1/files/upload`は、File全体をMemoryへ保持しないStreaming Multipart Uploadを提供している。一方、通信中断時は同じ`Idempotency-Key`でFile先頭から全体を再送するため、大容量動画、低速または不安定なネットワーク、長時間転送では再送量と完了までの時間が大きくなる。

今後のWebアプリと自動バックアップも大容量転送を利用するため、Client固有の処理ではなく、認証、認可、容量、冪等性、Storage安全境界を共有できるUpload Sessionが必要である。また、分割転送ではChunkの欠落、重複、順序違反、破損、Session期限切れ、Server停止、DBとHDDの途中状態が新たに発生するため、不完全Fileを公開せず、安全に再開、清掃、復旧できなければならない。

## 実装対象の機能

### 1. Upload Sessionの作成と状態照会

- 認証済みClientが、保存先Folder、File名、Content Type、期待Size、任意の全体SHA-256を指定してUpload Sessionを作成できる。
- UserとDeviceはAccess TokenおよびServer側認証Contextから取得し、Client指定値を認証・認可の根拠にしない。
- Session作成時に保存先、所有権、名前、期待Size、Storage利用可否、空き容量、設定上限、同時実行上限を検証する。
- Session作成には`Idempotency-Key`を使用し、同一User・同一Key・同一Metadataの再送は既存Sessionまたは完了済みFileへ収束させる。
- 同じKeyを異なるFile、名前、保存先、Size、Checksum等へ再利用した場合はConflictとして拒否する。
- ClientはSessionの状態、Serverが確定した受信済みByte、次に送信すべきOffset、Chunk上限、期限、再開可否、完了Fileを照会できる。
- 他Userまたは許可されないDeviceはSessionの存在、Metadata、進捗を取得できない。
- API Response、Log、AuditへStorageの物理Pathを公開しない。

### 2. Chunk転送と検証

- Fileを複数Chunkとして送信し、Client、Nginx、APIのいずれもFile全体をMemoryまたはRequest Bufferへ保持しない。
- 初回実装はServerが示すOffsetから連続した順序でChunkを受け付け、並列または順不同のChunk送信は受け付けない。
- 各Chunkについて、Session、認証User・Device、状態、有効期限、Offset、宣言長、実受信長、設定上限、File全体Size超過を検証する。
- 承認済み設計で定めたChunk Checksumを受信中に計算し、指定値と一致しないChunkを確定しない。
- 正常なChunkだけを一時ファイルとSession進捗へ確定し、途中切断、短いBody、長いBody、Checksum不一致、I/O Errorでは確定Offsetを進めない。
- 確定済みChunkの再送は、同じOffset、長さ、Checksumまたは同等の内容一致条件を満たす場合だけ冪等成功とする。
- 同じOffsetへ異なる内容を送る再試行、未来Offset、Gap、Overlap、負Offset、File範囲外を明確なErrorとして拒否する。
- 不完全Chunkを書き込んだ場合は、安全な確定Offsetまで切り戻し、次回の状態照会と再送で復旧できる。
- 同一SessionへのChunk送信、完了、取消、期限切れ、Device失効を複数API Process間でも競合安全に直列化する。

### 3. 中断再開とAndroid手動アップロード

- AndroidはSAFの`content://` URIからFileをStreamingし、Serverが確定したOffsetを進捗と再開位置の正として使用する。
- 通信切断、Response不明、Token更新、`429`、一時的な`503`の後にSession状態を照会し、確定済みByteを再送せず次Offsetから再開できる。
- 401後のToken更新、通信再試行、Offset再同期では、同じSession、`Idempotency-Key`、Chunk範囲、Chunk内容を維持する。
- Androidは送信済み容量、全体容量、進捗、再開中、完了検証中、成功、取消、失敗を区別して表示する。
- 再試行可能な通信障害と、期限切れ、Device失効、SAF権限喪失、送信元File変更等のユーザー対応が必要な状態を区別する。
- 送信元のSizeまたは内容がSession作成時から変化した場合、既存Sessionへ異なるPayloadを継続せず、新しい操作として扱う。
- 明示取消ではServer Sessionを取消し、単なる画面遷移や一時的なCoroutine Cancellationを誤って取消として扱わない。
- 完了後はServerからFile一覧を再取得し、通信結果不明時にClientが完了を推測して未確定Fileを表示しない。
- Android Process終了をまたぐ永続Queue、自動再開、OS制約下の長時間Background Uploadは今回の対象に含めない。

### 4. 完了検証とFile公開

- Session完了要求は、受信済みByte、一時ファイルの実Size、期待Sizeを再検証する。
- 全体SHA-256が指定されている場合は一時ファイルからStreaming計算し、一致した場合だけ公開する。
- SizeまたはChecksumが一致しない場合、FileEntryを作成せず、原因と再送可否をClientへ返す。
- 保存先Folderの状態、認可、同名競合、Storage状態、空き容量を公開直前にも再検証する。
- 完了処理は、既存のFileOperation、同一Filesystem上のatomic rename、DB Transaction、Audit、Recovery規則と整合させる。
- Session完了前の一時ファイルは、一覧、詳細、Download、検索、共有等の通常File操作から参照できない。
- 完了成功後はFileEntryを1件だけ作成し、Range Download、Trash、Restore、Rename、Move、Purge等で通常Fileと同様に扱える。
- 同じ完了要求を再送してもFileEntry、FileOperation、Auditを重複作成せず、同じ完了Fileへ収束する。
- 完了直前またはatomic rename後のProcess停止から、二重公開やFile喪失を起こさず復旧できる。

### 5. Sessionの取消、期限切れ、Device失効、清掃

- 未完了SessionにはServer UTC基準の有効期限を設定し、Client時刻を期限判定へ使用しない。
- Session期限、Chunk Size、File Size、同時Session・Chunk数、Cleanup間隔、Batch Sizeは型付き設定として検証する。
- Clientは自分の再開可能なSessionを明示的に取消でき、同じ取消要求の再送は冪等に成功する。
- Deviceが失効した場合、そのDeviceに属する未完了SessionへのChunk送信、状態照会、完了を拒否し、一時ファイルを安全に清掃対象へ移す。
- API内の期限付きHosted Serviceが起動時と設定周期に期限切れSessionをDBからBatch取得し、一時ファイルを削除して期限切れ状態へ確定する。
- Cleanupは期限順・ID順で処理し、複数Processの重複実行、転送中Sessionの誤削除、HDD全体走査を防ぐ。
- 1件の清掃失敗でBatch全体を停止せず、再試行可能な状態または`RECOVERY_REQUIRED`を残す。
- 取消、期限切れ、Device失効、Cleanup、Recoveryが競合しても、一時ファイルの誤削除、二重公開、状態巻き戻りを起こさない。
- Cleanup失敗、一時ファイル残存、Recovery Requiredを管理者が既存の運用手段で確認できる。

### 6. 障害復旧とStorage安全境界

- DBの確定Offsetと一時ファイル長が一致する正常中断は再開可能として扱う。
- 一時ファイルが確定Offsetより長い場合、安全に切り戻せる範囲だけをtruncateして再開可能にする。
- 一時ファイルが確定Offsetより短い、欠落している、Symbolic Linkである、Storage IDが一致しない等の状態を成功として推測しない。
- 自動判断できないDB・HDD不整合は`RECOVERY_REQUIRED`として隔離し、新規Chunk、完了、通常File公開を拒否する。
- HDD未Mount、read-only、容量不足、DB停止時にOS RootやStorage Root外へ一時Fileまたは公開Fileを書き込まない。
- Path Traversal、絶対Path、管理Root、Symbolic LinkをUploadの作成、追記、truncate、削除、公開の全段階で拒否する。
- Storageが利用できない間は物理状態を推測せず、利用可能になった後のRecoveryまたはCleanupへ延期する。
- 既存のStreaming Multipart Upload、FileOperation Recovery、Storage Guard、Lock規則を壊さない。

### 7. 後方互換性と将来Consumer境界

- 既存の`POST /api/v1/files/upload`と、同一`Idempotency-Key`による先頭からの全体再試行契約を維持する。
- Upload Sessionは既存Endpointの意味を暗黙に変更せず、独立したAPI契約として追加する。
- 旧Android ClientはServer更新後も既存Multipart Uploadを利用できる。
- 新しいAndroid Clientが旧Serverへ接続した場合の対応をProtocol Versionまたは明確な非対応Errorとして定義する。
- Upload SessionのApplication契約はAndroid Frameworkへ依存せず、将来のWeb Clientと自動バックアップが再利用できる。
- 動画等のContent Typeまたは大容量であることだけを理由に一律拒否せず、明示的なFile Size上限とStorage容量の範囲で扱う。
- 自動バックアップ固有の比較、Receipt、置換、端末文書Metadataは今回実装せず、後続機能がSessionへ拡張できる境界だけを維持する。

### 8. Security、資源制御、観測性

- すべてのSession APIで認証、Device状態、User所有権、保存先認可をServer側で検証する。
- 他Userまたは許可されないDeviceのSessionを、存在有無やMetadataを漏えいしないErrorで拒否する。
- File全体、Chunk全体、File名、相対・絶対Path、Checksum全文、Token、認証情報を不要にLogまたはAuditへ保存しない。
- 同時Session数、同時Chunk受付数、最大Chunk Size、最大File Sizeを設定で制限し、過負荷時は`Retry-After`を伴う再試行可能な応答を返す。
- Session作成数、受信Byte、再開、完了、取消、期限切れ、Cleanup、Recovery、失敗理由、処理時間を低CardinalityのMetricで確認できる。
- UploadとCleanupがAPIのHealth、認証更新、一覧、Range Download等を長時間停止させない。
- ServerとAndroidのMemory使用量がFile Sizeに比例して増加しない。
- Release、Migration、設定、Nginx、Rollback、未完了Sessionの運用手順を文書化する。

## 受け入れ条件

### Upload Session作成・照会

- [ ] 認証済みAndroid Clientが有効な保存先とMetadataでSessionを作成し、Session ID、次Offset、Chunk上限、有効期限を取得できる。
- [ ] 同一User・同一`Idempotency-Key`・同一Metadataの再送は同じSessionまたは完了Fileを返す。
- [ ] 同じKeyを異なるMetadataへ再利用するとConflictになり、新しいSessionやFileを作成しない。
- [ ] 他User、許可されないDevice、失効DeviceはSessionを作成・照会・更新・完了できない。
- [ ] 保存先不正、同名競合、容量不足、Storage未利用、上限超過を一時ファイル作成前に拒否する。

### Chunk検証・中断再開

- [ ] 複数Chunkを連続Offsetで送信し、受信済みByteと一時ファイル長が一致する。
- [ ] 通信切断後、Server状態を照会し、確定済みByteを再送せず次Offsetから転送を再開できる。
- [ ] APIまたはNginx再起動後も、整合した未完了Sessionを同じOffsetから再開できる。
- [ ] 正常な同一Chunk再送は冪等成功し、異なる内容の同一Offset再送は拒否される。
- [ ] Checksum不一致、短いBody、長いBody、Gap、Overlap、未来Offset、負Offset、Size超過では確定Offsetが進まない。
- [ ] Chunk送信途中の切断またはProcess停止後に、不完全Chunkを公開せず、確定Offsetから再送できる。
- [ ] AndroidはSAFからStreamingし、送信済み容量、進捗、再開中、検証中を表示できる。
- [ ] 401 Token更新、`429`、一時的な`503`、通信結果不明の再試行で同じSession、Key、Offset、Chunk内容を維持する。
- [ ] 送信元File変更、SAF権限喪失、期限切れ、Device失効を検出し、異なるPayloadを既存Sessionへ送らない。

### 完了・公開・復旧

- [ ] 全Chunk受信後、期待Sizeと指定された全体SHA-256が一致した場合だけFileを公開できる。
- [ ] SizeまたはChecksum不一致ではFileEntryを作成せず、不完全Fileを一覧・詳細・Downloadへ表示しない。
- [ ] 完了済みSessionへの完了再送は同じFileを返し、FileEntry、FileOperation、Auditを重複作成しない。
- [ ] 完了成功後のFileをRange Downloadし、送信元とSize・SHA-256が一致する。
- [ ] 完了FileをTrash、Restore、Rename、Move、Purgeで通常Fileと同様に操作できる。
- [ ] 完了直前、atomic rename後、DB確定中の停止から、Fileが0件または1件だけ存在する安全な状態へ復旧する。
- [ ] 判断不能なDB・HDD不整合は`RECOVERY_REQUIRED`となり、新規Chunk、完了、通常公開を拒否する。

### 取消・期限切れ・清掃

- [ ] 明示取消したSessionは再開・完了できず、一時ファイルが冪等に削除される。
- [ ] 有効期限前のSessionをCleanupが削除しない。
- [ ] 有効期限に到達した未完了Sessionを、起動時および定期CleanupがBatch単位で清掃する。
- [ ] Cleanup、Chunk送信、完了、取消、Device失効が競合しても、有効な一時ファイルの誤削除、二重公開、状態巻き戻りを起こさない。
- [ ] 1件のCleanup失敗後も他Sessionを処理し、失敗Sessionを次回再試行または`RECOVERY_REQUIRED`として追跡できる。
- [ ] CleanupはDB候補だけを処理し、HDD全体走査やAPI応答の長時間停止を行わない。

### Security・大容量・回帰

- [ ] Path Traversal、絶対Path、Symbolic Link、Storage Root外、HDD未Mount、read-onlyへの書込みを拒否する。
- [ ] Log、Audit、Metric、API ResponseにFile内容、物理Path、Checksum全文、Token、秘密情報を出力しない。
- [ ] 大容量動画をAndroid実機から分割アップロードし、Android HeapとServer RSSがFile Size比例で増加しない。
- [ ] 大容量Upload中もHealth、認証更新、一覧、Range Downloadが許容範囲で応答する。
- [ ] 既存`POST /api/v1/files/upload`と、その同一Keyによる全体再試行が引き続き成功する。
- [ ] 既存の一覧、詳細、Folder作成、Range Download、Trash、Restore、Rename、Move、Purgeへ回帰がない。
- [ ] Server・Androidの必須CI、Migration適用・Rollback、Security検査、Android実機E2Eが成功する。
- [ ] 正式文書、OpenAPI、設定例、Nginx、運用手順と実装が一致する。

### 将来Consumer境界

- [ ] Upload SessionのAPI・Application契約がAndroid固有型に依存していない。
- [ ] BrowserのFile Streamから同じSession APIを利用できる契約になっている。
- [ ] 後続の自動バックアップが認証済みDeviceとBackup Metadataを付加してChunk処理を再利用できる境界になっている。
- [ ] Web UI、自動バックアップ本体、Room、WorkManagerを今回の完了対象として誤って実装または記録していない。

## 成功指標

- 正常完了または復旧完了後の、送信元と公開FileのSize・SHA-256不一致: 0件。
- Session完了前または検証失敗時に通常Fileとして公開される項目: 0件。
- 冪等再送または競合処理による重複FileEntry・重複公開: 0件。
- 期限前SessionのCleanupによる誤削除: 0件。
- 期限切れSessionの次回正常Cleanup完了後に残る対象一時ファイル: 0件。
- 通信中断後の正常再開で再送する確定済みByte: 0 Byte。ただし応答不明Chunkの冪等確認に必要な再送は除く。
- 大容量Upload中のClient・ServerでのFile全体Buffering: 0件。
- 他User・許可されないDeviceによるSession情報取得、Chunk送信、完了: 0件。
- UploadによるStorage Root外書込み、物理Path・内容・認証情報の漏えい: 0件。

## スコープ外

以下はこの作業では実装しない。

- Webアプリ、Web UI、Browser向け認証方式そのもの。
- Android自動バックアップ、Backup Compare、Backup Receipt、既存File内容のatomic replace。
- Room、WorkManager、MediaStore差分監視、OS制約下の長時間Background Upload。
- Android Process終了または端末再起動をまたぐUpload Queueの永続化と自動再開。
- 並列Chunk Upload、順不同Chunk Upload、複数端末による同一Sessionへの同時送信。
- File全体の暗号化、Client側暗号化、重複排除、圧縮、Delta Upload。
- Object Storageまたは外部Cloud StorageへのMultipart Upload。
- 一般Internetへ公開するUpload Service。
- 既存同名Fileの上書きまたは内容置換。競合時は既存Fileを保持して拒否する。
- Upload済みFileの動画変換、Thumbnail生成、Preview、Streaming再生。
- Cleanup専用の新しい独立Worker。期限切れSession清掃は既存方針どおりAPI内Hosted Serviceで行う。

## 参照ドキュメント

- `docs/product-requirements.md` 5.4、12、14、15
- `docs/functional-design.md` 5.5、6.2.6、8.8、11.5、13、15、18
- `docs/architecture-design.md` 10、11.5、12、15.4、16
- `docs/repository-structure.md` 5、6、7.1、8、10
- `docs/development-guidelines.md` MVP実装規約、4、5、9、10
- `.steering/20260722-kurastorage-mvp/` - 既存Streaming Multipart Upload、Recovery、Android Transferの実装計画
- `.steering/20260817-file-rename-move/` - 既存File操作のLock、Recovery、Android状態管理の実装パターン
- `.steering/20260820-trash-permanent-delete-retention/` - Cleanup Batch、Worker競合、Storage容量、運用確認の実装パターン
- `.steering/20260822-resumable-chunk-upload/tasklist.md` - 本要求を実装・検証するタスクとPull Request分割
