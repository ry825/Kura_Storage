# Android自動バックアップ 要求内容

## 概要

Android端末内の写真、動画、音声、または利用者が選択した任意フォルダから、新規・変更されたファイルだけをKuraStorageへ自動的に一方向バックアップする。既存の認証、分割アップロード、File索引、検索、共有権限、状態管理、Version、操作履歴を再利用し、Roomによる永続状態、WorkManagerによるバックグラウンド実行、許可Wi-Fi規則、進捗・履歴表示を追加する。

## 背景

現在は利用者がファイルを選び、手動アップロードを開始する必要がある。写真などを継続的に保存する用途では、選択漏れ、通信中断、Process終了、端末再起動によりバックアップされないファイルが生じやすい。一方、毎回全ファイルを読み直す方式は端末のBattery、通信量、HDD、Raspberry Piの処理能力へ過大な負荷を与える。

自動バックアップでは、端末とServerのどちらかを正として双方向同期するのではなく、端末からServerへ内容を追加・更新するだけの安全な一方向処理が必要である。また、SSID名だけを信用した送信、Mobile通信での意図しない大量転送、権限失効後の継続、重複Workerや通信結果不明による二重File作成を防がなければならない。

## 対象環境と前提

- 対象Android versionはAndroid 10以降とし、現行の実機対象versionでも確認する。
- 写真、動画、音声はMediaStoreを使用し、利用者が選択するその他のフォルダはStorage Access Frameworkを使用する。
- Server側の認証済みDevice、Refresh Session、分割Upload Session、Chunk再開、Checksum検証、Storage Guard、FileOperation、FileEntry、共有権限、索引・状態管理、Version、UserActivityが利用可能であることを前提とする。
- `LOCAL_DIRECT`と`REMOTE_SECURE`は既存ConnectionCoordinatorの判定を使用し、Android側Backup機能が独自にServer identityを推測しない。
- Server上のファイル本体はHDD、索引・共有・Receipt等の管理情報はPostgreSQL、AndroidのRule・差分索引・Queue・履歴はRoomを正とする。

## 用語

- `LocalBackupRule`: 端末Source、Server保存先、実行条件を対応付ける端末内Rule。
- `LocalSyncItem`: 端末文書ごとの差分、保留、転送、完了、失敗状態を保持するRoom上の項目。
- `ExternalWifiPolicy`: ZeroTier経由の自動実行を明示的に許可する外部Wi-Fiの端末内設定。
- `BackupReceipt`: 認証済みUser、Device、端末文書とServer上のFileEntryの完了対応を保持するServer記録。
- `localDocumentKey`: UserとDeviceの名前空間内で端末文書を識別するopaqueなKey。Raw URIや物理Pathを識別子として公開しない。
- 初回バックアップ: Rule作成後、既存Source全体を最初に走査・送信する処理。

## 実装対象の機能

### 1. バックアップルール管理

- 利用者はSAFまたはMediaStore上の端末Sourceと、現在作成権限を持つServer Folderを対応付けられる。
- Ruleごとに有効状態、Network mode、最低Battery残量、初回のみ充電中とする条件を設定できる。
- 端末削除をServerへ反映しない一方向バックアップであり、双方向同期ではないことをUIへ明示する。

### 2. Server側BackupReceiptと重複判定

- Serverは`(userId, deviceId, localDocumentKey)`ごとに完了Receiptを一意に保持する。
- Androidが送る候補をReceiptおよび現在のFileEntryと比較し、`NEW`、`CHANGED`、`ALREADY_UPLOADED`へ分類する。
- Client指定のUser ID、Device ID、Remote File IDを認証・認可の根拠として信用しない。

### 3. 新規・変更ファイルの分割アップロード

- `NEW`は既存Upload Sessionを利用して新しいFileEntryへ公開する。
- `CHANGED`は既存FileEntryの内容を検証済み一時ファイルでatomic replaceし、同じFile IDのVersionを増加する。
- 通信中断、Process終了、API再起動後は、Serverが確定したoffsetから同じUpload Sessionを安全に再開する。

### 4. Roomによる端末内永続状態

- `LocalBackupRule`、`LocalSyncItem`、`ExternalWifiPolicy`、MediaStore generation、SAF走査checkpointをRoomへ保存する。
- Queueと転送状態はProcess終了や端末再起動をまたいで復元できる。
- RoomにはToken、Password、ZeroTier秘密情報、ファイル本文を保存せず、Android Auto Backupと端末移行から除外する。

### 5. MediaStore差分検出

- 写真、動画、音声はMediaStore generationと変更通知を優先して差分候補を検出する。
- Size、更新情報で候補を絞り、必要な場合だけContentResolverからChecksumを計算する。
- generationのreset、通知取りこぼし、権限変更は後続の再走査で回復する。

### 6. SAF差分検出

- 利用者が選択したTreeを定期的にStreaming走査し、保存済み索引と比較する。
- Sizeと更新日時が同じ項目は毎回本文を読まず、変更候補にだけ必要時のChecksumを使用する。
- 走査の完走前にcheckpointを進めず、中断やProvider失敗後の再走査で取りこぼしを検出する。

### 7. 外部Wi-Fi登録

- 利用者は現在接続中の外部Wi-FiをGUIから明示的に登録できる。
- 表示名、SSID、任意のBSSID制限、従量制扱い、有効状態を管理できる。
- Wi-Fi情報取得権限がない場合は自動実行を止め、権限の目的と再設定方法を案内する。

### 8. 自動実行Network Policy

- `LOCAL_DIRECT`はWi-Fi登録情報に依存せず、同一subnet、非ZeroTier基盤Networkへの明示binding、TLS、Host、KuraStorage API応答、認証状態を再確認する。
- 外部Wi-Fiでは登録情報、`REMOTE_SECURE`、ZeroTier経路、TLS、Host、KuraStorage API応答、認証状態をすべて満たす場合だけ自動実行する。
- Mobile通信、未登録Wi-Fi、従量制扱いWi-Fi、ZeroTierなしの外部Wi-Fi、HDD利用不可では自動送信しない。

### 9. WorkManagerバックグラウンド処理

- Rule、User、接続先ごとの一意なWork chainでScanner、Compare、Uploadを実行する。
- アプリが非表示またはProcess終了後でも予約を保持し、端末再起動後に再スケジュールする。
- 常時Foreground Serviceを標準動作にせず、大量転送時だけ進捗通知を持つForeground Workerへ切り替える。

### 10. 進捗・状態・履歴

- 最終成功日時、保留、アップロード中、成功、失敗の件数を表示する。
- File別に状態、失敗理由、最終試行日時、再試行可否を確認できる。
- 許可接続待ち、Battery／充電待ち、認証待ち、HDD待ち、Source権限待ちを区別する。
- 利用者は「今すぐバックアップ」、一時停止、再開、失敗項目の再試行を実行できる。

### 11. 一方向性と既存状態との整合

- 端末で消えた項目は`LOCAL_MISSING`として記録するだけで、ServerのFile、Receipt、Share等を削除しない。
- Server側のRename、Move、Share変更、Trash、Restore、MISSING、Purgeと競合した場合は、現在の権限・状態・Versionを再評価する。
- 内容更新時もFile ID、親、名前、共有、Favorite、Tag、Recentを不必要に失わない。

### 12. Security・Privacy・可観測性

- Backup APIはAccess TokenとServer側SessionからUser、Deviceを特定する。
- TLS証明書、Hostname、Server identity検証を無効化できない。
- Log、Metric label、例外、通知、Crash出力へ端末Path、相対Path、SSID、BSSID、ファイル名、Token、`localDocumentKey`を平文で残さない。
- 成功・失敗件数、Policy結果、Batch件数・Byte、処理時間、retry理由は低Cardinalityで観測できる。

## 受け入れ条件

### 1. バックアップルール設定

- [ ] 利用者が端末SourceとServer保存先Folderを選択し、Ruleを作成・編集・有効化・無効化できる。
- [ ] Server保存先は現在の所有、直接共有、祖先Folder共有を再評価し、新規作成権限がないFolderを確定できない。
- [ ] `LOCAL_DIRECT_ONLY`と`LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER`の2つのNetwork modeを選択できる。
- [ ] 最低Battery残量を設定でき、初回バックアップだけを充電中に限定できる。
- [ ] Sourceまたは保存先の権限を失った場合、理由と再選択方法を表示して自動実行を停止する。
- [ ] Ruleの無効化・削除でServer上のFileやReceiptを削除しない。
- [ ] UIに「端末削除はServerへ反映しない」「双方向同期ではない」を明示する。

### 2. BackupReceipt・比較・重複防止

- [ ] `BackupReceipt`は`(userId, deviceId, localDocumentKey)`で一意になり、同じUserでも別DeviceのKey空間を混同しない。
- [ ] `POST /api/v1/backup/compare`は候補を`NEW`、`CHANGED`、`ALREADY_UPLOADED`へ分類し、閲覧・作成権限のない情報を返さない。
- [ ] UserとDeviceはAccess TokenおよびServer側Sessionから導出し、Client指定値によるなりすましを拒否する。
- [ ] 同じ端末文書の並行Backup Uploadは1件に収束し、同じ内容の再送でFile、Version、Receiptを重複作成しない。
- [ ] 未完了・失敗・取消UploadではReceiptを進めない。
- [ ] Compare候補から項目が省略されてもFileEntryとReceiptを削除しない。

### 3. 新規・変更ファイルの確定

- [ ] `NEW`はChecksumとSizeの検証完了後だけ新しいFileEntryとして公開される。
- [ ] `CHANGED`は検証済み一時ファイルで同じFileEntryをatomic replaceし、Versionを1回だけ増加する。
- [ ] `CHANGED`後もFile ID、親、名前、共有、Favorite、Tag、Recentを維持し、内容依存索引・派生状態を既存規則どおり更新する。
- [ ] Upload完了、FileEntry／Version、FileOperation、Receiptの確定が整合し、DBまたはHDD失敗で片側だけ成功しない。
- [ ] Move、Rename、Share解除、Trash、MISSING、Purge、Version競合が発生した場合、古いCompare結果で内容を公開しない。
- [ ] 操作者が共有FolderへBackupする場合、作成FileのOwnerは共有FolderのOwnerとなり、操作者は監査・操作履歴へ記録される。

### 4. Room永続状態

- [ ] Rule、差分索引、Queue、Wi-Fi Policy、generation、checkpointがProcess終了後も復元される。
- [ ] 端末再起動後に`UPLOADING`のleaseを回復し、完了確認または再開によって二重送信を防ぐ。
- [ ] User、Server接続先、認証Sessionを切り替えた場合、以前のQueue・履歴・件数を別Contextへ表示または送信しない。
- [ ] Room Migrationを破壊的fallbackなしでTestし、DB破損時はServer削除や完了推測を行わず再走査とCompareへ収束する。
- [ ] Room DBはAndroid Auto Backup／端末移行から除外され、Token、Password、ファイル本文、ZeroTier秘密情報を含まない。

### 5. 差分検出

- [ ] 写真、動画、音声はMediaStore generationと変更通知から新規・変更候補を検出する。
- [ ] SAF Treeは保存済み`localDocumentKey`、相対Path、Size、更新日時との比較から候補を検出する。
- [ ] metadataが同じ全Fileへ毎回Checksumを計算せず、変更候補だけを必要時にStreaming検証する。
- [ ] MediaStore generation reset、通知取りこぼし、SAF走査中断、Provider例外後も、後続の全再走査で候補を検出できる。
- [ ] 同じRule・端末文書のQueueを重複作成せず、通知burstと同時Scannerを一意に収束させる。
- [ ] 端末から消えた項目は`LOCAL_MISSING`になり、ServerのDelete／Trash APIを呼ばない。

### 6. Wi-Fi登録とNetwork Policy

- [ ] 現在接続中Wi-Fiを明示確認して登録し、表示名、SSID、任意BSSID制限、従量制扱い、有効状態を編集・削除できる。
- [ ] Wi-Fi情報取得権限が未許可、拒否、恒久拒否、または情報取得不能の場合、自動実行をfail-closedで停止し案内する。
- [ ] `LOCAL_DIRECT`ではSSID／BSSIDに依存せず、非ZeroTier基盤Networkへのbinding、同一subnet、TLS、Hostname、KuraStorage応答、認証が成功した場合だけ実行する。
- [ ] 登録済み外部Wi-Fiでは`REMOTE_SECURE`、ZeroTier、TLS、Hostname、KuraStorage応答、User／Device／Session認証がすべて成功した場合だけ実行する。
- [ ] 未登録Wi-Fi、BSSID不一致、従量制扱いWi-Fi、ZeroTierなしの外部Wi-Fiでは自動実行しない。
- [ ] Mobile通信ではZeroTier接続中でも自動実行せず、既存の手動閲覧・手動Uploadの許可を後退させない。
- [ ] HDDが`AVAILABLE`でない場合は新しいBackupを開始せず、転送中に利用不可となった場合は安全に一時停止する。

### 7. WorkManager実行と復旧

- [ ] アプリが非表示、Process終了、端末再起動後でも予約済み処理を復元できる。
- [ ] Rule、User、接続先ごとの一意Work名により、複数の起動契機が同じWork chainへ収束する。
- [ ] Worker開始直後、Batch境界、転送継続中にNetwork Policy、Battery、Charging、HDD、認証状態を再確認する。
- [ ] 不許可接続では短時間で安全に終了し、許可接続へ戻った後に保留Queueから再実行する。
- [ ] 1 Workerは最大100 File、合計2GB、20分のいずれかで区切り、残件を後続Workerへ引き継ぐ。
- [ ] 通常時は常時Foreground Serviceを使用せず、大量転送だけ進捗通知付きForeground Workerへ切り替える。
- [ ] Androidの強制停止中は実行できないことを設定画面で案内する。

### 8. 分割Upload再開・失敗処理

- [ ] Upload Session ID、Idempotency Key、確定offset、Source fingerprint、retry状態をProcess境界で復元できる。
- [ ] 通信結果不明、429、一時503、API再起動、Network切断ではServer状態を再照会し、確定offsetから再開する。
- [ ] Wi-FiからMobile、外部Wi-Fi切替、ZeroTier切断、Route変更を検出した場合、Chunk境界から安全に一時停止する。
- [ ] Source変更、権限喪失、Session期限切れ、Device失効、Server競合を区別し、暗黙に別Sessionを作らない。
- [ ] 回復可能な連続失敗は指数的backoffで最大10回まで再試行し、その後は`FAILED`として利用者操作を待つ。認証・権限・Source変更等の要操作Errorは自動再試行しない。
- [ ] Worker停止・取消・Process終了中のChunkを正式Fileとして公開せず、無限retryを起こさない。

### 9. 状態・進捗・履歴UI

- [ ] 最終成功日時と、保留、Upload中、成功、失敗の件数をRule別および全体で表示する。
- [ ] 許可接続待ち、Battery／充電待ち、認証待ち、HDD待ち、Source権限待ちを区別して表示する。
- [ ] File別の状態、失敗理由、最終試行日時、retry回数、次に必要な操作を確認できる。
- [ ] 「今すぐバックアップ」、一時停止、再開、失敗retryを重複実行なしで操作できる。
- [ ] 完了・失敗履歴は未完了項目を削除せず、初期値として90日または最新10,000件の小さい方へ収める。上限値は実機測定により正式文書と設定可能定数を同じ変更で調整できる。
- [ ] Loading、Empty、Error、Paging、Refresh、画面回転、Process再生成、長文、font scale 2.0、dark mode、TalkBackを扱える。

### 10. 一方向性・既存機能との整合

- [ ] 端末での削除、Rule削除、候補省略、Source権限喪失をServer File削除へ変換しない。
- [ ] Serverだけに存在するFileを自動削除せず、Server側削除は既存の明示File操作だけで行う。
- [ ] Search、Share、Favorite、Tag、Recent、File state、Version、UserActivityの既存認可・整合性を後退させない。
- [ ] DeviceまたはSession失効後は新しいCompare／Uploadを拒否し、Androidは認証待ちまたは再Loginへ移行する。
- [ ] 同じ実データを繰り返し検出しても不要な通信、File、Version、Receiptを増やさない。

### 11. Security・Privacy・可観測性

- [ ] 不正な相対Path、Traversal、絶対Path、制御文字、過大metadata、重複Key、未知Checksumを`400`で拒否する。
- [ ] 別User／Device、権限不足Folder、古いVersion、改ざんCompare結果からのUploadを拒否する。
- [ ] TLS証明書・Hostname検証を無効化するProduction codeや設定がない。
- [ ] Room、Android Backup、通知、Logcat、Crash出力、API／Nginx／DB Logへ秘密情報、端末Path、相対Path、SSID、BSSID、ファイル名、`localDocumentKey`を平文で残さない。
- [ ] Backup成功・失敗数、Policy結果、Batch件数・Byte、処理時間、retry理由を低Cardinalityで観測できる。
- [ ] Release APKはnon-debuggableで、実SSID、実BSSID、実Endpoint、Credential、ZeroTier秘密情報をArtifactへ含めない。

### 12. 性能・品質

- [ ] 1万件Sourceの初回・増分走査で全件本文をMemoryへ保持せず、metadata不変Fileを再hashしない。
- [ ] 1 WorkerのMemory使用はFile sizeに比例せず、既存Chunk上限に従い最大1 ChunkとScanner／DB Batchに制限する。
- [ ] Raspberry Pi相当環境で通常のCompare requestを2秒以内に返し、件数上限下で全候補をMemoryへ無制限展開しない。
- [ ] Network Policyと重要状態遷移は95%以上、Android／ServerのDomain・Application全体は80%以上のUnit Test line coverageを満たす。
- [ ] Android 10と現行Android、Raspberry Pi実API・実HDDでProcess終了、端末再起動、Network切替、API／DB再起動、HDD unmount／remountを確認し、破損・重複・秘密情報漏えいがない。

## 成功指標

- 同じ端末文書・内容を10回検出しても、Server上のFile、Version、Receiptはそれぞれ1件の確定状態へ収束する。
- 通信中断、Process終了、端末再起動後に確定offsetから再開し、完全なFileだけを公開する。
- Mobile、未登録Wi-Fi、従量制扱いWi-Fi、ZeroTierなしの外部Wi-Fiで自動Upload requestを0件にする。
- 端末削除、Rule削除、候補省略のE2EでServer File、Receipt、Share、Favorite、Tag、Recentを削除しない。
- 1万件の増分確認でmetadata不変Fileの本文読取とChecksum再計算を0件にする。
- Backupの主要状態、待機理由、失敗理由を利用者がAndroid UIから確認・再試行できる。
- 必須CI、Unit／Integration／Instrumented／E2E、Migration、OpenAPI、Security検査がすべて成功する。

## スコープ外

以下は本作業では実装しない。

- 双方向同期、Server側変更の端末への自動反映、端末削除のServer反映。
- Mobile通信での自動バックアップを許可する設定。
- 常時Foreground Serviceによる監視・転送。
- KuraStorageアプリからZeroTierの接続、切断、Network ID登録、Controller操作を行う機能。
- SSID／BSSIDをServer identity、端末認証、`LOCAL_DIRECT`判定に使用すること。
- Webアプリの自動バックアップUI、iOS、Desktop Client。
- Server間Replication、第二HDD・CloudへのServer Backup、端末移行機能。
- 端末Source本文のRoom保存、一般的なOffline File Cache、任意Fileの双方向Conflict merge。
- 利用者が任意の物理Server Pathを指定する機能。

## 参照ドキュメント

- `docs/product-requirements.md` 7.9 - Android自動バックアップ要求
- `docs/functional-design.md` 5.6、6.1.6〜6.1.8、6.2.11、7.7、8.12、11.8〜11.9、15、17、18.3〜18.4 - データ、API、差分、UI、Background、Test、実装順序
- `docs/architecture-design.md` 7.6、8、11.5、17 - 実行モデル、データの正、統合順序
- `docs/repository-structure.md` 8〜9 - `core-database`、`feature-backup`、Android Test配置
- `docs/development-guidelines.md` - TDD、Coverage、Room／Migration、Android／Server検証規約
- `.steering/20260902-android-auto-backup/tasklist.md` - Pull Request単位の実装・検証計画
