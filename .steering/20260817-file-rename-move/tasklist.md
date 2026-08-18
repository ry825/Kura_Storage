# ファイル・フォルダの名前変更・移動 タスクリスト

## 進行ルール

**本ファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止する。**

- 実装開始前に`requirements.md`、`design.md`、本ファイル、対象PRの未完了項目を確認する。
- フェーズは上から順番に実施し、同時に複数のPull Request単位へ着手しない。
- タスク開始時は`[ ]`のままとし、実装と必要な検証が完了した項目だけを直ちに`[x]`へ更新する。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ更新する。
- 選択したPull Request単位に未完了タスクを残したまま停止しない。
- 後続Pull Requestのタスクは、前のPull Request作成後も`[ ]`のままでよい。
- 「時間の都合」「難しい」「後で実施する」を理由にタスクをスキップしない。
- タスクが大きすぎる場合は、本ファイルへ実装可能なサブタスクを追加してから分割実行する。
- 技術的に不要になったタスクだけ、取消理由と代替実装を記載したうえで完了扱いにできる。
- Pull Requestのタイトルと本文は英語で作成し、目的、対象タスク、変更内容、テスト結果、影響または未実施事項を記載する。
- コーディングエージェントはPull RequestをMergeしない。

## 共通Pull Request完了手順

各PRの「Pull Request完了」で次を実施する。

- 対象PR内の実装、Test、文書更新、検証項目がすべて`[x]`であることを確認する。
- `git diff --check`が成功する。
- 差分をセルフレビューし、デバッグコード、秘密情報、物理絶対Path、スコープ外変更がないことを確認する。
- 既存のUser所有境界、HDD誤保存防止、非上書き、`fileVersion`維持が損なわれていないことを確認する。
- 対象PRの変更と本ファイルの進捗をCommitする。
- 作業BranchをRemoteへPushする。
- `main`をbaseとしてPull Requestを作成する。
- 必須CIが成功するまで修正と再検証を継続する。
- Pull Request作成後、本ファイルの「各Pull Request完了記録」へ結果を追記する。
- 完了記録には完了日、PR番号またはURL、検証結果、計画との差分、追加タスク、技術的取消、後続PRへの引継ぎを記載し、該当なしも「なし」と明記する。
- 完了記録を同じ作業BranchへCommit・Pushし、Pull Requestへ反映されたことを確認する。
- Pull Request URLと検証結果をユーザーへ報告し、次のPull Request単位へ進まず停止する。

---

## PR1: Server・API契約・正式文書

### 1.1 作業開始

- [x] PR1の作業準備が完了している。
  - [x] `requirements.md`、`design.md`、本ファイルを再確認する。
  - [x] 名前変更・移動に直接関係する正式文書の節を再確認する。
  - [x] `git status`と既存差分を確認し、承認済みSteering文書を保全する。
  - [x] 現在のAPI、`FileEntry`、`FileOperation`、Recovery、Repository、FileStore、Server Testの類似実装を確認する。
  - [x] 原則として最新の`main`を基点に短命なPR1作業Branchを作成する。
  - [x] `main`上の`verify-config.sh`、`verify-server.sh`、`verify-security.sh`の開始状態を確認する（先頭の`verify-config.sh`はローカル環境の`shellcheck`未導入で停止。MVP PR7の最終CI run `31099468412`でConfig・Server・Security成功済み）。

### 1.2 正式文書・OpenAPI契約

- [x] 名前変更・移動の正式仕様を実装契約へ更新する。
  - [x] `docs/product-requirements.md`で名前変更・移動をPhase 1の実装対象として識別できるようにし、受け入れ条件を本Steeringと整合させる。
  - [x] `docs/functional-design.md`へ`PATCH /api/v1/files/{fileId}`、Request制約、Response、Error、処理Flow、Recovery、Test、実装順序を反映する。
  - [x] `docs/architecture-design.md`でRename・MoveをMVP後の未実装一覧から現行拡張へ移し、advisory lock、atomic rename、操作ジャーナル、隔離・復旧境界を反映する。
  - [x] `docs/repository-structure.md`へ既存Server File機能内で実装する配置を反映し、新しいProjectや将来用Directoryを追加しないことを明記する。
  - [x] `docs/development-guidelines.md`へAPI、Domain、非上書き、`fileVersion`維持、並行制御、Recovery、Test規約を反映する。
  - [x] 正式文書間でMVP完成範囲、Phase 1拡張、MVP後機能の表現が矛盾していないことを確認する。
- [x] OpenAPI契約を追加する。
  - [x] `PATCH /api/v1/files/{fileId}`を追加する。
  - [x] `UpdateFileRequest`で`name`または`parentId`の一方だけを許可する契約を定義する。
  - [x] 成功時の既存`FileItem` Responseを定義する。
  - [x] `VALIDATION_FAILED`、`FILE_NOT_FOUND`、`FILE_NAME_CONFLICT`、`FILE_MOVE_CYCLE`、`FILE_OPERATION_NOT_ALLOWED`、`RECOVERY_REQUIRED`、`STORAGE_UNAVAILABLE`を定義する。
  - [x] 既存EndpointとSchemaに非互換変更がないことを確認する。

### 1.3 Domain

- [x] `FileEntry`へ名前変更・移動の不変条件を実装する。
  - [x] `ACTIVE`かつ非RootのFile・FolderだけをRenameできるDomain操作を追加する。
  - [x] `ACTIVE`かつ非RootのFile・FolderだけをMoveできるDomain操作を追加する。
  - [x] 子孫の相対Pathだけを更新するDomain操作を追加する。
  - [x] RenameでName・RelativePath・UpdatedAtだけを更新する。
  - [x] MoveでParentId・RelativePath・UpdatedAtだけを更新する。
  - [x] File ID、Owner、種別、内容属性、CreatedAt、`fileVersion`を変更しない。
  - [x] 不正状態遷移をDomain例外として拒否する。
- [x] `FileOperation`をRename・Moveへ対応させる。
  - [x] `FileOperationType`へ`Rename`と`Move`を追加する。
  - [x] 既存のsource・target・FileEntry ID・状態遷移だけで復旧に必要な情報を保持できることを確認する。
  - [x] 既存文字列列の長さと変換で追加値を保存でき、専用Migrationが不要であることを確認する。

### 1.4 Persistence・並行制御

- [x] File Repositoryを配置変更へ対応させる。
  - [x] 所有UserとRelativePathから`ACTIVE` Folderを取得するQueryを追加する。
  - [x] `AuditLog`をFile操作と同じDbContextへ追加できるRepository操作を追加する。
  - [x] 未完了Rename・Moveを対象File IDと配下の取得処理から識別できるQueryを追加する。
  - [x] 既存の所有User、Active状態、同名一意制約を維持する。
- [x] PostgreSQL advisory lockを実装する。
  - [x] GUIDから安定した64-bit lock keyを導出する。
  - [x] 対象、source親、target親のlock keyを昇順に取得する。
  - [x] 同一DB Connectionで要求処理終了までLockを保持し、成功・失敗・取消・例外時に解放する。
  - [x] Rename・Move・Recoveryで同じLock規則を使用する。
  - [x] 同時Rename、Move、Trash、Restoreが上書き、循環、Deadlock、DB・HDD不整合を起こさないよう既存変更操作も同じ対象Lockへ整合させる。

### 1.5 Application名前変更・移動

- [x] Rename処理を実装する。
  - [x] `RenameFileCommand`と`FileService.RenameAsync`を追加する。
  - [x] StorageGuard、所有User、`ACTIVE`、非Root、FileNameを検証する。
  - [x] Lock取得後に対象と競合を再取得する。
  - [x] 同じ正規化済み名前への要求を副作用のない成功にする。
  - [x] DBとHDDの同名項目を上書きせず`FILE_NAME_CONFLICT`にする。
  - [x] `FileOperation(RENAME, PENDING)`をHDD操作前に保存する。
  - [x] 同一Filesystem内でFileまたはFolderをrenameする。
  - [x] Folder配下の全RelativePathをsource prefixからtarget prefixへ更新する。
  - [x] 対象と配下の`fileVersion`を変更しない。
  - [x] FileEntry更新、成功監査、Operation完了を同じDB Transactionで確定する。
- [x] Move処理を実装する。
  - [x] `MoveFileCommand`と`FileService.MoveAsync`を追加する。
  - [x] StorageGuard、所有User、対象`ACTIVE`・非Root、移動先`ACTIVE` Folderを検証する。
  - [x] Lock取得後に対象、移動先、競合を再取得する。
  - [x] 現在と同じ親への要求を副作用のない成功にする。
  - [x] Folderの自分自身・子孫への移動を`FILE_MOVE_CYCLE`で拒否する。
  - [x] 移動後の階層深度が64を超える操作をHDD変更前に拒否する。
  - [x] 他User、Trash、非Folder、Storage Root外を移動先にできない。
  - [x] DBとHDDの同名項目を上書きせず`FILE_NAME_CONFLICT`にする。
  - [x] `FileOperation(MOVE, PENDING)`をHDD操作前に保存する。
  - [x] 同一Filesystem内でFileまたはFolderをrenameする。
  - [x] 対象ParentId・RelativePathとFolder配下の全RelativePathを更新する。
  - [x] 対象と配下の`fileVersion`を変更しない。
  - [x] FileEntry更新、成功監査、Operation完了を同じDB Transactionで確定する。
- [x] File操作の監査を実装する。
  - [x] `FILE_RENAME`と`FILE_MOVE`へActor User、Actor Device、File ID、Result、Request IDを記録する。
  - [x] 名前、相対Path、物理絶対Path、ファイル内容、Credentialを監査・Logへ記録しない。
  - [x] 公開Error Codeと監査結果が対応する。

### 1.6 FileStore・障害復旧・隔離

- [x] FileStoreの安全なrename動作を確認・補強する。
  - [x] RelativePathをStorage Root内へ解決し、絶対Path、`..`、代替区切り文字、NUL、Symlinkを拒否する。
  - [x] targetが存在する場合にFile・Directoryを上書きしない。
  - [x] FileとFolderを区別し、内容コピーを行わず同一Filesystem内で移動する。
  - [x] IOException後にsource・targetを再確認し、競合、Storage異常、結果不明を区別する。
- [x] `FileOperationRecoveryService`をRename・Moveへ対応させる。
  - [x] Recoveryでも対象advisory lockを取得する。
  - [x] sourceあり・targetなし・DB sourceではHDD renameを安全に再実行する。
  - [x] sourceなし・targetあり・DB sourceでは`FILESYSTEM_DONE`からDB確定する。
  - [x] sourceなし・targetあり・DB targetではOperationだけを完了する。
  - [x] source・target両方あり、両方なし、DB不一致を`RECOVERY_REQUIRED`にする。
  - [x] FileEntryTypeでFile・Directoryの存在を確認する。
  - [x] Renameではtarget末尾のName、Moveではtarget親RelativePathのFolder IDを復元する。
  - [x] Folder配下RelativePath、成功監査、Operation完了を1 Transactionで確定する。
  - [x] target親が存在しない、非`ACTIVE`、別User、非一意の場合は自動確定しない。
- [x] 未完了操作の対象を通常利用から隔離する。
  - [x] 未完了Rename・Moveの対象と配下を通常一覧から公開しない。
  - [x] 詳細、Download、追加変更操作を`RECOVERY_REQUIRED`で拒否する。
  - [x] Recovery完了後に対象を通常利用へ戻す。

### 1.7 API実装

- [x] `PATCH /api/v1/files/{fileId}`を実装する。
  - [x] Access TokenからUser IDとDevice IDを取得する。
  - [x] `name`または`parentId`の一方だけを受け付ける。
  - [x] Rename・Move CommandへRequest IDを渡す。
  - [x] 成功時に更新後の既存`FileItem`を`200`で返す。
  - [x] Domain・Application FailureをOpenAPIどおりのHTTP StatusとError Codeへ変換する。
  - [x] 他Userの対象・移動先、非`ACTIVE`対象の存在を`404 FILE_NOT_FOUND`へ統一する。
  - [x] 既存認証・Device失効Middlewareを迂回しない。

### 1.8 Server自動Test

- [x] Domain・Application単体Testが完了している。
  - [x] File・FolderのRename・Move成功をTestする。
  - [x] Root、`TRASHED`、他User、非Folder移動先を拒否する。
  - [x] FileNameの空白、Unicode正規化、255文字境界、予約値、区切り文字、NUL、制御文字をTestする。
  - [x] 同名競合と同一名前・同一親への再実行をTestする。
  - [x] Folderの自分自身・子孫移動と深度64境界をTestする。
  - [x] Folder配下Path更新とFile ID・Owner・内容属性・`fileVersion`維持をTestする。
  - [x] Error Codeと監査内容をTestする。
- [x] Server結合Testが完了している。
  - [x] PATCHのRename、Move、両方指定、両方未指定、不正値をTestする。
  - [x] 認証なし、失効Device、他Userの対象・移動先をTestする。
  - [x] PostgreSQL一意制約とHDD target存在による競合をTestする。
  - [x] File・Folderのrenameと配下を持つFolder移動後のDB・HDD一致をTestする。
  - [x] 変更前後のFile ID、`fileVersion`、Range Download SHA-256一致をTestする。
  - [x] HDD未Mount、Storage ID不一致、読み取り専用、Path Traversal、SymlinkをTestする。
  - [x] 並行Rename、Move、Trash、Restoreの直列化とDeadlock不発生をTestする。
  - [x] source・target・DB状態のRecovery組合せをTestする。
  - [x] atomic rename後・DB確定前の停止を模擬し、再起動相当RecoveryをTestする。
  - [x] `RECOVERY_REQUIRED`対象と配下の一覧・詳細・Download隔離をTestする。
  - [x] Folder作成、Upload、Download、Trash、Restoreの回帰をTestする。
- [x] OpenAPI・設定・Security契約Testを更新する。
  - [x] Server Endpoint、Request、Response、ErrorとOpenAPIの一致をTestする。
  - [x] File・Folder IDOR、Path Traversal、Symlink、非上書きをSecurity Testへ追加する。
  - [x] 新しいSecret、環境変数、依存Packageが不要であることを検証する。
  - [x] Test専用`Testcontainers.PostgreSql`を4.14.0へ更新し、推移依存`SSH.NET 2025.1.0`の既知High脆弱性（`GHSA-q939-rpr3-3284`）を解消する（開始時Restoreで新たに検出されたため追加）。

### 1.9 PR1品質確認

- [x] PR1の自動検証がすべて成功している。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [x] `git diff --check`が成功する。
- [x] PR1の機能確認が完了している。
  - [x] API ClientからFile名変更、Folder名変更、File移動、配下を持つFolder移動を実行できる。
  - [x] 同名競合、循環、Root、他User、Storage異常を仕様どおり拒否する。
  - [x] 操作後のDB・HDD・File ID・`fileVersion`・SHA-256が要件どおりである。
  - [x] Recoveryと通常利用隔離を障害注入Testで確認できる。
  - [x] 既存MVP File APIに回帰がない。
- [x] PR1の設定・依存関係確認が完了している。
  - [x] 新しい環境変数、Secret、物理絶対Pathが追加されていない。
  - [x] 新しいNuGet Package、Android Library、Server Project、Android Moduleが追加されていない。
  - [x] Rename・Move専用Migrationがなくても既存DBへOperation Typeを保存できる。

### 1.10 Pull Request完了

- [ ] PR1が完了している。
  - [x] PR1内の1.1〜1.9がすべて`[x]`である。
  - [ ] 共通Pull Request完了手順をすべて実施する。
  - [ ] PR1の完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [ ] PR1のPull Requestへ完了記録Commitが反映されている。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR2: Android操作・実機E2E

### 2.1 作業開始

- [ ] PR2の作業準備が完了している。
  - [ ] PR1が`main`へMerge済みであることを確認する。
  - [ ] 最新の`main`を取得し、PR2用の短命Branchを作成する。
  - [ ] `requirements.md`、`design.md`、本ファイル、PR1完了記録を確認する。
  - [ ] `git status`と既存差分を確認する。
  - [ ] AndroidのFile DTO、API、Repository、ViewModel、Compose UI、Testの既存パターンを再確認する。
  - [ ] Raspberry Pi、共有exFAT HDD、Android実機、LAN、ZeroTier、Release署名入力が利用可能であることを確認する。

### 2.2 Android Network・Data

- [ ] AndroidのAPI契約を実装する。
  - [ ] `UpdateFileRequestDto`を追加する。
  - [ ] `FileApi`へ`PATCH /api/v1/files/{fileId}`を追加する。
  - [ ] Renameでは`name`だけ、Moveでは`parentId`だけを送る。
  - [ ] 新しいError Codeを既存のValidation、Conflict、Storage分類へ追加する。
  - [ ] Access Token Refresh、Device失効、Request ID保持を既存Executorで維持する。
- [ ] AndroidのFile Repositoryを実装する。
  - [ ] `rename(fileId, name)`を追加する。
  - [ ] `move(fileId, targetParentId)`を追加する。
  - [ ] Server Responseを既存`FileEntry` Modelへ変換する。
  - [ ] 通信結果不明時に成功状態をローカル合成しない。

### 2.3 ViewModel・移動先選択

- [ ] `FileBrowserViewModel`へ名前変更状態と操作を追加する。
  - [ ] Rename対象、入力、処理中、Error、結果状態を管理する。
  - [ ] Rename成功後に現在一覧と選択中詳細をServerから再取得する。
  - [ ] 失敗または通信結果不明時に再取得操作を提供する。
- [ ] `FileBrowserViewModel`へ移動先Picker状態と操作を追加する。
  - [ ] Move対象、現在Folder、Folder stack、候補Paging、Loading、Errorを管理する。
  - [ ] 個人RootからPickerを開始する。
  - [ ] `ACTIVE` Folderだけを候補として扱う。
  - [ ] Folder移動時は対象Folderを開けず、配下へNavigationできないようにする。
  - [ ] 現在親を確定不可または副作用のない選択として扱う。
  - [ ] Move成功後に元一覧と必要な詳細をServerから再取得する。
  - [ ] Serverの循環・所有者・状態検証をClient制御で代替しない。

### 2.4 Compose UI

- [ ] 名前変更UIを実装する。
  - [ ] 通常一覧または詳細のFile・Folder ActionからRename Dialogを開ける。
  - [ ] 現在名を入力済みで表示する。
  - [ ] 空白、明らかな長さ超過、区切り文字をClient側で検証する。
  - [ ] 実行中の重複送信を防ぎ、成功後の名前を再取得結果から表示する。
- [ ] 移動UIを実装する。
  - [ ] 通常一覧または詳細のFile・Folder ActionからMove Pickerを開ける。
  - [ ] Folder階層を進む、戻る、移動先に確定する操作を提供する。
  - [ ] 移動対象名と選択した移動先を確定前に確認できる。
  - [ ] 対象Folderとその配下をUI上で移動先に選択できない。
  - [ ] 実行中の重複送信を防ぎ、成功後の場所を再取得結果から表示する。
- [ ] Errorと状態表示を実装する。
  - [ ] 同名競合では別名または別Folderの選択を案内する。
  - [ ] 循環移動ではPickerへ戻れる表示を行う。
  - [ ] 対象・移動先消失では一覧再取得を案内する。
  - [ ] Storage異常とRecovery要求を成功表示せず区別する。
  - [ ] 認証切れ、Device失効を既存Flowへ接続する。
  - [ ] Trash画面にRename・Move Actionを表示しない。

### 2.5 Android自動Test

- [ ] Network・Repository Testが完了している。
  - [ ] Rename・Move Request JSONとResponse変換をTestする。
  - [ ] OpenAPI FixtureとDTO・Endpointの一致をTestする。
  - [ ] 401 Refresh後の再送、Device失効、各Error Code、通信結果不明をTestする。
  - [ ] RepositoryがRename・Moveで正しい項目だけを送信することをTestする。
- [ ] ViewModel Testが完了している。
  - [ ] Rename成功、競合、失敗、再取得をTestする。
  - [ ] PickerのRoot開始、Folder遷移、Back、Paging、確定をTestする。
  - [ ] Folder対象と子孫へのNavigation抑止をTestする。
  - [ ] Move成功、循環、対象消失、Storage異常、再取得をTestする。
- [ ] Compose UI Testが完了している。
  - [ ] File・FolderのRename入口、入力済みDialog、確認、Loading、ErrorをTestする。
  - [ ] File・FolderのMove入口、Picker遷移、移動先確認、Loading、ErrorをTestする。
  - [ ] Root、対象Folder、現在親、Trash画面の操作制限をTestする。
  - [ ] 既存一覧、詳細、Folder作成、Transfer、Trash、Restore UI Testに回帰がない。
- [ ] Android Instrumented Testが完了している。
  - [ ] FakeまたはTest ServerでRename・Moveの正常系と主要Errorを確認する。
  - [ ] `connectedDebugAndroidTest --max-workers=1`が成功する。

### 2.6 Artifact・Raspberry Pi配置

- [ ] ServerとAndroidの実機確認用Artifactを生成する。
  - [ ] PR1 Merge済みServerを既存Release手順でPublishする。
  - [ ] Server ArtifactのChecksumを生成・検証する。
  - [ ] 確定済みRoot CA、API設定、Repository外Signing KeyでRelease APKを生成する。
  - [ ] APKの署名、Package ID、Version、Debuggable無効、Checksumを検証する。
  - [ ] Secret、Private Key、実環境CredentialをRepositoryへ追加しない。
- [ ] Raspberry Piへ安全に配置する。
  - [ ] 配置前にDatabaseとKuraStorage Storage RootのBackupを取得する。
  - [ ] 既存Install・Upgrade手順でServer Artifactを配置する。
  - [ ] Migration追加がないことを確認し、既存Databaseを保持する。
  - [ ] `deployment/raspberry-pi/verify.sh`でAPI、Nginx、PostgreSQL、HDD、Storage IDを確認する。
  - [ ] Rollback可能な直前Artifactと設定を保持する。

### 2.7 実機E2E

- [ ] Android実機で正常系を確認する。
  - [ ] LANでFile名変更とFolder名変更を実行する。
  - [ ] LANでFile移動と配下を持つFolder移動を実行する。
  - [ ] ZeroTierでFile名変更、Folder名変更、File移動、Folder移動を実行する。
  - [ ] 変更前後のFile ID、`fileVersion`、内容SHA-256一致を確認する。
  - [ ] 変更後の一覧、詳細、Range Downloadを確認する。
  - [ ] Raspberry PiとAndroid再起動後も変更後の項目を利用できる。
- [ ] Android実機で拒否・障害系を確認する。
  - [ ] 同名Renameと同名項目のあるFolderへのMoveが既存項目を上書きしない。
  - [ ] Folderを自分自身または子孫へ移動できない。
  - [ ] Root、Trash項目、他User項目を変更できない。
  - [ ] HDD未MountまたはStorage利用不可時に操作を開始せず、Pi本体へ誤保存しない。
  - [ ] 通信中断または結果不明時にAndroidが成功表示せず、再取得で確定状態を表示する。
  - [ ] LogにPassword、Token、Key、ファイル内容、物理絶対Pathがない。
- [ ] 連続実行と回帰を確認する。
  - [ ] 名前変更・移動を含む主要シナリオをLANで10回連続成功させる。
  - [ ] 名前変更・移動を含む主要シナリオをZeroTierで10回連続成功させる。
  - [ ] Folder作成、Upload、Download、Trash、Restoreを実機で再確認する。
  - [ ] DB・HDD不整合、上書き、意図しないID・`fileVersion`変更が0件である。

### 2.8 文書最終整合・品質確認

- [ ] 実装結果と文書を最終整合する。
  - [ ] `requirements.md`の全受け入れ条件に対応する実装または検証記録がある。
  - [ ] `design.md`と実装の差分がある場合、理由と確定設計を反映する。
  - [ ] 5つの正式文書、OpenAPI、Server、AndroidでEndpoint、DTO、Error、状態名が一致する。
  - [ ] Android操作と実機確認手順を必要な運用文書へ反映する。
  - [ ] 将来の共有・派生データ・検索への拡張境界を損なっていない。
- [ ] 全自動検証が成功している。
  - [ ] `./scripts/ci/verify-config.sh`が成功する。
  - [ ] `./scripts/ci/verify-server.sh`が成功する。
  - [ ] `./scripts/ci/verify-security.sh`が成功する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] `./apps/android/gradlew -p apps/android connectedDebugAndroidTest --max-workers=1`が成功する。
  - [ ] `git diff --check`が成功する。
- [ ] CIと成果物確認が完了している。
  - [ ] GitHub ActionsのConfig、Server、Security、Android必須Jobが最終実装HEADで成功する。
  - [ ] Server Artifact、Release APK、Checksumを再現可能な既存手順で生成できる。
  - [ ] 新しい環境変数、Secret、依存Library、Moduleが不要であることを確認する。

### 2.9 Pull Request完了

- [ ] PR2が完了している。
  - [ ] PR2内の2.1〜2.8がすべて`[x]`である。
  - [ ] 共通Pull Request完了手順をすべて実施する。
  - [ ] PR2の完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [ ] PR2のPull Requestへ完了記録Commitが反映されている。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## 各Pull Request完了記録

> Pull Request作成後に、そのPull Requestの記録だけを追記する。後続PRが未完了でも、完了したPRの記録はその時点で行う。

### PR1: Server・API契約・正式文書

- 完了日: 2026-08-18
- Pull Request: [#9 Add server-side file rename and move support](https://github.com/ry825/Kura_Storage/pull/9)
- 主な変更: `PATCH /api/v1/files/{fileId}`、Rename・MoveのDomain/Application/Persistence/FileStore/Recovery、PostgreSQL advisory lock、未完了操作の隔離、監査、OpenAPI・正式文書を実装した。File ID、Owner、内容属性、`fileVersion`を維持し、所有境界、非上書き、循環・深度制約、Storage安全性を既存File操作と整合させた。
- 実施した自動Test・Build・静的解析: ローカルで`verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-android.sh`、`git diff --check`が成功した。Server TestはDomain 17件、Application 12件、Integration 32件の計61件が成功した。`dotnet list ... --vulnerable --include-transitive`で既知脆弱性0件を確認した。GitHub Actions run `32138160746`でConfig、Security、Server、Androidがすべて成功した。
- 実施した手動・結合・障害確認: Test API ClientとPostgreSQL・実Filesystemを使用し、File・Folder Rename、File・配下を持つFolder Move、同名・循環・Root・他User・失効Device・Storage異常の拒否、HDD競合、並行変更、atomic rename後のRecovery、一覧・詳細・Download隔離、ID・`fileVersion`・SHA-256維持を確認した。
- 計画と実装の差分: 開始時の依存脆弱性検査でTestcontainers 4.13.0の推移依存`SSH.NET 2025.1.0`にHigh脆弱性が判明したため、Testcontainersを4.14.0へ更新した。API実装の認証Middleware再利用を明示検証するため、失効DeviceのPATCH結合Testを追加した。その他の要求・設計差分はなし。
- 実装中に追加したタスクと理由: Testcontainers.PostgreSql 4.14.0への更新と推移依存脆弱性解消を1.8へ追加した。理由は開始時Restoreで`GHSA-q939-rpr3-3284`が新たに検出されたため。失効DeviceのPATCH結合Testも受け入れ条件の直接確認として追加した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- PR2への引継ぎ事項: PR #9を`main`へMergeした後、最新`main`からPR2 Branchを作成する。既存OpenAPIとError Codeを使用してAndroid Network・Repository・ViewModel・Compose UIを実装し、Raspberry Pi・共有exFAT HDD・Android実機でLAN/ZeroTier E2Eを完了する。PR1はMergeしないまま停止する。

### PR2: Android操作・実機E2E

- 完了日: 未完了
- Pull Request: 未作成
- 主な変更: 未記録
- 実施した自動Test・Build・静的解析: 未記録
- 実施した手動・実機確認: 未記録
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク、理由、代替実装: 未記録
- 後続作業への引継ぎ事項: 未記録

---

## 全体振り返り

> PR1とPR2を含む本ファイルの全タスクが完了し、各Pull Request完了記録が存在する場合だけ記入する。

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
