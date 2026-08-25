# 検索・最近使用したファイル タスクリスト

## 対象

- 正式要件: `docs/product-requirements.md` 7.7「MVP後: 検索と整理」
- 目的: 個人・直接共有・Folder継承・複数共有経路を含む閲覧可能範囲だけをPostgreSQLで検索し、User単位の最近使用履歴へ現在の権限と`MISSING`状態を反映する。
- 完了条件: 名前・種類・日時・サイズ・Owner・共有元・状態Filter、安定Pagination、最近使用履歴、Android UI、30万件性能、Raspberry Pi／Android実機E2Eが実装・検証されている。

## 作業開始前の前提

- [x] 本作業の`requirements.md`を作成し、一括作成・実装指示へ基づく要求を反映している。
- [x] 本作業の`design.md`を作成し、Search／Recent契約、認可SQL、Index、Android、性能・運用方針を定義している。
- [x] 要求と設計を3つのPull Request単位へ分割している。
- [x] 実装開始時に本Steering 3文書と正式文書の差分を再確認し、矛盾がない。
- [x] PR #22が`main`へMerge済みで、Android、Config、Security、Serverの必須CIが成功している。
- [ ] 各PR開始時に依存元PRのMergeと必須CI成功を確認する。

## タスク完全完了の原則

**本ファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止する。**

- PR1からPR3まで順番に実施し、同時に複数PRの範囲へ着手しない。
- 後続PRは依存元PRが`main`へMergeされた後、最新`main`から短命Branchを作成する。
- 実装と対応するUnit・Integration・Contract・Performance・UI・E2E Test、文書更新を同じPR範囲に含める。
- 選択したPR単位に未完了タスク`[ ]`を残したまま作業を終了しない。
- 「時間の都合」「難しい」を理由に省略しない。大きい項目は実装可能な子タスクへ分割する。
- 技術的に不要になった項目だけ、取消理由と代替実装を明記して完了扱いにできる。
- OCR・全文検索、検索候補、保存済み検索、タグ・お気に入り、履歴手動削除、推薦、Admin横断検索、Media派生データ、自動Backupを追加しない。

## Pull Request構成

1. **PR1: 権限対応Search API・PostgreSQL Index・性能基盤**
2. **PR2: 最近使用履歴・権限再評価API**
3. **PR3: Android検索・最近使用UI・実機E2E**

---

## PR1: 権限対応Search API・PostgreSQL Index・性能基盤

### 1.1 作業開始と正式文書整合

- [x] PR1の作業準備を完了する。
  - [x] PR #22が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`を取得し、PR1用の短命Branchを作成する。
  - [x] `requirements.md`、`design.md`、本ファイル、5つの正式文書の検索・認可・MISSING・性能節を再確認する。
  - [x] `git status`と既存差分を確認し、ユーザーの変更を保護する。
  - [x] `FileEntry`、`AuthorizationService`、PostgreSQL固有Query、Migration、API/OpenAPI、Integration Testの既存パターンを確認する。
  - [x] PostgreSQL 17 Testcontainer、`pg_trgm` Extension権限、k6または同等の再現可能な負荷試験手段を確認する。（Docker 29.6.1と`postgres:17-alpine` Testcontainersを利用し、k6 ScriptをPR1で追加する）
- [x] 正式文書へ承認済みSearch／Recent設計を反映する。
  - [x] `docs/product-requirements.md`へ入力境界、Pagination、権限失効、状態、履歴記録境界、性能・Log受け入れ条件を追加する。
  - [x] `docs/functional-design.md`へSearch／Recent API、Filter、Response、User flow、履歴upsert、失効時挙動を追加する。
  - [x] `docs/architecture-design.md`へSearch Query、Index、短語分岐、認可SQL、Recent schema、Log保護、性能測定を追加する。
  - [x] `docs/repository-structure.md`へServer Search／Recent、Android `feature-search`、Performance Testの配置を追加する。
  - [x] `docs/development-guidelines.md`へ検索入力、LIKE escape、Query plan、query string Log禁止、Recent記録境界の規則を追加する。
  - [x] 上位文書間の用語、enum、Endpoint、Error code、Page上限、スコープが一致する。

### 1.2 Search契約・Application model

- [x] Search contractをTDDで実装する。
  - [x] `SearchQuery`、`SearchFilter`、`SearchResultItem`、`SearchPage`をApplicationへ追加する。
  - [x] `FileCategory`を`IMAGE`、`VIDEO`、`AUDIO`、`DOCUMENT`、`ARCHIVE`、`OTHER`で定義する。
  - [x] MIMEの既知prefix／値を安定Categoryへ変換し、未知・nullを`OTHER`へ変換する。
  - [x] FolderとFile category、size Filterの不正組合せを拒否する。
  - [x] Search結果へOwner、Permission、PermissionSource、ShareTargetId、Status、MIME、Size、UpdatedAtを含める。
- [x] Search入力の正規化・検証をTDDで実装する。
  - [x] qのtrim、NFC、1〜200 code point、Filterなしq省略拒否を実装する。
  - [x] 1〜2文字のPrefix分岐と3文字以上のcontains分岐を決定的に選択する。
  - [x] LIKEの`%`、`_`、`\`をliteral escapeし、SQL patternをClient入力として扱わない。
  - [x] entryType、category、status、UUID、ISO日時、size、page／pageSizeを検証する。
  - [x] updatedFrom > updatedTo、minSize > maxSize、負値、`TRASHED`、pageSize > 100を拒否する。
  - [x] `INVALID_SEARCH_QUERY`と`INVALID_SEARCH_FILTER`を既存Error modelへ追加する。

### 1.3 Name Index Migration

- [x] Search用PostgreSQL Migrationを実装する。
  - [x] `CREATE EXTENSION IF NOT EXISTS pg_trgm`を前方Migrationへ追加する。
  - [x] `lower(name) gin_trgm_ops`のGIN Indexを命名規約どおり追加する。
  - [x] 短いPrefix検索用`lower(name) text_pattern_ops`と安定IDを考慮したB-tree Indexを追加する。
  - [x] 大Table Index作成時のTransaction、Lock、空き容量、失敗回復をMigrationと運用手順で確定する。
  - [x] Downで他機能が利用するExtensionを無条件削除せず、本作業Indexだけを安全に戻す。
  - [x] Model SnapshotとMigration metadataを更新し、手動SQLを使用する箇所と理由を記録する。
- [x] Migration Testを完了する。
  - [x] 空DBと既存FileEntry DBのUpが成功し、既存行・Share・MISSING状態を保持する。
  - [x] Index定義、operator class、命名、Extension有効化をPostgreSQL実体で確認する。
  - [x] DownがFileEntryを削除せず、再度Upできる。
  - [x] `dotnet ef migrations has-pending-model-changes`で未反映変更がない。

### 1.4 権限対応Search Query

- [x] `ISearchRepository`とPostgreSQL実装をTDDで追加する。
  - [x] Actor User IDをSecurity Contextから受け取り、ClientのUser IDを信用しない。
  - [x] Owner、直接File／Folder Share、祖先Folder Shareを既存規則と同じ深度64の有界Queryで解決する。
  - [x] 複数経路の最強PermissionとDIRECT／最も近いINHERITEDのTie-breakを返す。
  - [x] File直接Shareを子へ継承せず、Folder Shareだけを子孫へ継承する。
  - [x] `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`を対象とし、`TRASHED`と未完了FileOperation中の対象を除外する。
  - [x] ADMINへ暗黙の他User検索権限を付与しない。
- [x] 全Search FilterをSQL段階へ実装する。
  - [x] qのexact／prefix／trigram条件と順位をparameterized SQLで実装する。
  - [x] Entry type、File category、status、updated range、size range、Owner、Share Targetを組合せ可能にする。
  - [x] Owner結果ではShare Target Filterへ一致させず、現在の実効共有元だけを評価する。
  - [x] `totalCount`を閲覧可能かつFilter一致した範囲だけから計算する。
  - [x] qありとFilterのみの決定的な排序、offset、limitを実装する。
  - [x] 必要列だけをProjectionし、`SELECT *`、N+1、Entity graph全件materializeを行わない。
- [x] Search Application serviceをTDDで実装する。
  - [x] Query検証、Repository呼出し、Response mappingを1つのUse Caseへ集約する。
  - [x] Query timeout、DB error、Cancellationを既存Error方針へMappingする。
  - [x] q、File名、User名、物理PathをLog、Metric label、例外へ含めない。

### 1.5 Search API・OpenAPI・Access Log保護

- [x] `GET /api/v1/search`を実装する。
  - [x] 全Query parameterをAPI contractからApplication contractへ厳密にMappingする。
  - [x] 成功Pageに物理Path、他Userの非公開情報、内部Share IDを不要に含めない。
  - [x] 400、401、404相当、Storage／DB Errorを既存EnvelopeとRequest IDで返す。
  - [x] EndpointからDbContext、Repository、SQLを直接呼ばない。
- [x] OpenAPIとContract fixtureを更新する。
  - [x] 全parameter、enum、format、min／max、必須組合せ、Response schemaを記載する。
  - [x] `MISSING_CANDIDATE`／`MISSING`、Owner、Permission Source、Share Targetのexampleを追加する。
  - [x] 不正Query／Filter Error exampleを追加し、OpenAPI parseと契約Testを成功させる。
- [x] Access Logから検索語を保護する。
  - [x] Nginx safe log formatを追加し、`$request`／`$request_uri`／`$args`を使用せずmethod、`$uri`、protocolだけを記録する。
  - [x] LAN／ZeroTier両Server blockが同じsafe formatを使用する。
  - [x] API middleware／framework LogがQuery stringやResponse File名を記録しないことを確認する。
  - [x] 配置Config Testでq、File名、Token、物理PathがLogへ出ないことを検証する。

### 1.6 Search自動Test・性能Test

- [x] Application Unit Testを完了する。
  - [x] q正規化、Unicode、short／long分岐、escape、全境界値をTestする。
  - [x] MIME category、Unknown、全Filter組合せと不正組合せをTestする。
  - [x] Owner、Direct、Inherited、複数経路、Tie-break、UnknownをTestする。
  - [x] Search／認可境界のLine Coverage 95%以上、Domain/Application全体80%以上を確認する。
- [x] PostgreSQL Integration Testを完了する。
  - [x] 個人、直接File、直接Folder、継承、複数祖先、直接+継承、未共有を実DBでTestする。
  - [x] Share解除、Permission変更、Move、Trash、Restore、Purge、MISSING索引削除後の結果をTestする。
  - [x] 全Filter、Pagination、同名、大小文字、特殊文字、Unicode、不正UUID、SQL Injection入力をTestする。
  - [x] 100件PageでSQL回数が固定され、深度64と循環で無制限再帰しないことをTestする。
- [x] 再現可能なSearch性能資材を追加する。
  - [x] `performance/k6/search.js`またはRepository規約に沿う同等Scriptを追加する。
  - [x] 30万FileEntry、10 User、個人・共有・MISSINGを含む匿名化seed生成手順を追加する。
  - [x] 代表20検索、warm-up、反復回数、p50／p95、Error率を固定する。
  - [x] `EXPLAIN (ANALYZE, BUFFERS)`でtrigram／prefix／認可Indexを使用し、不要なSeq Scan、temp spillがないことを確認する。
  - [x] Pi相当環境で通常2秒以内を満たし、Index size、Migration時間、CPU／Memory、Query planを`docs/testing/`へ記録する。

### 1.7 PR1標準検証・手動確認・完了

- [x] API ClientでSearch主要フローを確認する。
  - [x] Ownerと受信Userで同じQueryを実行し、閲覧可能範囲・Permission Source・件数が異なることを確認する。
  - [x] q、種類、日時、size、Owner、Share Target、statusと組合せFilterを確認する。
  - [x] 未共有User、ADMIN、不正Filter、過大入力、Share解除後の存在秘匿を確認する。
  - [x] Nginx／API／PostgreSQL Logにq、File名、User名、物理Pathがないことを確認する。
- [x] PR1の標準検証を完了する。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-deployment.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が既存Androidに対して成功する。
  - [x] `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`が成功する。
  - [x] `dotnet ef migrations has-pending-model-changes`相当が成功する。
  - [x] `git diff --check`が成功する。
- [x] PR1差分をセルフレビューする。
  - [x] 認可をSQL後に隠しておらず、HDD走査、N+1、無制限再帰がない。
  - [x] Search query、File名、実User識別情報、物理Path、Credentialが差分・Log・Test記録にない。
  - [x] Recent persistence／API、Android UI、OCR／全文検索を先行実装していない。
- [ ] PR1を完了する。
  - [ ] 1.1〜1.7のPR1対象項目がすべて`[x]`である。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR1完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR2: 最近使用履歴・権限再評価API

### 2.1 作業開始

- [ ] PR2の作業準備を完了する。
  - [ ] PR1が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`からPR2用の短命Branchを作成する。
  - [ ] Steering、PR1完了記録、Search contract／Query／Migration、File詳細API、認可、Purgeの既存実装を確認する。
  - [ ] `git status`と既存差分を確認する。

### 2.2 RecentFile Domain・Persistence・Migration

- [ ] `RecentFile` domain modelをTDDで実装する。
  - [ ] User ID、File ID、OpenedAtを表現し、空IDと非UTC時刻を拒否する。
  - [ ] Server clockによる再Open更新を実装し、Client時刻を受け取らない。
  - [ ] DomainがEF Core、Npgsql、ASP.NET Coreに依存しないことを保護する。
- [ ] EF Core永続化を実装する。
  - [ ] `recent_files`と`DbSet`を追加する。
  - [ ] `(user_id, file_id)`複合Primary Key、User／FileEntry Foreign Key、両方のDelete Cascadeを定義する。
  - [ ] `(user_id, opened_at DESC, file_id)`Indexを追加する。
  - [ ] File単独Indexの必要性をCascade／Query planで測定し、必要な場合だけ追加する。
  - [ ] PostgreSQL単一Statementのupsertで同時要求を1行へ収束させる。
- [ ] `AddRecentFiles`相当のMigrationを作成する。
  - [ ] Upが既存User、File、Share、Search Indexを保持する。
  - [ ] DownがRecent履歴だけを対象とし、FileEntryやSearch Indexを変更しない。
  - [ ] Rollback時に履歴が失われることを事前集計・明示し、無言削除しない運用手順を追加する。
  - [ ] Migration Testと未反映model確認を成功させる。

### 2.3 Recent記録Service

- [ ] Recent記録Use CaseをTDDで実装する。
  - [ ] Actor UserをSecurity Context、OpenedAtをServer clockから取得する。
  - [ ] 対象が`FILE`、`ACTIVE`、閲覧可能であることをupsert直前に再評価する。
  - [ ] Folder、`MISSING*`、`TRASHED`、未完了操作、権限なし、削除済みを存在秘匿404で拒否する。
  - [ ] Viewer以上を許可し、ADMINへ暗黙権限を付与しない。
  - [ ] 同一User／Fileの再送・同時送信で重複せず、openedAtがServer上の最新成功時刻へ進む。
  - [ ] 認可とupsertの競合で失効後の履歴更新が成功しないようTransaction／Lock境界を確定する。

### 2.4 Recent一覧Query・Service

- [ ] Recent一覧RepositoryをTDDで実装する。
  - [ ] Actor User本人の行だけを起点にQueryする。
  - [ ] 現在のOwner、Direct、Inherited、複数経路の実効権限をSQL段階で再評価する。
  - [ ] 権限失効中の履歴行を保持したまま結果と`totalCount`から除外する。
  - [ ] `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`を返し、`TRASHED`、未完了操作、削除済みを返さない。
  - [ ] `opened_at DESC, file_id ASC`の安定順とpageSize 1〜100を実装する。
  - [ ] Search resultと同じOwner、Permission、Source、Share Target、Status metadataへMappingする。
  - [ ] N+1、HDD走査、全User履歴materializeを行わない。
- [ ] 権限・状態遷移をTestする。
  - [ ] Share解除／Permission変更後の次要求で非表示になる。
  - [ ] 別経路が残る場合は最強Permissionと新しいSourceで表示を維持する。
  - [ ] Moveによる継承失効／取得、Trash／Restore、MISSING二段階、Purge／索引削除を反映する。
  - [ ] 権限再取得時は保持された過去openedAtで再表示し、先頭へ不正更新しない。

### 2.5 Recent API・OpenAPI

- [ ] `PUT /api/v1/recent-files/{fileId}`を実装する。
  - [ ] Request bodyなし、成功204、冪等再送、UUID検証を実装する。
  - [ ] ClientのUser、Device、OpenedAt、Owner入力を受け付けない。
  - [ ] 401 Refresh後の再送と結果不明後の再送で1行へ収束する。
- [ ] `GET /api/v1/recent-files`を実装する。
  - [ ] page／pageSizeを検証し、本人の認可済みPageだけを返す。
  - [ ] MISSING状態、Owner、Permission／Source、Share Target、OpenedAtを返す。
  - [ ] 別Userの行・件数・File名を返さない。
- [ ] OpenAPIとContract Testを更新する。
  - [ ] PUT／GET、Path／Query、204、Page schema、全Errorを追加する。
  - [ ] Search item共通schemaを重複・不整合なく再利用する。
  - [ ] 旧Clientへ不要なServer stateを要求せず、既存Endpointに回帰を起こさない。

### 2.6 Recent自動Test・手動確認

- [ ] Domain／Application Unit Testを完了する。
  - [ ] Server clock、再Open、User分離、非FILE／非ACTIVE／権限境界をTestする。
  - [ ] 同時upsert、認可失効競合、Cancellation、DB errorをTestする。
  - [ ] Search／Recent／認可境界95%以上、Domain/Application全体80%以上のLine Coverageを確認する。
- [ ] PostgreSQL Integration Testを完了する。
  - [ ] PK、FK、Index、Cascade、Migration Up／Downを実DBでTestする。
  - [ ] User A/Bの同一File履歴分離、別User取得拒否、同時PUTをTestする。
  - [ ] Owner／Direct／Inherited／複数経路／失効／再取得／Move／Trash／Restore／Purge／MISSINGをTestする。
  - [ ] Page 100件で固定SQL回数、Index利用、物理Path・検索語・履歴内容のLog非出力を確認する。
- [ ] API ClientでRecent主要フローを確認する。
  - [ ] File表示後PUT、再Open順序更新、別User分離、Folder／MISSING記録拒否を確認する。
  - [ ] Share解除後非表示、別経路維持、MISSING状態表示、Purge後Cascadeを確認する。
  - [ ] API／Nginx／DB LogがFile名、User名、物理Path、Tokenを含まない。

### 2.7 PR2標準検証・完了

- [ ] PR2の標準検証を完了する。
  - [ ] `./scripts/ci/verify-config.sh`が成功する。
  - [ ] `./scripts/ci/verify-server.sh`が成功する。
  - [ ] `./scripts/ci/verify-security.sh`が成功する。
  - [ ] `./scripts/ci/verify-deployment.sh`が成功する。
  - [ ] `./scripts/ci/verify-android.sh`が既存Androidに対して成功する。
  - [ ] `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`が成功する。
  - [ ] Migration未反映model確認と`git diff --check`が成功する。
- [ ] PR2差分をセルフレビューする。
  - [ ] Recent履歴がUser単位で、現在権限をSQL段階で評価している。
  - [ ] GET／検索／Background処理が履歴を暗黙更新せず、Server clockだけを使用する。
  - [ ] Android UI、履歴手動削除、閲覧回数、推薦、将来Columnを追加していない。
  - [ ] Credential、実環境値、物理Path、実User識別情報、生成物が差分にない。
- [ ] PR2を完了する。
  - [ ] 2.1〜2.7のPR2対象項目がすべて`[x]`である。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR2完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR3: Android検索・最近使用UI・実機E2E

### 3.1 作業開始

- [ ] PR3の作業準備を完了する。
  - [ ] PR2が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`からPR3用の短命Branchを作成する。
  - [ ] Steering、PR1／PR2完了記録、OpenAPI、Search／Recent API、Android sharing／files／Navigationの既存実装を確認する。
  - [ ] `git status`と既存差分を確認する。
  - [ ] Android実機、Raspberry Pi、PostgreSQL、実HDD、LAN、ZeroTier、Release署名入力を確認する。

### 3.2 Android model・Network・Repository

- [ ] Search／Recent domain modelをTDDで実装する。
  - [ ] Search query、Filter、File category、Result item、Page、Recent itemを`core-model`へ追加する。
  - [ ] Owner、Permission、Source、Share Target、Statusは既存File／Sharing modelと単一化する。
  - [ ] Unknown enum、欠落値、不正UUID／日時をFail-closedで扱う。
  - [ ] Search入力境界とFilter組合せをUIとRepositoryで共通利用できる形にする。
- [ ] Search／Recent Retrofit APIとDTOを実装する。
  - [ ] GET Searchの全Query parameter、Header、Page responseをOpenAPIと一致させる。
  - [ ] GET RecentとPUT RecentのPath、204、Errorを一致させる。
  - [ ] 401 Refresh後にSearch GETを再送し、PUT Recentは冪等に同じFileへ再送する。
  - [ ] Contract TestでMethod、URL encoding、Query、Header、BodyなしPUT、Response mappingを確認する。
- [ ] RepositoryとPagingを実装する。
  - [ ] 全要求を`AuthenticatedRequestExecutor`経由で送る。
  - [ ] Search世代またはCoroutine cancelで古い応答が新しい条件を上書きしない。
  - [ ] Refreshと次Pageの条件を固定し、Filter変更時はPage 1から再取得する。
  - [ ] Recent PUTのNetwork結果不明で成功を合成せず、Recent GETまたはFile詳細を再取得する。
  - [ ] Server契約の重複ID、不正Page、Unknown metadataを成功扱いにしない。

### 3.3 `feature-search`・Search画面

- [ ] `feature-search` Moduleを追加する。
  - [ ] Build設定、Dependency、Lockfile、Assembly marker、Unit／Instrumented Test source setを追加する。
  - [ ] 既存Compose Theme・Componentを再利用し、新しい外部Packageを追加しない。
  - [ ] `feature-files`／`feature-sharing`への直接Module依存を追加しない。
- [ ] Search ViewModelをTDDで実装する。
  - [ ] Query、Filter、Loading、Empty、Success、Paging、Refresh、Error stateを実装する。
  - [ ] 明示検索またはIME actionで実行し、過大な入力中Requestを発行しない。
  - [ ] 入力変更中の旧Request cancel／世代破棄、二重LoadMore防止を実装する。
  - [ ] Token refresh、通信結果不明、Storage/API Error、権限失効時の再取得を実装する。
- [ ] Search Compose画面を実装する。
  - [ ] Home導線、検索語、Entry種別、Category、日時、size、Owner／共有元、status Filterを提供する。
  - [ ] Owner／共有元候補を本人と既存受信Shareから構成し、未共有User一覧を取得しない。
  - [ ] 結果へ種別、名前、Owner、Permission／Source、共有元、size、更新日時、MISSING状態を表示する。
  - [ ] FolderはFile browser、Fileは最新詳細／DownloadへApp callbackで遷移する。
  - [ ] Unknown／MISSING／権限失効で破壊的操作を表示せず、既存capabilityを再利用する。
  - [ ] Narrow screen、Font scale、回転、Keyboard、Filter折返し、Scrollを確認する。
  - [ ] qをLog、Analytics、Crash message、永続SavedStateへ保存しない。

### 3.4 最近使用画面・履歴記録・Navigation

- [ ] Recent ViewModelとCompose画面をTDDで実装する。
  - [ ] Loading、Empty、Success、Paging、Refresh、Error、権限失効状態を実装する。
  - [ ] 新しい順にFile名、Owner、Permission／Source、共有元、OpenedAt、MISSING状態を表示する。
  - [ ] MISSING項目は状態案内だけを表示し、Download／変更操作を有効にしない。
  - [ ] 項目選択時は既存File詳細APIで最新状態を再取得してから表示する。
  - [ ] 解除・404後はRecent一覧をRefreshし、Clientから項目だけを成功削除したと推測しない。
- [ ] File表示成功後のRecent記録を接続する。
  - [ ] File詳細が実際にユーザーへ表示された後だけPUTを呼ぶ。
  - [ ] Folder、一覧、Search結果表示、Background refresh、MISSING詳細ではPUTしない。
  - [ ] 回転・再Composition・同一詳細再読込による過剰送信を抑え、Server冪等性も維持する。
  - [ ] PUT失敗でFile閲覧自体を失敗扱いにせず、履歴同期Errorを非破壊的に扱う。
- [ ] App NavigationとDIを接続する。
  - [ ] Homeへ「検索」と「最近使用」導線を追加する。
  - [ ] `AppDestination`、`ServiceContainer`、Session単位Repository／ViewModelを追加する。
  - [ ] Search／Recent結果から既存File browser／detailへIDだけを渡す。
  - [ ] Logout、User切替、接続経路変更時に前UserのQuery、Filter、結果、Recentを再利用しない。

### 3.5 Android自動Test

- [ ] Model／Network／Repository Unit・Contract Testを完了する。
  - [ ] 全Category、Status、Owner、Direct、Inherited、Unknown、Share Target mappingをTestする。
  - [ ] Search全Queryのencoding、Filter省略、Page、401 Refresh、ErrorをTestする。
  - [ ] Recent GET／PUT、204、401、結果不明、重複抑止をTestする。
  - [ ] Paging、Filter変更、古い応答破棄、不正Server responseをTestする。
- [ ] ViewModel／Compose UI Testを完了する。
  - [ ] SearchのLoading、Empty、Success、Filter、Paging、Refresh、Error、結果遷移をTestする。
  - [ ] q境界、short query、特殊文字、IME、二重検索、古い応答をTestする。
  - [ ] RecentのLoading、Empty、Success、MISSING、失効、Paging、項目遷移をTestする。
  - [ ] File表示成功だけがRecent PUTを呼び、Folder／一覧／Search表示が呼ばないことをTestする。
  - [ ] Owner／Viewer／Contributor／Editor／Manager／Unknownで操作表示が既存権限表と一致する。
  - [ ] 回転、Narrow screen、Scroll、Dialog／Keyboard表示で操作不能にならない。
- [ ] Android標準検証を完了する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] 接続Android実機で全対象Moduleの`connectedDebugAndroidTest --max-workers=1`相当が成功する。
  - [ ] `./scripts/ci/verify-config.sh`、`verify-server.sh`、`verify-security.sh`、`verify-deployment.sh`、`git diff --check`が成功する。

### 3.6 Rollout・性能・Raspberry Pi Server E2E

- [ ] 本番相当環境の事前保護とRolloutを完了する。
  - [ ] PostgreSQLとStorage Rootの対応するBackup、復元可能性、Storage ID、Service状態を確認する。
  - [ ] 30万件性能seedとE2E一時データを実データから分離し、限定識別子と清掃手順を確定する。
  - [ ] Search Migration、Recent Migration、API、Worker、署名Androidの順序で適用する。
  - [ ] Index作成時間、Lock、DB容量、Extension、Rollback制約、Recent件数を適用前後に確認する。
- [ ] Raspberry Piで30万件Search性能を確定する。
  - [ ] 個人、直接共有、継承、複数経路、MISSINGを含む代表20検索を測定する。
  - [ ] warm／cold条件、p50／p95、最大、Error率、CPU、Memory、DB connection、Index sizeを記録する。
  - [ ] 通常2秒以内を満たし、`EXPLAIN ANALYZE BUFFERS`で意図したIndexと有界認可Queryを確認する。
  - [ ] 短語Prefix、trigram contains、Filterのみ、Owner、Share Target、Page後半の最悪条件を含める。
  - [ ] 目標未達の場合はQuery／Indexを修正して同じ条件で再測定し、未達のまま完了扱いにしない。
- [ ] Server Search／Recent E2Eを完了する。
  - [ ] Owner、Viewer、Contributor、Editor、Manager、Admin、未共有Userで許可範囲と存在秘匿を確認する。
  - [ ] File／Folder名、Category、日時、size、Owner、共有元、status、組合せFilter、Paginationを確認する。
  - [ ] Direct、Inherited、複数祖先、直接+継承、最強PermissionとSourceを確認する。
  - [ ] Share解除／変更、Move、Rename、Trash、Restore、Purge、MISSING二段階／索引削除をSearch／Recentへ反映する。
  - [ ] Recent再Open、User分離、Folder／MISSING記録拒否、失効中非表示、再取得時過去時刻、Cascadeを確認する。
  - [ ] API／Worker／PostgreSQL再起動、同時Search／Share変更／Recent PUT後もDBと応答が収束する。
  - [ ] Nginx／API／PostgreSQL Logにq、File名、User名、物理Path、Tokenがない。

### 3.7 Android実機・LAN／ZeroTier・回帰・清掃

- [ ] Android実機Search user flowを完了する。
  - [ ] HomeからSearchへ移動し、名前、Category、日時、size、Owner／共有元、statusで検索する。
  - [ ] Folder結果を開き、File結果のOwner、Permission／Source、共有元、MISSING状態を確認する。
  - [ ] Viewer／Contributor／Editor／Managerで表示操作が権限表と一致し、ADMIN／未共有Userに結果が漏れない。
  - [ ] Search中のShare解除、Move、Trash、通信断、Token refresh後に古い結果を操作できない。
  - [ ] 長い語、1〜2文字、Unicode、特殊文字、空結果、複数Page、回転、狭い画面を確認する。
- [ ] Android実機Recent user flowを完了する。
  - [ ] 複数Fileを開いて新しい順を確認し、同じFile再Openで1件のまま先頭へ移動する。
  - [ ] User切替で履歴が分離され、Folder／一覧／検索結果表示だけでは履歴が増えない。
  - [ ] Share解除後に非表示、別経路維持、MISSING状態表示、Trash／Purge後非表示を確認する。
  - [ ] RecentからFileを開く際に最新権限が再取得され、失効後の変更操作が拒否される。
- [ ] LAN／ZeroTierと既存機能回帰を確認する。
  - [ ] 両経路で同じHTTPS Hostname、TLS、認証、Search／Recent契約が機能する。
  - [ ] Personal／Shared一覧、Upload、Download、Rename、Move、Trash、Restore、Purge、MISSING、中断再開Uploadが従来どおり動作する。
  - [ ] Storage未接続時にSearch metadataとFile openの状態が設計どおりで、OS Rootへ誤保存しない。
- [ ] E2E環境を安全に清掃する。
  - [ ] 限定識別子の試験User、File、Folder、Share、Recent、Session、性能seedだけを削除する。
  - [ ] 実User、実File、実Share、Backup、運用資格情報を削除しない。
  - [ ] Storage ID一致、全Service active、未完了FileOperation／Upload Session、孤立Share／Recentが0件である。
  - [ ] 手順、結果、検索条件、Permission、性能、失敗注入、所要時間、清掃結果を機密情報なしで`docs/testing/`へ記録する。

### 3.8 最終文書・Release・セルフレビュー・完了

- [ ] 全文書と実装を最終整合する。
  - [ ] 5つの正式文書、Steering、OpenAPI、Migration、Server、Android、Performance、運用・Test記録が一致する。
  - [ ] `feature-search`、Navigation、Server Search／Recent、Performance資材の実配置をRepository文書へ反映する。
  - [ ] Migration、Index、Extension、API／Android Rollout、Rollback、Recent保護を運用文書へ反映する。
  - [ ] E2E／性能記録に実User識別情報、File名、検索語、物理Path、Token、Credentialがない。
- [ ] Release Buildと最終検証を完了する。
  - [ ] `./scripts/ci/build-release.sh`でlinux-arm64 Serverと署名済み・非debuggable Android Releaseを生成する。
  - [ ] 最終Releaseを本番相当順序で配置し、Version、署名、Root CA、Hostnameを検証する。
  - [ ] 全必須CI、Server／Android Test、Migration、Performance、実機E2Eが最終HEADで成功する。
- [ ] 全体差分をセルフレビューする。
  - [ ] 認可前の候補、他User履歴、q、File名、物理PathをClient／Logへ漏らす経路がない。
  - [ ] HDD走査、N+1、無制限再帰、長期Permission cache、Client-only認可がない。
  - [ ] Search表示だけのRecent更新、Client時刻、重複履歴、TRASHED表示がない。
  - [ ] OCR、全文検索、候補、タグ、推薦、Admin横断検索、不要Package、将来用Schemaがない。
  - [ ] 生成物、実環境値、Credentialが差分にない。
- [ ] PR3を完了する。
  - [ ] 3.1〜3.8のPR3対象項目がすべて`[x]`である。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR3完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## 各Pull Request完了記録

各Pull Request作成後に`steering`スキルのモード3-Aを使用して追記する。対象Pull Request内のタスクがすべて完了するまで記録しない。

### PR1: 権限対応Search API・PostgreSQL Index・性能基盤

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・性能確認: 未実施
- 計画と実装の差分: 未完了
- 実装中に追加したタスクと理由: 未完了
- 技術的に不要になったタスク・理由・代替実装: 未完了
- 後続Pull Requestへの引継ぎ事項: 未完了

### PR2: 最近使用履歴・権限再評価API

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・性能確認: 未実施
- 計画と実装の差分: 未完了
- 実装中に追加したタスクと理由: 未完了
- 技術的に不要になったタスク・理由・代替実装: 未完了
- 後続Pull Requestへの引継ぎ事項: 未完了

### PR3: Android検索・最近使用UI・実機E2E

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・性能確認: 未実施
- 計画と実装の差分: 未完了
- 実装中に追加したタスクと理由: 未完了
- 技術的に不要になったタスク・理由・代替実装: 未完了
- 後続作業への引継ぎ事項: 未完了

---

## 全体振り返り

PR1〜PR3、本ファイルの全タスク、各Pull Request完了記録が完了した後にだけ、`steering`スキルのモード3-Bを使用して記録する。

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
