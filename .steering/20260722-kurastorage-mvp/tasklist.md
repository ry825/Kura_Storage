# KuraStorage MVP タスクリスト

## 🚨 タスク完全完了の原則

**本タスクのすべての項目は最終的に完了させる。ただし、1回の実装で1つのPull Request単位を完了し、Pull Request作成後に停止してよい。**

### 必須ルール

- 実装開始前に本ファイルと対象PRの未完了項目を確認する。
- 開始時点では対象タスクを`[ ]`のままとし、実装・検証・必要な文書更新が完了した後だけ`[x]`へ変更する。
- 親タスクは、すべての子タスクが完了した後だけ`[x]`にする。
- 選択したPull Request単位に未完了タスクを残したまま停止しない。
- 技術的に不要になったタスクは、取消理由と代替実装を記録して完了扱いにする。
- 「時間の都合」「難しい」「後で実装する」を理由にMVPタスクをスキップしない。
- 後続Pull Requestは、依存先Pull Requestが`main`へMergeされた後だけ開始する。
- Pull RequestはコーディングエージェントがMergeしない。
- Pull Request作成後は、同じ実行内で次のPull Request単位へ進まない。

### 共通Pull Request完了手順

各PRの「Pull Request完了」で次を実施する。

- 対象PRの全実装・テスト・文書タスクが`[x]`であることを確認する。
- 差分をセルフレビューし、デバッグコード、秘密情報、物理絶対Path、スコープ外変更がないことを確認する。
- `tasklist.md`の進捗を更新する。
- 変更をCommitし、作業BranchをRemoteへPushする。
- `main`をBaseにPull Requestを作成する。
- Pull Requestのタイトルと本文を英語で作成し、目的、対象タスク、変更概要、テスト結果、手動確認、未実施事項を記載する。
- 本ファイルの「各Pull Request完了記録」へ実績を追記する。
- 完了記録を同じBranchへCommit・Pushし、Pull Requestへ反映されたことを確認する。
- Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR1: MVP再定義と環境前提

### 1.1 作業開始

- [x] PR1の作業準備が完了している
  - [x] `requirements.md`と`design.md`を再確認する
  - [x] `git status`と既存差分を確認する
  - [x] 最新`main`を基点に短命Branchを作成する
  - [x] 対象文書の現行Versionと参照関係を記録する

### 1.2 正式文書のMVP再定義

- [x] `docs/product-requirements.md`のMVP範囲を更新する
  - [x] 接続、認証、個人領域、一覧、詳細、Folder作成、Streaming Upload、Range Download、Trash、RestoreをMVPとする
  - [x] 後続機能を「MVP後の機能追加」へ移す
  - [x] KPI、リスク、リリース判定基準、MVP確定事項を新しい範囲へ整合させる
- [x] `docs/functional-design.md`をMVP実装契約へ更新する
  - [x] MVP Data ModelをUser、Device、RefreshSession、AuthenticationAttempt、AuditLog、FileEntry、FileOperationに絞る
  - [x] MVP Endpointを`design.md`の契約と一致させる
  - [x] 分割UploadをMVP後とし、Streaming Multipart Uploadの契約と処理Flowを定義する
  - [x] MVP Android画面、Error、Test、実装順序を絞る
- [x] `docs/architecture-design.md`を最小実行構成へ更新する
  - [x] 独立Worker HostをMVP後とし、API Hosted Serviceの限定的復旧境界を定義する
  - [x] Server、Android、Nginx、PostgreSQL、HDD、ZeroTierの実行境界を更新する
  - [x] Streaming Upload、Trash、Restore、FileOperation復旧の整合性設計を更新する
- [x] `docs/repository-structure.md`をMVPの最小Project・Moduleへ更新する
  - [x] ServerのDomain、Application、Infrastructure、Api、AdminCliとTestだけをMVP配置とする
  - [x] AndroidのMVP Moduleだけを現行配置とする
  - [x] Worker、Room、WorkManager、Media、Sharing、SearchはMVP後の追加Ruleとする
- [x] `docs/development-guidelines.md`をMVPのBuild・Test・DoDへ更新する
  - [x] Streaming、Idempotency、FileOperation、SAFの実装規約を追加する
  - [x] MVPで使用しない依存を追加しないRuleを追加する
  - [x] PRごとの必須検証Commandと実機確認基準を整合させる

### 1.3 環境項目

- [x] `docs/environment-info.example.md`のMVP必須項目を整合させる
  - [x] LAN、ZeroTier、TLS、JWT、HDD、Android署名の公開項目構造を定義する
  - [x] SecretまたはZeroTier Node Identityを記載しないRuleを維持する
- [x] Git管理外の`docs/environment-info.md`の現在状態を確認する
  - [x] `NET-LAN-CIDR`の実値を確定できるか確認する（現時点では`SET_LOCALLY`のため未確定）
  - [x] `NET-ZEROTIER-API-IP`、`NET-ZEROTIER-CIDR`、`NET-ZEROTIER-NETWORK-ID`、`NET-ZEROTIER-CONTROLLER-TYPE`の未設定状態を記録する
  - [x] 未確定値はPR7の実機E2E開始条件とし、SecretをCommitしない

### 1.4 整合性検証

- [x] 文書間の整合性検証が完了している
  - [x] MVPとMVP後の機能分類が5つの正式文書で一致する
  - [x] API Path、Data Type、Error Code、状態名が一致する
  - [x] ZeroTierの外部管理境界と`REMOTE_SECURE`の意味が一致する
  - [x] 文書Versionと参照Versionが一致する
  - [x] MarkdownのCode Fence、Table、節番号、ローカルPath参照を検証する
  - [x] 旧MVPだけに存在するWorker、Media、Sharing、BackupのMVP必須表現が残っていない

### 1.5 Pull Request完了

- [x] PR1が完了している
  - [x] 共通Pull Request完了手順をすべて実施する
  - [x] PR1の完了記録を本ファイルへ追記する
  - [x] Pull Requestのタイトルと本文を英語へ修正する
  - [x] 今後のPull Requestのタイトルと本文を英語で作成するルールを文書化する
  - [x] PR1完了記録に追加修正を反映する

---

## PR2: Repository・Build・CI基盤

### 2.1 作業開始

- [x] PR2の作業準備が完了している
  - [x] PR1が`main`へMerge済みであることを確認する
  - [x] 最新`main`を基点に短命Branchを作成する
  - [x] .NET 10、JDK、Android SDK、PostgreSQL、対応するGradle・AGP・Kotlinの利用可能性を確認する（.NET 10.0.110、JDK 17、Android SDK 36、Gradle 8.13、AGP 8.13.2、Kotlin 2.3.21、PostgreSQL 17を使用する）

### 2.2 Repository Root

- [x] Repository Rootの共通設定を作成する
  - [x] `.editorconfig`を作成する
  - [x] `.gitattributes`を作成する
  - [x] `.gitignore`にServer、Android、Local設定、Certificate、Key、Build生成物の除外を追加する
  - [x] `global.json`で.NET 10 SDKを固定する
  - [x] Lock FileとVersion中央管理の方針を設定する

### 2.3 Server Scaffold

- [x] Server Solutionの最小構成を作成する
  - [x] Domain、Application、Infrastructure、Api、AdminCli Projectを作成する
  - [x] Domain、Application、Integration Test Projectを作成する
  - [x] Project Referenceが設計した依存方向だけになっている
  - [x] Central Package ManagementとNuGet Lock Fileを有効にする
  - [x] APIとCLIが最小の起動・Help出力を実行できる

### 2.4 Android Scaffold

- [x] Android Gradle Projectの最小構成を作成する
  - [x] Gradle Wrapper、Version Catalog、Compose BOM、Convention Pluginを構成する
  - [x] `minSdk 29`とApplication ID・Namespaceを固定する
  - [x] `app`、`core-model`、`core-network`、`core-data`、`core-security`、`core-ui`、`feature-connection`、`feature-auth`、`feature-files`を作成する
  - [x] Module依存にCycleがなく、Feature間が直接依存しない
  - [x] Room、WorkManager、Media3、Coil、PDF Libraryが追加されていない
  - [x] 空のDebug AppがBuildできる

### 2.5 API契約・設定Template

- [x] MVPのOpenAPI契約と設定Templateを作成する
  - [x] `contracts/openapi/kurastorage-api.yaml`にMVP Endpoint、DTO、Errorを定義する（正式文書の命名へ統一）
  - [x] ServerとAndroidで共通の契約Fixtureを用意する
  - [x] Serverの`appsettings.example.json`を作成する
  - [x] Androidの`local.properties.example`とRelease Build Inputの公開構造を作成する
  - [x] ExampleにSecret、Private Key、実IP、ZeroTier Network IDの実値がない

### 2.6 CI・検証Script

- [x] Repository統一の検証Scriptを作成する
  - [x] `scripts/ci/verify-config.sh`を作成する
  - [x] `scripts/ci/verify-server.sh`を作成する
  - [x] `scripts/ci/verify-security.sh`を作成する
  - [x] `scripts/ci/verify-android.sh`を作成する
  - [x] GitHub ActionsでConfig、Server、Security、Android Jobを作成する
  - [x] Dependency CacheがLock Fileと対応し、SecretをCacheへ含めない

### 2.7 検証

- [x] PR2の自動検証が完了している
  - [x] `./scripts/ci/verify-config.sh`が成功する
  - [x] `./scripts/ci/verify-server.sh`が成功する
  - [x] `./scripts/ci/verify-security.sh`が成功する
  - [x] `./scripts/ci/verify-android.sh`が成功する
  - [x] Debug APKとServer Build Artifactが生成できる
  - [x] CIの必須Jobがすべて成功する

### 2.8 Pull Request完了

- [x] PR2が完了している
  - [x] 共通Pull Request完了手順をすべて実施する
  - [x] PR2の完了記録を本ファイルへ追記する

---

## PR3: Server基盤・認証・Device

### 3.1 作業開始

- [x] PR3の作業準備が完了している
  - [x] PR2が`main`へMerge済みであることを確認する
  - [x] 最新`main`から短命Branchを作成する
  - [x] 認証、Token、Route Header、HDDの正式設計節を再確認する

### 3.2 Configuration・DB基盤

- [x] ServerのConfigurationとDB基盤を実装する
  - [x] Optionsの起動時Validationを実装する
  - [x] Secret Fileと環境変数の読み込み境界を実装する
  - [x] EF Core DbContextとPostgreSQL接続を構成する
  - [x] User、Device、RefreshSession、AuthenticationAttempt、AuditLogのEntity・Mapping・Indexを実装する
  - [x] 初回Migrationを作成する
  - [x] API起動時にMigrationを自動適用しない

### 3.3 StorageGuard・Health・Route

- [x] Serverの接続・Storage基盤を実装する
  - [x] Mount Point、`.storage-identity`、実Path、読み書き、空き容量を検証する`StorageGuard`を実装する
  - [x] HDD未Mount時にOS Rootへ書き込まないTestを実装する
  - [x] Nginxが上書きするRoute Headerの検証Middlewareを実装する
  - [x] Unix Socket以外または不正RouteのDevice登録判定を信頼しない
  - [x] `GET /api/v1/system/health`を実装する
  - [x] Health ResponseからDB詳細、Path、storageId、OS情報を除外する

### 3.4 Password・User・認証失敗

- [x] User認証のDomain・Applicationを実装する
  - [x] Username正規化と一意性制約を実装する
  - [x] Argon2id v1.3、Salt 16Byte、メモリ19MiB、反復2、並列度1のHash生成・検証を実装する
  - [x] 弱い保存ParameterのLogin成功時Rehashを実装する
  - [x] User存在の有無を過度に公開しないAuthentication Errorを実装する
  - [x] 15分以内10回連続失敗のSecurity Lockと成功時Resetを実装する
  - [x] AuthenticationAttemptとAuditLogをSecretなしで記録する

### 3.5 Device・Session・Token

- [x] DeviceとSessionのDomain・Applicationを実装する
  - [x] Local Direct限定Device登録とUserあたり初期10台上限を実装する
  - [x] Deviceと初回Refresh Sessionを単一Transactionで作成する
  - [x] ES256 Access Token発行とIssuer、Audience、Expiration、`sub`、`device_id`、`session_family_id`検証を実装する
  - [x] 256bit以上のRandom Refresh TokenとSHA-256 Hash保存を実装する
  - [x] 15分のAccess Tokenと24時間のRefresh Token有効期限を実装する
  - [x] Refresh Tokenの排他Rotation、使用済みToken再利用検知、Session Family失効を実装する
  - [x] LogoutとDevice失効時のSession失効を実装する
  - [x] 保護APIでUser、Device、Session Familyの有効性を検証する

### 3.6 API・Admin CLI

- [x] 認証APIとAdmin CLIを実装する
  - [x] `POST /api/v1/auth/register-device`を実装する
  - [x] `POST /api/v1/auth/login`を実装する
  - [x] `POST /api/v1/auth/refresh`を実装する
  - [x] `POST /api/v1/auth/logout`を実装する
  - [x] Error ResponseとHTTP StatusをOpenAPI契約に一致させる
  - [x] `user create`、`device list`、`device revoke`、`user unlock`のCLI Commandを実装する
  - [x] CLIがApplication Serviceを使用し、SQLを直接実行しない

### 3.7 Test・検証

- [x] Server基盤・認証・DeviceのTestが完了している
  - [x] Password Hash、Lock、Rehash、Username正規化の単体Testを実装する
  - [x] Device登録、上限、失効、Session Rotation、Reuseの単体Testを実装する
  - [x] PostgreSQLでMigration、Register、Login、Refresh並列実行、Logout、Device失効を結合Testする
  - [x] Local Direct登録成功とRemote Secure登録拒否をTestする
  - [x] Secret、Password、Token、KeyがLogへ出力されないことをTestする
  - [x] `./scripts/ci/verify-config.sh`、`verify-server.sh`、`verify-security.sh`が成功する
  - [x] CIの必須Jobがすべて成功する

### 3.8 Pull Request完了

- [x] PR3が完了している
  - [x] 共通Pull Request完了手順をすべて実施する
  - [x] PR3の完了記録を本ファイルへ追記する

---

## PR4: Server基本ファイル操作

### 4.1 作業開始

- [ ] PR4の作業準備が完了している
  - [ ] PR3が`main`へMerge済みであることを確認する
  - [ ] 最新`main`から短命Branchを作成する
  - [ ] File API、HDD、Operation Journalの正式設計節を再確認する

### 4.2 File Domain・DB

- [ ] File DomainとDB Schemaを実装する
  - [ ] `FileEntry`、`FileOperation`、`RelativeStoragePath`、`FileName`、`FileVersion`を実装する
  - [ ] `ACTIVE`、`TRASHED`とOperation状態遷移を実装する
  - [ ] Owner・Parent・Nameの部分Unique IndexとIdempotency Indexを実装する
  - [ ] File Table用Migrationを作成する
  - [ ] User個人領域とRoot Folderの作成を実装する

### 4.3 Path・所有者認可

- [ ] Fileアクセスの共通防御を実装する
  - [ ] 物理絶対PathをClient入力またはResponseにしない
  - [ ] `..`、絶対Path、NUL、不正区切り文字を拒否する
  - [ ] Rootから解決した実Pathが専用領域内であることを再検証する
  - [ ] シンボリックリンクを作成・追跡しない
  - [ ] TokenのUserからOwnerを決定し、Client指定User IDを信頼しない
  - [ ] 他UserのFile IDに対して共通`FILE_NOT_FOUND`を返す

### 4.4 一覧・詳細・Folder作成

- [ ] File CatalogとFolder作成を実装する
  - [ ] Paging付き`GET /api/v1/files`を実装する
  - [ ] `GET /api/v1/files/{fileId}`を実装する
  - [ ] `POST /api/v1/folders`を実装する
  - [ ] 同名File・Folder競合をDB制約とApplicationの両方で処理する
  - [ ] File DTOからOwnerの内部ID、物理Path、運用情報を除外する

### 4.5 Streaming Upload

- [ ] Streaming Uploadを実装する
  - [ ] `POST /api/v1/files/upload`をMultipart Streamingで実装する
  - [ ] `Idempotency-Key`、Metadata、Destination Folder、Owner、空き容量を検証する
  - [ ] ASP.NET Coreの全Body Bufferingを使用せず、HDDの`upload-temp`へStreamする
  - [ ] 受信Sizeと任意SHA-256をStream中に検証する
  - [ ] 検証後だけ同一Filesystem内でatomic renameする
  - [ ] 同名競合時に既存Fileを上書き・削除しない
  - [ ] 同じIdempotency Key・Metadataの再送で重複Fileを作成しない
  - [ ] 中断・検証失敗Fileを通常一覧へ公開しない

### 4.6 Range Download

- [ ] Range Downloadを実装する
  - [ ] `GET /api/v1/files/{fileId}/content`を実装する
  - [ ] Full Downloadと単一Rangeの`200`・`206`・`416`を正しく返す
  - [ ] `Content-Length`、`Content-Range`、`Accept-Ranges`、安全な`Content-Disposition`を設定する
  - [ ] File全体をMemoryへ読み込まずStreamする
  - [ ] HDD利用不可、File未存在、Owner不一致を正しく拒否する

### 4.7 Trash・Restore・復旧

- [ ] Trash・RestoreとOperation復旧を実装する
  - [ ] `DELETE /api/v1/files/{fileId}`でゴミ箱へ移動する
  - [ ] `GET /api/v1/trash`でUserの`TRASHED`項目だけを返す
  - [ ] `POST /api/v1/files/{fileId}/restore`で元の場所へ復元する
  - [ ] 復元先の同名競合時に上書きせず`FILE_RESTORE_CONFLICT`を返す
  - [ ] Folder配下の実File移動とDB状態を一貫して変更する
  - [ ] `FileOperation` の`PENDING`、`FILESYSTEM_DONE`、`COMPLETED`、`RECOVERY_REQUIRED`を実装する
  - [ ] API起動時と限定Hosted Serviceで未完了Operationを冪等に復旧する
  - [ ] 安全に自動判定できないOperationを通常一覧へ公開せず`RECOVERY_REQUIRED`とする

### 4.8 Test・検証

- [ ] Server File操作のTestが完了している
  - [ ] Path、FileName、Owner認可、状態遷移の単体Testを実装する
  - [ ] 一覧、Paging、詳細、Folder競合の結合Testを実装する
  - [ ] Streaming Uploadの成功、中断、Size、Checksum、Idempotency、同名競合をTestする
  - [ ] Rangeの先頭、中間、末尾、範囲外をTestする
  - [ ] File・FolderのTrash、Restore、復元競合をTestする
  - [ ] Process停止を模擬したOperation復旧Testを実装する
  - [ ] HDD未Mount、storageId不一致、読取専用、容量不足をTestする
  - [ ] IDOR、Path Traversal、Symlink、NUL、不正RangeをSecurity Testする
  - [ ] OpenAPI契約Testを更新する
  - [ ] `verify-config.sh`、`verify-server.sh`、`verify-security.sh`が成功する
  - [ ] CIの必須Jobがすべて成功する

### 4.9 Pull Request完了

- [ ] PR4が完了している
  - [ ] 共通Pull Request完了手順をすべて実施する
  - [ ] PR4の完了記録を本ファイルへ追記する

---

## PR5: Android接続・認証

### 5.1 作業開始

- [ ] PR5の作業準備が完了している
  - [ ] PR4が`main`へMerge済みであることを確認する
  - [ ] 最新`main`から短命Branchを作成する
  - [ ] Android対象端末、API Level、LAN接続と公開Root CA Test Inputを確認する

### 5.2 Android共通基盤

- [ ] Androidの共通Model・Data・UI基盤を実装する
  - [ ] API DTOとDomain Modelの変換を`core-model`・`core-data`に実装する
  - [ ] 共通API Error変換とRequest ID表示用Modelを実装する
  - [ ] Compose Theme、Navigation、Loading、Empty、Error、Progressの共通UIを実装する
  - [ ] ApplicationとModuleのDI Compositionを実装する
  - [ ] DebugとReleaseで設定入力・Log・Certificateの扱いを分離する

### 5.3 Network・TLS・接続判定

- [ ] Androidの接続基盤を実装する
  - [ ] `NET-API-HOSTNAME`をLANまたはZeroTier API IPへ固定解決するOkHttp DNSを実装する
  - [ ] Local Direct確認を基盤Wi-Fi・Ethernet Networkへ明示Bindする
  - [ ] 同一Subnet、HTTPS Health、TLS、Hostnameをすべて検証する
  - [ ] Local失敗時にZeroTier API IPのHTTPSを確認し`REMOTE_SECURE`を判定する
  - [ ] LocalとZeroTierが両方利用可能な場合にLocalを優先する
  - [ ] KuraStorage専用Root CAと`NET-API-HOSTNAME`だけを信頼するNetwork Security Configurationを実装する
  - [ ] ProductionでCertificate・Hostname検証を回避できる実装を禁止する

### 5.4 Secure Credential・Session

- [ ] Androidの認証情報保護を実装する
  - [ ] Keystoreの取り出し不可AES-256鍵とAES-GCMを使う`SecureCredentialStore`を実装する
  - [ ] Refresh Tokenを暗号化して保存し、Access TokenをMemoryだけに保持する
  - [ ] 非秘密の`deviceId`と最後のUsernameをDataStoreへ保存する
  - [ ] 401の同時発生時にRefreshを1回だけ実行する排他制御を実装する
  - [ ] Refresh成功後だけ原Requestを1回再送する
  - [ ] Logout、Device失効、Refresh失敗、Keystore鍵消失時のCredential削除を実装する
  - [ ] Android BackupからKeystore関連DataとRefresh Token暗号文を除外する

### 5.5 Device登録・Login・Logout

- [ ] Androidの認証Flowを実装する
  - [ ] `LOCAL_DIRECT`かつ有効Credentialなしの場合だけDevice登録を開始する
  - [ ] `REMOTE_SECURE`で未登録の場合はLocal Directが必要と表示する
  - [ ] 登録済みDeviceのLogin、Refresh、Logoutを実装する
  - [ ] Device失効時に認証情報を削除し、Local Directでの再登録案内を表示する
  - [ ] Password、Token、Certificate情報をLogへ出力しない

### 5.6 接続・認証UI

- [ ] MVPの接続・認証画面を実装する
  - [ ] Splashと接続確認中画面を実装する
  - [ ] Local Direct、ZeroTier経由、未接続、TLS失敗、HDD利用不可を区別して表示する
  - [ ] ZeroTier別アプリ確認案内と復帰後・手動の再確認を実装する
  - [ ] Device登録画面とLogin画面を実装する
  - [ ] Homeに個人File、Trash、接続状態、Logoutの入口だけを実装する

### 5.7 Test・検証

- [ ] Android接続・認証のTestが完了している
  - [ ] Local Direct、Remote Secure、Disconnected、TLS Failure判定の単体Testを実装する
  - [ ] 同一SSIDだがAP Isolationの場合にLocal DirectにしないTestを実装する
  - [ ] ZeroTierなしで異なるSubnetから到達できてもLocal DirectにしないTestを実装する
  - [ ] Credential暗号化、削除、Keystore利用不可のTestを実装する
  - [ ] Refresh排他、成功、失敗、Device失効のTestを実装する
  - [ ] Connection、Registration、Login、HomeのCompose UI Testを実装する
  - [ ] Mock APIまたはTest ServerでRegister・Login・Refresh・LogoutのContract Testを実装する
  - [ ] `verify-config.sh`、`verify-security.sh`、`verify-android.sh`が成功する
  - [ ] Debug APKが生成できる
  - [ ] CIの必須Jobがすべて成功する

### 5.8 Pull Request完了

- [ ] PR5が完了している
  - [ ] 共通Pull Request完了手順をすべて実施する
  - [ ] PR5の完了記録を本ファイルへ追記する

---

## PR6: Android基本ファイル操作

### 6.1 作業開始

- [ ] PR6の作業準備が完了している
  - [ ] PR5が`main`へMerge済みであることを確認する
  - [ ] 最新`main`から短命Branchを作成する
  - [ ] Server File APIが結合Test済みであることを確認する

### 6.2 File Data・Repository

- [ ] AndroidのFile Data層を実装する
  - [ ] File・Folder・Trash・Paging・Transfer ProgressのModelを実装する
  - [ ] 一覧、詳細、Folder作成、Trash一覧、Trash、RestoreのRepositoryを実装する
  - [ ] API ErrorをStorage、Conflict、Authorization、Authentication、Validationへ変換する
  - [ ] Pagingの初回取得、追加取得、Retryを実装する
  - [ ] 再接続後に古い絶対PathまたはResponseを保存せずAPIから再取得する

### 6.3 Upload・Download

- [ ] AndroidのStreaming Transferを実装する
  - [ ] SAFでUpload元Fileを選択する
  - [ ] `ContentResolver` InputStreamをOkHttp Multipart Request BodyへStreamする
  - [ ] OperationごとにUUIDの`Idempotency-Key`を生成・再利用する
  - [ ] 送信Byte数と進捗をFlowで公開する
  - [ ] 中断後のUpload再試行でFile全体を再送する
  - [ ] SAFでDownload先を選択し、Response StreamをOutputStreamへ書き込む
  - [ ] Download進捗、Cancel、通信中断、書き込み失敗を扱う
  - [ ] 失敗した中途Downloadの削除を試み、残る場合は利用者へ表示する
  - [ ] ダウンロード完了後にOSの対応アプリで開くIntentを提供する

### 6.4 File UI

- [ ] MVPのFile操作画面を実装する
  - [ ] 個人領域のFile・Folder一覧とPagingを実装する
  - [ ] Folder遷移とBack Navigationを実装する
  - [ ] File・Folder詳細を実装する
  - [ ] Folder作成Dialogと同名Errorを実装する
  - [ ] Upload選択、進捗、Cancel、Retry、完了を実装する
  - [ ] Download保存先選択、進捗、Cancel、Retry、完了を実装する
  - [ ] Trash前の確認と完了後の一覧更新を実装する
  - [ ] Trash一覧、Restore確認、復元競合Errorを実装する
  - [ ] Storage利用不可、認証必要、Device失効、権限拒否を別状態で表示する

### 6.5 Test・検証

- [ ] Android File操作のTestが完了している
  - [ ] File Repositoryの一覧、詳細、Folder、Trash、Restoreの単体Testを実装する
  - [ ] Upload・DownloadのStreaming、進捗、Cancel、RetryをFake StreamでTestする
  - [ ] Idempotency Keyの再試行時維持をTestする
  - [ ] Paging、Empty、Error、RetryのViewModel Testを実装する
  - [ ] File一覧、Folder作成、Transfer進捗、Trash、RestoreのCompose UI Testを実装する
  - [ ] OpenAPI FixtureとAndroid DTOのContract Testを実装する
  - [ ] Android Emulatorまたは実機でSAF Upload・DownloadをInstrumented Testする
  - [ ] `verify-config.sh`、`verify-security.sh`、`verify-android.sh`が成功する
  - [ ] Debug APKが生成できる
  - [ ] CIの必須Jobがすべて成功する

### 6.6 Pull Request完了

- [ ] PR6が完了している
  - [ ] 共通Pull Request完了手順をすべて実施する
  - [ ] PR6の完了記録を本ファイルへ追記する

---

## PR7: Raspberry Pi配置・実機MVP完成

### 7.1 作業開始・環境確定

- [ ] PR7の作業準備が完了している
  - [ ] PR6が`main`へMerge済みであることを確認する
  - [ ] 最新`main`から短命Branchを作成する
  - [ ] Raspberry Pi、HDD、Android実機、LAN、ZeroTierの利用可能性を確認する
  - [ ] `NET-LAN-CIDR`、`NET-ZEROTIER-API-IP`、`NET-ZEROTIER-CIDR`、`NET-ZEROTIER-NETWORK-ID`、`NET-ZEROTIER-CONTROLLER-TYPE`をLocal文書に設定する
  - [ ] TLS Certificate、JWT Signing Key、Android Signing KeyのPathと公開FingerprintをLocal文書で確認する
  - [ ] Secret本文がRepositoryまたはLocal環境情報文書へ記載されていない

### 7.2 Raspberry Pi配置資材

- [ ] Raspberry Piへの再現可能な配置資材を実装する
  - [ ] APIのPublish、Version付き配置、`current`切替を実装する
  - [ ] 専用OS User、Directory、Permissionの作成を実装する
  - [ ] PostgreSQL 17、Database、Role、Connection制限の構築を実装する
  - [ ] Migrationを明示的に適用するCommandとBackupを実装する
  - [ ] HDD Mount、`.storage-identity`、専用Directory、Permissionの構築を実装する
  - [ ] `kurastorage-api.service`と起動順序、Restart Policy、リソース制限を実装する
  - [ ] NginxのLAN・ZeroTier 443 Listen、TLS、Unix Socket Proxy、Streaming設定を実装する
  - [ ] NginxがClient指定Route Headerを破棄し、Listen IP・Source CIDRで上書きする設定を実装する
  - [ ] nftablesでLAN・ZeroTierのHTTPSだけを許可し、DB、SSH、SMB、Forwardを制限する
  - [ ] Install、Upgrade、Verify、Rollback、Uninstall Scriptを実装する

### 7.3 TLS・Signing・Release Build

- [ ] TLSとRelease Artifactの生成を完了する
  - [ ] Root CA、Server Certificate、JWT Signing Keyを生成・検証する再現可能なScriptを実装する
  - [ ] Server CertificateのSAN、Key Usage、Extended Key Usage、Chain、期限、Key一致を検証する
  - [ ] Root CA秘密鍵をServer・Repository・APKへ配置しない
  - [ ] Android Release BuildにRoot CA公開証明書と公開設定値を供給する
  - [ ] Android Signing KeyをRepository外から供給する
  - [ ] Release APKを署名し、署名、Package ID、Version、Debuggable無効を検証する
  - [ ] Server ArtifactとRelease APKのChecksumを生成する

### 7.4 Raspberry Pi実機検証

- [ ] Raspberry Pi上のServer運用確認が完了している
  - [ ] 新規Install、Migration、Admin User作成、Healthを確認する
  - [ ] Raspberry Pi再起動後のHDD、PostgreSQL、API、Nginxの起動順序を確認する
  - [ ] API Processが非rootで動作し、必要最小限のFile・Socket権限だけを持つことを確認する
  - [ ] HDD未Mount、読取専用、storageId不一致時の拒否を確認する
  - [ ] UpgradeとRollbackをテスト用Version間で実行する
  - [ ] LogにPassword、Token、Key、File本文、物理Pathがないことを確認する

### 7.5 Android・LAN・ZeroTier E2E

- [ ] Android実機でMVP E2Eを完了する
  - [ ] Release APKを対象Android 10以上の端末へインストールする
  - [ ] Local Directから未登録Deviceを登録する
  - [ ] ZeroTier経由で未登録Deviceの登録が拒否される
  - [ ] 登録済みDeviceでLAN・ZeroTierの両方からLogin・Refreshする
  - [ ] Folder作成、Upload、一覧、Detail、Range Download、内容一致を確認する
  - [ ] File・FolderのTrash、Trash一覧、Restoreを確認する
  - [ ] 復元競合で既存Fileが上書きされないことを確認する
  - [ ] 別UserのFile IDへの操作が拒否される
  - [ ] Device失効後にRefreshと保護APIが拒否される
  - [ ] Android・Raspberry Piの再起動後に再接続・再Loginできる
  - [ ] 正常LAN・ZeroTierの基本E2Eをそれぞれ10回連続で完了する

### 7.6 Network・Security実機検証

- [ ] 実構成のNetwork・Security検証が完了している
  - [ ] 不正なTLS Certificate、Hostname、Root CAでAndroid接続が拒否される
  - [ ] ClientがRoute Headerを偽装してもRemoteからDevice登録できない
  - [ ] ZeroTier MemberからKuraStorage HTTPS以外のSSH、PostgreSQL、SMB、LAN端末、他ZeroTier端末へ到達できない
  - [ ] APIのUnix Socketまたは内部Listen先へ外部から直接到達できない
  - [ ] 認証なし、不正Token、失効Device、他User IDOR、Path Traversalが拒否される

### 7.7 運用文書・最終検証

- [ ] MVPの運用文書と最終検証が完了している
  - [ ] Install、Configuration、Migration、Admin CLI、Start・Stop、Upgrade、Rollback手順を文書化する
  - [ ] ZeroTier参加、Member認可、Managed IP、Member失効はKuraStorage外の運用であることを文書化する
  - [ ] Backup・Restore対象とMVPでの手動復旧手順を文書化する
  - [ ] `verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-android.sh`が成功する
  - [ ] `connectedDebugAndroidTest`またはRelease実機同等Testが成功する
  - [ ] Server Artifact、Release APK、Checksumを生成できる
  - [ ] 必須CIがすべて成功する
  - [ ] `requirements.md`のすべての受け入れ条件を実測・記録により確認する

### 7.8 Pull Request完了

- [ ] PR7が完了している
  - [ ] 共通Pull Request完了手順をすべて実施する
  - [ ] PR7の完了記録を本ファイルへ追記する

---

## 各Pull Request完了記録

> Pull Request作成後に、対象PRの記録を追記する。後続PRが未完了でも、完了したPRの記録はその時点で行う。

### PR1: MVP再定義と環境前提

- 完了日: `2026-07-22`
- Pull Request: `https://github.com/ry825/Kura_Storage/pull/1`
- 対象タスク: `tasklist.md` 1.1〜1.5
- 実施した自動テスト: PR1文書検証（必須文書の存在、Markdownコードフェンス、文書Version参照、`docs/environment-info.md`のGit除外）成功、Pull Request英語化ルールの記載検査成功、`git diff --check`成功
- 実施した手動・実機確認: 5つの正式文書、Steeringの`requirements.md`・`design.md`、ローカル環境情報の未確定値と秘密情報非保存ルールを確認。実機確認はPR1の対象外
- 計画と実装の差分: 正式文書のMVP再定義はリポジトリ初期コミットで既に`main`へ収録済みだったため、本PRでは再検証と進捗確定を記録した。Pull Request作成後のユーザー指示により、タイトルと本文を英語へ修正した
- 実装中に追加したタスクと理由: Pull Request #1の英語化と、今後のPull Requestのタイトル・本文を英語で作成するルールの文書化を、ユーザー指示へ対応するため追加した
- 技術的に不要になったタスク・理由・代替実装: なし
- 後続Pull Requestへの引継ぎ: PR2は本PRの`main`へのMerge後に開始する。`NET-LAN-CIDR`とZeroTier関連の未確定値はPR7の実機E2E開始までに確定する

### PR2: Repository・Build・CI基盤

- 完了日: `2026-07-23`
- Pull Request: 実装 `https://github.com/ry825/Kura_Storage/pull/2`、完了記録追補 `https://github.com/ry825/Kura_Storage/pull/3`
- 対象タスク: `tasklist.md` 2.1〜2.8
- 実施した自動テスト: `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-server.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-android.sh`、`actionlint .github/workflows/ci.yml`、OpenAPI YAML Parse、`git diff --check`が成功。Pull Request #2のGitHub Actions `Config`、`Server`、`Security`、`Android`がすべて成功
- 実施した手動・実機確認: Debug APK、APIとAdmin CLIのServer Build Artifactが生成されることを確認。Raspberry Pi配置とAndroid実機確認は後続PRの対象
- 計画と実装の差分: Repository、Server、Android、API契約、CI基盤の実装範囲に差分なし。Pull Request #2が完了記録の反映前にMergeされたため、本記録と残っていた進捗Checkは追補Pull Requestで`main`へ反映する
- 実装中に追加したタスクと理由: GitHub Actions実行環境で検証Toolを明示Installする対応と、NuGet Cache PathをRepository内の実Pathへ合わせる対応を、CI失敗の解消と再現性確保のため追加した
- 技術的に不要になったタスク・理由・代替実装: なし
- 後続Pull Requestへの引継ぎ: 本追補Pull Requestが`main`へMergeされた後、PR3のServer基盤・認証・Device実装を開始する

### PR3: Server基盤・認証・Device

- 完了日: `2026-07-23`
- Pull Request: `https://github.com/ry825/Kura_Storage/pull/4`
- 対象タスク: `tasklist.md` 3.1〜3.8
- 実施した自動テスト: `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-server.sh`、`./scripts/ci/verify-security.sh`、`git diff --check`が成功。Server検証ではDomain 3件、Application 8件、Integration 11件が成功し、PostgreSQL 17 TestcontainersでMigration、Local Direct登録、Remote Secure登録拒否、Login、並列Refresh、Reuse検知、Logout、Device失効、Log秘密情報非出力を確認。Pull Request #4のGitHub Actions `Config`、`Server`、`Security`、`Android`がすべて成功
- 実施した手動・実機確認: Release Buildが警告0・Error 0で成功し、Admin CLIのHelp出力を確認。Raspberry Pi配置、実HDD Mount、Nginx Unix Socket、Android実機確認は後続PRの対象
- 計画と実装の差分: なし。Refresh RotationはPostgreSQLの部分Unique Indexと自己参照外部Keyを維持するため、旧Session使用済み化、新Session作成、置換先設定を同一Transaction内の段階的Saveとして実装した
- 実装中に追加したタスクと理由: API起動時Migration禁止を運用可能にするためAdmin CLIへ`database migrate`を追加し、Security検証ScriptのPassword検出をHard-coded文字列に限定して正当な変数名を誤検出しないよう更新した
- 技術的に不要になったタスク・理由・代替実装: なし
- 後続Pull Requestへの引継ぎ: PR4は本Pull Requestの`main`へのMerge後に開始し、Identity MigrationへFileEntry・FileOperation用Migrationを追加する。実環境のPostgreSQL、HDD、ES256 Key、Nginx、Raspberry Piでの確認はPR7で行う

### PR完了記録Template

#### PR{N}: {Pull Request名}

- 完了日: `{YYYY-MM-DD}`
- Pull Request: `{URLまたは#番号}`
- 対象タスク: `{tasklist.mdの節}`
- 実施した自動テスト: `{Commandと結果}`
- 実施した手動・実機確認: `{...}`
- 計画と実装の差分: `なし` または詳細
- 実装中に追加したタスクと理由: `なし` または詳細
- 技術的に不要になったタスク・理由・代替実装: `なし` または詳細
- 後続Pull Requestへの引継ぎ: `なし` または詳細

---

## 全体振り返り

> PR1〜PR7と本タスクのすべての項目が完了し、各Pull Request完了記録が存在する場合だけ記入する。

### 実装完了日

`{YYYY-MM-DD}`

### 計画と実績の差分

- `{PRごとの完了記録を基に記入}`

### 主な設計変更と理由

- `{...}`

### 技術的な学び

- `{...}`

### プロセス上の改善点

- `{...}`

### 次回への改善提案

- `{...}`
