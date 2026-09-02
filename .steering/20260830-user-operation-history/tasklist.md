# ユーザー向け操作履歴 タスクリスト

## 対象要件

- `docs/product-requirements.md` 7.12.4「操作履歴」
- Upload、Move、Edit、Share、Deleteの利用者向け表示契約を、既存Security Audit Logと分離して実装する。

## タスク完全完了の原則

- 全タスクを最終的に`[x]`にし、親は全子タスク完了後だけ完了にする。
- 1回の実装では1つのPull Request単位をCommit、Push、英語PR、CI、モード3-A記録まで完了し停止する。
- 先行PRが必要な範囲はMergeと必須CI成功後に開始し、TDDで実装する。

## スコープ境界

- [x] 利用者向け`UserActivity`と既存`AuditLog`を別table・別契約・別queryとして維持する。
- [x] 記録対象をUpload、Move、Text Edit／Version Restore、Share、Trash／Purgeの成功状態変更へ限定する。
- [x] 失敗Security event、Download、View、Rename、Favorite、Tag、通知、Web UIを追加しない。
- [x] 全User横断検索はローカルAdmin CLIだけに提供し、通常HTTP APIへ公開しない。

---

## フェーズ0: 要求・設計承認

- [x] `requirements.md`のUser承認を得る。
  - [x] 記録対象、可視性、Purge後snapshot、管理者検索、無期限保持を確定する。
  - [x] Audit Logとの目的・列・API・保持境界を確定する。
- [x] `design.md`のUser承認を得る。
  - [x] Schema、operationId、transaction、permission query、CLI、Androidを確定する。
  - [x] 100万件性能、security、test matrixを確定する。
- [x] 承認内容に合わせて本tasklistと正式文書の更新範囲を確定する。

---

## PR1: UserActivity永続化・対象操作への記録統合

### 1.1 作業開始・正式文書

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0が完了し、Upload／Move／Sharing／Trash／Purgeの先行PRが`main`へMerge済みである。
  - [x] `.steering/20260830-text-file-version-history/`のPR2が`main`へMerge済みで、Text Edit／Version Restoreの記録境界を利用できる。
  - [x] 最新`main`から短命Branchを作成し、`git status`、`AuditLog`、各Use Case、journal、transaction patternを確認する。（`7798289`から`feat/user-activity-foundation`を作成）
- [x] 永続文書を更新する。
  - [x] `docs/product-requirements.md`へ利用者履歴とSecurity Auditの境界、可視性、管理者検索を追加する。
  - [x] `docs/functional-design.md`へactivity type、snapshot、記録契機、no-op、保持、API概要を追加する。
  - [x] `docs/architecture-design.md`へ二重目的の分離、transaction、Schema、permission、Log保護、性能を追加する。
  - [x] `docs/repository-structure.md`と必要な`docs/development-guidelines.md`を更新する。

### 1.2 Domain・Migration

- [x] `UserActivity`と型付きdetailをTest firstで実装する。
  - [x] ID、operationId、type、actor、target／owner snapshot、UTC日時、不変条件を定義する。
  - [x] Activity typeごとの必須／禁止detail組合せをfail-closedにする。
  - [x] snapshot長、NFC、control character、version／permission／delete kindを検証する。
  - [x] File本文、物理Path、Request ID、OS User、token、自由形式metadataをmodelに持たせない。
- [x] EF Core mappingとMigrationを実装する。
  - [x] `user_activities`、detail列／table、operationId unique、keyset／admin検索Indexを追加する。
  - [x] User／File削除でActivityをcascade削除せず、snapshotを維持する。
  - [x] Up／Down／再Up、既存Audit／File／Share保持、Model Snapshot、pending modelなしを実DBでTestする。
  - [x] 100万件でIndex容量、作成時間、insert overhead、Backup増加量を測定できるseedを追加する。
    - 2026-09-02実測: seed 128,398 ms、insert 75.6 µs／row、table 221,495,296 bytes、Index 507,682,816 bytes、合計729,178,112 bytes、論理Backup相当210,750,000 bytes。

### 1.3 記録factory・transaction統合

- [x] 共通Activity factory／repositoryを実装する。
  - [x] Actor／DeviceをSecurity Context、日時をServer clock、operationIdを既存request／journal境界から取得する。
  - [x] snapshotを状態変更前後の正しい時点で構築し、Client表示名入力を信用しない。
  - [x] unique operationIdでretryを1件へ収束し、no-opを記録しない。
  - [x] Activity永続化失敗で対象状態だけ成功しないtransaction／journal境界を実装する。
- [x] UploadとMoveへ統合する。
  - [x] Upload正式公開時だけ`UPLOAD`を記録し、中断sessionや重複completeで追加しない。
  - [x] 親変更成功時だけ`MOVE`を記録し、source／destination snapshotを保持する。
  - [x] Renameのみ、同一親no-op、競合、recovery retryをTestする。
- [x] Shareへ統合する。
  - [x] Create／permission update／revokeの実状態変更だけを`SHARE`として記録する。
  - [x] recipient、permission、action snapshotを必要最小限で保持する。
  - [x] 同値update、二重revoke、共有失効競合で重複しない。
- [x] Trash／Purgeへ統合する。
  - [x] Trash成功を`DELETE/TRASHED`、完全削除確定を`DELETE/PURGED`として別Activityにする。
  - [x] Purge前にFile／Owner snapshotを確保し、FileEntry削除後もActivityを保持する。
  - [x] 既存Purge Audit unique制約を維持し、ActivityとAuditの片側だけが成功しない。
  - [x] retention purge、manual purge、recovery retry、folder subtreeの粒度を承認済み契約どおりTestする。
- [x] Edit記録を統合する。
  - [x] テキスト保存／Version Restore Server PRがMerge済みであることを確認する。
  - [x] 新version確定時だけ`EDIT`を記録し、結果versionとedit kindを保持する。
  - [x] 409競合、同一operationId再送、保存rollbackでActivityを残さない。

### 1.4 Audit分離・Regression

- [x] Security Auditとの分離を自動Testで保護する。
  - [x] Login失敗、Device、Session、CLI、Recovery等がUserActivityへ入らない。
  - [x] Share／Purge等は必要に応じ両tableへ入り、列・目的が混在しない。
  - [x] UserActivity API repositoryから`audit_logs`をqueryできない構造にする。
  - [x] Auditの追記専用、通常API削除不可、Purge成功一意制約を後退させない。

### 1.5 PR1検証・完了

- [x] Domain／Application／PostgreSQL／journal recovery TestとCoverageを完了する。
  - [x] 新規記録・transaction境界95%以上、Domain／Application全体80%以上を満たす。（新規Activity model／factory 98.99%、Domain／Application全体89.32%）
  - [x] 同時要求、retry、rollback、Purge、User削除、File削除、snapshot sanitizeをTestする。
- [x] `verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-deployment.sh`、format、Migration、`git diff --check`を成功させる。
- [x] 差分をself-reviewし、一般API／Android／Admin CLIの先行実装、秘密情報、実環境値がない。
- [x] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR2: 利用者向けAPI・管理者CLI検索

### 2.1 作業開始・Query contract

- [x] PR2の開始条件を満たす。
  - [x] PR1が`main`へMerge済みで、最新`main`から短命Branchを作成する。（PR #41 Merge後の`267f27e`から`feat/user-activity-query`を作成）
  - [x] Search／Recent permission CTE、Admin CLI、OpenAPI、safe Log patternを確認する。
- [x] Activity response／filter／cursorをTest firstで定義する。
  - [x] 公開type、occurredAt、actor／target snapshot、許可detailだけを含める。
  - [x] pageSize、type filter、opaque cursor、未知enum、破損cursorを検証する。
  - [x] 内部Audit ID、Device ID、Request ID、OS User、物理Path、result codeを含めない。

### 2.2 利用者向け認可Query・API

- [x] permission-aware activity queryを実装する。
  - [x] Actor本人、現在閲覧可能target、Purge済みのactor／snapshot ownerをSQL段階で和集合にする。
  - [x] Owner、直接／継承Share、複数経路、深度64、Admin暗黙権限なしを既存規則と一致させる。
  - [x] Share解除、permission変更、Move、Trash、Restore、Purgeを次要求へ反映する。
  - [x] `occurred_at DESC, id DESC`のkeyset paginationとtype filterを実装する。
  - [x] page後filter、offset、N+1、HDD走査、全件materializeを行わない。
- [x] `GET /api/v1/activities`を実装する。
  - [x] Security Context以外のUser／Owner入力を受けず、認証、Rate Limit、Request ID、Error envelopeを適用する。
  - [x] cursor／page／typeの正常・境界・不正、401、Session／Device失効をTestする。（Session／Device失効は全認証Endpoint共通のJWT event検証で保護）
  - [x] OpenAPI schema、example、pagination、visibility note、全Errorを追加しContract Testを成功させる。

### 2.3 Admin CLI検索

- [x] Admin activity search Application queryを実装する。
  - [x] actor、owner、type、UTC期間、file ID、limit、cursorを組合せ可能にする。
  - [x] 既定100／最大1000、期間最大365日、決定的keyset順を検証する。
  - [x] CLI query repositoryを一般API repositoryと分離する。
- [x] `KuraStorage-admin activity search`を実装する。
  - [x] 端末tableと`--json`、next cursor、empty、invalid、cancelを実装する。
  - [x] User selectorの曖昧性、UTC parsing、出力escape、pipe時の終了codeをTestする。（User selectorはunique usernameまたはUUIDだけを受理し、曖昧な表示名を受けない）
  - [x] 検索実行をAuditへ記録し、条件の秘密値／結果内容を通常Logへ残さない。
  - [x] CLIからUserActivityの更新・削除を提供しない。

### 2.4 性能・Security・PR2完了

- [x] 100万Activity性能資材と結果を追加する。
  - [x] 10 User、所有／共有／失効／Purge、全typeを含む匿名seedを用意する。
  - [x] 利用者先頭／後続page、type filter、admin各filterを`EXPLAIN ANALYZE BUFFERS`で確認する。
  - [x] p50／p95、CPU／Memory、Index size、insert overheadを記録し通常2秒以内を満たす。（最大p50 241.1ms、最大p95 269.1ms）
- [x] API clientとCLIでUser A/B、共有解除、Move、Trash、Purge、Admin filter、Log非漏えいを確認する。
- [x] 全Server Test、Coverage、CI、format、Migration、OpenAPI、`git diff --check`を成功させる。
- [x] 正式文書、Admin CLI usage、repository structure、testing記録を実績へ更新する。
- [x] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR3: Android操作履歴UI・実機E2E

### 3.1 Android contract・Repository

- [x] PR3の開始条件を満たす。
  - [x] PR2が`main`へMerge済みで、最新`main`から短命Branchを作成する。（PR #42 Merge後の`4976a18`から`feat/android-user-activity`を作成）
  - [x] Home／Navigation、Files、Sharing、Session、Paging、OpenAPI契約を確認する。
- [x] `core-model`／`core-network`／`core-data`をTest firstで拡張する。
  - [x] Activity item、type、typed detail、page、cursorを追加し未知値をfail-closedにする。
  - [x] Retrofit method／query／DTO mappingをOpenAPIへ一致させる。
  - [x] Repositoryで401 refresh、cancel、generation、cursor重複、Session分離を実装する。

### 3.2 `feature-activity`

- [x] Module、Navigation、履歴画面を実装する。
  - [x] Build、dependency、unit／instrumented source set、assembly markerを追加する。
  - [x] HomeまたはProfileから履歴へ遷移し、Feature間直接依存を作らない。
  - [x] Loading、Empty、Success、Paging、Refresh、filter、Error、retryをTDDで実装する。
  - [x] Upload／Move／Edit／Share／Deleteを利用者向け文言とiconで区別する。
  - [x] snapshotと現在targetを混同せず、アクセス可能時だけFile詳細導線を表示する。
  - [x] unknown type、Purge済み、actor削除済み、権限失効を安全に表示する。
  - [x] TalkBack、font scale、locale、日時／名前の長文、tap target、contrastを確認する。

### 3.3 Android検証・全体完了

- [x] Unit／Screenshot／Instrumented Testを完了する。
  - [x] type mapping、Paging、filter、refresh、401、offline、unknown enum、Session切替をTestする。
  - [x] User A/Bで同じActivityの表示可否がServer結果どおり異なることをTestする。
  - [x] `./scripts/ci/verify-android.sh`と`git diff --check`が成功する。
- [x] 実機E2Eを完了する。
  - [x] Upload、Move、Edit、Share、Trash／Purgeを実行し順序・snapshot・重複なしを確認する。
  - [x] 共有解除、LAN／ZeroTier、切断、Token refresh、Device／Session失効を確認する。
  - [x] Raspberry PiのAdmin CLI検索とAudit記録、API／Nginx／DB Log非漏えいを確認する。
- [x] PR3と全体を完了する。
  - [x] 正式文書、OpenAPI、test記録、repository structureを実績へ更新する。（OpenAPIはPR2の公開契約と一致し、PR3でServer変更なし）
  - [x] 全task・全PR記録完了後だけモード3-Bで全体振り返りを記録する。
  - [x] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## 各Pull Request完了記録

> Pull Request作成後にモード3-Aで追記する。後続PRが未完了でも、完了したPRの記録は行う。

### PR1: UserActivity永続化・対象操作への記録統合

- 完了日: 2026-09-02
- Pull Request: [#41 Add user activity persistence and recording foundation](https://github.com/ry825/Kura_Storage/pull/41)
- 実施した検証:
  - `./scripts/ci/verify-server.sh`: Release Build警告0件、Domain 119件、Application 318件、Integration 208件の計645件成功。
  - Coverlet: Domain／Application全体6,806／7,620行（89.32%）、新規Activity model／factory 293／296行（98.99%）。
  - PostgreSQL 17: Migration Up／Down／再Up、制約、Index、`SET NULL` snapshot保持、既存Audit／File／Share保持、100万件capacity測定を成功。
  - `dotnet ef migrations has-pending-model-changes`: 未反映Model変更なし。
  - `verify-config.sh`、`verify-security.sh`、`verify-deployment.sh`、format、`git diff --check`を成功。
  - GitHub必須CI: Android、Config、Security、Serverの4件すべて成功。
  - 手動確認: 差分scope、Audit分離、秘密情報／実環境値の非混入、一般API／Admin CLI／Android未実装を確認。
- 計画と実装の差分:
  - recovery時にもServer確定Actor snapshotを再構築できるよう、既存`FileOperation`へnullable `ActorUserId`を追加した。Migration前のjournalはnullableのまま安全に回復する。
  - MoveとTrashの全体Regressionで顕在化した競合窓に対し、Trashが最新親を含む整列済みlock集合を一度再取得するよう補強した。記録対象や公開contractの変更はない。
- 実装中に追加したタスクと理由:
  - Upload／Move／Share／Delete／System Purgeの同一operationId再送をfactory単体Testへ追加し、全Activity typeの冪等収束と95%カバレッジ条件を直接保護した。
  - 100万件測定値と制限環境での検証条件を`docs/testing/20260902-user-activity-pr1.md`へ記録した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - PR1 Merge後に最新`main`からPR2 Branchを作成し、permission-aware keyset query、利用者API、ローカルAdmin CLI、OpenAPI、100万件query性能測定を実装する。
  - PR1で一般HTTP Endpoint、Admin activity search、Android `feature-activity`は実装していない。
  - 100万件でActivity relation合計729,178,112 bytes、論理Backup相当210,750,000 bytesを観測したため、PR2の認可Query計画と運用容量評価で基準値として使用する。

### PR2: 利用者向けAPI・管理者CLI検索

- 完了日: 2026-09-02
- Pull Request: [#42 Add user activity API and administrator search](https://github.com/ry825/Kura_Storage/pull/42)
- 実施した検証:
  - `./scripts/ci/verify-server.sh`: Release Build警告0件、Domain 119件、Application 332件、Integration 217件の計668件成功。
  - Coverlet: Domain／Application全体89.53%、新規Application query各file 95%以上、PostgreSQL一般／Admin query repositoryはline 99.17%、branch 92.42%、method 100%。
  - PostgreSQL 17: Migration Up／Down／再Up、全6 Index、既存制約・Audit・Share保持、permission-aware queryとAdmin Auditを成功。
  - 10 User、30万FileEntry、100万Activityの匿名seedで利用者3経路とAdmin 4 filterを各10回測定し、最大p50 241.1ms、最大p95 269.1ms。全対象の`EXPLAIN ANALYZE BUFFERS`も成功。
  - `dotnet ef migrations has-pending-model-changes`: 未反映Model変更なし。
  - `verify-config.sh`、`verify-security.sh`、`verify-deployment.sh`、format、OpenAPI Contract、`git diff --check`を成功。
  - GitHub必須CI: Android、Config、Security、Serverの4件すべて成功。
  - 手動確認: 公開responseの内部ID／OS User／物理Path非混入、User A/Bと共有解除／Move／Trash／Purgeの可視性、Admin CLIのfilter／出力／秘密情報非Log化を確認。
- 計画と実装の差分:
  - typeなしの一般／Admin検索を100万件で安定して最新順走査するため、当初のActor／Owner／Target／Type別Indexに`(occurred_at DESC, id DESC)`を追加し、正式設計とMigrationへ反映した。
  - 一般Queryは全可視Activityを先にmaterializeせず、global Indexの最新順走査中に現在権限をSQL評価して必要件数で停止する構成にした。公開条件とkeyset順は計画どおりである。
- 実装中に追加したタスクと理由:
  - 一般APIと管理CLIの権限境界をクラス単位でも明示するため、PostgreSQL query実装を一般用とAdmin用の2 repositoryへ分割し、同じintegration suiteで再検証した。
  - 権限失効後もActor本人へsnapshotを返しつつ、アクセス不能target IDを公開しない回帰Testを追加した。
  - 実測条件を再現可能にするため、30万FileEntryを含むopt-in性能Testと`docs/testing/20260902-user-activity-pr2.md`を追加した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - PR2 Merge後に最新`main`からPR3 Branchを作成し、OpenAPIの`ActivityPage`／opaque cursorを使用するAndroid data層と`feature-activity`を実装する。
  - Androidではunknown typeをfail-closedにし、snapshot表示と現在アクセス可能な`targetEntryId`による詳細導線を区別する。
  - PR2ではAndroid module、画面、実機E2Eを実装していない。全体振り返りはPR3完了後にだけ行う。

### PR3: Android操作履歴UI・実機E2E

- 完了日: 2026-09-02
- Pull Request: [#43 Add Android user activity history](https://github.com/ry825/Kura_Storage/pull/43)
- 実施した検証:
  - `./scripts/ci/verify-android.sh`: app／全test APK assembly、Unit Test、coverage検証、SBOM、ktlint、detekt、lintの1,206 Gradle taskを成功。
  - OPPO CPH2333（Android 13）: `ActivityScreenTest` 2件を成功し、全5 type／detail、Paging、filter、Refresh、Loading／Empty／Error／unknown、Purge snapshot、導線、font scale 2.0、screenshot、accessibility semanticsを確認。
  - Production CA／署名付きversion code 16 APK: 署名fingerprint、non-debuggable、version、upgrade install、LAN外切断表示、ZeroTier到達、remote初回登録拒否を確認。
  - Raspberry Pi実API: Upload、Move、Edit、Share作成／解除、Trash、Purgeの厳密な新しい順、snapshot、Move／Upload重複なし、page／filterを確認。
  - User A／B／無関係User、共有解除後の再認可、LAN、ZeroTier health、切断／再起動回復、Token refresh、logout、Device失効を確認。
  - Raspberry Pi Admin CLIの7件検索、`ACTIVITY_SEARCH` Audit、API／Worker／Nginx Log非漏えい、E2E合成データの限定cleanupとサービス／mount健全性を確認。
  - Pre-deployのStorage／PostgreSQL対応Backup、checksum、archive list、restore structureを確認し、Backupは保持した。
  - GitHub必須CI: Android、Config、Security、Serverの4件すべて成功。`git diff --check`と秘密情報／実環境値の非混入確認も成功。
- 計画と実装の差分:
  - 画面更新は追加依存を必要としない既存UI patternの明示的`Refresh` actionとして実装し、Steering設計の表現を実績へ合わせた。filter／Session変更を含むgeneration破棄契約は計画どおりである。
  - 実機の現在地Wi-FiがPiのLAN subnetと異なるため、署名済みAPKではZeroTier health到達とlocal-only初回登録拒否までを確認した。認証済みUser A／Bと全操作は同じPiの実API、全画面状態は同じAndroid実機のinstrumentationで検証し、Credential制約を迂回しなかった。
- 実装中に追加したタスクと理由:
  - Session切替時のCompose ViewModel再利用を直接防ぐため、Session IDを含むActivity ViewModel keyと回帰Testを追加した。
  - 画面lock中も再現可能に実機testを起動するtest-only Activity manifest、font scale 2.0とscreenshot captureを追加した。production manifestへの影響はない。
  - E2Eの複数試行で生成した合成データをprefix・件数で限定して削除し、認証秘密と失敗stageを除去する最終cleanup検証を追加した。
- 技術的に不要になったタスク、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ事項:
  - 本作業の後続Pull Requestはない。Review後にPR #43をMergeし、保持中のpre-deploy Backupは既存運用方針に従って管理する。

## 全体振り返り

- 実装完了日: 2026-09-02
- 全体の計画と実績の差分:
  - 計画どおり、PR1で永続化とtransaction記録、PR2で認可Query／一般API／Admin CLI、PR3でAndroid UI／実機E2Eへ分割した。各PRは先行PR Merge後の最新`main`から開始し、Security Auditとの分離を維持した。
  - 実装中にjournal recovery用nullable Actor、Trash lock再取得、100万件のglobal ordering Index、一般／Admin repository分割、Android Session keyを追加し、発見した整合性・性能・Session分離要件を正式設計とTestへ反映した。
- 主な設計変更と理由:
  - UserActivityをAuditLogからtable・model・repository・APIの全層で分離し、成功操作の同一transaction追記と現在権限のrequest時再評価を採用した。利用者表示とSecurity調査で保持・公開情報が異なるためである。
  - `operationId`一意制約とjournal再利用でretryを収束し、Purge後は参照を外して型付きsnapshotだけを保持した。状態変更の欠落／重複防止と削除後説明を両立するためである。
  - Androidは未知値をraw表示せず、DTOからtyped detailへ厳密変換し、現在target IDがある行だけ開ける構成にした。Server contract進化と権限失効時の安全性を優先した。
- 技術的な学び:
  - 100万件規模では個別filter Indexだけでなくglobal newest-first Indexがpermission-aware keyset走査に必要であり、実測Query planとp95の両方で確認する価値があった。
  - 利用者履歴は記録時snapshotと閲覧時認可を分離することで、Rename／Move／Share解除／Purge後も説明の安定性と最小権限を同時に保てる。
  - Androidのpaging generation、cursor停滞、Session ViewModel keyを別々に保護することで、非同期応答とUser切替による旧データ混入を防げる。
- プロセス上の改善点:
  - Deployment stagingの相対pathと同名Backup検査を事前確認できず、状態変更前に停止した試行が2回発生した。安全停止と対応Backup照合により本番データ影響はなかったが、preflightを先に独立実行すべきだった。
  - E2E scriptの`jq`判定優先順位、再起動ready判定、health pathに検証script由来の失敗があった。製品障害とtest harness障害を切り分ける小さなprobeを先に通す必要がある。
  - Android instrumented testは画面lock状態に依存したため、test-only host Activityでscreen-on条件を固定すると再現性が向上した。
- 次回への改善提案:
  - Release前にstaging hierarchy、Backup名、Migration集合、health URLをread-only preflightで検証する共通scriptを用意する。
  - User A／B、Token／Device失効、service restart、log marker、限定cleanupを共通E2E harnessへまとめ、shell assertion自体のUnit Testまたはfixture testを追加する。
  - 物理ネットワークが離れている場合の検証matrixをLAN登録、ZeroTier到達、認証済みAPI、実機UIへ明示分割し、開始時に各経路のsubnet／interface条件を記録する。
