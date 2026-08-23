# ファイル・フォルダ共有と権限制御 タスクリスト

## 対象

- 正式要件: `docs/product-requirements.md` 7.8.2「MVP後: ファイル・フォルダ共有」
- 目的: 個人ストレージの分離を維持しながら、家族UserにFileまたはFolder単位の権限を付与し、所有者・直接共有・継承共有を全File APIで一貫して評価する。
- 完了条件: `VIEWER`、`CONTRIBUTOR`、`EDITOR`、`MANAGER`、Folder継承、複数経路の最強権限、共有管理、Rename・Move・Trash・Restore・Purgeの整合性がServer、Android、Raspberry Pi実機で検証されている。

## 作業開始前の前提

- [x] 同じ作業ディレクトリの`requirements.md`が作成・承認されている。
- [x] 同じ作業ディレクトリの`design.md`が作成・承認されている。
- [x] 承認済みの要求と設計を4つのPull Request単位へ分解している。
- [x] 実装開始時に承認済み設計と本タスクリストの差分がないことを確認する。
- [ ] 各PR開始時に依存元PRのMergeと必須CI成功を確認する。（PR1開始時: PR #18 Merge済み、Config/Server/Security/Android CI成功。PR2〜PR4開始時に継続確認）

## タスク完全完了の原則

**本ファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止する。**

- PR1からPR4まで順番に実施し、同時に複数PRの範囲へ着手しない。
- 実装と対応するUnit・Integration・Contract・UI・E2E Test、文書更新を同じPR範囲に含める。
- 選択したPR単位に未完了タスク`[ ]`を残したまま作業を終了しない。
- 後続PRは依存元PRが`main`へMergeされた後に、最新の`main`から短命Branchを作成する。
- 「時間の都合」「難しい」を理由にタスクを省略しない。大きすぎるタスクは実装可能な子タスクへ分割する。
- 技術的に不要になったタスクだけ、取消理由と代替実装を明記して完了扱いにできる。
- 検索、最近使用、公開Link、Group/Deny ACL、自動Backup、Media派生データは本作業に含めない。

---

## PR1: Sharing domain・Persistence・Authorization基盤

### 1.1 作業開始と仕様整合

- [x] PR1の作業準備を完了する。
  - [x] `requirements.md`、`design.md`、本ファイルと、5つの正式文書の共有・認可・File操作・Testの関連節を再確認する。
  - [x] `git status`と既存差分を確認し、ユーザーの変更を保護する。（本作業の未追跡Steering文書だけを保持）
  - [x] 未Mergeの依存PRがないことを確認し、最新`main`からPR1用の短命Branchを作成する。（PR #18のMergeと必須CI成功を確認し、`feat/file-sharing-authorization-foundation`を作成）
  - [x] `FileEntry`、`FileRepository`、`FileService`、`UploadSession`、`TrashPurgeService`、`MissingEntryService`、EF Migration、API Error mappingの既存パターンを再確認する。
  - [x] PostgreSQL TestcontainerとServer検証Scriptを実行できることを確認する。（変更前`verify-server.sh`: Domain 34件、Application 107件、Integration 77件成功）
- [x] 正式文書へ承認済みの設計決定を反映する。
  - [x] `docs/product-requirements.md`の受け入れ条件に4段階権限、複数経路、Rename・Move・Trash・Restore・Purge整合性を欠落なく追加する。
  - [x] `docs/functional-design.md`にActor/Owner分離、操作別権限表、Moveの3対象認可、Trash非公開、Owner Restore、Tie-break、Upload完了時再認可を反映する。
  - [x] `docs/architecture-design.md`にSharing Domain、Authorization Query、Schema・Index、Batch解決、Lock・TOCTOU対策、Test方針を反映する。
  - [x] `docs/repository-structure.md`にServer Sharing配置とAndroid `feature-sharing`の実配置を反映する。
  - [x] `docs/development-guidelines.md`に認可の共通境界、Actor/Owner非混同、認可失効競合、共有Testの必須規則を反映する。
  - [x] 上位文書間の用語、Error Code、権限表、MVP後スコープが一致している。

### 1.2 Sharing Domain

- [x] `SharePermission`と認可内部の強度をTDDで実装する。
  - [x] `VIEWER < CONTRIBUTOR < EDITOR < MANAGER`の順序と比較を定義する。
  - [x] `OWNER`と`NONE`は共有永続化値に含めず、認可内部でだけ扱う。
  - [x] 操作別の最小権限を1か所に定義し、境界の許可・拒否をUnit Testする。
- [x] `Share`と`ShareMember`をTDDで実装する。
  - [x] ID、Target Entry ID、Owner User ID、Member User ID、Permission、作成・更新時刻を表現する。
  - [x] Member追加、Permission変更、Member解除の状態変更と`updatedAt`更新を実装する。
  - [x] 空GUID、重複User、無効Permission、所有者のMember登録をApplicationと連携して拒否できる境界を作る。
  - [x] 最後のMember解除後に空Shareを残さない規則を実装・Testする。（`RemoveMember`の戻り値でAggregate削除を要求）
  - [x] DomainがEF Core、Npgsql、ASP.NET Coreに依存しないことをArchitecture Testで保護する。

### 1.3 PersistenceとMigration

- [x] Share AggregateのEF Core永続化を実装する。
  - [x] `KuraStorageDbContext`へ`Share`と`ShareMember`のDbSetを追加する。
  - [x] `shares`と`share_members`のTable名、snake_case列、Permission変換、時刻、Foreign KeyをEF Configurationで定義する。
  - [x] `shares.target_entry_id`の一意制約と`share_members(share_id, user_id)`の複合Primary Key/一意制約を実装する。
  - [x] `share_members.permission`を4値に限定するCheck Constraintを実装する。
  - [x] FileEntry削除からShare、Share削除からMemberへのCascadeと、User削除の安全な制約を定義する。
  - [x] `shares(owner_user_id, updated_at, id)`、`share_members(user_id, share_id)`、認可Queryに必要なIndexを追加する。
- [x] Upload SessionのActorとTarget Ownerを分離するSchema基盤を追加する。
  - [x] 現行`owner_user_id`を`actor_user_id`へ安全に改名する。
  - [x] `target_owner_user_id`を追加し、既存行を`actor_user_id`からBackfillしてから`NOT NULL`にする。
  - [x] Actor・Idempotency Keyの一意制約、Actor・Status Index、User Foreign Keyを改名後も維持する。
  - [x] DomainとRepositoryの命名をActor/Target Ownerに合わせ、PR1時点の個人Uploadに回帰を起こさない。（Upload Session API統合Test 13件成功）
- [x] `AddFileSharing`相当のMigrationとModel Snapshotを作成する。
  - [x] Migration Upが既存User、FileEntry、Upload Sessionを保持して成功する。
  - [x] Rollback可否と共有先Upload Sessionがある場合の制約を明記し、データを無言削除しない。（ShareまたはActor/Target Owner差異がある場合はRollbackを明示拒否）
  - [x] `dotnet ef migrations has-pending-model-changes`またはリポジトリ標準の同等確認が成功する。

### 1.4 Authorization RepositoryとService

- [x] `EffectivePermission`と候補契約を実装する。
  - [x] `Permission`、`OWNER | DIRECT | INHERITED`、`ShareTargetId`、`ShareId`を表現する。
  - [x] Ownerは常に最強とし、共有元IDを返さない。
  - [x] 同強度ではDIRECTをINHERITEDより優先し、INHERITED同士では最も近い祖先を説明用の権限元とする。
- [x] `IAuthorizationRepository`とPostgreSQL実装を追加する。
  - [x] Actor User IDと複数Entry IDから、Owner、直接Share、祖先Folder Shareの候補をBatch取得する。
  - [x] 再帰CTEまたは同等の有界Queryで祖先を辿り、深度64で打ち切り、循環や不正Treeを成功扱いにしない。
  - [x] `TRASHED`と未完了FileOperation中の対象・子孫を通常の共有解決から除外する。
  - [x] File直接Shareが親・兄弟へ波及せず、Folder Shareだけが子孫へ継承するQueryにする。
  - [x] `share_members(user_id, share_id)`とFile階層Indexを利用するQuery planを確認する。
- [x] `AuthorizationService`をTDDで実装する。
  - [x] 単一項目とページ内複数項目を同じBatch解決ロジックで判定する。
  - [x] 複数経路で弱い直接Shareが強い継承Shareを弱めない。
  - [x] `ADMIN` Roleに暗黙の他User File権限を付与しない。
  - [x] 操作ごとの必要Permission以上かを判定する共通APIを提供する。
  - [x] 権限解決結果を要求を超えてCacheせず、Share解除後の次要求で失効させる。

### 1.5 PR1 Test・性能・セキュリティ

- [x] Domain・Application Unit Testを完了する。
  - [x] Permission順序、操作境界、Share Aggregate、Member状態変更の正常・異常系をTestする。
  - [x] Owner、直接、継承、複数祖先、最強、Tie-break、未共有をTestする。
  - [x] Fileの非継承、Folderの全子孫継承、Trash/未完了操作の隔離をTestする。
  - [x] 認証・認可のUnit Testカバレッジ95%以上、Domain/Application全体80%以上を確認する。（AuthorizationService 100%、Domain 85.61%、Application 80.33% line coverage）
- [x] PostgreSQL Integration Testを完了する。
  - [x] Migration Up、既存Upload Session Backfill、Foreign Key、Unique、Check Constraint、CascadeをTestする。
  - [x] 実際のFolder階層で直接・継承・複数経路のBatch Query結果をTestする。
  - [x] 100件PageでN+1 Queryが発生せず、上限64と不正循環で無制限に再帰しないことを確認する。
  - [x] User ID、File ID、File名、物理PathをMetric Labelや不要なLogへ出さない。（Authorization QueryはMetric/Logを生成せず、識別子をLabel化しない）
- [x] PR1の標準検証を完了する。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。（Domain 49件、Application 119件、Integration 79件）
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-deployment.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [x] `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`が成功する。
  - [x] `git diff --check`が成功する。

### 1.6 PR1完了

- [x] PR1差分をセルフレビューする。
  - [x] 1.1〜1.5が承認済み要求・設計と対応し、Share APIやFile API統合を先行実装していない。
  - [x] Domain/Application/Infrastructureの依存方向と単一設置を家族境界とする前提が明確である。
  - [x] 新しい外部Package、将来用Column、Group/Deny ACL、検索を追加していない。
  - [x] Credential、実環境値、物理Path、生成物が差分に含まれていない。
- [x] PR1を完了する。
  - [x] 1.1〜1.6のPR1対象項目がすべて`[x]`である。
  - [x] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR1完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR2: 共有管理APIと共有Fileの閲覧

### 2.1 作業開始

- [ ] PR2の作業準備を完了する。
  - [ ] PR1が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`からPR2用の短命Branchを作成する。
  - [ ] `requirements.md`、`design.md`、本ファイル、PR1完了記録を確認する。
  - [ ] `git status`、PR1のSchema・AuthorizationService、既存API/OpenAPI/Integration Testパターンを確認する。

### 2.2 SharingServiceとRepository

- [ ] 共有候補Use CaseをTDDで実装する。
  - [ ] 認証User本人を除く`ACTIVE`なUserを表示名・IDの安定順で取得する。
  - [ ] 非`ACTIVE` User、ロック状態の扱い、候補APIが返す最小情報を正式文書と一致させる。
  - [ ] 候補APIからUser作成、無効化、Role変更を実行できない。
- [ ] Share作成Use CaseをTDDで実装する。
  - [ ] 対象をIDで取得し、`ACTIVE`、非Root、ActorがOwner、File/Folderのどちらかを再検証する。
  - [ ] 初期Memberを1件以上必須とし、重複、本人/所有者、非`ACTIVE` Userを拒否する。
  - [ ] File共有の`CONTRIBUTOR`を`INVALID_SHARE_PERMISSION`で拒否し、Folder共有の4権限を許可する。
  - [ ] Share、Member、監査Logを同一DB Transactionで作成し、一意制約競合を`SHARE_CONFLICT`へ変換する。
- [ ] 所有・受信共有一覧とShare詳細Use Caseを実装する。
  - [ ] `scope=owned|received`、`targetType=FILE|FOLDER`、page、pageSizeを検証する。
  - [ ] 受信一覧は直接Shareのルートだけを返し、Folder子孫を個別展開しない。
  - [ ] `TRASHED`、Purge中、未完了配置操作中の対象を通常共有一覧から除外する。
  - [ ] Shareの所有者または有効なMemberだけが詳細を取得できる。
  - [ ] Entry名、種別、Owner、Recipientの実効Permission、Member一覧を過不足なく返す。
- [ ] Member更新・解除とShare全体解除をTDDで実装する。
  - [ ] Ownerまたは現在の`MANAGER`にだけ操作を許可する。
  - [ ] `PUT members/{userId}`を未登録の追加と登録済みのPermission更新として冪等にする。
  - [ ] 更新時にもFileの`CONTRIBUTOR`、所有者、非`ACTIVE` Userを再検証する。
  - [ ] 最後のMember解除でShare自体を削除し、同一Transactionで監査Logを追記する。
  - [ ] 解除後の次File API要求でアクセスが失効し、他の共有経路があればその最強権限を維持する。

### 2.3 Share API・OpenAPI・Error

- [ ] 共有EndpointをApplication Use Caseへ接続する。
  - [ ] `GET /api/v1/shares/candidates`を追加する。
  - [ ] `POST /api/v1/shares`と`GET /api/v1/shares`を追加する。
  - [ ] `GET /api/v1/shares/{shareId}`を追加する。
  - [ ] `PUT /api/v1/shares/{shareId}/members/{userId}`とMember解除Endpointを追加する。
  - [ ] `DELETE /api/v1/shares/{shareId}`を追加する。
  - [ ] EndpointはJWTの`sub`、`device_id`、Request IDを使い、Client指定のOwner/Actor/Deviceを受け付けない。
- [ ] 共有ContractとError mappingを実装する。
  - [ ] `ShareCandidate`、`ShareMemberItem`、`ShareItem`、一覧Page、Create/Update Requestを定義する。
  - [ ] `INVALID_SHARE_PERMISSION`、`SHARE_NOT_FOUND`、`SHARE_MEMBER_NOT_FOUND`、`SHARE_CONFLICT`、`SHARE_OPERATION_NOT_ALLOWED`を安定したHTTP応答へMappingする。
  - [ ] 存在を隠すべきFile/Shareは404へ統一し、Owner、Member、Path、SQLをErrorに含めない。
  - [ ] `contracts/openapi/kurastorage-api.yaml`に全Endpoint、Schema、Paging、Filter、Error、認証を反映する。

### 2.4 File一覧・詳細・Downloadの共有認可

- [ ] File response契約を後方互換に拡張する。
  - [ ] `FileItem`にOwner ID・表示名、Permission、`OWNER | DIRECT | INHERITED`、`shareTargetId?`を追加する。
  - [ ] Ownerには`permission=MANAGER`、`permissionSource=OWNER`を返し、`shareTargetId`を省略する。
  - [ ] v1の追加Fieldとして旧Clientが無視でき、破壊的変更がないため`protocolVersion: 2`を維持する。
- [ ] 認証UserとFile Ownerを分離した参照フローを実装する。
  - [ ] `GET /files?parentId=null`は従来どおり認証UserのPersonal RootをProvision・取得する。
  - [ ] `parentId`/`fileId`ありはEntryをIDで取得し、Actorの`VIEWER`以上を確認する。
  - [ ] Folder一覧はOwner Treeの直下を取得し、Page内のPermissionをBatch解決する。
  - [ ] File直接Shareで親・兄弟を取得できず、Folder Shareの子孫だけを階層閲覧できる。
  - [ ] `DownloadAsync`はContent Open直前にActorの`VIEWER`を再確認し、対象のOwnerから安全な相対Pathを解決する。
  - [ ] Share解除、User非`ACTIVE`化、Entry状態変更の直後に古いPermissionで読み続けられない。

### 2.5 PR2 Test・手動確認

- [ ] SharingServiceとShare APIの自動Testを完了する。
  - [ ] 候補、Share作成、所有/受信一覧、詳細、Member追加・更新・解除、Share解除の正常系をTestする。
  - [ ] Fileへの`CONTRIBUTOR`、本人・非`ACTIVE` User、重複Share/Member、無権限、Root、Trash対象をTestする。
  - [ ] Owner、Manager、Editor、Viewer、未共有、Adminごとの参照・管理境界をTestする。
  - [ ] Share解除後の即時失効と別経路維持、同時Member更新競合をTestする。
- [ ] 共有File参照の自動Testを完了する。
  - [ ] 所有者、File直接Share、Folder継承、最強経路のList/Get/DownloadをTestする。
  - [ ] Fileの兄弟漏えい、祖先未共有、Admin暗黙アクセス、無権限ID推測をTestする。
  - [ ] Range Download、MISSING表示、Storage unavailable、未完了Operation隔離の回帰をTestする。
  - [ ] 100件PageのPermission付与がN+1 Queryにならないことを確認する。
- [ ] API ClientでPR2の主要フローを手動確認する。
  - [ ] OwnerがFileとFolderを複数Userへ共有し、所有・受信一覧と詳細を確認する。
  - [ ] RecipientがFileだけまたはFolder子孫を閲覧・Range Downloadできる。
  - [ ] ManagerがMemberとPermissionを更新し、解除後にアクセスが失効する。
  - [ ] Error responseやLogから他UserのOwner、File名、物理Path、SQLが漏えいしない。
- [ ] PR2の標準検証を完了する。
  - [ ] `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-server.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`が成功する。
  - [ ] `./scripts/ci/verify-android.sh`が既存Androidに対して成功する。
  - [ ] `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`、OpenAPI Contract Test、`git diff --check`が成功する。

### 2.6 PR2完了

- [ ] PR2差分をセルフレビューする。
  - [ ] 2.1〜2.5と承認済み要求・設計・OpenAPIが対応している。
  - [ ] EndpointからDbContext/FileStoreを直接呼ばず、全参照要求でAuthorizationServiceを使用している。
  - [ ] Permissionをクライアントだけで判定せず、共有解除を即時反映している。
  - [ ] 不要なPackage、生成物、実環境情報、Credentialが差分にない。
- [ ] PR2を完了する。
  - [ ] 2.1〜2.6のPR2対象項目がすべて`[x]`である。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR2完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR3: 共有先の作成・Upload・変更と整合性

### 3.1 作業開始

- [ ] PR3の作業準備を完了する。
  - [ ] PR2が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`からPR3用の短命Branchを作成する。
  - [ ] Steering文書、PR1・PR2完了記録、`git status`、File mutation・Upload・Recovery・Participantの既存パターンを再確認する。

### 3.2 ContributorのFolder作成とMultipart Upload

- [ ] Folder作成のActor/Owner分離をTDDで実装する。
  - [ ] CommandにActor User・Device・親Folder・Request IDを持たせ、Ownerを親Folderから導出する。
  - [ ] 親Folderへ`CONTRIBUTOR`以上を要求し、`VIEWER`と未共有UserをHDD更新前に拒否する。
  - [ ] 作成するFolderの`OwnerUserId`を親Folder Ownerとし、AuditのActorは実際の作成User/Deviceとする。
  - [ ] 同名、Storage不可、未完了Operation、Share解除競合を従来の整合性契約で扱う。
- [ ] Multipart UploadをContributor対応する。
  - [ ] Actor User/DeviceとDestination FolderからTarget Ownerを導出し、`CONTRIBUTOR`以上を要求する。
  - [ ] 正式公開直前にDestination状態・Owner・Permission・同名をLock内で再検証する。
  - [ ] FileEntry/FileOperationのOwnerはTarget Owner、AuditのActorはUpload User/Deviceとする。
  - [ ] Request BodyのStreaming、Size/Checksum、Idempotency、atomic publish、Recoveryの現行契約を維持する。

### 3.3 Resumable Upload SessionのActor/Target Owner対応

- [ ] Upload Session DomainとRepositoryをActor/Target Ownerに対応する。
  - [ ] Session作成時にActor、Device、Target Owner、Destination Folderを固定する。
  - [ ] Idempotency、User別上限、Device活性、参照・Chunk・取消はActor User/Deviceに対して評価する。
  - [ ] 一時PathをServerがActor/Session IDから生成し、Target OwnerやClient指定Pathを信用しない。
  - [ ] 既存の個人Upload Sessionの冪等性、Offset、Checksum、期限、Cleanup、Recoveryを維持する。
- [ ] Session作成と完了の認可をTDDで実装する。
  - [ ] CreateでDestination Folderへ`CONTRIBUTOR`以上を要求する。
  - [ ] CompleteでDestinationの存在・Owner・`CONTRIBUTOR`・同名・Storage状態をLock内で再検証する。
  - [ ] Create後のShare解除またはPermission低下時は、Chunkの一時保存を正式公開と扱わず、Completeを拒否して取消・Cleanup可能な状態にする。
  - [ ] 完了FileEntry/FileOperationのOwnerをTarget Owner、AuditのActorをSession Actorとする。
  - [ ] 通信結果不明のComplete再送で二重FileEntryを作成しない。

### 3.4 Rename・Move・Trashの共有認可

- [ ] Renameを`EDITOR`対応する。
  - [ ] Commandの`OwnerUserId`とActorの混同を解消し、対象OwnerをFileEntryから導出する。
  - [ ] 対象の`EDITOR`以上をLock後に再確認し、Root、非`ACTIVE`、同名、未完了Operationを従来どおり拒否する。
  - [ ] Rename後もFile ID・Owner・Share行・`fileVersion`を維持し、一覧に最新名を返す。
- [ ] Moveを権限境界迂回なしで実装する。
  - [ ] 対象、source parent、target parentをIDで取得し、3対象すべてに`EDITOR`以上を要求する。
  - [ ] 対象・source・targetが同一Owner Treeであることを要求し、異Owner Treeへの移動をHDD変更前に拒否する。
  - [ ] 3対象のMutation Lockを安定順で取得し、Reload後に状態・Owner・Permission・競合を再検証する。
  - [ ] 循環、深度64、同名、同じ親への再送、Recoveryの現行契約を維持する。
  - [ ] 直接Share行は維持し、移動後の新祖先チェーンから次要求で継承Permissionを再解決する。
- [ ] Trashを`EDITOR`対応する。
  - [ ] 対象の`EDITOR`以上をLock内で確認し、対象OwnerのTrash Containerへ移動する。
  - [ ] ActorとOwnerをAudit/FileOperationで混同せず、対象と子孫のShare行を保持する。
  - [ ] Trash完了後は対象を受信Share一覧、通常一覧、詳細、Download、追加変更から除外する。

### 3.5 Restore・Purge・MISSING整合性

- [ ] Trash管理はOwner限定を維持する。
  - [ ] Trash一覧、Restore、手動Purgeは対象Ownerだけが実行でき、Recipientの`EDITOR | MANAGER`に開放しない。
  - [ ] RestoreはShare行を更新・複製せず、復元先の祖先チェーンと保持した直接Shareを次要求で再解決する。
  - [ ] Restoreの同名競合、Storage異常、RecoveryでShare行が壊れず、途中状態を公開しない。
- [ ] Sharingの関連データ削除participantを実装する。
  - [ ] `IPermanentDeleteParticipant`でPurge対象と子孫のShare/MemberをFileEntry削除と同じDB Transaction内で削除する。
  - [ ] `IFileIndexDeletionParticipant`でMISSING索引削除対象と子孫のShare/MemberをHDD操作なしで削除する。
  - [ ] Participantの再送を冪等にし、関連Shareが0件でも成功させる。
  - [ ] Retention Workerの自動Purgeでも同じApplication participantを使用する。
- [ ] Recoveryと並行操作を共有認可と整合させる。
  - [ ] Rename、Move、Trash、Restore、Uploadの未完了対象と子孫をShare一覧・認可から隔離する。
  - [ ] HDD変更後のDB競合は`RECOVERY_REQUIRED`へ正規化し、復旧後に最新階層からPermissionを解決する。
  - [ ] Share解除・Permission変更とFile mutationの競合で、Lock後の古い認可によるHDD変更を起こさない。
  - [ ] Lock順序が安定し、無関係なUser/Treeを不要に直列化しない。

### 3.6 PR3自動Test

- [ ] 作成・UploadのUnit/Integration Testを完了する。
  - [ ] `VIEWER`拒否、`CONTRIBUTOR`作成・Upload、`EDITOR | MANAGER`の上位互換をTestする。
  - [ ] 作成File/FolderのOwnerがDestination Owner、Audit ActorがRecipientであることをTestする。
  - [ ] MultipartとResumableのPermission失効競合、同名、Idempotency、二重完了、RecoveryをTestする。
  - [ ] Session Migration後の既存個人Upload、Chunk、Cleanup、Device失効への回帰がない。
- [ ] Rename・Move・Trash・Restore・PurgeのUnit/Integration Testを完了する。
  - [ ] 各4Permissionの許可・拒否とOwnerをTestする。
  - [ ] Moveの対象/source/target Permissionの全不足パターン、異Owner、循環、深度、同名をTestする。
  - [ ] Rename/Move前後のFile ID、Owner、`fileVersion`、直接Share維持と継承Permission変化をTestする。
  - [ ] Trash中の非公開、Owner Restore後の直接/継承Share復活、Recipient Restore拒否をTestする。
  - [ ] Purge、Retention Purge、MISSING索引削除後に対象・子孫Share/Memberと孤立行が残らない。
  - [ ] Share変更競合、FileOperation中断、API再起動復旧後の認可整合性をTestする。
- [ ] Security・回帰・標準検証を完了する。
  - [ ] 他User ID/File ID推測、Client Owner/Device偽造、物理Path、Symlink、Storage Root外を拒否する。
  - [ ] 個人領域のFolder作成、Multipart/Resumable Upload、Download、Rename、Move、Trash、Restore、Purge、MISSING管理へ回帰がない。
  - [ ] `./scripts/ci/verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-deployment.sh`、`verify-android.sh`が成功する。
  - [ ] `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`、Migration整合、OpenAPI Contract Test、`git diff --check`が成功する。

### 3.7 PR3手動確認・文書・完了

- [ ] API Clientと実FilesystemでPR3の主要フローを確認する。
  - [ ] Contributorが共有FolderへFolder作成、Multipart Upload、中断再開Uploadを行い、OwnerのTreeに所有される。
  - [ ] EditorがRename、同一Owner Tree内Move、Trashを行い、Viewer/Contributorの不足操作は拒否される。
  - [ ] Move前後の直接Share維持、継承経路変化、Trash中の非公開、Owner Restore後の再公開を確認する。
  - [ ] PurgeとMISSING索引削除でShare関連行が消え、対象が共有一覧・認可Queryから消える。
- [ ] PR3の文書と差分をセルフレビューする。
  - [ ] 3.1〜3.6、正式文書、Steering、OpenAPI、Migration、Server実装のActor/Owner・Permission・状態・Error用語が一致する。
  - [ ] Migration順序、Server Rollout、Rollback制約、Share失効中のSession清掃を運用文書へ反映する。
  - [ ] HDD更新前とLock後の認可再確認があり、File ID・Owner・Share・`fileVersion`を予期せず変更しない。
  - [ ] 不要なPackage、生成物、実環境情報、Credentialが差分にない。
- [ ] PR3を完了する。
  - [ ] 3.1〜3.7のPR3対象項目がすべて`[x]`である。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR3完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR4: Android共有UI・権限表示・実機E2E

### 4.1 作業開始

- [ ] PR4の作業準備を完了する。
  - [ ] PR3が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`からPR4用の短命Branchを作成する。
  - [ ] Steering文書、PR1〜PR3完了記録、`git status`、Android core-model/network/data、FileBrowser、Navigation、Compose Testの既存パターンを再確認する。
  - [ ] Raspberry Pi、PostgreSQL、実HDD、Android実機、LAN、ZeroTier、Release署名入力の利用可否を確認する。

### 4.2 Android model・Network・Repository

- [ ] Androidの共有Domain modelとFile model拡張をTDDで実装する。
  - [ ] `SharePermission`、`PermissionSource`、Owner、Candidate、Member、ShareItem、Pageを`core-model`へ追加する。
  - [ ] `FileEntry`にOwner、Permission、PermissionSource、ShareTargetIdを追加する。
  - [ ] 未知Permission/Sourceを`UNKNOWN`へ変換し、破壊的操作とShare管理を有効化しない。
  - [ ] Owner、Viewer、Contributor、Editor、ManagerのUI操作可否行列を共通modelから導出する。
- [ ] `SharingApi`とDTO/Mappingを実装する。
  - [ ] 候補、Create、所有/受信一覧、詳細、Member Put/Delete、Share Deleteを`core-network`へ追加する。
  - [ ] Paging、Filter、Owner、Permission、Source、時刻、ErrorをOpenAPIと一致させる。
  - [ ] 401 Refresh後の再送で同じ意図を維持し、共有変更を意図せず二重送信しない。
  - [ ] 旧Server/不完全Response/未知enumを安全側に扱うContract Testを追加する。
- [ ] `SharingRepository`とPagingを`core-data`へ実装する。
  - [ ] 全Use Caseを`AuthenticatedRequestExecutor`経由で呼び出す。
  - [ ] DTOをDomain modelへ厳密にMappingし、不正UUID・時刻・enumを成功扱いにしない。
  - [ ] 一覧Refreshと次Pageが安定順で重複・取りこぼしなく動作する。
  - [ ] 更新競合・結果不明・解除後はServerの一覧/詳細を再取得し、Clientで成功を推測しない。

### 4.3 `feature-sharing`とNavigation

- [ ] `feature-sharing`モジュールをリポジトリ規約に従って追加する。
  - [ ] Build設定、Dependency、Lockfile、Assembly marker、Unit/Instrumented Test source setを追加する。
  - [ ] 既存Compose Theme・Componentを再利用し、新しい外部Packageを追加しない。
- [ ] 共有一覧画面とViewModelをTDDで実装する。
  - [ ] Folder Shareのルートと個別File Shareを、種別、名前、Owner、自分のPermissionとともに表示する。
  - [ ] Loading、空、Paging、Refresh、認証更新、Storage/API Error、共有失効の状態を表示する。
  - [ ] Folderルートから既存File browserへ`targetEntryId`を渡し、File Shareから詳細またはDownloadへ移動できる。
- [ ] 共有設定・候補・Permission選択画面とViewModelをTDDで実装する。
  - [ ] OwnerまたはManagerがMember追加、Permission変更、Member解除、Share全体解除を実行できる。
  - [ ] FileのPermission選択肢から`CONTRIBUTOR`を除外し、Folderでは配下継承の説明を表示する。
  - [ ] 継承で開いたEntryに共有元Folderと実効Permissionを表示する。
  - [ ] Member解除、Share解除、Manager付与に確認Dialogを表示し、処理中の二重送信を防ぐ。
  - [ ] 共有解除競合やアクセス失効後に安全な画面へ戻り、最新Server状態を表示する。
- [ ] App NavigationとDIを接続する。
  - [ ] `AppDestination`とHome画面へ「共有」導線を追加する。
  - [ ] `ServiceContainer`と`SessionServices`にSharing Repositoryを追加する。
  - [ ] ログアウト・接続経路変更時にSharing ViewModelと前Sessionの状態を使い回さない。

### 4.4 File UIのPermission制御

- [ ] `feature-files`をServerのPermission metadataに対応する。
  - [ ] Owner、Permission、PermissionSource、共有元Folderを一覧・詳細で表示する。
  - [ ] Viewerは一覧・詳細・Downloadだけ、ContributorはそれにFolder作成・Uploadを追加して表示する。
  - [ ] Editor/ManagerはRename・許可されたMove・Trashを表示し、ManagerはShare設定導線を表示する。
  - [ ] Trash一覧・Restore・PurgeとMISSING管理はOwnerだけに表示する。
  - [ ] `UNKNOWN`または欠落したPermissionでDownload以外を有効にせず、Serverの拒否後は一覧を再取得する。
  - [ ] 共有Folder内のUpload・作成・MoveによりFileBrowserのOwner・Parent・戻る導線が個人Rootと混同しない。

### 4.5 Android自動Test

- [ ] Model・Network・Repository Unit/Contract Testを完了する。
  - [ ] 全4Permission、Owner、Direct、Inherited、Unknown、ShareTargetのMappingをTestする。
  - [ ] 全Sharing EndpointのMethod、Path、Query、Header、Body、Response、ErrorをMockWebServer等の既存手段でTestする。
  - [ ] 401 Refresh、通信結果不明、Paging、競合後再取得、二重送信防止をTestする。
  - [ ] FileRepositoryの既存Status・Purge・MISSING・Upload Mappingへ回帰がない。
- [ ] ViewModel・Compose UI Testを完了する。
  - [ ] 共有一覧のLoading、空、成功、Paging、Error、Refreshと項目遷移をTestする。
  - [ ] Share作成・Member更新・解除・全解除の成功、競合、失効、二重送信をTestする。
  - [ ] FileとFolderのPermission選択肢、継承説明、Manager確認DialogをTestする。
  - [ ] 各Permission/SourceでFile操作の表示・非表示とUnknownのFail-closedをTestする。
- [ ] Android標準検証を完了する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] 接続されたAndroid実機で`connectedDebugAndroidTest --max-workers=1`相当が全対象Moduleに成功する。
  - [ ] `./scripts/ci/verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-deployment.sh`、`git diff --check`が成功する。

### 4.6 Raspberry Pi・Android実機E2E

- [ ] 本番相当環境の事前保護とRolloutを完了する。
  - [ ] PostgreSQLとStorage RootのBackup、復元可能性、対象Serviceの開始状態を確認する。
  - [ ] Migration、Server、Worker、署名済みAndroid Releaseの順序を運用文書に従って適用する。
  - [ ] Rollback制約と共有先Upload Sessionの有無を確認し、共有データを無言削除しない。
- [ ] Server共有・認可E2Eを完了する。
  - [ ] Owner、Viewer、Contributor、Editor、Manager、Adminを含む複数の試験Userで共有を作成する。
  - [ ] File直接Share、Folder継承、複数祖先、直接+継承の最強権限と権限元を確認する。
  - [ ] List、Get、Range Download、Folder作成、Multipart/Resumable Upload、Rename、Move、Trashの4Permissionごとの許可・拒否を確認する。
  - [ ] Share解除・Permission変更後の即時失効、Move後の継承変化、Trash中の非公開、Owner Restore後の再評価を確認する。
  - [ ] PurgeとMISSING索引削除後にShare/Memberが残らず、未完了FileOperationが0件へ収束する。
  - [ ] API/Worker/PostgreSQL再起動、Share変更とFile操作の競合後も認可とHDD/DBが収束する。
- [ ] Android実機のユーザーフローE2Eを完了する。
  - [ ] OwnerがFile/FolderのShare作成、候補選択、Permission設定を行う。
  - [ ] Recipientが共有一覧、Folder配下、File詳細、Owner・Permission・Source・共有元を確認する。
  - [ ] ContributorがFolder作成・中断再開Upload、EditorがRename・Move・Trash、ManagerがMember更新を行う。
  - [ ] Viewerの変更操作、FileのContributor選択、RecipientのRestore/Purge、Adminの暗黙アクセスがUIとServerの両方で拒否される。
  - [ ] Member/Share解除後に画面が最新状態へ戻り、共有対象を操作できない。
- [ ] LAN・ZeroTierと回帰を確認する。
  - [ ] LAN直接とZeroTier経由で同じHTTPS Hostname、TLS、認証、共有契約が機能する。
  - [ ] Personal RootのUpload、Download、Rename、Move、Trash、Restore、Purge、MISSING、中断再開Uploadが従来どおり動作する。
  - [ ] 試験中のUser、Share、File、一時データ、資格情報を安全に整理し、実稼働データを削除しない。
  - [ ] Storage ID一致、全Service active、未完了FileOperation/Upload Session・孤立Shareが0件の最終状態を確認する。
  - [ ] E2E手順、結果、付与Permission、失敗注入、所要時間、清掃結果を`docs/testing/`へ記録する。

### 4.7 文書整合・最終セルフレビュー・完了

- [ ] 全文書と実装を最終整合する。
  - [ ] 5つの正式文書、Steering、OpenAPI、Migration、Server、Worker、Android、運用・Test記録の用語、状態、Permission、Errorが一致する。
  - [ ] Android `feature-sharing`の実配置、Navigation、依存方向を`docs/repository-structure.md`へ最終反映する。
  - [ ] Migration、Server/Worker、AndroidのRollout順序、Rollback制約、Shareデータ保護を運用文書へ反映する。
  - [ ] E2E記録が機密情報、実Userの識別情報、物理Path、Tokenを含まない。
- [ ] 全体差分をセルフレビューする。
  - [ ] すべての保護File APIが所有者・直接・継承を再評価し、認可を回避するEndpointや復旧経路がない。
  - [ ] ActorとOwnerがFileEntry、Upload Session、FileOperation、Audit、Android表示で混同されていない。
  - [ ] Permissionの長期Cache、Clientだけの認可、FileのContributor、Deny ACL、Group、公開Linkが入り込んでいない。
  - [ ] QueryにN+1、無制限再帰、物理Path漏えいがなく、認証・認可95%とDomain/Application 80%のカバレッジ目標を満たす。
  - [ ] スコープ外機能、不要なPackage、生成物、実環境情報、Credentialが差分にない。
- [ ] PR4を完了する。
  - [ ] 4.1〜4.7のPR4対象項目がすべて`[x]`である。
  - [ ] `./scripts/ci/build-release.sh`を含む全必須検証、Commit、Push、英語のPull Request作成、CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR4完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## 各Pull Request完了記録

各Pull Request作成後に`steering`スキルのモード3-Aを使用して追記する。対象Pull Request内のタスクがすべて完了するまで記録しない。

### PR1: Sharing domain・Persistence・Authorization基盤

- 完了日: 2026-08-23
- Pull Request: [#19 Add file sharing authorization foundation](https://github.com/ry825/Kura_Storage/pull/19)
- 実施したTest・Build・静的解析:
  - `./scripts/ci/verify-config.sh`: 成功
  - `./scripts/ci/verify-server.sh`: 成功（Domain 49件、Application 119件、Integration 79件）
  - `./scripts/ci/verify-security.sh`: 成功
  - `./scripts/ci/verify-deployment.sh`: 成功
  - `./scripts/ci/verify-android.sh`: 成功
  - `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`: 成功
  - `dotnet ef migrations has-pending-model-changes`: 未反映のModel変更なし
  - `git diff --check`: 成功
  - Line Coverage: `AuthorizationService` 100%、Domain 85.61%、Application 80.33%
  - GitHub Actions: Android、Config、Security、Serverの必須Checkがすべて成功
- 手動確認・実機確認:
  - PR1はAPI・Android UIへ接続しない基盤PRのため、ユーザーフローの手動確認と実機確認はなし。
  - PostgreSQLの`EXPLAIN`で、共有Member検索が追加Indexを使用することを確認した。
- 計画と実装の差分:
  - 承認済みのPR1範囲どおりに実装し、Share API、既存File APIへの認可接続、Android共有UIは後続PRへ維持した。
  - MigrationのDownは、Shareが存在する場合に加え、ActorとTarget Ownerが異なるUpload Sessionが存在する場合も明示的に拒否する安全策とした。
- 実装中に追加したタスクと理由:
  - 非ACTIVE Actorの拒否と、祖先階層のOwner・Folder種別の連続性検証をセルフレビューで追加し、異常データ時もFail-closedとなるようにした。
  - Schema変更に伴い、既存のTrash purge Migration TestのIndex期待値を更新した。
- 技術的に不要になったタスク・理由・代替実装: なし
- 後続Pull Requestへの引継ぎ事項:
  - PR2はPR1のMerge後、最新`main`から開始する。
  - `SharingService`では最後のMember削除時に空のShareも同一Transactionで削除する。
  - File参照系は`AuthorizationService`のBatch認可を使用し、File直接Shareを子へ継承せず、長期Permission Cacheを追加しない。
  - PR3では`TargetOwnerUserId`を使用し、File Lock取得後に認可を再評価する。既存Create系がPersonal限定である点はPR1では意図的に維持している。

### PR2: 共有管理APIと共有Fileの閲覧

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・実機確認: 未実施
- 計画と実装の差分: 未完了
- 実装中に追加したタスクと理由: 未完了
- 技術的に不要になったタスク・理由・代替実装: 未完了
- 後続Pull Requestへの引継ぎ事項: 未完了

### PR3: 共有先の作成・Upload・変更と整合性

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・実機確認: 未実施
- 計画と実装の差分: 未完了
- 実装中に追加したタスクと理由: 未完了
- 技術的に不要になったタスク・理由・代替実装: 未完了
- 後続Pull Requestへの引継ぎ事項: 未完了

### PR4: Android共有UI・権限表示・実機E2E

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・実機確認: 未実施
- 計画と実装の差分: 未完了
- 実装中に追加したタスクと理由: 未完了
- 技術的に不要になったタスク・理由・代替実装: 未完了
- 後続作業への引継ぎ事項: 未完了

---

## 全体振り返り

PR1〜PR4、本ファイルの全タスク、各Pull Request完了記録が完了した後にだけ、`steering`スキルのモード3-Bを使用して記録する。

### 実装完了日

未完了

### 計画と実績の差分

未完了

### 主な設計変更と理由

未完了

### 技術的な学び

未完了

### プロセス上の改善点

未完了

### 次回への改善提案

未完了
