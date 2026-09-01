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

- [ ] PR1の開始条件を満たす。
  - [ ] フェーズ0が完了し、Upload／Move／Sharing／Trash／Purgeの先行PRが`main`へMerge済みである。
  - [ ] `.steering/20260830-text-file-version-history/`のPR2が`main`へMerge済みで、Text Edit／Version Restoreの記録境界を利用できる。
  - [ ] 最新`main`から短命Branchを作成し、`git status`、`AuditLog`、各Use Case、journal、transaction patternを確認する。
- [ ] 永続文書を更新する。
  - [ ] `docs/product-requirements.md`へ利用者履歴とSecurity Auditの境界、可視性、管理者検索を追加する。
  - [ ] `docs/functional-design.md`へactivity type、snapshot、記録契機、no-op、保持、API概要を追加する。
  - [ ] `docs/architecture-design.md`へ二重目的の分離、transaction、Schema、permission、Log保護、性能を追加する。
  - [ ] `docs/repository-structure.md`と必要な`docs/development-guidelines.md`を更新する。

### 1.2 Domain・Migration

- [ ] `UserActivity`と型付きdetailをTest firstで実装する。
  - [ ] ID、operationId、type、actor、target／owner snapshot、UTC日時、不変条件を定義する。
  - [ ] Activity typeごとの必須／禁止detail組合せをfail-closedにする。
  - [ ] snapshot長、NFC、control character、version／permission／delete kindを検証する。
  - [ ] File本文、物理Path、Request ID、OS User、token、自由形式metadataをmodelに持たせない。
- [ ] EF Core mappingとMigrationを実装する。
  - [ ] `user_activities`、detail列／table、operationId unique、keyset／admin検索Indexを追加する。
  - [ ] User／File削除でActivityをcascade削除せず、snapshotを維持する。
  - [ ] Up／Down／再Up、既存Audit／File／Share保持、Model Snapshot、pending modelなしを実DBでTestする。
  - [ ] 100万件でIndex容量、作成時間、insert overhead、Backup増加量を測定できるseedを追加する。

### 1.3 記録factory・transaction統合

- [ ] 共通Activity factory／repositoryを実装する。
  - [ ] Actor／DeviceをSecurity Context、日時をServer clock、operationIdを既存request／journal境界から取得する。
  - [ ] snapshotを状態変更前後の正しい時点で構築し、Client表示名入力を信用しない。
  - [ ] unique operationIdでretryを1件へ収束し、no-opを記録しない。
  - [ ] Activity永続化失敗で対象状態だけ成功しないtransaction／journal境界を実装する。
- [ ] UploadとMoveへ統合する。
  - [ ] Upload正式公開時だけ`UPLOAD`を記録し、中断sessionや重複completeで追加しない。
  - [ ] 親変更成功時だけ`MOVE`を記録し、source／destination snapshotを保持する。
  - [ ] Renameのみ、同一親no-op、競合、recovery retryをTestする。
- [ ] Shareへ統合する。
  - [ ] Create／permission update／revokeの実状態変更だけを`SHARE`として記録する。
  - [ ] recipient、permission、action snapshotを必要最小限で保持する。
  - [ ] 同値update、二重revoke、共有失効競合で重複しない。
- [ ] Trash／Purgeへ統合する。
  - [ ] Trash成功を`DELETE/TRASHED`、完全削除確定を`DELETE/PURGED`として別Activityにする。
  - [ ] Purge前にFile／Owner snapshotを確保し、FileEntry削除後もActivityを保持する。
  - [ ] 既存Purge Audit unique制約を維持し、ActivityとAuditの片側だけが成功しない。
  - [ ] retention purge、manual purge、recovery retry、folder subtreeの粒度を承認済み契約どおりTestする。
- [ ] Edit記録を統合する。
  - [ ] テキスト保存／Version Restore Server PRがMerge済みであることを確認する。
  - [ ] 新version確定時だけ`EDIT`を記録し、結果versionとedit kindを保持する。
  - [ ] 409競合、同一operationId再送、保存rollbackでActivityを残さない。

### 1.4 Audit分離・Regression

- [ ] Security Auditとの分離を自動Testで保護する。
  - [ ] Login失敗、Device、Session、CLI、Recovery等がUserActivityへ入らない。
  - [ ] Share／Purge等は必要に応じ両tableへ入り、列・目的が混在しない。
  - [ ] UserActivity API repositoryから`audit_logs`をqueryできない構造にする。
  - [ ] Auditの追記専用、通常API削除不可、Purge成功一意制約を後退させない。

### 1.5 PR1検証・完了

- [ ] Domain／Application／PostgreSQL／journal recovery TestとCoverageを完了する。
  - [ ] 新規記録・transaction境界95%以上、Domain／Application全体80%以上を満たす。
  - [ ] 同時要求、retry、rollback、Purge、User削除、File削除、snapshot sanitizeをTestする。
- [ ] `verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-deployment.sh`、format、Migration、`git diff --check`を成功させる。
- [ ] 差分をself-reviewし、一般API／Android／Admin CLIの先行実装、秘密情報、実環境値がない。
- [ ] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR2: 利用者向けAPI・管理者CLI検索

### 2.1 作業開始・Query contract

- [ ] PR2の開始条件を満たす。
  - [ ] PR1が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [ ] Search／Recent permission CTE、Admin CLI、OpenAPI、safe Log patternを確認する。
- [ ] Activity response／filter／cursorをTest firstで定義する。
  - [ ] 公開type、occurredAt、actor／target snapshot、許可detailだけを含める。
  - [ ] pageSize、type filter、opaque cursor、未知enum、破損cursorを検証する。
  - [ ] 内部Audit ID、Device ID、Request ID、OS User、物理Path、result codeを含めない。

### 2.2 利用者向け認可Query・API

- [ ] permission-aware activity queryを実装する。
  - [ ] Actor本人、現在閲覧可能target、Purge済みのactor／snapshot ownerをSQL段階で和集合にする。
  - [ ] Owner、直接／継承Share、複数経路、深度64、Admin暗黙権限なしを既存規則と一致させる。
  - [ ] Share解除、permission変更、Move、Trash、Restore、Purgeを次要求へ反映する。
  - [ ] `occurred_at DESC, id DESC`のkeyset paginationとtype filterを実装する。
  - [ ] page後filter、offset、N+1、HDD走査、全件materializeを行わない。
- [ ] `GET /api/v1/activities`を実装する。
  - [ ] Security Context以外のUser／Owner入力を受けず、認証、Rate Limit、Request ID、Error envelopeを適用する。
  - [ ] cursor／page／typeの正常・境界・不正、401、Session／Device失効をTestする。
  - [ ] OpenAPI schema、example、pagination、visibility note、全Errorを追加しContract Testを成功させる。

### 2.3 Admin CLI検索

- [ ] Admin activity search Application queryを実装する。
  - [ ] actor、owner、type、UTC期間、file ID、limit、cursorを組合せ可能にする。
  - [ ] 既定100／最大1000、期間最大365日、決定的keyset順を検証する。
  - [ ] CLI query repositoryを一般API repositoryと分離する。
- [ ] `KuraStorage-admin activity search`を実装する。
  - [ ] 端末tableと`--json`、next cursor、empty、invalid、cancelを実装する。
  - [ ] User selectorの曖昧性、UTC parsing、出力escape、pipe時の終了codeをTestする。
  - [ ] 検索実行をAuditへ記録し、条件の秘密値／結果内容を通常Logへ残さない。
  - [ ] CLIからUserActivityの更新・削除を提供しない。

### 2.4 性能・Security・PR2完了

- [ ] 100万Activity性能資材と結果を追加する。
  - [ ] 10 User、所有／共有／失効／Purge、全typeを含む匿名seedを用意する。
  - [ ] 利用者先頭／後続page、type filter、admin各filterを`EXPLAIN ANALYZE BUFFERS`で確認する。
  - [ ] p50／p95、CPU／Memory、Index size、insert overheadを記録し通常2秒以内を満たす。
- [ ] API clientとCLIでUser A/B、共有解除、Move、Trash、Purge、Admin filter、Log非漏えいを確認する。
- [ ] 全Server Test、Coverage、CI、format、Migration、OpenAPI、`git diff --check`を成功させる。
- [ ] 正式文書、Admin CLI usage、repository structure、testing記録を実績へ更新する。
- [ ] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## PR3: Android操作履歴UI・実機E2E

### 3.1 Android contract・Repository

- [ ] PR3の開始条件を満たす。
  - [ ] PR2が`main`へMerge済みで、最新`main`から短命Branchを作成する。
  - [ ] Home／Navigation、Files、Sharing、Session、Paging、OpenAPI契約を確認する。
- [ ] `core-model`／`core-network`／`core-data`をTest firstで拡張する。
  - [ ] Activity item、type、typed detail、page、cursorを追加し未知値をfail-closedにする。
  - [ ] Retrofit method／query／DTO mappingをOpenAPIへ一致させる。
  - [ ] Repositoryで401 refresh、cancel、generation、cursor重複、Session分離を実装する。

### 3.2 `feature-activity`

- [ ] Module、Navigation、履歴画面を実装する。
  - [ ] Build、dependency、unit／instrumented source set、assembly markerを追加する。
  - [ ] HomeまたはProfileから履歴へ遷移し、Feature間直接依存を作らない。
  - [ ] Loading、Empty、Success、Paging、Refresh、filter、Error、retryをTDDで実装する。
  - [ ] Upload／Move／Edit／Share／Deleteを利用者向け文言とiconで区別する。
  - [ ] snapshotと現在targetを混同せず、アクセス可能時だけFile詳細導線を表示する。
  - [ ] unknown type、Purge済み、actor削除済み、権限失効を安全に表示する。
  - [ ] TalkBack、font scale、locale、日時／名前の長文、tap target、contrastを確認する。

### 3.3 Android検証・全体完了

- [ ] Unit／Screenshot／Instrumented Testを完了する。
  - [ ] type mapping、Paging、filter、refresh、401、offline、unknown enum、Session切替をTestする。
  - [ ] User A/Bで同じActivityの表示可否がServer結果どおり異なることをTestする。
  - [ ] `./scripts/ci/verify-android.sh`と`git diff --check`が成功する。
- [ ] 実機E2Eを完了する。
  - [ ] Upload、Move、Edit、Share、Trash／Purgeを実行し順序・snapshot・重複なしを確認する。
  - [ ] 共有解除、LAN／ZeroTier、切断、Token refresh、Device／Session失効を確認する。
  - [ ] Raspberry PiのAdmin CLI検索とAudit記録、API／Nginx／DB Log非漏えいを確認する。
- [ ] PR3と全体を完了する。
  - [ ] 正式文書、OpenAPI、test記録、repository structureを実績へ更新する。
  - [ ] 全task・全PR記録完了後だけモード3-Bで全体振り返りを記録する。
  - [ ] Commit、Push、英語PR、必須CI、モード3-A記録、再Pushを完了して報告・停止する。

---

## 各Pull Request完了記録

> Pull Request作成後にモード3-Aで追記する。後続PRが未完了でも、完了したPRの記録は行う。

## 全体振り返り

> 全タスク、全Pull Request、各完了記録が揃った後にだけモード3-Bで記録する。
