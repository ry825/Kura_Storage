# 検索・最近使用したファイル 設計書

## アーキテクチャ概要

既存のClean Architecture、FileEntry索引、共有認可Queryを拡張する。検索と最近使用一覧はPostgreSQL上の読み取りQueryで、対象選択、実効権限解決、Filter、並び順、Paginationまで完了させる。ApplicationまたはAndroidで他User項目を取得後に隠さない。

```text
Android feature-search
  ├─ Search screen ─────────────── GET /api/v1/search
  └─ Recent screen / file opened ─ GET/PUT /api/v1/recent-files
                    │
                    v
API contract -> Application service -> Query/Repository -> PostgreSQL
                                         ├─ file_entries + pg_trgm
                                         ├─ shares / share_members / ancestor CTE
                                         └─ recent_files

File/Folderを開く -> 既存 feature-files -> 最新GET /files/{id}
File表示成功 -> PUT recent-files/{fileId} -> Server時刻でupsert
```

HDDはFile本体と物理階層の正、PostgreSQLは検索索引・状態・共有・最近使用履歴の正とする。Search／Recent APIはHDDを走査しない。Storage未接続時も索引metadataは返せるが、Fileを開く操作は既存Storage状態と最新File APIの判定に従う。

## 主要設計決定

### 検索入力と一致規則

- `q`はtrim後1〜200 Unicode code pointとする。`q`を省略する場合は少なくとも1つのFilterを必須とする。
- 3文字以上は`lower(name) LIKE '%' || escaped_query || '%'`とGIN trigram Indexを使用する。
- 1〜2文字は全件containsを許可せず、`lower(name) LIKE escaped_query || '%'`のPrefix一致と`text_pattern_ops` B-tree Indexを使用する。
- `%`、`_`、`\`はliteralとしてescapeし、利用者が任意のLIKE patternを注入できないようにする。
- Unicode正規化はApplicationのNFCとPostgreSQL保存済みNameのlower比較を基準とし、accent folding、かな変換、曖昧語補正は行わない。

### 検索結果の順序

- `q`あり: 完全一致、Prefix一致、trigram similarity、`updated_at DESC`、`id ASC`の順。
- Filterのみ: `updated_at DESC`、`id ASC`の順。
- `page`は1以上、`pageSize`は1〜100。既存APIとの互換性を優先してoffset pageを使用し、同一データ状態では決定的な順序を保証する。Page取得中のRename、権限変更、削除ではRefreshを必要とし、Clientで旧Pageを成功状態として固定しない。

### 最近使用履歴の記録境界

- File詳細をユーザーへ表示できた後、Androidが`PUT /api/v1/recent-files/{fileId}`を呼ぶ。
- EndpointはJWTからUserを特定し、対象が`FILE`、`ACTIVE`、閲覧可能であることを確認してServer時刻でupsertする。
- Folder、`MISSING*`、`TRASHED`、未完了FileOperation、権限なしは記録しない。存在秘匿のため不正・権限なしは安定した404へ正規化する。
- 同じUser・Fileへの再送は同じ行の`opened_at`更新となるため、401 Refreshや通信結果不明後の再試行で重複しない。
- 最近使用一覧は現在の閲覧権限を毎要求で再評価する。失効中の履歴行はDBに保持するが返さず、権限再取得時に過去時刻の位置で再表示する。

## API契約

### `GET /api/v1/search`

Query parameter:

| 名前 | 型・制約 | 意味 |
| --- | --- | --- |
| `q` | optional string、trim後1〜200 | 名前検索。省略時はFilterが1つ以上必要 |
| `entryType` | `FILE`／`FOLDER` | Entry種別 |
| `fileCategory` | `IMAGE`／`VIDEO`／`AUDIO`／`DOCUMENT`／`ARCHIVE`／`OTHER` | `FILE`のMIME分類 |
| `status` | `ACTIVE`／`MISSING_CANDIDATE`／`MISSING` | 状態。`TRASHED`は指定不可 |
| `updatedFrom`／`updatedTo` | ISO 8601 UTC | 更新日時の包含範囲 |
| `minSize`／`maxSize` | 0以上の64-bit整数 | File sizeの包含範囲。Folderはsize Filter対象外 |
| `ownerUserId` | UUID | 所有者。閲覧可能結果内だけで評価 |
| `shareTargetId` | UUID | 現在の実効共有元。Owner結果には一致しない |
| `page`／`pageSize` | 1以上／1〜100 | Pagination |

Response:

```json
{
  "items": [
    {
      "id": "uuid",
      "entryType": "FILE",
      "name": "example.jpg",
      "mimeType": "image/jpeg",
      "fileCategory": "IMAGE",
      "size": 12345,
      "status": "ACTIVE",
      "updatedAt": "2026-08-24T00:00:00Z",
      "owner": { "id": "uuid", "displayName": "Family member" },
      "permission": "VIEWER",
      "permissionSource": "DIRECT",
      "shareTargetId": "uuid"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 1
}
```

`totalCount`は閲覧可能かつFilter一致した件数だけを返す。Owner名やShare名のFacet APIは追加せず、AndroidのOwner／共有元選択肢は本人と既存の受信共有一覧から構成する。

### `GET /api/v1/recent-files?page=1&pageSize=50`

- User本人の履歴だけを`openedAt DESC, fileId ASC`で返す。
- Search item相当のFile metadata、Owner、実効Permission／Source／Share Targetと`openedAt`を返す。
- 現在`TRASHED`、権限なし、未完了操作中、完全削除済みの項目は返さない。
- `MISSING_CANDIDATE`／`MISSING`は履歴行とともに返し、状態を表示する。

### `PUT /api/v1/recent-files/{fileId}`

- Request bodyなし。成功は`204 No Content`。
- User ID、Device ID、Owner、OpenedAtをClient入力に含めない。
- 同一User・Fileの同時要求は一意制約とupsertで1行へ収束する。

### Error code

| Code | HTTP | 用途 |
| --- | --- | --- |
| `INVALID_SEARCH_QUERY` | 400 | q欠落・長さ・Filterなし・escape不能 |
| `INVALID_SEARCH_FILTER` | 400 | enum、UUID、日時、size、範囲、Pagination不正 |
| `FILE_NOT_FOUND` | 404 | Recent記録対象なし、権限なし、Folder、非ACTIVEを存在秘匿 |
| 既存認証・Storage Error | 既存どおり | Token失効、DB／Storage状態、Request ID |

## コンポーネント設計

### 1. Search Application

**責務**:

- `SearchQuery`の正規化、範囲・組合せ検証。
- `FileCategory`の安定enumとMIME分類規則。
- Query repositoryの呼出しとAPI非依存Contract返却。

**実装の要点**:

- Domain/ApplicationはASP.NET Core、EF Core、Npgsqlに依存しない。
- Parameterized SQLだけを使用し、LIKE escapeも値parameterとして渡す。
- Unknown status、`TRASHED`、`fileCategory + FOLDER`、`min > max`をFail-fastで拒否する。

### 2. PostgreSQL Search Query

**責務**:

- Actor Userの所有・直接共有・祖先Folder共有を深度64以内で解決する。
- 最強Permissionと既存Tie-breakを各結果へ付与する。
- Name、MIME category、時刻、size、Owner、Share Target、status Filter、排序、count、PageをDBで処理する。

**実装の要点**:

- PostgreSQL固有SQLは`Infrastructure/Persistence/Queries/`へ配置する。
- `file_entries`、`shares`、`share_members`の既存Indexと新しいName Indexを使用する。
- `AsNoTracking`相当のprojectionを使い、Entity graph全体をmaterializeしない。
- Query plan TestでHDD I/O、N+1、無制限CTE、`SELECT *`がないことを保護する。

### 3. RecentFile Domain・Persistence

**責務**:

- User ID、File ID、OpenedAtの関係と更新を表現する。
- `(user_id, file_id)`一意、File/User削除Cascade、User単位の新しい順Indexを保証する。
- 記録時認可と一覧時認可を分離し、どちらも現在状態を評価する。

**実装の要点**:

- `opened_at`はServer clockだけで更新する。
- PostgreSQL `INSERT ... ON CONFLICT ... DO UPDATE`または同等の単一Statementで競合を処理する。
- 一覧はRecent行を起点に権限Queryへjoinし、別Userの履歴件数も返さない。

### 4. API・OpenAPI・Log保護

**責務**:

- Search／Recent Endpoint、Contract mapping、Error mapping、認証境界を提供する。
- Nginx/API Access Logからquery stringと検索語を除外する。

**実装の要点**:

- API EndpointからDbContextやSQLを直接呼ばない。
- OpenAPIに全Filter、enum、上限、Response、Error exampleを定義する。
- Nginxは`$request`や`$request_uri`ではなく、queryを含まない`$request_method $uri $server_protocol`を使用する専用safe log formatへ変更し、配置検証で確認する。
- Application Log、Metric label、Auditへq、File名、User名、物理Pathを含めない。

### 5. Android core-model／network／data

**責務**:

- Search filter/result/page、File category、Recent itemを表現する。
- Retrofit APIとDTO mapping、Authentication refresh、Pagingを提供する。

**実装の要点**:

- Unknown enum、不正UUID・日時、欠落Owner／Permissionを安全側のErrorへ変換する。
- Search世代IDまたはJob cancelで古い応答が新しい条件を上書きしない。
- Recent PUTは冪等として401 Refresh後に再送可能。Network結果不明時は成功を合成せずGET recentを再取得する。
- Pagingは重複IDをClientで隠して成功扱いにせず、ServerのPage契約逸脱をTestで検出する。

### 6. Android `feature-search`

**責務**:

- Search、Filter、結果一覧、最近使用一覧、Loading／Empty／Error／Paging状態を表示する。
- 既存File browser／detailへのNavigationを提供する。

**実装の要点**:

- `feature-search`から`feature-files`へ直接依存せず、`app`がIDと遷移意図を仲介する。
- Owner／共有元Filter候補は本人と受信Shareから構成し、Serverの横断User候補APIを追加しない。
- `MISSING*`、Unknown Permission、権限失効は既存Fail-closed capabilityを再利用する。
- 検索語をSavedState、Analytics、Log、Crash messageへ永続化しない。回転時は非機密Filterだけを必要最小限復元する。

## データモデル

### `recent_files`

| 列 | 型 | 制約 |
| --- | --- | --- |
| `user_id` | uuid | PK一部、FK users、ON DELETE CASCADE |
| `file_id` | uuid | PK一部、FK file_entries、ON DELETE CASCADE |
| `opened_at` | timestamptz | NOT NULL、Server UTC |

Index:

- Primary Key `(user_id, file_id)`。
- `ix_recent_files_user_opened_file (user_id, opened_at DESC, file_id)`。
- File単独Indexは複合PKで先頭でないため、Cascade／管理Queryの実測に応じて`file_id` Indexを追加し、Migration Testで確認する。

### FileEntry検索Index

- `CREATE EXTENSION IF NOT EXISTS pg_trgm`。既存環境でExtension権限を配置前に検証する。
- `GIN (lower(name) gin_trgm_ops)`。
- `B-tree (lower(name) text_pattern_ops, id)`を短いPrefix検索に使用する。
- 大TableへのIndex作成所要時間とLockをPi相当30万件で測定し、Production手順を確定する。Migration transactionと`CONCURRENTLY`の制約を独自判断せず、実測後に正式文書へ記録する。

## データフロー

### Search

```text
1. Androidが入力を検証し、現在世代のSearch requestを送る。
2. APIがJWT UserをSecurity Contextから取得し、SearchServiceへ渡す。
3. SearchServiceがq・Filter・Pageを正規化する。
4. PostgreSQL Queryが閲覧可能候補、実効権限、Filter、排序、Pageを一度に評価する。
5. APIが物理Pathを含まない結果を返す。
6. Androidは世代一致した応答だけを表示し、項目選択時は既存File APIで最新状態を再取得する。
```

### Recent記録・表示

```text
1. AndroidがFile詳細の表示成功を確認する。
2. PUT recent-files/{fileId}を送る。
3. ServerがFILE・ACTIVE・閲覧権限を再評価し、Server時刻でupsertする。
4. GET recent-filesはUser本人の履歴と現在の閲覧権限をDBでjoinする。
5. 権限なし・TRASHED・削除済みを除外し、MISSING状態は保持して返す。
6. Androidが履歴項目を選ぶと既存File APIで最新状態を再取得する。
```

## Migration・Rollout・Rollback

1. PR1で`pg_trgm`とFileEntry Name Indexを追加し、検索APIを配置する。
2. PR2で`recent_files` MigrationとRecent APIを配置する。
3. PR3でServer契約と同じversionの署名Android Releaseを配置する。
4. 各Migration前にPostgreSQLとStorage Rootの対応するBackup、Storage ID、Service状態を確認する。
5. Rollbackでは先にAndroidを互換versionへ戻し、API／Workerを同一Artifactで戻す。Recent履歴を保護したままSchemaを維持できる場合はApplication rollbackを優先する。
6. Schema rollbackが必要な場合、Recent履歴削除とName Index／Extension依存を事前集計し、無言削除しない。Down Migrationの挙動と復元手順をTest・運用文書へ記録する。

## エラーハンドリング戦略

- 入力不正は安定CodeとRequest IDを返し、SQL・内部enum・File名を含めない。
- DB timeout／Network結果不明ではAndroidが成功結果を合成せず、同じ条件のGETを再取得する。
- Search中のShare解除・Move・Trashは次要求で再評価する。結果選択後の404／403相当は安全な一覧へ戻してRefreshする。
- Recent PUTの対象なし・権限なし・非ACTIVE・Folderは404へ統一し、存在を推測させない。
- Unknown Permission／Source／Status／CategoryはAndroidで破壊的操作を有効化せず、更新案内またはErrorを表示する。

## テスト戦略

### Unit Test

- q正規化、長さ、LIKE escape、short-prefix分岐、Filter組合せ、日時・size境界。
- MIME category分類、Unknown、Folderとの不正組合せ。
- Owner／Direct／Inherited／複数経路／Tie-breakとSearch result mapping。
- Recent upsert、Server clock、User分離、非ACTIVE・Folder拒否。
- Android DTO mapping、Paging、世代競合、401 Refresh、結果不明後Refresh、Fail-closed capability。

### Integration・Contract Test

- PostgreSQL Extension、Index、Migration Up／Down、既存30万件相当データ保持。
- 個人、直接File share、Folder継承、複数祖先、直接+継承、Share解除、Move、Trash、Restore、Purge、MISSINGのSearch結果。
- Search全parameterのOpenAPI／HTTP contract、存在秘匿、Injection、Pagination。
- Recentの同時upsert、User分離、権限失効、再取得、MISSING保持、Cascade削除。
- Nginx／API Logにq、File名、query string、物理Pathがないこと。

### Android Test

- Search／Recent ViewModelのLoading、Empty、Success、Filter、Paging、Refresh、Error、古い応答破棄。
- ComposeでFilter、Owner／共有元、MISSING表示、結果遷移、権限別操作、回転、狭い画面を確認する。
- 接続実機で全対象Moduleの`connectedDebugAndroidTest --max-workers=1`を実行する。

### 性能・E2E

- 30万FileEntry、User 10名、直接／継承Shareを含む代表20検索を`EXPLAIN (ANALYZE, BUFFERS)`とk6で測定する。
- Raspberry PiのCPU、Memory、DB connection、p50／p95、Index size、Migration時間を記録する。
- LAN／ZeroTier、Owner／Viewer／Contributor／Editor／Manager／Admin、Android実機で検索・最近使用・失効・MISSING・回帰を確認する。
- E2E一時User、Share、Recent、File、Sessionを限定識別子で清掃し、実データを削除しない。

## 依存ライブラリ

- Server／Androidとも新しい外部Packageは原則追加しない。
- PostgreSQL標準Extension `pg_trgm`を使用する。
- 既存のEF Core、Npgsql、Retrofit、Coroutines、Compose、MockWebServer、Testcontainers、k6を再利用する。

## ディレクトリ構造

```text
server/src/KuraStorage.Application/
├── Search/
└── RecentFiles/

server/src/KuraStorage.Infrastructure/
├── Persistence/Queries/Search/
├── Persistence/Configurations/RecentFileConfiguration.cs
└── Persistence/Migrations/

server/src/KuraStorage.Api/
├── Contracts/Search/
├── Contracts/RecentFiles/
└── Endpoints/Search|RecentFiles/

apps/android/
├── core-model/.../SearchModels.kt
├── core-network/.../SearchApi.kt
├── core-data/.../SearchRepository.kt
└── feature-search/
    ├── src/main/.../SearchScreens.kt
    ├── src/main/.../SearchViewModels.kt
    ├── src/test/
    └── src/androidTest/

server/tests/
├── KuraStorage.Application.Tests/Search|RecentFiles/
└── KuraStorage.IntegrationTests/Search|RecentFiles/
```

既存Repositoryが小規模な平坦配置を採用している場合は、その規則に合わせて不要な空Directoryを作らない。正式な配置は実装時に`docs/repository-structure.md`へ同期する。

## 実装の順序

1. 正式文書とOpenAPIへSearch／Recent契約、Log保護、性能条件を反映する。
2. PR1でSearch contract、Migration、Index、Application、PostgreSQL Query、API、Server Testを実装する。
3. PR2でRecentFile domain、Migration、upsert／認可Query、API、Server Testを実装する。
4. PR3でAndroid core、`feature-search`、Navigation、Search／Recent UI、履歴記録を実装する。
5. PR3で30万件性能、Raspberry Pi、LAN／ZeroTier、Android実機、署名Release、回帰、清掃を完了する。

## セキュリティ考慮事項

- 認可はSQL段階で適用し、Client-side filteringを禁止する。
- ADMINも所有または明示共有されていないFileを検索できない。
- Query parameter、結果件数、Owner／Share filterから未共有データを推測させない。
- Parameterized SQL、LIKE escape、入力上限、Page上限、Rate LimitでInjectionと高負荷入力を抑制する。
- Search query、File名、User名、物理Path、Recent履歴をLog・Metric label・例外・監査詳細へ含めない。
- RecentのUser・OpenedAtはJWT／Server clockから導出し、Client指定を信用しない。
- Share解除・Permission変更を要求を超えてCacheせず、次要求で失効する。

## パフォーマンス考慮事項

- 30万件、10 User、Page size 100を上限とし、HDD走査と全件materializeを禁止する。
- trigram GINとshort-prefix B-treeをQuery形状ごとに検証する。
- 実効権限をPage各行の個別Queryで解決せず、CTE／Batch projectionで処理する。
- Count queryを含む総SQL回数、Buffers、temp file、sort、Index sizeを記録する。
- AndroidはPagingし、古いRequestをCancelし、結果全件を一括取得しない。

## 将来の拡張性

Search Application contractは将来のWebでも再利用できる。OCR・全文検索、tag、favorite、候補、rankingを追加する場合は別table／job／endpointとして追加し、今回のName検索とRecent履歴の正を変更しない。外部Search engineは30万件規模でPostgreSQL目標を満たせない実測とADRがある場合にだけ検討する。
