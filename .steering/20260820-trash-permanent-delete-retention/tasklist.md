# ゴミ箱の完全削除・30日保持・自動清掃 タスクリスト

## タスク完全完了の原則

**本ファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止する。**

### 必須ルール

- 全タスクを最終的に`[x]`にする。
- 実装開始時は対象タスクを`[ ]`のままにし、実装と検証が完了した直後に個別に`[x]`へ更新する。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ更新する。
- 「時間の都合」「実装が難しい」「別タスクで対応」などを理由に選択したPull Request単位のタスクを残さない。
- 技術的に不要になったタスクだけを、取消理由と代替実装を明記して完了扱いにできる。
- 実装中に必要性が判明した作業は、開始前に本ファイルの対象Pull Requestへ追加する。
- 各Pull Request作成後は、そのPull Requestの完了記録を本ファイルへ追加し、同じBranchへCommit・Pushして停止する。
- 後続Pull Requestのタスクは先行Pull Request完了時に`[ ]`のままでよい。
- 全体振り返りは、PR1〜PR3と全タスクの完了記録が揃った後にだけ記入する。

### Pull Request順序

1. PR1: Server完全削除・復旧・API契約
2. PR2: 30日Worker・容量管理・運用配置
3. PR3: Android完全削除・容量警告・実機E2E

各PRは前段PRが`main`へMergeされたことを確認してから、最新の`main`を基点に短命Branchを作成する。現在の`feat/file-rename-move-android`とPull Request #10が未Mergeの場合、PR1の実装を開始しない。

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

## PR1: Server完全削除・復旧・API契約

### 1.1 作業開始

- [x] PR1の作業準備が完了している。
  - [x] Pull Request #10を含む先行作業が`main`へMerge済みであることを確認する。
  - [x] 最新の`main`を取得し、PR1用の短命Branchを作成する。
  - [x] `requirements.md`、`design.md`、本ファイルと作業に直接関係する正式文書の節を再確認する。
  - [x] `git status`と既存差分を確認し、ユーザーの変更を保護する。
  - [x] 既存Trash、Restore、Rename、Move、FileOperation Recovery、Audit、OpenAPI、Integration Testの実装パターンを再確認する。
  - [x] 現行DB Schemaを棚卸しし、削除対象の`FileEntry`以外に実装済みのShare、Sync、Recent、Backup Receipt、Derivative等の関連管理情報が存在するか記録する。（棚卸し結果: 2026-08-20時点のDbSetはUser、Device、RefreshSession、AuthenticationAttempt、AuditLog、FileEntry、FileOperationのみで、該当する関連管理情報は未実装）
  - [x] PostgreSQL Testcontainers、一時Storage、Server検証Scriptを実行できることを確認する。

### 1.2 正式文書・API契約

- [x] 手動完全削除と保持期限表示の正式仕様を更新する。
  - [x] `docs/product-requirements.md`の完全削除、関連管理情報、監査、30日保持、容量警告の要求を今回の実装範囲と整合させる。
  - [x] `docs/functional-design.md`へ`DELETE /api/v1/trash/{fileId}`、`Idempotency-Key`、`204`、Error、`purgeEligibleAt`、Purge Journal Flowを反映する。
  - [x] `docs/architecture-design.md`へPurge Aggregate、Operation状態、Lock、Recovery、関連情報削除境界、監査分離を反映する。
  - [x] `docs/repository-structure.md`へ実際に採用するPurge Use Case、Persistence、Test配置を反映する。
  - [x] `docs/development-guidelines.md`へ安全な再帰削除、Storage Delete Intent、完全削除Test規則を反映する。
  - [x] PR1完了時点で未実装のWorker、容量画面、Android受け入れ条件を誤って完了扱いにしない。
- [x] OpenAPIとFixtureを更新する。
  - [x] `DELETE /api/v1/trash/{fileId}`の認証、必須`Idempotency-Key`、`204`、`400`、`401`、`404`、`409`、`503`を定義する。
  - [x] `FileEntry`へnullable `purgeEligibleAt`を追加する。
  - [x] 同一Key再送と異なるTargetへのKey再利用契約をDescriptionへ記載する。
  - [x] 完全削除Request・Response用Fixtureを追加または更新する。
  - [x] 現行Android Clientが追加Response fieldを安全に無視でき、PR1で既存Android契約Testが壊れないことを確認する。

### 1.3 Domain・監査・Migration

- [x] Purge Domain契約を実装する。
  - [x] `FileOperationType.Purge`を追加する。
  - [x] `FileEntryStatus`へ`DELETED`を追加しない。
  - [x] `PurgeFileCommand`、`PurgeTrigger`、成功・失敗Result、`PermanentDeleteTarget` Snapshotを追加する。
  - [x] Target SnapshotにRoot ID、Owner、Entry Type、Trash Container、Descendant ID、Size合計だけを保持し、名前、元Path、内容を不要に保持しない。
  - [x] 共有`TrashPurgeOptions.RetentionDays`を既定30・下限30で追加し、API起動時に検証する。
  - [x] `trashedAt + RetentionDays`から`purgeEligibleAt`をUTCで算出する。
- [x] 監査Actor種別を実装する。
  - [x] `USER_DEVICE`、`SYSTEM_WORKER`、`ADMIN_CLI`、`SYSTEM`をDomainで表現する。
  - [x] 既存Login、Device、Admin CLI、Rename、Move等の監査作成箇所へ正しいActor Typeを設定する。
  - [x] `FILE_PURGE_MANUAL`と`FILE_PURGE_RETENTION`のAction契約を追加する。
  - [x] Target Type、Target ID、結果、Actor、Request ID、時刻だけを記録し、ファイル名、Path、Size、内容、認証情報を記録しない。
- [x] PR1用Migrationを作成する。
  - [x] `audit_logs.actor_type`を追加する。
  - [x] 既存行をActor列から安全にBackfillし、判定不能行を`SYSTEM`にする。
  - [x] `file_entries(status, parent_id, trashed_at, id)`の期限候補Indexを追加する。
  - [x] 1つの`file_entry_id`に複数の未完了Purgeを許さない部分Unique Indexを`file_operations`へ追加する。
  - [x] Audit成功Eventの重複防止に必要なIndexまたは一意性を追加する。
  - [x] `file_operations.file_entry_id`へ`FileEntry`削除を妨げる外部キーを追加しない。
  - [x] Migration Up/DownとModel Snapshotを整合させる。

### 1.4 Storage安全境界

- [x] `StorageGuard`を操作Intent別へ変更する。
  - [x] `Read`、`CreateOrUpdate`、`Delete`を定義する。
  - [x] `Read`でMount、Storage ID、読取可能性を確認する。
  - [x] `CreateOrUpdate`で書込み可否と`MinimumFreeBytes`を確認する。
  - [x] `Delete`でMount、Storage ID、読取専用状態を確認し、`MinimumFreeBytes`不足だけでは拒否しない。
  - [x] 既存一覧、Download、Folder作成、Upload、Trash、Restore、Rename、Moveを正しいIntentへ移行する。
  - [x] Uploadの要求Size確認は`HasCapacityAsync`で維持する。
- [x] 安全なFile・Folder再帰削除を実装する。
  - [x] `IFileStore.DeleteTreeIfExistsAsync`を追加する。
  - [x] `RelativeStoragePath`から検証済みStorage Root配下へだけ解決する。
  - [x] Storage Root、User Root、`files`、`trash`等の管理Rootを削除対象にできないDefense in Depthを実装する。
  - [x] File、空Folder、配下を持つFolderを深い順に削除する。
  - [x] 存在しないFile・Folderを冪等成功として扱う。
  - [x] 対象、Ancestor、列挙したDescendantのSymbolic Linkを拒否し、Linkを辿らない。
  - [x] Cancellation、Unauthorized、I/O Errorを握り潰さず上位へ返す。
  - [x] 物理絶対PathをLog、Audit、Exception Responseへ出力しない。

### 1.5 関連管理情報削除境界

- [x] `IPermanentDeleteParticipant`を実装する。
  - [x] 関連物理Artifact列挙とDB管理情報削除を別Phaseとして定義する。
  - [x] Participantが検証済み相対Pathだけを返す契約にする。
  - [x] DB削除Phaseを`FileEntry`削除と同じTransactionで実行する。
  - [x] 実装済み関連機能が存在する場合、各機能のParticipantまたは検証済みCascadeを登録する。
  - [x] 未実装のShare、Sync、Recent、Backup、Derivative用の空Tableや空Featureを追加しない。
  - [x] 将来の関連Table追加時にParticipantまたはCascade Testを必須化するArchitecture Testまたは開発規則を追加する。
- [x] File catalogの削除順序を実装する。
  - [x] Trash Root FileではRoot `FileEntry`を削除する。
  - [x] Trash Root FolderではPrefix配下のDescendantを深い順に削除してからRootを削除する。
  - [x] `FileOperation`と`AuditLog`を削除対象から除外する。
  - [x] Folder配下Descendantを個別API対象や個別Worker候補として扱わない。

### 1.6 TrashPurgeService・並行制御

- [x] 手動Purge Application Serviceを実装する。
  - [x] Command、User、Device、Target、Idempotency Key、Request IDを検証する。
  - [x] 同一User・同一Key・同一Targetの完了済み再送を冪等成功にする。
  - [x] 同一User・同一Keyを異なるTargetで使用した場合に`IDEMPOTENCY_CONFLICT`を返す。
  - [x] User所有かつ`TRASHED`・`parent_id IS NULL`だけをPurge対象にする。
  - [x] `ACTIVE`、User Root、他User、存在しない対象を`FILE_NOT_FOUND`へ統一する。
  - [x] 手動Purgeは30日前でも許可する。
  - [x] Target IDのadvisory lockを取得し、Lock内で状態と所有者を再読込する。
  - [x] Targetまたは配下の未完了操作を検出してPurgeを開始しない。
  - [x] Root、Descendant、Size、関連ArtifactのSnapshotを確定する。
  - [x] `FileOperation(PURGE, PENDING)`を物理削除前に保存する。
  - [x] 関連Artifactと`trash/<root-id>` Containerを冪等削除する。
  - [x] 物理削除完了後だけ`FILESYSTEM_DONE`へ進める。
  - [x] 関連管理情報、Descendant、Root、成功Audit、Operation完了を1つのDB Transactionで確定する。
  - [x] 監査保存失敗時に関連情報と`FileEntry`削除をRollbackする。
  - [x] 拒否と`RECOVERY_REQUIRED`を結果Code付きで監査し、監査失敗で所有・Path・Storage検証を迂回しない。
  - [x] 容量警告閾値以下でも手動Purgeを実行できる。
- [x] Purge中の隔離と既存操作との競合制御を実装する。
  - [x] 未完了Purge Rootと配下をTrash一覧と件数から除外する。
  - [x] 未完了または`RECOVERY_REQUIRED`のPurge対象をRestoreできない。
  - [x] Restore、手動Purge、将来Worker Purgeが同じTarget lockへ整合する。
  - [x] 二重Requestが二重削除、重複監査、Operation競合を起こさない。
  - [x] 無関係なUserまたはTargetの操作を不要に直列化しない。

### 1.7 Purge Recovery

- [x] 既存`FileOperationRecoveryService`へPurge復旧を追加する。
  - [x] `PENDING`かつContainer存在時に安全な再帰削除を再実行する。
  - [x] `PENDING`かつContainer不在時に`FILESYSTEM_DONE`へ進める。
  - [x] `FILESYSTEM_DONE`かつContainer不在・`FileEntry`存在時にDB確定Phaseを再実行する。
  - [x] `COMPLETED`かつContainer・`FileEntry`不在を冪等完了として扱う。
  - [x] 再試行可能な削除途中I/O Errorは`PENDING`を保持し、次回復旧対象にする。
  - [x] Symbolic Link、物理・DB状態矛盾、Target Snapshot不正を`RECOVERY_REQUIRED`にする。
  - [x] `RECOVERY_REQUIRED`への移行をTarget IDと必要最小限の情報だけで監査する。
  - [x] Storage未利用時に物理状態を推測せず次回へ延期する。
  - [x] RecoveryでもTarget lockと同じDB確定Methodを使用する。
  - [x] Recovery成功監査を重複作成しない。
- [x] 既存Recoveryとの回帰を防ぐ。
  - [x] Upload、CreateFolder、Trash、Restore、Rename、Moveの既存状態表を維持する。
  - [x] Purge完了後の`FileOperation`読込が`FileEntry`不在でも失敗しない。
  - [x] Rollback前に未完了`PURGE`を検出できる運用QueryまたはCLI手順を用意する。

### 1.8 API・認証・認可基盤

- [x] 手動完全削除Endpointを実装する。
  - [x] `DELETE /api/v1/trash/{fileId}`を追加する。
  - [x] `sub`、`device_id`、Request IDを認証Contextから取得する。
  - [x] 必須`Idempotency-Key`を検証してApplicationへ渡す。
  - [x] 成功と同一Key再送へ`204`を返す。
  - [x] `400`、`401`、`404`、`409`、`503`をOpenAPIどおり共通Error Responseへ変換する。
  - [x] Infrastructure例外、Path、所有情報をResponseへ漏らさない。
- [x] Admin認可のServer基盤を実装する。
  - [x] Access TokenへUser Role Claimを追加する。
  - [x] Login、Device登録、RefreshのToken発行でDB上のRoleを使用する。
  - [x] 既存User・Device・Session検証をRole Claim追加後も維持する。
  - [x] `AdminOnly` Authorization Policyを追加する。
  - [x] Member TokenがAdmin Policyを通過できないTest用EndpointまたはPolicy Testを追加する。
  - [x] PR1では未完成のAdmin Storage APIを公開しない。

### 1.9 PR1自動Test

- [x] Domain・Application Testが完了している。
  - [x] File・Folder Target Snapshot、Descendant深度順、Size合計をTestする。
  - [x] 手動30日前Purge、対象状態、所有者、Root条件、IdempotencyをTestする。
  - [x] Participant呼出順序、Transaction、Audit RollbackをTestする。
  - [x] Storage Intent別判定と容量警告中のDelete許可をTestする。
  - [x] 監査Payloadへ名前、Path、Size、内容、Secretが入らないことをTestする。
- [x] Infrastructure・Integration Testが完了している。
  - [x] Migration Up/Down、Backfill、Index、部分Unique制約をPostgreSQLでTestする。
  - [x] File、空Folder、配下Folder、部分削除済み再試行を実FilesystemでTestする。
  - [x] Path Traversal、絶対Path、管理Root、Symbolic Linkを拒否する。
  - [x] Root・Descendant削除後もOperationとAuditだけが独立して残る。
  - [x] 物理削除後DB失敗、監査保存失敗、Process停止相当から復旧する。
  - [x] Purge、Restore、二重Purge、配下操作の並行実行を直列化する。
  - [x] Purge中項目を一覧・Restoreから隔離する。
  - [x] 他User、`ACTIVE`、Root、失効Device、Storage異常を拒否する。
  - [x] 同一Key再送が成功し、異なるTargetへの再利用を拒否する。
  - [x] OpenAPI、Fixture、API Response、`purgeEligibleAt`が一致する。
  - [x] 一覧、詳細、Folder作成、Upload、Range Download、Trash、Restore、Rename、Moveへ回帰がない。
- [x] PR1の標準検証が成功している。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [x] `git diff --check`が成功する。

### 1.10 PR1手動確認・セルフレビュー

- [x] API Clientと実FilesystemでPR1機能を確認する。
  - [x] Trash Fileを手動完全削除し、204、HDD、DB、Audit、Operationを照合する。
  - [x] 配下を持つTrash Folderを手動完全削除し、配下残存が0件である。
  - [x] 同一Key再送、異なるTargetへのKey再利用、Restore競合を確認する。
  - [x] 容量安全余裕未満を再現し、期限前自動削除なしのまま手動Purgeが容量解放に使用できる。
  - [x] 物理削除後にDB確定を失敗させ、Recoveryで完了する。
  - [x] Storage Root外、管理Root、Symbolic Linkが削除されない。
- [x] PR1差分をセルフレビューする。
  - [x] `requirements.md`と`design.md`のPR1範囲に対応する実装・Testがある。
  - [x] 現行Schemaの関連管理情報棚卸し結果とParticipant登録が一致する。
  - [x] 未実装Feature用の空Table、空Module、不要な依存Packageを追加していない。
  - [x] 物理絶対Path、ファイル名、内容、CredentialがLog、Audit、Fixture、Commitへ含まれない。
  - [x] Migrationと旧DBデータの互換性、Rollback前提が文書化されている。

### 1.11 Pull Request完了

- [x] PR1が完了している。
  - [x] 1.1〜1.10がすべて`[x]`である。
  - [x] 共通Pull Request完了手順をすべて実施する。
  - [x] PR1完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [x] 完了記録CommitがPR1へ反映されている。
  - [x] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR2: 30日Worker・容量管理・運用配置

### 2.1 作業開始

- [ ] PR2の作業準備が完了している。
  - [ ] PR1が`main`へMerge済みであることを確認する。
  - [ ] 最新の`main`を取得し、PR2用の短命Branchを作成する。
  - [ ] `requirements.md`、`design.md`、本ファイル、PR1完了記録を確認する。
  - [ ] `git status`と既存差分を確認する。
  - [ ] PR1のPurge Service、Recovery、Migration、設定、DI、Test Helperを再確認する。
  - [ ] 既存systemd、Install、Upgrade、Rollback、Backup、Config検証Scriptを再確認する。
  - [ ] Raspberry Pi、PostgreSQL、共有exFAT HDDをWorker検証に使用できることを確認する。

### 2.2 TrashPurgeRun・Persistence

- [ ] Trash Purge Run Domainを実装する。
  - [ ] `RUNNING`、`COMPLETED`、`COMPLETED_WITH_ERRORS`、`FAILED`を定義する。
  - [ ] Started、Completed、Examined、Deleted、Released Bytes、Error Countを管理する。
  - [ ] 完了状態からの不正な再遷移を拒否する。
  - [ ] Process停止で残った`RUNNING` Runを次回起動時に失敗終了へ確定できる。
- [ ] `trash_purge_runs` PersistenceとMigrationを実装する。
  - [ ] Table、Column、Check Constraint、最新Run取得Indexを追加する。
  - [ ] Run開始、Batch進捗、完了、失敗を保存するRepositoryを追加する。
  - [ ] 最新RunをAdmin状態用にNo Trackingで取得する。
  - [ ] Migration Up/DownとModel Snapshotを整合させる。

### 2.3 保持設定・候補Query

- [ ] PR1の`TrashPurgeOptions`をWorker設定へ拡張する。
  - [ ] `RetentionDays=30`と30未満の起動拒否をAPI・Workerで共通利用する。
  - [ ] `IntervalHours=24`、範囲1〜168を検証する。
  - [ ] `BatchSize=100`、範囲1〜500を検証する。
  - [ ] `RetryDelayMinutes=15`、範囲1〜1440を検証する。
  - [ ] API、Worker、`purgeEligibleAt`が同じ設定Sourceを使用する。
- [ ] 期限候補Queryを実装する。
  - [ ] `TRASHED`、`parent_id IS NULL`、`trashed_at <= now - retention`だけを取得する。
  - [ ] 30日未満、`trashedAt`なし、Descendant、Purge中、Recovery隔離対象を除外する。
  - [ ] `trashedAt`昇順、ID昇順で安定して取得する。
  - [ ] Batch Sizeを超えてMaterializeしない。
  - [ ] 候補取得後にPurge ServiceのLock内で期限と状態を再検証する。
  - [ ] Worker用Idempotency KeyをRoot IDとTrash世代から決定的に生成し、128文字以内にする。

### 2.4 KuraStorage.Worker・自動清掃

- [ ] `server/src/KuraStorage.Worker`を追加する。
  - [ ] Generic Host、既存Application/Infrastructure、保護設定を使用する最小Projectを作成する。
  - [ ] HTTP Portや一般公開Endpointを持たせない。
  - [ ] Solution、Package Lock、Build、PublishへProjectを追加する。
  - [ ] APIと同じDB・Storage設定を使用し、SecretをRepositoryへ追加しない。
- [ ] `TrashPurgeWorker`を実装する。
  - [ ] Process起動後に1回実行する。
  - [ ] 以後`IntervalHours`ごとに実行する。
  - [ ] Run開始前に停止した`RUNNING` Runと未完了Purge Recoveryを処理する。
  - [ ] 候補をBatch単位でPurge Serviceへ渡す。
  - [ ] 自動Purgeでは保持期限到達を必須にする。
  - [ ] 1項目の再試行可能な失敗を記録し、安全な後続項目を継続する。
  - [ ] DB接続不能、Storage未Mount、読取専用等の基盤障害でRunを`FAILED`にする。
  - [ ] Cancellationを各Batchと項目境界で尊重する。
  - [ ] 複数Workerと手動Purgeの重複を部分Unique Index、Target lock、再検証で防ぐ。
  - [ ] Runごとに`TRASH_PURGE_RUN`監査を必要最小限で記録する。
  - [ ] 30日未満を容量不足や警告により選択する分岐を実装しない。

### 2.5 Storage容量・Admin状態

- [ ] Storage容量取得を実装する。
  - [ ] `IFileStore.GetCapacityAsync`で検証済みStorage RootのTotal・Available Byteを取得する。
  - [ ] `StorageOptions.CapacityWarningFreeBytes`を既定10 GiBで追加する。
  - [ ] Warning閾値を正数かつ`MinimumFreeBytes`以上に制約する。
  - [ ] `availableBytes <= threshold`をWarningとする。
  - [ ] Storage未利用時に物理Path、Storage ID、不正な容量値を返さない。
- [ ] Admin Storage集計を実装する。
  - [ ] 全`TRASHED` FileのSize合計をTrash概算容量としてDB集計する。
  - [ ] FolderとDescendantを二重計上しない。
  - [ ] 期限超過Trash Root件数を集計する。
  - [ ] 最新Purge Runと`RECOVERY_REQUIRED`件数を取得する。
  - [ ] Admin Storage取得を理由にPurgeを起動しない。
- [ ] `GET /api/v1/admin/storage`を実装する。
  - [ ] `AdminOnly` Policyを適用する。
  - [ ] Storage状態、Total、Available、Warning閾値・判定、Trash概算、期限超過件数、Retention、最新Runを返す。
  - [ ] `RECOVERY_REQUIRED`のPurge件数をAdminだけへ返す。
  - [ ] 未認証へ401、Memberへ403を返す。
  - [ ] 匿名`GET /api/v1/system/health`へ容量、Worker、DB、Path、Storage IDを追加しない。
  - [ ] OpenAPI、Fixture、Server Contract Testを更新する。

### 2.6 配置・設定・運用文書

- [ ] WorkerのRaspberry Pi配置を実装する。
  - [ ] `kurastorage-worker.service`を追加する。
  - [ ] APIと同じ非Root User・共有Storage Groupで動作させる。
  - [ ] Workerへ不要なNetwork Listen、Root権限、Home書込み権限を与えない。
  - [ ] APIとWorkerの起動順序、再起動、停止、障害分離を定義する。
  - [ ] Install、Upgrade、Rollback、Verify ScriptへWorkerを追加する。
  - [ ] Server Release ArtifactへWorker publish成果物を再現可能に含める。
- [ ] 設定契約を更新する。
  - [ ] `appsettings.example.json`とdeployment ConfigへRetention、Interval、Batch、Retry、Warning閾値を追加する。
  - [ ] CI Config検証で既定値、範囲、Production必須値を検証する。
  - [ ] 実環境値やSecretをGit管理対象へ追加しない。
  - [ ] 30日未満の保持設定を配置Scriptでも拒否する。
- [ ] 運用手順を更新する。
  - [ ] `docs/operations/`へWorker状態、最新Run、失敗、Recovery、再実行、Log確認を記載する。
  - [ ] 容量Warning時の対応を、不要な手動Purge、Storage増設、障害確認の順で記載する。
  - [ ] Warningが30日保持を短縮しないことを明記する。
  - [ ] `trashBytes`と`releasedBytes`がDB Size Snapshotによる概算であることを明記する。
  - [ ] 配置前Backup、Migration順序、Worker停止、未完了PURGE確認を含むRollback手順を記載する。

### 2.7 PR2自動Test

- [ ] Run・候補・Worker Testが完了している。
  - [ ] 30日未満、ちょうど30日、30日超過、UTC境界をTestする。
  - [ ] 起動時実行、24時間周期、Cancellation、Batch継続をTestする。
  - [ ] 古い順・ID順、Batch Size、次Batch継続をTestする。
  - [ ] 1件失敗、基盤失敗、停止Run回収、次回再試行をTestする。
  - [ ] 手動Purge、Restore、複数Workerの競合で二重削除しない。
  - [ ] 容量Warning中も30日未満を削除しない。
- [ ] Admin Storage API Testが完了している。
  - [ ] Adminの正常Responseと全集計値をTestする。
  - [ ] Member 403、未認証401、失効Device・Session拒否をTestする。
  - [ ] Storage未利用時の非公開項目とErrorをTestする。
  - [ ] 匿名Health Responseが変更されていないことをTestする。
  - [ ] Warning閾値境界と不正設定起動拒否をTestする。
- [ ] Migration・Deployment Testが完了している。
  - [ ] `trash_purge_runs` Migration Up/DownをPostgreSQLでTestする。
  - [ ] APIとWorkerが同じMigration済みDBへ接続できる。
  - [ ] Worker停止中もAPIが利用でき、API停止中のWorkerが安全に失敗する。
  - [ ] Install、Upgrade、Rollback、Verify Scriptの静的検証が成功する。
  - [ ] Server/Worker Artifactの構成とChecksumを確認する。
- [ ] PR2の標準検証が成功している。
  - [ ] `./scripts/ci/verify-config.sh`が成功する。
  - [ ] `./scripts/ci/verify-server.sh`が成功する。
  - [ ] `./scripts/ci/verify-security.sh`が成功する。
  - [ ] `./scripts/ci/verify-deployment.sh`が成功する。
  - [ ] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [ ] `git diff --check`が成功する。

### 2.8 Raspberry Pi Server・Worker確認

- [ ] 実環境相当で30日Workerを確認する。
  - [ ] 配置前にPostgreSQLとStorage RootのBackupを取得する。
  - [ ] Migrationを適用し、APIとWorkerを既存手順で配置する。
  - [ ] `deployment/raspberry-pi/verify.sh`でAPI、Worker、Nginx、PostgreSQL、HDD、Storage IDを確認する。
  - [ ] Test用ClockまたはTest専用データで30日未満、ちょうど30日、超過を再現する。本番設定を30日未満へ変更しない。
  - [ ] 起動時Runと定期Runで期限超過File・Folderを削除する。
  - [ ] 30日未満のFile・Folderが残る。
  - [ ] HDD未Mount、読取専用、DB停止、Worker停止・再起動から安全に復旧する。
  - [ ] 同時手動Purge、Restore、Workerで二重削除・不整合がない。
  - [ ] Warning閾値以下でも期限到達Purgeと手動Purgeが動作し、期限前自動Purgeが0件である。
  - [ ] Audit、Operation、Run、HDD、DBを照合し、残存・欠落・重複が0件である。
- [ ] Admin Storage APIを実環境相当で確認する。
  - [ ] Admin Tokenで容量、Trash概算、期限超過件数、最新Runを取得する。
  - [ ] Member Tokenで403となる。
  - [ ] 匿名Healthから詳細容量とWorker状態を取得できない。
  - [ ] Application LogとAudit Logに秘密情報、ファイル名、相対・絶対Pathがない。

### 2.9 文書整合・セルフレビュー

- [ ] PR2実装と文書を整合する。
  - [ ] `requirements.md`のWorker、30日保持、容量、監査の条件に対応する実装・検証がある。
  - [ ] `design.md`と実装差分がある場合、理由と確定設計を反映する。
  - [ ] 5つの正式文書、OpenAPI、Config、Migration、API、Worker、運用手順の名称と既定値が一致する。
  - [ ] Repository Structureへ実際のWorker配置を反映する。
  - [ ] PR3で行うAndroid表示を誤って完了扱いにしない。
- [ ] PR2差分をセルフレビューする。
  - [ ] Workerが業務ロジック、物理Path、HTTP Endpointを直接持っていない。
  - [ ] 期限QueryがIndexを利用し、HDD全走査や全件Materializeを行わない。
  - [ ] Config、Unit、systemdの最小権限とSecret境界を確認する。
  - [ ] 未完了PURGEを残したまま旧ServerへRollbackしない手順になっている。
  - [ ] 不要なPackage、Module、生成物、実環境情報が差分にない。

### 2.10 Pull Request完了

- [ ] PR2が完了している。
  - [ ] 2.1〜2.9がすべて`[x]`である。
  - [ ] 共通Pull Request完了手順をすべて実施する。
  - [ ] PR2完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [ ] 完了記録CommitがPR2へ反映されている。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR3: Android完全削除・容量警告・実機E2E

### 3.1 作業開始

- [ ] PR3の作業準備が完了している。
  - [ ] PR2が`main`へMerge済みであることを確認する。
  - [ ] 最新の`main`を取得し、PR3用の短命Branchを作成する。
  - [ ] `requirements.md`、`design.md`、本ファイル、PR1・PR2完了記録を確認する。
  - [ ] `git status`と既存差分を確認する。
  - [ ] AndroidのCredential、Token Refresh、File API、Repository、ViewModel、Compose UI、Testの既存パターンを再確認する。
  - [ ] Raspberry Pi、Worker、PostgreSQL、共有exFAT HDD、Android実機、LAN、ZeroTier、Release署名入力を利用できることを確認する。

### 3.2 Android認証・Network契約

- [ ] Role契約をAndroidへ実装する。
  - [ ] Token ResponseへRoleを追加し、Server OpenAPIとFixtureを更新する。
  - [ ] `UserRole.ADMIN`と`MEMBER`をCore Modelへ追加する。
  - [ ] RoleをKeystore保護済みCredentialへ保存・読出する。
  - [ ] Register、Login、RefreshでRoleを欠落・改変させない。
  - [ ] LogoutとCredential破棄でRoleも削除する。
- [ ] Purge Network契約を実装する。
  - [ ] `DELETE /api/v1/trash/{fileId}`を`KuraStorageApi`へ追加する。
  - [ ] UUID `Idempotency-Key`をHeaderへ設定する。
  - [ ] `204`を成功として処理する。
  - [ ] 401 Refresh後の再送でも同じIdempotency Keyを維持する。
  - [ ] 404、409 `IDEMPOTENCY_CONFLICT`、409 `RECOVERY_REQUIRED`、503、通信結果不明を既存Error分類へ追加する。
  - [ ] `FileEntryDto`へ`purgeEligibleAt`を追加する。
- [ ] Admin Storage Network契約を実装する。
  - [ ] `GET /api/v1/admin/storage`とResponse DTOを追加する。
  - [ ] Long Byte値、nullable最新Run、Storage unavailableを欠落なく変換する。
  - [ ] Admin Roleの場合だけRepositoryがEndpointを呼ぶ。
  - [ ] Member 403を通常利用の認証失敗として誤処理しない。

### 3.3 Android Data・Idempotency

- [ ] File Repositoryへ完全削除を追加する。
  - [ ] `purge(fileId, idempotencyKey)`を追加する。
  - [ ] 成功Responseから削除済み項目をローカル合成しない。
  - [ ] 通信結果不明時に成功を推測しない。
  - [ ] 同一操作の再試行で同じKeyを使用できるAPIにする。
  - [ ] `purgeEligibleAt`をUTC `Instant`へ変換する。
- [ ] Admin Storage Repositoryを実装する。
  - [ ] `AdminStorageStatus`と`TrashPurgeRunSummary`へDTOを変換する。
  - [ ] Byte値と表示用単位変換を分離する。
  - [ ] Adminだけが取得でき、MemberではNetwork Requestを生成しない。
  - [ ] 取得失敗をFile一覧Repositoryの失敗と分離する。

### 3.4 Trash ViewModel

- [ ] 完全削除状態を実装する。
  - [ ] Target、確認表示、Idempotency Key、Submitting、Result Unknown、Errorを管理する。
  - [ ] 確認開始時に操作Keyを1回だけ生成する。
  - [ ] Cancel後に未送信Keyを破棄する。
  - [ ] 送信中の二重Tapと同じTargetのRestoreを防ぐ。
  - [ ] 204後にTrash一覧をServerから再取得する。
  - [ ] 通信結果不明時は対象を一覧から消さず、再取得と同一Key再試行を提供する。
  - [ ] 再取得で対象不在なら削除完了状態として表示を確定する。
  - [ ] 404、Idempotency Conflict、Recovery Required、Storage異常を区別する。
  - [ ] 画面再生成で送信済み操作Keyと結果不明状態を必要範囲で保持する。
- [ ] 保持期限表示を実装する。
  - [ ] Serverの`purgeEligibleAt`を使用し、Androidで30日を再計算しない。
  - [ ] 期限前、期限到達、時刻不明を区別する。
  - [ ] Device Local Timezoneでは表示変換だけを行い、削除判定に使用しない。

### 3.5 完全削除Compose UI

- [ ] Trash項目の危険操作UIを実装する。
  - [ ] Trash項目の詳細から`Restore`と`Delete permanently`へ到達できる。
  - [ ] 完全削除を通常操作から視覚的に分離する。
  - [ ] 確認Dialogへ対象名と「この操作は取り消せません」を表示する。
  - [ ] 確認前にRequestを送信しない。
  - [ ] FileとFolderで同じ安全確認を行い、Folderは配下も削除されることを示す。
  - [ ] Submitting中は操作を無効化し、Progressを表示する。
  - [ ] 成功後は再取得済みTrash一覧を表示する。
  - [ ] 結果不明、404、Conflict、Recovery、Storage異常に適切な案内と再試行を表示する。
  - [ ] 保持期限を項目詳細へ表示する。
  - [ ] 一括削除、「ゴミ箱を空にする」、30日未満の自動削除操作を追加しない。

### 3.6 Admin容量警告UI

- [ ] Admin Storage状態をViewModelへ実装する。
  - [ ] Admin Login時にFiles/Homeで状態を取得する。
  - [ ] Refresh操作でFile一覧と独立して再取得する。
  - [ ] Capacity Warning、Storage unavailable、取得失敗、正常を区別する。
  - [ ] Memberでは状態取得Actionを実行しない。
  - [ ] API失敗でFile一覧、Trash、Transferを利用不能にしない。
- [ ] 容量Warning Panelを実装する。
  - [ ] `capacityWarning == true`のAdminにだけ表示する。
  - [ ] 空き容量、Warning閾値、Trash概算容量、期限超過件数、最新清掃結果を表示する。
  - [ ] 人間向け単位を表示し、丸めでWarning判定を変えない。
  - [ ] Trash画面への導線を提供する。
  - [ ] 30日保持を短縮しないことと、手動PurgeまたはStorage増設の案内を表示する。
  - [ ] Member、未認証、匿名Health由来の詳細を表示しない。

### 3.7 Android自動Test

- [ ] Network・Repository Testが完了している。
  - [ ] Purge Path、Idempotency Header、204、Error ResponseをTestする。
  - [ ] 401 Refresh後再送と同一Key維持をTestする。
  - [ ] 通信結果不明時に成功を合成しないことをTestする。
  - [ ] `purgeEligibleAt`、Role、Admin Storage、最新RunのDTO変換をTestする。
  - [ ] AdminだけがStorage Endpointを呼び、Memberが呼ばないことをTestする。
  - [ ] OpenAPI FixtureとDTO・Endpointの一致をTestする。
- [ ] ViewModel Testが完了している。
  - [ ] 確認、Cancel、送信、二重Tap、成功後再取得をTestする。
  - [ ] 結果不明、同一Key再試行、再取得による完了確定をTestする。
  - [ ] 404、Idempotency Conflict、Recovery Required、Storage異常をTestする。
  - [ ] 保持期限のUTC境界と表示状態をTestする。
  - [ ] Admin Warning、正常、Storage unavailable、取得失敗、Member非取得をTestする。
- [ ] Compose UI Testが完了している。
  - [ ] File・Folderの完全削除入口、不可逆確認、取消、Loading、成功、ErrorをTestする。
  - [ ] Restoreと完全削除が誤操作しにくく分離されていることをTestする。
  - [ ] 保持期限表示をTestする。
  - [ ] Admin Warning Panel、Trash導線、Member非表示をTestする。
  - [ ] 一覧、詳細、Folder作成、Transfer、Trash、Restore、Rename、Move UIに回帰がない。
- [ ] Android Instrumented Testが完了している。
  - [ ] FakeまたはTest ServerでPurge正常系、結果不明、主要Errorを確認する。
  - [ ] Admin/Memberの容量表示境界を確認する。
  - [ ] `connectedDebugAndroidTest --max-workers=1`が成功する。

### 3.8 Artifact・Raspberry Pi・Android配置

- [ ] 最終Server、Worker、Android Artifactを生成する。
  - [ ] PR2 Merge済みServer・Workerを既存Release手順でPublishする。
  - [ ] Server・Worker Artifactの構成とChecksumを検証する。
  - [ ] 確定済みRoot CA、API設定、Repository外Signing KeyでRelease APKを生成する。
  - [ ] APK署名、Package ID、Version、Debuggable無効、Checksumを検証する。
  - [ ] Secret、Private Key、実環境Credential、生成済み保護設定をRepositoryへ追加しない。
- [ ] Raspberry Piへ安全に配置する。
  - [ ] 配置前にPostgreSQLとStorage RootのBackupを取得する。
  - [ ] Install・Upgrade手順でServer・Workerを配置する。
  - [ ] Migration Versionと既存Trash項目を確認する。
  - [ ] API、Worker、Nginx、PostgreSQL、HDD、Storage ID、最新Purge Runを確認する。
  - [ ] Rollback可能な直前Artifactと設定を保持する。

### 3.9 実機E2E

- [ ] Android実機で手動完全削除を確認する。
  - [ ] LANでTrash Fileを完全削除する。
  - [ ] LANで配下を持つTrash Folderを完全削除する。
  - [ ] ZeroTierでTrash File・Folderを完全削除する。
  - [ ] 確認取消では対象が残る。
  - [ ] 成功後の一覧、詳細、Download、Restoreから対象へ到達できない。
  - [ ] 通信中断・結果不明時に成功表示せず、再取得または同一Key再試行で確定する。
  - [ ] 二重Tap、Restore競合、他User、`ACTIVE`、Storage異常を安全に処理する。
- [ ] Android実機で30日保持と容量警告を確認する。
  - [ ] 30日未満、ちょうど30日、超過項目の保持・自動削除境界を確認する。
  - [ ] AdminでCapacity Warning Panel、Trash概算、期限超過件数、最新Runを確認する。
  - [ ] Memberで詳細容量が表示・取得されない。
  - [ ] Warning中も30日未満の自動削除が0件である。
  - [ ] Warning中の手動Purgeと期限到達Purgeで容量を解放できる。
- [ ] 障害・再起動・回帰を確認する。
  - [ ] HDD未Mount、読取専用、DB停止、API停止、Worker停止、Pi再起動から安全に復旧する。
  - [ ] 物理削除後DB失敗を再現し、Recoveryで管理情報削除と監査を完了する。
  - [ ] File・Folder Purgeを含む主要シナリオをLANで10回連続成功させる。
  - [ ] File・Folder Purgeを含む主要シナリオをZeroTierで10回連続成功させる。
  - [ ] Folder作成、Upload、Range Download、Trash、Restore、Rename、Moveを実機で再確認する。
  - [ ] HDD、DB、Operation、Audit、Runの不整合、残存、重複、他User削除が0件である。
  - [ ] Android、API、Worker LogとAuditにPassword、Token、Key、ファイル名、内容、相対・絶対Pathがない。

### 3.10 最終文書整合・品質確認

- [ ] 要求・設計・実装・検証を最終整合する。
  - [ ] `requirements.md`の全受け入れ条件に対応する実装または検証記録がある。
  - [ ] `design.md`と実装差分がある場合、理由と確定設計を反映する。
  - [ ] 5つの正式文書、OpenAPI、Server、Worker、Android、Migration、Config、運用手順の名称・数値・Errorが一致する。
  - [ ] `docs/product-requirements.md`の今回対象チェック項目を、実装・自動Test・実機確認が完了したものだけ更新する。
  - [ ] 完全削除、30日保持、容量警告、監査、関連情報削除、復旧の運用手順を最終化する。
  - [ ] 未実装の共有、同期、Recent、Backup、Derivative Featureを実装済みとして記載しない。
- [ ] 全自動検証が成功している。
  - [ ] `./scripts/ci/verify-config.sh`が成功する。
  - [ ] `./scripts/ci/verify-server.sh`が成功する。
  - [ ] `./scripts/ci/verify-security.sh`が成功する。
  - [ ] `./scripts/ci/verify-deployment.sh`が成功する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] `./apps/android/gradlew -p apps/android connectedDebugAndroidTest --max-workers=1`が成功する。
  - [ ] `git diff --check`が成功する。
- [ ] CIと成果物確認が完了している。
  - [ ] GitHub ActionsのConfig、Server、Security、Android必須Jobが最終HEADで成功する。
  - [ ] Server・Worker Artifact、Release APK、Checksumを再現可能な手順で生成できる。
  - [ ] 新規依存Packageの必要性、Lock File、既知脆弱性を確認する。
  - [ ] Secret、Private Key、物理絶対Path、実環境Credential、不要な生成物が差分にない。

### 3.11 Pull Request完了

- [ ] PR3が完了している。
  - [ ] 3.1〜3.10がすべて`[x]`である。
  - [ ] 共通Pull Request完了手順をすべて実施する。
  - [ ] PR3完了記録を本ファイルへ追加し、同じBranchへCommit・Pushする。
  - [ ] 完了記録CommitがPR3へ反映されている。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## 各Pull Request完了記録

> Pull Request作成後に、対象Pull Requestの記録だけを追記する。後続PRが未完了でも、完了したPRの記録はその時点で行う。

### PR1: Server完全削除・復旧・API契約

- 完了日: 2026-08-20
- Pull Request: [#11 Add recoverable permanent deletion for trash](https://github.com/ry825/Kura_Storage/pull/11)
- 主な変更: 手動完全削除API、Purge Journal・advisory lock・隔離・復旧、関連管理情報Participant境界、安全な再帰削除、Storage Intent、最小監査、Actor種別、Role Claim・Admin Policy、Migration、`purgeEligibleAt`、OpenAPI・Fixture・正式文書を追加・更新した。
- 実施した自動Test・Build・静的解析: `verify-config.sh`、`verify-server.sh`（Domain 17件、Application 20件、Integration 46件）、`verify-security.sh`、`verify-android.sh`（656 tasks）、EF Core pending model changes確認、`git diff --check`が成功した。GitHub ActionsのConfig、Server、Security、Androidも実装Commit `2a464bd`で成功した。
- 実施した手動・結合・障害確認: API用HttpClient、PostgreSQL Testcontainers、一時実Filesystemを組み合わせ、File・配下Folderの完全削除、204、DB・Audit・Operation、冪等再送、Key競合、Restore競合、容量閾値以下のDelete、物理削除後DB・監査失敗からのRecovery、管理RootおよびTarget・Ancestor・Descendant symlink拒否を確認した。
- 計画と実装の差分: PR1の範囲変更はない。Android既存契約互換のため、nullable `purgeEligibleAt`は既存必須引数の後ろへ追加した。EF Coreが同一列の複数Indexをモデル化しないため、Snapshotは新しい部分Unique Indexをモデル化し、既存非Unique IndexはDB上で維持する構成とした。
- 実装中に追加したタスクと理由: 追加なし。セルフレビューでTarget・Ancestor symlinkの例外分類差を検出したため、計画済みのsymlink安全境界内で専用例外化と実Filesystem Testを補強した。
- 技術的に不要になったタスク、理由、代替実装: 取消なし。Schema棚卸しでShare、Sync、Recent、Backup Receipt、Derivative等は未実装と確認できたため、計画どおり空Table・空Featureを作らず、将来拡張用Participant契約と開発規則を実装した。
- PR2への引継ぎ事項: PR1 Migration適用後、共有`TrashPurgeOptions`と`TrashPurgeService`の`RetentionWorker` triggerを用いて30日Workerを実装する。Worker候補はTrash Rootだけを取得し、Serviceのlock内で期限・状態を再検証する。容量状態・Run履歴・systemd・運用検証はPR2で追加し、PR1をMergeするまでPR2を開始しない。

### PR2: 30日Worker・容量管理・運用配置

- 完了日:
- Pull Request:
- 主な変更:
- 実施した自動Test・Build・静的解析:
- 実施した手動・結合・障害確認:
- 計画と実装の差分:
- 実装中に追加したタスクと理由:
- 技術的に不要になったタスク、理由、代替実装:
- PR3への引継ぎ事項:

### PR3: Android完全削除・容量警告・実機E2E

- 完了日:
- Pull Request:
- 主な変更:
- 実施した自動Test・Build・静的解析:
- 実施した手動・実機確認:
- 計画と実装の差分:
- 実装中に追加したタスクと理由:
- 技術的に不要になったタスク、理由、代替実装:
- 後続作業への引継ぎ事項:

---

## 全体振り返り

> PR1〜PR3を含む本ファイルの全タスクが完了し、各Pull Request完了記録が存在する場合だけ、`steering`スキルのモード3-Bで記入する。

### 実装完了日

### 計画と実績の差分

### 主な設計変更と理由

### 技術的な学び

### プロセス上の改善点

### 次回への改善提案
