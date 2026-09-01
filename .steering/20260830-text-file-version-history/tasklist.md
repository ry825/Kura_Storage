# テキスト表示・編集／ファイルバージョン タスクリスト

## 対象要件

- `docs/product-requirements.md` 7.6.4「テキストファイル閲覧・編集」
- `docs/product-requirements.md` 7.12.5「ファイルバージョン」
- `fileVersion`を競合tokenだけでなく、実内容を保持する履歴へ拡張する。

## タスク完全完了の原則

- 全タスクを最終的に`[x]`にする。親タスクは全子タスク完了後だけ`[x]`にする。
- 1回の実装では1つのPull Request単位を、実装、Test、文書、Commit、Push、英語PR作成まで完了して停止する。
- 未Mergeの先行PRへ依存する範囲は、先行PRが`main`へMergeされ必須CI成功後に開始する。
- 実装はTDDのRed、Green、Refactor、Verifyで進める。
- Pull Request作成後は`steering`スキルのモード3-Aで完了記録を追記し、同BranchへCommit・Pushする。

## スコープ境界

- [x] 対応対象をUTF-8・最大1 MiB・承認済み6 MIMEのテキスト表示／保存／履歴／復元へ限定する。
- [x] 共同リアルタイム編集、強制上書き、3-way merge、任意文字コード変換、Web UIを追加しない。
- [x] HDD上の現行内容を正とし、過去版本文をKuraStorage管理領域のimmutable dataとして扱う。
- [x] Rename・Move・Trash・ゴミ箱からのRestoreだけでは内容versionを増やさず、過去版Restoreは新しい内容versionを作る。

---

## フェーズ0: 要求・設計承認

- [x] `requirements.md`のUser承認を得る。
  - [x] 対応MIME、UTF-8、1 MiB、権限、競合時操作を確定する。
  - [x] version作成契機、Purge、共有失効、復元の意味を確定する。
- [x] `design.md`のUser承認を得る。
  - [x] metadata／本文store、baseline、journal、transaction、APIを確定する。
  - [x] Android state、test matrix、性能・security境界を確定する。
- [x] 承認内容に合わせて本tasklistを再確認する。
  - [x] API名、Error、制約値、PR依存、検証コマンドを具体化する。
  - [x] 正式文書との矛盾がないことを確認し、不足更新をPRへ割り当てる。

---

## PR1: ファイルバージョン永続化・回復基盤

### 1.1 作業開始

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0が完了し、先行するFile／Sharing／External change／Media PRが`main`へMerge済みである。
  - [x] 最新`main`から短命Branchを作成し、`git status`と既存差分を確認する。（`feat/text-version-foundation`、Steering 2組だけが未追跡）
  - [x] `FileEntry`、`FileOperation`、Upload publish、Index reconciliation、Purge、Derivative invalidationの類似実装を確認する。

### 1.2 正式文書のServer契約更新

- [x] 永続文書を承認済み設計へ更新する。
  - [x] `docs/product-requirements.md`へversion保持、履歴、復元、権限、Purgeの検証可能な条件を追加する。
  - [x] `docs/functional-design.md`へ`FileVersionRecord`、版作成契機、baseline、API、状態遷移、Errorを追加する。
  - [x] `docs/architecture-design.md`へimmutable store、journal、transaction、recovery、security、性能を追加する。
  - [x] `docs/repository-structure.md`へDomain／Application／Infrastructure／Testの実配置を追加する。
  - [x] `docs/development-guidelines.md`へ内容変更時のversion発行規約と本文Log禁止を追加する。

### 1.3 Domain・Migration

- [x] `FileVersionRecord`をTest firstで実装する。
  - [x] ID、File ID、version、size、SHA-256、relative path、change kind、actor、UTC日時の不変条件をTestする。
  - [x] version 1以上、size 0〜1 MiB、checksum形式、許可change kindをfail-closedにする。
  - [x] `FileEntry.fileVersion`のchecked incrementと現行record一致規則をDomain／Applicationで保護する。
- [x] EF Core mappingとMigrationを実装する。
  - [x] table、FK、`(file_entry_id, version)` unique、一覧・cleanup Indexを追加する。
  - [x] FileEntry Purgeでmetadataだけを無秩序cascadeせず、本文削除journalと整合する管理処理を使う。
  - [x] Up／Down／再Up、既存データ保持、Model Snapshot、未反映modelなしをPostgreSQL実体でTestする。
  - [x] Migration transaction内で既存HDDを全走査しない。

### 1.4 Immutable version store・baseline

- [x] version本文storeをTest firstで実装する。
  - [x] Server生成IDだけからstorage root配下のrelative pathを導出する。
  - [x] temp write、flush、size／SHA-256検証、atomic publish、同一内容の安全な再送を実装する。
  - [x] traversal、symlink／reparse相当、mount不一致、read-only、容量不足、途中切断を拒否する。
  - [x] 本文、File名、物理PathをLog／例外へ含めない。
- [x] 既存Fileのbaseline作成を実装する。
  - [x] advisory lock後にFileEntry、HDD実体、size、versionを再確認する。
  - [x] record欠落時だけ現在の`fileVersion`で1件作り、同時要求とretryを一意制約で収束させる。
  - [x] `MISSING*`、`TRASHED`、未完了操作、破損、HDD unavailableを安全に拒否する。
  - [x] Migration／Admin CLIで全件backfillせず、対応テキストへの最初の履歴対応操作でだけlazy baselineを作ることをTestする。

### 1.5 内容変更・Purge統合

- [x] Upload／外部変更／Purgeをversion基盤へ統合する。
  - [x] 対応テキストのUpload正式公開時に初版metadataと本文を作り、retryで重複しない。
  - [x] 対応テキストの外部size／mtime変更確定時に変更前後を取り違えず新versionを作る。
  - [x] Rename／Moveではrecordを作らず、File IDとの関連を維持する。
  - [x] Trash／Restoreでは履歴を保持し、新versionを作らない。
  - [x] Purgeで現行、version本文、metadata、Share／Recent／Derivative等をjournalで限定削除する。
  - [x] Purge再実行、部分削除、DB失敗、本文欠落を回復し、別Fileの履歴を削除しない。
- [x] FileOperation recoveryを拡張する。
  - [x] operation kind、旧／新version、temp／final path、checksum、段階を永続化する。
  - [x] 各crash pointでroll-forward／rollbackを決定的にTestする。
  - [x] 完了応答済みversionをcleanupが削除しないことをTestする。

### 1.6 PR1検証・完了

- [x] Server品質基準を満たす。
  - [x] Domain／Application／PostgreSQL／実Filesystem Integration Testが成功する。
  - [x] 新規version・回復境界95%以上、Domain／Application全体80%以上のLine Coverageを満たす。
    - 2026-09-01実測: `FileVersionRecord` 100%、`FileVersionService` 98.29%、`FileVersionStore` 95.32%、Domain全体80.03%、Application Unit＋Integration合算83.72%。
  - [x] 30万FileEntry・100万version metadataでIndex、baseline、Purge性能と容量を記録する。
    - 2026-09-01実測: seed 107,395 ms、履歴／baseline lookup p95 116.2 ms、対象Purge 3.8 ms、metadata relation 517,513,216 bytes。履歴・lookupともIndex planを確認。
  - [x] `./scripts/ci/verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-deployment.sh`が成功する。
  - [x] `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`、Migration確認、`git diff --check`が成功する。
- [x] PR1を完了する。
  - [x] 差分をself-reviewし、API／Android先行実装、credential、実環境値、本文fixture混入がない。
  - [x] Commit、Push、英語PR、必須CI、モード3-A完了記録、再Pushを完了して報告・停止する。

---

## PR2: テキスト・履歴・復元Server API

### 2.1 作業開始・Application contract

- [ ] PR2の開始条件を満たす。
  - [ ] PR1が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [ ] Steering、version store、permission、mutation lock、OpenAPI、Activity連携点を確認する。
- [ ] Text／version contractをTest firstで実装する。
  - [ ] MIME、UTF-8、BOM、Rune／byte上限、content、expectedVersion、operationIdを検証する。
  - [ ] history item、page、checksum、change kind、actor表示、restore resultを定義する。
  - [ ] 未知enum、overflow、不正version／cursorをfail-closedにする。

### 2.2 Text取得・保存

- [ ] Text取得Use Caseを実装する。
  - [ ] current permissionを再評価し、File／`ACTIVE`／未完了なしを確認してbounded streaming readする。
  - [ ] UTF-8 strict decode、BOM除去、1 MiB、size／versionの整合を検証する。
  - [ ] Owner／共有／Admin暗黙権限なし／存在秘匿をTestする。
- [ ] Text保存Use Caseを実装する。
  - [ ] advisory lock後に`EDITOR`以上、状態、`expectedVersion`、baselineを再確認する。
  - [ ] temp writeから現行置換、新version record、FileEntry更新、Activity／Auditを回復可能に確定する。
  - [ ] 同時保存、同時Move／Trash／Purge／外部変更、共有失効、operationId再送をTestする。
  - [ ] 競合で現行内容と履歴を変更せず`FILE_VERSION_CONFLICT`を返す。

### 2.3 履歴・復元

- [ ] version一覧／本文取得を実装する。
  - [ ] 現在権限をSQL／Application境界で再評価し、version降順のpageを返す。
  - [ ] 本文取得でchecksum、size、File状態、権限を再検証する。
  - [ ] N+1、全metadata materialize、HDD directory走査を行わない。
  - [ ] Share失効、Move、Trash、MISSING、Purge、actor削除後の表示をTestする。
- [ ] 復元Use Caseを実装する。
  - [ ] `EDITOR`以上、`expectedVersion`、target version、現行状態をlock内で再確認する。
  - [ ] target本文を新しい現行versionとして発行し、復元前versionを保持する。
  - [ ] 同じ過去番号を再利用せず、change kindとactorを`RESTORE`で記録する。
  - [ ] 同時復元／保存、破損過去版、容量不足、DB失敗、retryをTestする。

### 2.4 API・OpenAPI・Security

- [ ] 5つのText／version endpointを実装する。
  - [ ] Method、path、body、page、status、Error envelope、Request IDを設計と一致させる。
  - [ ] body size limit、Content-Type、non-empty／unknown field、UUID、versionを検証する。
  - [ ] 401、存在秘匿404、409、413、415、422相当、Storage／DB Errorを統一する。
  - [ ] Rate limit、Cancellation、401 refresh後のoperationId再送を安全にする。
- [ ] OpenAPIとContract Testを更新する。
  - [ ] schema、example、上限、権限、冪等性、競合、restore意味を記載する。
  - [ ] 本文、物理Path、内部storage key、他User内部IDを不要に返さない。
- [ ] Log／security regressionを検証する。
  - [ ] API、Nginx、DB、journal、Auditに本文、File名、物理Path、Tokenがない。
  - [ ] 共有解除・Session／Device失効直後に履歴と本文を取得・更新できない。

### 2.5 PR2検証・完了

- [ ] Unit／Integration／API Test、Coverage、性能、全Server CI、format、Migration、`git diff --check`を成功させる。
- [ ] API clientで2 User／2 Device競合、履歴、比較用取得、復元、失効、PurgeをLAN／ZeroTierで確認する。
- [ ] 正式文書とrepository structureを実装結果へ更新する。
- [ ] Commit、Push、英語PR、必須CI、モード3-A完了記録、再Pushを完了して報告・停止する。

---

## PR3: Androidテキストエディター・履歴UI・実機E2E

### 3.1 Android基盤

- [ ] PR3の開始条件を満たす。
  - [ ] PR2が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [ ] Android module、Files／Sharing／Media、Navigation、Session、OpenAPI契約を確認する。
- [ ] `core-model`／`core-network`／`core-data`をTest firstで拡張する。
  - [ ] Text document、version item／page、change kind、conflict、restore resultを追加する。
  - [ ] Retrofit契約とDTO mappingをOpenAPIへ一致させ、未知値をfail-closedにする。
  - [ ] Repositoryで401 refresh、operationId再送、cancel、generation管理、Session分離を実装する。

### 3.2 `feature-text`

- [ ] ModuleとNavigationを追加する。
  - [ ] Build、dependency、unit／instrumented source set、assembly markerを追加する。
  - [ ] File一覧／詳細から対応MIMEだけを開き、Feature間直接依存を作らない。
- [ ] Text editorを実装する。
  - [ ] Loading、view、edit、dirty、saving、saved、conflict、error stateをTDDで実装する。
  - [ ] dirty離脱確認、SavedStateHandle上限、IME、accessibility、長文scrollを実装する。
  - [ ] VIEWERはread-only、EDITOR以上は保存可能とし、権限変化を次要求へ反映する。
  - [ ] 競合時の再読込、有界な行比較、別名Upload導線を実装し、強制上書きを提供しない。
- [ ] version履歴・復元UIを実装する。
  - [ ] Paging、Refresh、Empty、Error、version metadata、actor／external表示を実装する。
  - [ ] 過去版previewと現在版との差分表示を実装する。
  - [ ] Restore確認、expectedVersion競合、成功後再取得を実装する。
  - [ ] 旧Session／旧File／旧requestが画面を上書きしない。

### 3.3 Android検証・全体完了

- [ ] Android Unit／Screenshot／Instrumented Testを完了する。
  - [ ] MIME／UTF-8／size、dirty、process recreation、rotation、offline、401、409、403／404をTestする。
  - [ ] history Paging、restore、shared permissions、unknown enum、accessibilityをTestする。
  - [ ] `./scripts/ci/verify-android.sh`と`git diff --check`が成功する。
- [ ] 実機E2Eを完了する。
  - [ ] Android 13物理端末2台相当で同時編集競合、再読込、別名保存、復元を確認する。
  - [ ] Owner／VIEWER／EDITOR、共有解除、LAN／ZeroTier、切断・再接続、Session失効を確認する。
  - [ ] Raspberry Pi、PostgreSQL、実HDDでcrash recovery、version内容、Log非漏えいを確認する。
- [ ] PR3と全体を完了する。
  - [ ] 正式文書、OpenAPI、test記録、repository structureを実績へ更新する。
  - [ ] 全task、全PR記録の完了後だけモード3-Bで全体振り返りを記録する。
  - [ ] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## 各Pull Request完了記録

> Pull Request作成後にモード3-Aで追記する。後続PRが未完了でも、完了したPRの記録は行う。

### PR1: ファイルバージョン永続化・回復基盤

- 完了日: 2026-09-01
- Pull Request: [#38 Add file version persistence and recovery foundation](https://github.com/ry825/Kura_Storage/pull/38)
- Commit: `c50e913 feat(server): add file version persistence foundation`
- 実施したTest・Build・静的解析:
  - `./scripts/ci/verify-config.sh`: 成功
  - `./scripts/ci/verify-server.sh`: 成功（Domain 101件、Application 246件、Integration 193件）
  - `./scripts/ci/verify-security.sh`: 成功
  - `./scripts/ci/verify-deployment.sh`: 成功
  - `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`: 成功
  - EF Core pending-model確認、PostgreSQL Migration Up／Down／再Up、`git diff --check`: 成功
  - Coverage: `FileVersionRecord` 100%、`FileVersionService` 98.29%、`FileVersionStore` 95.32%、Domain全体80.03%、Application Unit＋Integration合算83.72%
  - 性能: 30万FileEntry／100万versionで履歴・baseline lookup p95 116.2 ms、対象Purge 3.8 ms、metadata 517,513,216 bytes、Index plan使用を確認
  - GitHub必須CI: Android、Config、Security、Serverの全Job成功
- 計画と実装の差分: PR1の承認済み境界どおり、HTTP APIとAndroidを追加せず永続化・回復基盤までを実装した。`FileOperation`の既存operation kindと状態を維持し、旧／新version、temp／final path、checksum、version publish段階をnullable拡張として追加した。
- 実装中に追加したタスクと理由: 外部変更でversion公開が失敗した場合に追跡中`FileEntry`をreloadして未対応versionだけが後続Saveへ漏れない回帰保護、実Filesystemの途中切断・破損immutable artifact・容量不足・symlink、Upload復旧後の完了済みartifact保持Testを追加した。理由はcrash／storage境界をfail-closedに収束させるため。
- 技術的に不要になったタスク・理由・代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: PR #38の`main`へのMergeと必須CI成功をPR2開始条件とする。PR2では本PRのlazy baseline、immutable store、version journal metadata、mutation lock、Purge participantを使用してText取得／保存、履歴、過去版本文取得、復元APIとOpenAPI契約を実装する。ユーザー向け操作履歴は別SteeringのPR1から開始し、セキュリティ監査ログとの表示契約を混在させない。

## 全体振り返り

> 全タスク、全Pull Request、各完了記録が揃った後にだけモード3-Bで記録する。
