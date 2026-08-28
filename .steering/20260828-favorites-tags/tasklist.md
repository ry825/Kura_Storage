# お気に入り・タグ タスクリスト

## 対象要件

- `docs/product-requirements.md` 7.12.2「お気に入り」
  - UserごとにFileとFolderをお気に入り登録できる。
  - Home画面からお気に入りへアクセスできる。
- `docs/product-requirements.md` 7.12.3「タグ」
  - FileとFolderへTagを付与できる。
  - Tagで検索・絞り込みできる。
- 実装済みの権限対応Search API、PostgreSQL Query、Android `feature-search`を再利用する。

## タスク完全完了の原則

**このファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止してよい。**

### 必須ルール

- 全タスクを最終的に`[x]`にする。
- 「時間の都合」「実装が複雑」などを理由にタスクを後回しまたは省略しない。
- 選択したPull Request単位に未完了タスクを残したまま作業を終了しない。
- 後続Pull Requestのタスクは、先行Pull Request完了時点では`[ ]`のままでよい。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ更新する。

### Pull Request運用

- 各Pull Requestは原則として最新の`main`から短命Branchを作成する。
- 未Mergeの先行Pull Requestに依存する作業は、先行Pull Requestが`main`へMergeされ、必須CIが成功してから開始する。
- 実装、Test、文書、tasklist更新、Commit、Push、英語のPull Request作成までを同じPull Request単位で完了する。
- Pull RequestはMergeしない。作成後は`steering`スキルのモード3-Aで完了記録を追記し、Commit・Pushして停止する。
- 実装時はTDDのRed、Green、Refactor、Verifyを各変更単位で行う。

## スコープ境界

- [x] 実装対象をお気に入り登録・解除・一覧、Tag管理・付与・解除、Tag検索・絞り込み、Android導線に限定する。
- [x] OCR・全文検索、検索候補、保存済み検索、推薦・ranking、Smart Folder、Tag階層、Tag自動付与、Tag共有、色・アイコン、Web UIを追加しない。
- [x] 外部Search engineや別Search clusterを追加せず、既存PostgreSQL検索基盤を拡張する。
- [x] File名変更・MoveではFile IDとの関連を維持し、Trash、Purge、`MISSING`、共有解除、権限変更では承認済み仕様どおりに表示・関連を更新する。
- [x] Androidだけで非認可項目を隠さず、一覧・検索の閲覧可能範囲をServerのQuery段階で確定する。

---

## フェーズ0: 実装前の要求・設計確定

> 正式要件の機能概要を基に所有範囲、命名制約、状態別動作、API契約を具体化し、User承認まで完了した。

- [x] `.steering/20260828-favorites-tags/requirements.md`を`steering`スキルのモード1で作成し、Userの承認を得る。
  - [x] お気に入りとTagをUser個人の非公開整理情報とし、`VIEWER`を含む閲覧可能Userが自分の関連だけを操作できると確定する。
  - [x] Owner、直接共有、継承共有、権限失効、再取得時の可視性と操作可否を確定する。
  - [x] `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`、`TRASHED`、Purge後の一覧・関連保持規則を確定する。
  - [x] Tag名をtrim・NFC後1〜50 code point、control characterなし、User内case-insensitive一意と確定する。
  - [x] User当たり200 Tag、Entry当たりUserごとに20 Tag、お気に入りPage size 1〜100・既定50と確定する。
  - [x] Tag CRUD、Entryへの冪等な付与・解除、最大10 TagのAND検索を確定する。
  - [x] 30万件で通常2秒以内、Coverage、LAN／ZeroTier／署名Android実機の完了条件を確定する。
- [x] `.steering/20260828-favorites-tags/design.md`を`steering`スキルのモード1で作成し、Userの承認を得る。
  - [x] `FavoriteEntry`、`Tag`、`EntryTag`、DB制約、Index、Cascade、Migration Up／Downを設計する。
  - [x] `OrganizationService`、Repository、advisory lock後再認可、冪等性、Transaction境界を設計する。
  - [x] Favorites、Tags、Entry organization state、Tag付与・解除、Search `tagId`のREST／OpenAPI契約を設計する。
  - [x] 既存`SearchService`／`PostgreSqlSearchRepository`へTag ownership検証とAND条件を追加する方法を設計する。
  - [x] Android `core-*`、`feature-search`、`feature-files`、Navigation、Session分離を設計する。
  - [x] Test matrix、30万件性能測定、Migration、Rollback、実機E2E、限定データ清掃を設計する。
- [x] 承認済み`requirements.md`と`design.md`に合わせて本タスクリストを再確認する。
  - [x] API名、制約値、配置、確認コマンドを具体化する。
  - [x] 正式文書との矛盾がないことを確認し、正式文書の不足はPR1／PR2の更新対象へ含める。
  - [x] PR1とPR2の依存関係と完了条件が承認済み設計を過不足なく覆うことを確認する。

---

## フェーズ1 / PR1: お気に入り・タグ Server APIと検索基盤

### 1.1 開始条件と既存実装確認

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0の全項目が`[x]`で、`requirements.md`と`design.md`が承認済みである。
  - [x] Search／Recent Server実装を含む先行Pull Requestが`main`へMerge済みで、必須CIが成功している。（PR #26、Android／Config／Security／Server成功）
  - [x] `git status`と既存差分を確認し、今回の変更とUserの作業を混在させない。（未追跡の承認済みSteering 3文書だけを保全）
  - [x] 既存Search、Recent、Sharing、Trash／Purge、MISSING、Migration、OpenAPI Testの実装パターンを確認する。
  - [x] 最新`main`からPR1用Branchを作成する。（`feat/favorites-tags-server`）

### 1.2 Domain・永続化モデル

- [x] お気に入りとTag関連のDomain modelをTest firstで実装する。
  - [x] `FavoriteEntry`、`Tag`、`EntryTag`の空ID、UTC、不変条件を表す失敗Testを追加する。
  - [x] `TagNameNormalizer`でtrim、NFC、`Rune`数1〜50、control category拒否、NFC後`ToUpperInvariant`の`NameKey`を実装・Testする。（承認済み設計どおりApplicationへ一元配置）
  - [x] Favorite再登録では最初の`FavoritedAt`、Entry Tagでは最初の`AttachedAt`を維持する。
  - [x] Server時刻、認証User、UUIDをServer側で確定し、Client指定User、Owner、時刻、物理Pathを受け入れない。
- [x] EF Core mappingとMigrationを実装する。
  - [x] `favorite_entries`へ複合PK、User／FileEntry cascade FK、一覧Index、Entry Indexを追加する。
  - [x] `tags`へUser cascade FK、`name` check、`(user_id, name_key)` unique、一覧順Indexを追加する。
  - [x] `entry_tags`へ複合PK、Tag／FileEntry cascade FK、`(entry_id, tag_id)` Indexを追加する。
  - [x] `KuraStorageDbContext`へ3つの`DbSet`を追加し、既存Migration命名・snapshot patternに従う。
  - [x] Migration Up、Down、再Upとmodel snapshot一致をTestする。
  - [x] Rename／Moveで関連を維持し、Purge／索引削除／User削除／Tag削除で対象関連だけがcascadeすることをTestする。（ID参照を維持し、FileEntry／User／Tag FK cascadeをPostgreSQLで確認）
  - [x] 既存FileEntry、User、Share、Recent、Search Indexへ破壊的変更がないことを確認する。
  - [x] 大規模既存DBでのIndex作成時間、Lock、容量、Rollback制約を確認できるようにする。（新規3 Tableはbackfillなしで空TableへIndex作成。30万件seed・ANALYZE 52,961ms、運用監視・Rollback制約を文書化）

### 1.3 Application・Repository

- [x] お気に入りUse CaseをTest firstで実装する。
  - [x] `OrganizationContracts`、`OrganizationService`、`OrganizationAbstractions`を追加する。
  - [x] 登録はEntry／祖先を最大64段で取得し、既存mutation lockを昇順取得後に`ACTIVE`・未完了操作・現在権限を再評価する。
  - [x] 登録を`ON CONFLICT DO NOTHING`、解除をActor行だけの条件付きDELETEとし、どちらも冪等に処理する。
  - [x] お気に入り一覧を現在の閲覧権限・Entry状態で再評価し、`favoritedAt DESC, entryId ASC`、Page size 1〜100で返す。
  - [x] 共有解除・権限変更・Move・Trash・Restore・Purge・MISSING遷移を次要求へ反映する。
  - [x] 他Userのお気に入りの存在・件数・対象を推測できないFailureへ正規化する。
- [x] Tag管理・Entry関連Use CaseをTest firstで実装する。
  - [x] User organization lock内で最大200件、NameKey一意、NameKey／ID順の作成・一覧・名前変更・削除を実装する。
  - [x] 同じName／NameKeyへのrenameを冪等成功、別Tagとの重複を`TAG_NAME_CONFLICT`へ正規化する。
  - [x] File／Folderへの付与はEntry／祖先とUser lockを昇順取得後にTag ownership、`ACTIVE`、未完了操作、現在権限、最大20件を再確認する。
  - [x] 付与を`ON CONFLICT DO NOTHING`、解除をActor所有Tag関連だけの条件付きDELETEとして冪等に処理する。
  - [x] Owner、`VIEWER`、`CONTRIBUTOR`、`EDITOR`、`MANAGER`が自分のTagだけを操作でき、Adminに暗黙権限がないことをTestする。
  - [x] `MISSING_CANDIDATE`／`MISSING`で新規付与を拒否し、既存関連の表示・解除を許可する。
  - [x] 同時作成、上限到達、同時付与、二重解除、Tag削除対付与、Share失効対付与を安全に収束させる。
- [x] PostgreSQL Repositoryを実装する。
  - [x] `PostgreSqlOrganizationRepository`で一覧、状態、CRUD、付与・解除を固定回数の有界Queryとして実装する。
  - [x] Favoritesは既存Recentのpermission CTEを再利用し、SQL段階で認可・状態・Permission rank・Pageを確定する。
  - [x] `GetEntryOrganizationAsync`で`isFavorite`とActor所有の付与済みTag最大20件を返す。
  - [x] `ACTIVE`／`MISSING_CANDIDATE`／`MISSING`を取得可能とし、Trash／未完了／非認可／不存在を`FILE_NOT_FOUND`へ統一する。
  - [x] 別の権限表、Entry単位N+1、長期Permission cache、HDD走査を作らない。
  - [x] 競合、取消、DB Error後に一部関連や重複行が残らないことを統合Testする。

### 1.4 Tag対応Search

- [x] 既存Search契約へ承認済みTag filterを追加する。
  - [x] `SearchQuery`、`SearchFilter`、Android／OpenAPI契約へrepeated `tagId`を追加する。（PR1ではServer／OpenAPI契約、Android呼出しはPR2対象）
  - [x] 省略、1件、10件、11件、重複、不正UUIDを検証し、Tag指定をqなしSearchの有効Filterとして数える。
  - [x] 他UserTagと不存在Tagを区別せず`INVALID_SEARCH_FILTER`の`400`にする。
  - [x] 名前、Entry種別、Category、日時、size、Owner、共有元、状態とTag条件を組み合わせられるようにする。
  - [x] 複数Tagをすべて付与済みのEntryだけへ一致させるAND条件とする。
  - [x] Tag filter後もOwner、Direct、Inherited、複数経路のPermission／Source解決を維持する。
  - [x] `TRASHED`、Purge済み、未完了操作、未共有の他User Entryを結果・件数へ含めない。
  - [x] 既存の安定Paginationと決定的な並び順を維持する。
- [x] Tag検索のPostgreSQL QueryとIndexを実装・検証する。
  - [x] Tag指定時だけread-only同一snapshotでActor ownershipを検証し、`entry_tags`の`GROUP BY/HAVING`でAND条件を適用する。
  - [x] TagなしSearchへ不要なTransaction／joinを追加せず、既存Query pathを維持する。
  - [x] ApplicationやAndroidで候補を後Filterせず、SQL内で認可、状態、Tag、他Filterを適用する。
  - [x] 30万FileEntry条件で意図したIndexを使用し、既存の通常2秒以内目標を後退させない。（p50 554ms、p95／最大1,072ms、18 sample）
  - [x] Tag 1／10件、Tagのみ、名前＋Tag、共有＋Tag、MISSING＋Tag、Page後半を`EXPLAIN ANALYZE BUFFERS`で確認する。
  - [x] N+1、無制限再帰、全件Memory保持、不要なsequential scanがないことを確認する。

### 1.5 API・OpenAPI・Server Test

- [x] お気に入り・Tag endpointを実装する。
  - [x] `GET /api/v1/favorites`と`PUT／DELETE /api/v1/favorites/{entryId}`を実装する。
  - [x] `GET／POST /api/v1/tags`と`PATCH／DELETE /api/v1/tags/{tagId}`を実装する。
  - [x] `GET /api/v1/files/{entryId}/organization`を実装する。
  - [x] `PUT／DELETE /api/v1/files/{entryId}/tags/{tagId}`を実装する。
  - [x] bodyなしendpointのnon-empty bodyを`INVALID_ORGANIZATION_REQUEST`で拒否する。
  - [x] `INVALID_FAVORITES_REQUEST`、`TAG_LIMIT_EXCEEDED`、`ENTRY_TAG_LIMIT_EXCEEDED`、`TAG_NOT_FOUND`、`TAG_NAME_CONFLICT`をHTTP statusへ対応させる。
  - [x] 保護APIとして既存認証、Device／Session状態、Rate Limit、Request ID、共通Error形式を適用する。
  - [x] 正常、冪等再送、Validation、認証なし、権限なし、状態不正、競合、通信結果不明相当を統合Testする。
  - [x] Request body／queryにClient指定User、Owner、物理Path、時刻を追加しない。
  - [x] Search query、Tag名、File名、User名、物理Path、Tokenを通常Log、Metric label、例外へ残さない。
- [x] `contracts/openapi/kurastorage-api.yaml`を更新する。
  - [x] `FavoriteItem／Page`、`TagItem`、`EntryOrganizationState`、Tag request／error schemaを追加する。
  - [x] `tagId`を`style: form`、`explode: true`、1〜10件、uniqueのarrayとして追加する。
  - [x] endpoint、Pagination、bodyなし契約、上限、冪等性を実装と一致させる。
  - [x] OpenAPI Contract Testと未知／不正responseの境界Testを追加する。
- [x] Server自動Testと品質基準を完了する。
  - [x] Domain Test、Application Test、PostgreSQL Integration Test、API Integration Testを追加する。
  - [x] Owner、各共有Permission、直接／継承／複数経路、未共有User、Adminの認可境界をTestする。
  - [x] Rename、Move、Share変更、Trash、Restore、Purge、MISSING二段階、索引削除との整合性をTestする。
  - [x] Domain／Application全体Line Coverage 80%以上、今回の認可・Validation境界95%以上を満たす。（全体85.86%、境界95.65%）
  - [x] `./scripts/ci/verify-server.sh`、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`git diff --check`が成功する。

### 1.6 文書・PR1完了

- [x] PR1に必要な文書を実装と同じ変更で更新する。
  - [x] `docs/functional-design.md`へお気に入り・TagのServer契約、状態、認可、検索動作を追加する。
  - [x] `docs/architecture-design.md`へDomain、Application、DB、認可、性能、Security境界を追加する。
  - [x] `docs/repository-structure.md`へ実配置を反映する。
  - [x] `docs/development-guidelines.md`へ新規の恒久的な実装規約がある場合だけ追記する。（既存のSecurity、TDD、Migration、Query規約で充足するため追記不要）
  - [x] Migration適用順、Backup、Lock、Rollback、データ保持を運用文書へ反映する。
- [ ] PR1を完了する。
  - [x] フェーズ1の全項目が`[x]`であることを確認する。
  - [x] 差分にAndroid UI、Web UI、将来用Schema、不要Package、実環境値、Credentialがないことをセルフレビューする。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR1完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をUserへ報告して停止する。

---

## フェーズ2 / PR2: Androidお気に入り・タグUIと実機E2E

### 2.1 開始条件とAndroid既存実装確認

- [ ] PR2の開始条件を満たす。
  - [ ] PR1とAndroid Search／Recentの先行Pull Requestが`main`へMerge済みで、必須CIが成功している。
  - [ ] `git status`と既存差分を確認し、最新`main`からPR2用Branchを作成する。
  - [ ] `feature-search`、Home、File／Folder詳細、一覧action、Navigation、Session単位Repositoryの既存patternを確認する。

### 2.2 Android model・network・data

- [ ] `core-model`へお気に入り・Tag modelを追加する。
  - [ ] `OrganizationModels.kt`へ`TagItem`、`FavoriteItem／Page`、`EntryOrganizationState`、Validationを追加する。
  - [ ] Favorite metadataは既存`SearchResultItem`を再利用し、File／Folder、状態、Permission／Source、共有元を重複定義しない。
  - [ ] Tag名1〜50 code point、control拒否、Tag 200件、Entry Tag 20件、Search Tag 10件の境界をTestする。
  - [ ] 未知enum、不正response、欠落必須項目をfail-closedで扱うTestを追加する。
- [ ] `core-network`へAPI contractを追加する。
  - [ ] DTO、Retrofit endpoint、`OrganizationApi`を追加し、設計済み10 endpointをOpenAPIどおり実装する。
  - [ ] Search requestへ`@Query("tagId") List<String>`のrepeated parameterを追加し、Tag名をURLへ含めない。
  - [ ] URL encoding、Pagination、201、204、400、401 Refresh、404、409、429、5xx、取消をTestする。
  - [ ] 通信結果不明時に登録状態を成功と推測せず、Server再取得へ収束させる。
- [ ] `core-data`へRepositoryを追加する。
  - [ ] `OrganizationRepository.kt`へFavorite Pager、Tag CRUD、Entry organization state、付与・解除を追加する。
  - [ ] strict DTO mappingでUUID、UTC、Page、重複項目、Tag件数、Metadataを検証する。
  - [ ] PUT／DELETEのNetwork結果不明時はorganization stateまたはFavorites一覧を再取得する。
  - [ ] User／Session／接続先をまたいでお気に入り、Tag、検索条件、処理中状態を再利用しない。
  - [ ] 連打、画面再構成、401再送で二重行・逆転・古い応答による上書きを起こさない。
  - [ ] API Errorを既存共通Error modelへ変換し、Tag名、File名、ID、TokenをLogへ残さない。

### 2.3 Androidお気に入りUI

- [ ] Homeにお気に入り入口を追加する。
  - [ ] `AppDestination.FAVORITES`を追加し、`MainActivity`と`ServiceContainer`へNavigation／DIを接続する。
  - [ ] 既存Homeの情報階層、接続状態、検索・最近使用導線を崩さず表示する。
  - [ ] 選択時にUser本人のお気に入り一覧へ遷移する。
- [ ] お気に入り一覧を実装する。
  - [ ] `OrganizationViewModels.kt`／`OrganizationScreens.kt`へFavorites state、Pager、Compose画面を追加する。
  - [ ] Loading、Empty、Success、Pagination、Refresh、認証更新、通信Error、権限失効を表示する。
  - [ ] File／Folder、Owner、Permission／Source、共有元、更新日時、`MISSING`状態を既存表示modelから描画する。
  - [ ] 項目選択時はIDだけを既存Navigationへ渡し、最新詳細・権限を再取得してから開く。
- [ ] お気に入り登録・解除操作を既存File／Folder UIへ追加する。
  - [ ] `feature-files`はApp callbackへEntry IDだけを返し、`core-data`や`feature-search`へ直接依存しない。
  - [ ] Entry actionから`GET /files/{entryId}/organization`を取得し、登録・解除できる。
  - [ ] 処理中の二重操作を抑止し、成功後はServer結果を反映する。
  - [ ] 失敗・結果不明・権限失効時はローカル成功を合成せず、再取得可能な表示にする。

### 2.4 Android Tag UI・Search統合

- [ ] Tag管理UIを実装する。
  - [ ] `AppDestination.TAGS`とTag management ViewModel／Compose画面を追加する。
  - [ ] NameKey／ID順の一覧、作成、名前変更、削除確認Dialogを実装する。
  - [ ] 正規化、長さ、重複、上限の入力ErrorをServer契約と一致させる。
  - [ ] Loading、Empty、Success、処理中、Validation、競合、通信Errorを表示する。
- [ ] File／FolderへのTag付与・解除UIを実装する。
  - [ ] Entry organization stateの現在Tag最大20件と、本人Tag候補最大200件を表示するselectorを実装する。
  - [ ] `ACTIVE`かつ既知Permissionでは付与・解除、`MISSING_CANDIDATE`／`MISSING`では解除だけを有効にする。
  - [ ] 二重送信を抑止し、失敗・結果不明時はServerから再取得する。
  - [ ] `MISSING`、Trash、権限失効などの操作不可状態をfail-closedで表示する。
- [ ] 既存Search画面へTag filterを追加する。
  - [ ] `SearchInput`／`ValidatedSearchInput`へ重複しないTag ID最大10件を追加する。
  - [ ] 本人Tagの複数選択UIとTag管理導線を追加する。
  - [ ] 名前、種類、日時、size、Owner／共有元、状態と組み合わせて検索できる。
  - [ ] Filter変更時にPageと古い結果を破棄し、古い要求が新しい結果を上書きしない。
  - [ ] Search結果選択時に最新詳細・権限を再取得し、Clientの古いお気に入り／Tag状態だけで操作しない。

### 2.5 Android自動Test・標準検証

- [ ] model／network／repository Testを完了する。
  - [ ] 全Entry状態、Permission／Source、Pagination、Tag 0／1／10／11件、重複、401 Refresh、結果不明、未知responseをTestする。
  - [ ] User切替、Logout、接続先変更、連打、取消、古い応答破棄をTestする。
- [ ] ViewModel／Compose UI Testを完了する。
  - [ ] Home導線、お気に入り一覧のLoading／Empty／Success／Paging／Error／遷移をTestする。
  - [ ] お気に入り登録・解除、Tag CRUD・付与・解除、Validation、上限、競合、権限失効をTestする。
  - [ ] Tag単独と既存Filterとの組合せ検索、Filter変更、Refresh、古い応答をTestする。
  - [ ] 回転、狭い画面、Scroll、Keyboard、Dialog表示でも主要操作が可能であることをTestする。
- [ ] Android標準検証を完了する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] 接続Android実機で対象Moduleの`connectedDebugAndroidTest --max-workers=1`相当が成功する。
  - [ ] `./scripts/ci/verify-server.sh`、`./scripts/ci/verify-config.sh`、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`git diff --check`が成功する。

### 2.6 Raspberry Pi・Android実機E2E

- [ ] 本番相当環境の事前保護とRolloutを完了する。
  - [ ] PostgreSQLとStorage Rootの対応Backup、復元可能性、Storage ID、Service状態を確認する。
  - [ ] Migration、API、署名Androidの適用順とRollback手順を確認する。
  - [ ] E2E User、File、Folder、Share、Tag、お気に入りを限定識別子で作成し、実データと分離する。
- [ ] お気に入りE2Eを完了する。
  - [ ] UserごとのFile／Folder登録・解除、Home入口、一覧順、Pagination、User分離を確認する。
  - [ ] Rename、Move、Share解除／再取得、Permission変更、Trash、Restore、Purge、MISSING遷移を確認する。
  - [ ] 古い一覧から項目を開く場合も最新権限が適用されることを確認する。
- [ ] Tag E2Eを完了する。
  - [ ] Tag管理、File／Folderへの付与・解除、重複・境界入力、同時操作を確認する。
  - [ ] Tag単独および名前、種類、日時、size、Owner／共有元、状態との組合せ検索を確認する。
  - [ ] 未共有User、権限失効User、AdminへTag名、対象、件数が漏れないことを確認する。
- [ ] 性能・経路・回帰を確認する。
  - [ ] 30万FileEntry、10 User、User当たり200 Tag、Entry当たり20 Tag条件で通常2秒以内を満たす。
  - [ ] Tag 1／10件、Tagのみ、名前＋Tag、共有＋Tag、MISSING＋Tag、Page後半で意図したIndexを使用する。
  - [ ] LANとZeroTierで同じHTTPS Hostname、TLS、認証、API契約、Android操作が機能する。
  - [ ] Personal／Shared／Search／Recent、Upload、Download、Rename、Move、Trash、Restore、Purge、MISSINGが従来どおり動作する。
  - [ ] Nginx、API、Android、PostgreSQL LogにTag名、検索語、File名、User名、物理Path、Tokenがない。
- [ ] E2E環境を安全に清掃・記録する。
  - [ ] 限定識別子の試験User、File、Folder、Share、Tag、お気に入りだけを削除する。
  - [ ] 実User、実File、実Share、Backup、運用資格情報を削除しない。
  - [ ] 孤立Tag関連・お気に入り、未完了FileOperation、active Upload Sessionが0件で、全ServiceとStorageが正常である。
  - [ ] 手順、結果、性能、失敗注入、清掃結果を機密情報なしで`docs/testing/`へ記録する。

### 2.7 最終文書・Release・PR2完了

- [ ] 正式文書と実装を最終整合する。
  - [ ] 5つの正式文書、Steering、OpenAPI、Migration、Server、Android、運用・Test記録が一致する。
  - [ ] `docs/functional-design.md`のHome主要カード、Navigation、画面仕様、API、Test戦略、実装順序を更新する。
  - [ ] `docs/repository-structure.md`へAndroidの実配置を反映する。
  - [ ] 実装により`docs/product-requirements.md`の要件変更が必要になった場合は同じPull Requestで更新する。
- [ ] Release Buildと最終検証を完了する。
  - [ ] `./scripts/ci/build-release.sh`でlinux-arm64 Serverと署名済み・非debuggable Android Releaseを生成する。
  - [ ] Version、署名、Root CA、Hostname、Migration適用状態を確認する。
  - [ ] 全必須CI、Server／Android Test、Migration、性能、実機E2Eが最終HEADで成功する。
- [ ] 全体差分をセルフレビューする。
  - [ ] Client-only認可、N+1、HDD走査、無制限Query、長期Permission cache、他User情報漏えいがない。
  - [ ] 古いお気に入り／Tag状態による操作許可、通信結果不明時の成功合成、Session間状態漏えいがない。
  - [ ] OCR、全文検索、推薦、Tag階層、自動Tag、Web UI、不要Package、将来用Schemaがない。
  - [ ] 生成物、実環境値、Credential、機密情報を含むTest記録が差分にない。
- [ ] PR2を完了する。
  - [ ] フェーズ2の全項目が`[x]`であることを確認する。
  - [ ] Commit、Push、英語のPull Request作成、必須CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR2完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をUserへ報告して停止する。

---

## 各Pull Request完了記録

各Pull Request作成後に`steering`スキルのモード3-Aを使用して追記する。対象Pull Request内のタスクがすべて完了するまで記録しない。

### PR1: お気に入り・タグ Server APIと検索基盤

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・性能確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク・理由・代替実装: 未記録
- 後続Pull Requestへの引継ぎ事項: 未記録

### PR2: Androidお気に入り・タグUIと実機E2E

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・性能確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク・理由・代替実装: 未記録
- 後続作業への引継ぎ事項: 未記録

---

## 全体振り返り

PR1、PR2、本ファイルの全タスク、各Pull Request完了記録が完了した後にだけ、`steering`スキルのモード3-Bを使用して記録する。

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
