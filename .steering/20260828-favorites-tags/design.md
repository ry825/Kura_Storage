# お気に入り・タグ 設計書

## アーキテクチャ概要

既存のClean Architecture、FileEntry ID、共有認可Query、Search API、Android `feature-search`を拡張する。お気に入りとTagはUser個人の整理情報としてFileEntryから分離し、PostgreSQLを正とする。Serverは認証UserをJWTから取得し、一覧・検索・状態取得ではSQL段階で現在の閲覧権限を解決する。

```text
Android
  app Home / Navigation
      ├─ feature-search: Favorites / Tag management / Search tag filter
      └─ feature-files: Entry organization action
                    │
                    v
core-model <- core-data <- core-network
                    │ HTTPS
                    v
KuraStorage.Api endpoint
                    │
                    v
KuraStorage.Application/Organization + existing SearchService
                    │
                    v
KuraStorage.Infrastructure/Persistence/Queries
      ├─ existing permission-aware Search CTE
      ├─ favorite_entries
      ├─ tags
      └─ entry_tags
```

File内容、物理Path、Folder階層、共有設定は変更しない。Rename／MoveはFileEntry IDを維持するため関連を更新せず、Purge、`MISSING`索引削除、User削除はforeign key cascadeで関連を削除する。共有失効、Trash、`MISSING`は行を先に消さず、Query時の現在権限と状態で表示可否を決める。

## 設計原則

- お気に入りとTagはUser本人だけが参照・変更できる非公開Metadataとする。
- Client指定のUser ID、Owner ID、物理Path、時刻を契約へ含めない。
- Entryへの付与は`VIEWER`を含む閲覧可能Userに許可するが、自分の整理情報以外を変更できない。
- 登録系mutationは現在の`ACTIVE`状態と権限をlock後に再確認する。
- 解除系mutationはUser本人の関連だけを条件付き削除し、対象Entryの存在や他User情報を公開しない。
- 一覧とSearchはApplication／Androidで候補を後Filterせず、PostgreSQL内で認可・状態・Tag条件を確定する。
- 既存Search result metadata、Permission ranking、Pagination、Android capability導出を再利用する。
- 新しいServer Project、Android Gradle module、外部Search engine、background Worker、依存Packageを追加しない。

## コンポーネント設計

### 1. Domain model

#### `FavoriteEntry`

```text
FavoriteEntry
  UserId: Guid
  EntryId: Guid
  FavoritedAt: DateTimeOffset (UTC)
```

- `UserId`、`EntryId`は空を許可しない。
- `FavoritedAt`はUTCだけを許可する。
- 同じUser・Entryへの再登録では日時を更新しない。最初に登録した順序を維持する。

#### `Tag`

```text
Tag
  Id: Guid
  UserId: Guid
  Name: string
  NameKey: string
  CreatedAt: DateTimeOffset (UTC)
  UpdatedAt: DateTimeOffset (UTC)
```

- `Id`はServerで生成するUUIDとする。
- `Name`はtrim・NFC正規化済みの表示値を保持する。
- `NameKey`は`Name.ToUpperInvariant()`を再度NFC正規化した比較専用値とする。Clientから受け取らない。
- `TagNameNormalizer`をApplicationに1つだけ置き、作成・名前変更・Testで共有する。
- Unicode code point数は`Rune`列挙で1〜50を検証し、Unicode control categoryを拒否する。
- 同じ`NameKey`への名前変更は表示上の大文字小文字変更を含め、正規化後`Name`が同じなら副作用なし、異なる場合は表示名と`UpdatedAt`を更新する。

#### `EntryTag`

```text
EntryTag
  TagId: Guid
  EntryId: Guid
  AttachedAt: DateTimeOffset (UTC)
```

- Userは`Tag.UserId`から導出し、関連Tableへ重複保存しない。
- `AttachedAt`は監査表示には使用せず、Server UTCで作成する。
- 同じTag・Entryは1件だけ保持する。

Domain entityはEF Core、Npgsql、ASP.NET Core、Androidへ依存しない。認可、件数上限、DB競合はApplication／Infrastructure境界で処理する。

### 2. PostgreSQL schema

#### `favorite_entries`

| Column | Type | Constraint |
| --- | --- | --- |
| `user_id` | `uuid` | PK part、FK `users(id)` ON DELETE CASCADE |
| `entry_id` | `uuid` | PK part、FK `file_entries(id)` ON DELETE CASCADE |
| `favorited_at` | `timestamptz` | NOT NULL |

Index:

- Primary key `(user_id, entry_id)`で冪等性を保証する。
- `(user_id, favorited_at DESC, entry_id ASC)`で一覧のFilter・sort・pageを支える。
- `(entry_id)`でFileEntry削除と整合性確認を支える。

#### `tags`

| Column | Type | Constraint |
| --- | --- | --- |
| `id` | `uuid` | PK |
| `user_id` | `uuid` | FK `users(id)` ON DELETE CASCADE、NOT NULL |
| `name` | `text` | NOT NULL |
| `name_key` | `text` | NOT NULL |
| `created_at` | `timestamptz` | NOT NULL |
| `updated_at` | `timestamptz` | NOT NULL |

Constraint／Index:

- `char_length(name) BETWEEN 1 AND 50`、`name = btrim(name)`をcheck constraintにする。
- `(user_id, name_key)`をuniqueとし、User内case-insensitive重複をDBでも防ぐ。
- `(user_id, name_key, id)`でTag一覧順を支える。unique indexで同じprefixを満たせる場合は重複Indexを作らない。

#### `entry_tags`

| Column | Type | Constraint |
| --- | --- | --- |
| `tag_id` | `uuid` | PK part、FK `tags(id)` ON DELETE CASCADE |
| `entry_id` | `uuid` | PK part、FK `file_entries(id)` ON DELETE CASCADE |
| `attached_at` | `timestamptz` | NOT NULL |

Index:

- Primary key `(tag_id, entry_id)`で冪等性とTag検索を支える。
- `(entry_id, tag_id)`でEntryの付与済みTag取得、件数制約、Cascade確認を支える。

Migrationは3 Table、constraint、Index、foreign keyを同時に追加する。既存行のbackfillは不要である。Up、Down、再Up、model snapshot差分なしをPostgreSQL Integration Testで確認する。本番適用はPostgreSQL／Storage Backup後に明示実行し、API／Androidより先に適用する。

### 3. Application contract

`KuraStorage.Application/Organization/`へ次を配置する。

```text
FavoriteItem(Search metadata + FavoritedAt)
FavoritePage(Items, Page, PageSize, TotalCount)
TagItem(Id, Name)
EntryOrganizationState(IsFavorite, Tags)
CreateTagCommand(Name)
RenameTagCommand(TagId, Name)
OrganizationResult<T>
OrganizationFailure(Code, Kind)
```

`OrganizationService`は入力検証、Tag名正規化、Error変換、clock使用を担当する。SQL、DbContext、HTTP statusは持たない。

Repository interfaceは`KuraStorage.Application/Abstractions/OrganizationAbstractions.cs`へ置き、次の操作を定義する。

```text
TryAddFavoriteAuthorizedAsync(userId, entryId, now)
RemoveFavoriteAsync(userId, entryId)
ListFavoritesAsync(userId, page, pageSize)
ListTagsAsync(userId)
TryCreateTagAsync(userId, normalizedName, nameKey, now)
TryRenameTagAsync(userId, tagId, normalizedName, nameKey, now)
DeleteTagAsync(userId, tagId)
GetEntryOrganizationAsync(userId, entryId)
TryAttachTagAuthorizedAsync(userId, entryId, tagId, now)
DetachTagAsync(userId, entryId, tagId)
```

競合、上限、NotFoundをboolだけで潰さず、Repository内部結果を`Created`、`NoChange`、`NotFound`、`Conflict`、`UserLimitExceeded`、`EntryLimitExceeded`へ分類する。Applicationが公開Error codeへ変換する。

### 4. Mutationと競合制御

#### お気に入り登録

1. User ID、Entry IDを検証する。
2. DB Transactionを開始する。
3. Entryと祖先Folderを最大64段で取得し、Entry／祖先のadvisory lock keyを昇順に取得する。
4. lock後に階層を再読込し、scopeが変化していないことを確認する。
5. Userが`ACTIVE`、Entryが`ACTIVE`、未完了FileOperationがなく、現在閲覧可能であることを既存共有規則で確認する。
6. `INSERT ... ON CONFLICT (user_id, entry_id) DO NOTHING`を実行する。
7. Commitし、初回・再送とも`204`を返す。

Recent記録と同じlock後再認可patternを共通SQL helperまたは共通CTEとして再利用する。共有解除やMoveとの競合で、失効後に新規登録が確定しないようにする。

お気に入り解除は`DELETE FROM favorite_entries WHERE user_id = @actor AND entry_id = @entry`だけを実行し、存在しない場合も`204`とする。Entryや他User関連を照会しないため、失効後のcleanupと存在秘匿を両立する。

#### Tag作成・名前変更・削除

- User IDから専用namespace付きadvisory lock keyを導出し、同じUserのTag件数と名前一意性を直列化する。
- 作成は既存件数を確認し、200件未満の場合だけinsertする。unique constraint違反は`TAG_NAME_CONFLICT`へ正規化する。
- 名前変更はUser lock取得後に`user_id`と`tag_id`で対象を限定する。同じName／NameKeyは冪等成功、別TagとのNameKey競合は`409`とする。
- 削除はUser lock取得後、本人Tagだけを削除する。`entry_tags`はcascadeし、対象なしは他UserTagと区別せず`TAG_NOT_FOUND`とする。

#### Tag付与・解除

- 付与はEntry／祖先lockとUser organization lockを数値昇順で取得し、lock orderを全mutationで統一する。
- lock後にUser、Tag ownership、Entry `ACTIVE`、未完了操作、現在権限を再確認する。
- 既存関連があれば上限到達時でも冪等`204`とする。
- 関連がなければ、Actor UserのTagに限定してEntryの付与数を数え、20件未満の場合だけinsertする。
- primary keyとTransactionで同時付与を1行へ収束させる。
- 解除はActor所有Tagとの関連だけを単一DELETEで消し、関連なしも`204`とする。Entryの失効・`MISSING`後にもcleanupできる。

advisory lock keyは既存`ToAdvisoryLockKey`と衝突しない機能namespaceを含める。hash衝突は安全側の不要な直列化にだけ影響し、認可の根拠には使用しない。

### 5. Read Queryと認可

#### お気に入り一覧

`PostgreSqlOrganizationRepository`は既存Recent Queryのpermission CTEを再利用し、起点を`recent_files`ではなくActorの`favorite_entries`にする。

```text
actor favorites
  -> eligible FileEntry state / unfinished operation exclusion
  -> owner + direct share + ancestor share candidates
  -> strongest permission / direct / nearest ancestor ranking
  -> permission_rank = 1
  -> ORDER BY favorited_at DESC, entry_id ASC
  -> OFFSET / LIMIT + count
```

- `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`だけを含める。
- User本人のfavoriteを先に絞り、他User行をcandidateへ入れない。
- Page queryが0件の場合は同じCTEのcount queryを固定1回追加し、offset後の正しいTotalCountを返す。
- Page内Entryごとの認可Queryを行わない。

#### Entry organization state

`GetEntryOrganizationAsync`はEntry 1件について現在権限を既存規則で解決し、次を返す。

```json
{
  "isFavorite": true,
  "tags": [
    { "id": "uuid", "name": "Work" }
  ]
}
```

- `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`を取得可能とする。
- `TRASHED`、未完了操作、非認可、存在なしはすべて`FILE_NOT_FOUND`へ正規化する。
- TagはActor所有かつEntryへ付与されたものだけをNameKey／ID順で最大20件返す。
- お気に入りとTag候補全件をAndroidで突き合わせない。

### 6. Search拡張

`SearchQuery`と`SearchFilter`へ`IReadOnlyList<Guid> TagIds`を追加する。HTTP queryは同名parameterの繰返しを使用する。

```text
GET /api/v1/search?q=report&tagId={uuid1}&tagId={uuid2}&page=1&pageSize=50
```

OpenAPIは`tagId`をarray、`style: form`、`explode: true`、`minItems: 1`、`maxItems: 10`、`uniqueItems: true`として記載する。Android Retrofitは`@Query("tagId") List<String>`で送信する。Tag名をURLへ含めない。

Application validation:

- 0件はTag filterなしとして扱う。
- 1〜10件、非empty UUID、重複なしを要求する。
- Tag指定は既存の「qなしの場合はFilterを1つ以上」のFilterとして数える。
- parse失敗、重複、上限超過は`INVALID_SEARCH_FILTER`とする。

RepositoryはTag指定時だけread-only Transactionを開始し、同一snapshot内で次を行う。

1. `tags`を`user_id = actor`かつ`id = ANY(@tag_ids)`で数え、要求数と一致しない場合はinvalid filterを返す。
2. 既存Search CTEのFilterへ次のsemi-joinを追加する。

```sql
entry.id IN (
  SELECT entry_tag.entry_id
  FROM entry_tags AS entry_tag
  WHERE entry_tag.tag_id = ANY(@tag_ids)
  GROUP BY entry_tag.entry_id
  HAVING count(DISTINCT entry_tag.tag_id) = cardinality(@tag_ids)
)
```

3. 既存Permission ranking、結果metadata、sort、offset page、countを変更せず実行する。

Tag ownership検証とSearchの間にTagが削除されても同一snapshotで契約を一貫させる。TagなしSearchは現在のQuery pathを維持し、不要なTransaction／joinを追加しない。

### 7. HTTP API

すべて`/api/v1`配下の既定認証必須endpointとし、JWTのUserだけをActorに使用する。

| Method | Path | Request | Success |
| --- | --- | --- | --- |
| `GET` | `/favorites?page=1&pageSize=50` | Query | `200 FavoritePage` |
| `PUT` | `/favorites/{entryId}` | bodyなし | `204` |
| `DELETE` | `/favorites/{entryId}` | bodyなし | `204` |
| `GET` | `/tags` | なし | `200 TagItem[]`、最大200件 |
| `POST` | `/tags` | `{ "name": "Work" }` | `201 TagItem` |
| `PATCH` | `/tags/{tagId}` | `{ "name": "Archive" }` | `200 TagItem` |
| `DELETE` | `/tags/{tagId}` | bodyなし | `204` |
| `GET` | `/files/{entryId}/organization` | なし | `200 EntryOrganizationState` |
| `PUT` | `/files/{entryId}/tags/{tagId}` | bodyなし | `204` |
| `DELETE` | `/files/{entryId}/tags/{tagId}` | bodyなし | `204` |
| `GET` | `/search?...&tagId={id}` | repeated Query | `200 SearchPage` |

Bodyなしendpointはnon-empty bodyを`INVALID_ORGANIZATION_REQUEST`の`400`で拒否し、Client指定時刻や状態を黙って無視しない。Tag create／renameは`Content-Type: application/json`、単一`name`を要求する。

#### Error code

| Code | HTTP | 用途 |
| --- | --- | --- |
| `INVALID_FAVORITES_REQUEST` | 400 | 不正Page／Page size |
| `INVALID_ORGANIZATION_REQUEST` | 400 | 不正body、ID、Tag名 |
| `TAG_LIMIT_EXCEEDED` | 400 | UserのTagが200件 |
| `ENTRY_TAG_LIMIT_EXCEEDED` | 400 | User・EntryのTagが20件 |
| `INVALID_SEARCH_FILTER` | 400 | Tag filterの形式、所有、存在、重複、上限 |
| `FILE_NOT_FOUND` | 404 | Entryなし、非認可、登録不可状態 |
| `TAG_NOT_FOUND` | 404 | 本人Tagなし。他UserTagと区別しない |
| `TAG_NAME_CONFLICT` | 409 | User内NameKey重複 |

認証、Device失効、Rate Limit、Storage／DB障害、Request ID、共通Error envelopeは既存middlewareと規則を使用する。DB一時障害は成功へ変換せず既存`5xx`方針に従う。

### 8. Android model・data flow

#### 配置

- `core-model/OrganizationModels.kt`: `TagItem`、`FavoriteItem`、`FavoritePage`、`EntryOrganizationState`、Validation。
- `core-network`: DTO、Retrofit endpoint、`OrganizationApi`実装。Search requestへTag IDsを追加する。
- `core-data/OrganizationRepository.kt`: API mapping、Pager、通信結果不明時のreconcile。
- `feature-search/OrganizationViewModels.kt`: Favorites、Tag management、Entry organization state。
- `feature-search/OrganizationScreens.kt`: Favorites、Tag management、Tag selector。
- `feature-search/SearchModels／ViewModels／Screens`: Tag filter選択を追加する。
- `feature-files`: 既存File／Folder actionからApp callbackを呼び、Repositoryや`feature-search`へ直接依存しない。
- `app/MainActivity.kt`、`ServiceContainer.kt`: Navigation、DI、Session単位instanceを接続する。

新しいGradle moduleは追加しない。`feature-search`は検索・最近使用・個人整理の一覧画面を担当し、`feature-files`はEntry操作の表示だけを担当する。Module間遷移はIDとcallbackで行う。

#### UI state

Favorites:

```text
FavoriteUiState
  items, page, totalCount
  isLoading, isLoadingNext, isRefreshing
  error, staleGeneration
```

Tag management:

```text
TagUiState
  tags
  dialog(create|rename|deleteConfirm)
  validationError, operationError
  pendingTagId
```

Entry organization:

```text
EntryOrganizationUiState
  entryId
  isFavorite
  attachedTags
  availableTags
  isLoading
  pendingFavorite
  pendingTagIds
  error
```

- Favorite／Tag mutation成功後はServer responseまたはorganization state再取得を正とする。
- Network結果不明では成功状態を反転せず、`GET /files/{entryId}/organization`またはFavorites再取得で収束させる。
- ViewModelはgeneration counterとCoroutine cancellationを併用し、古いUser、Filter、Pageのresponseを破棄する。
- Logout、User切替、接続先変更でAppのSession containerを破棄し、Tag／Favorites／Search stateを再生成する。
- `MISSING_CANDIDATE`／`MISSING`は既存capabilityと同じfail-closed表示とし、Tag解除だけをorganization state画面で許可する。

#### Navigation

`AppDestination`へ`FAVORITES`、`TAGS`、必要なら引数付き`ENTRY_ORGANIZATION`を追加する。HomeはFavoritesへ遷移するButton／cardを追加する。Tag管理はFavoritesまたはSearch filterの管理actionから到達可能にし、Entry organizationはFile／Folder actionからEntry IDだけを渡す。

Favorites／Search結果選択は既存patternどおり、Folderならbrowser、FileならdetailへIDだけを返す。遷移先で最新詳細を取得するまで変更操作を有効にしない。

## データフロー

### お気に入り登録

```text
1. AndroidがEntry IDでPUT /favorites/{entryId}を送る。
2. APIがJWT ActorをOrganizationServiceへ渡す。
3. RepositoryがEntry／祖先lockを取得する。
4. lock後にACTIVE、未完了操作、現在権限を再評価する。
5. favorite_entriesへON CONFLICT DO NOTHINGでinsertする。
6. APIが204を返す。
7. Androidがorganization stateを再取得して表示を確定する。
```

### お気に入り一覧

```text
1. HomeからFavoritesへ移動する。
2. AndroidがGET /favorites?page=1&pageSize=50を送る。
3. PostgreSQLがActor favorites、状態、現在権限、Permission rankを一括解決する。
4. Serverが安定順Pageを返す。
5. Androidが段階表示し、選択時は既存詳細APIを再取得する。
```

### Tag作成と付与

```text
1. AndroidがNameをPOST /tagsへ送る。
2. Applicationがtrim、NFC、code point、control、NameKeyを検証する。
3. RepositoryがUser lock内で件数とunique constraintを確認してTagを作る。
4. AndroidがEntry IDとTag IDでPUT /files/{entryId}/tags/{tagId}を送る。
5. Repositoryがlock後にTag ownership、ACTIVE、権限、20件上限を確認する。
6. entry_tagsへON CONFLICT DO NOTHINGでinsertし、204を返す。
7. Androidがorganization stateを再取得する。
```

### Tag検索

```text
1. Androidが本人Tag IDを1〜10件選択する。
2. SearchInputが重複、UUID、件数、既存Filterを検証する。
3. APIがrepeated tagId queryをSearchQueryへ変換する。
4. Repositoryが同一read snapshotでTag ownershipを検証する。
5. 既存Search SQLがTag AND条件、認可、状態、他Filter、sort、pageを適用する。
6. Androidが既存SearchPageとして表示する。
```

## エラーハンドリング戦略

- Domain／ApplicationのValidation failureは安定Error codeへ変換し、入力値をmessageやLogへ含めない。
- 他UserTag、存在しないTagはSearchでは同じ`INVALID_SEARCH_FILTER`、管理では同じ`TAG_NOT_FOUND`にする。
- 非認可Entry、存在しないEntry、操作不可状態は`FILE_NOT_FOUND`へ統一する。
- unique violationはconstraint名をInfrastructure内で判定し、Tag名をLogせず`TAG_NAME_CONFLICT`へ変換する。
- limit競合はUser／Entry lock内で再計数してから判定する。
- Androidは400を入力／上限、404を失効／消失、409を重複名、429を再試行待ち、5xx／networkを再取得可能Errorとして扱う。
- PUT／DELETEの通信結果不明を成功と推測しない。GET stateまたは一覧Refreshで収束させる。
- Cancellationは成功・失敗へ変換せず上位へ伝播し、Server Transactionをrollbackする。

## セキュリティ設計

- API routeは既定Authorization policy配下とし、匿名許可を追加しない。
- ActorはJWTだけから取得し、RequestのUser、Owner、時刻を信頼しない。
- `GET /tags`、Favorites、organization stateは必ずActor所有行から開始する。
- Tag付与時はTag ownershipとEntry accessをlock後に両方再確認する。
- Search Tag IDsはActor所有を検証し、他UserTagと不存在を区別しない。
- Admin Roleへ他UserFile、Tag、Favoritesの暗黙権限を与えない。
- Tag名をURLへ入れず、Request bodyとDB parameterで扱う。SQLを文字列連結しない。
- Nginxは既存どおりQuery stringをAccess Logへ記録せず、Server／AndroidはTag名、検索語、File名、User名、Path、TokenをLogへ残さない。
- Metric labelはendpoint template、status、安定Error codeなど低cardinality値だけを使用する。
- OpenAPI、Error、件数、Paginationから非認可対象の存在を推測できないことをSecurity Testする。

## パフォーマンス設計

- Favoritesは`user_id`先頭Indexから最大100件だけを取得し、Page内認可を1つのCTEでBatch解決する。
- Tag一覧はUser当たり最大200件の有界Queryとし、Androidでのみ一時保持する。Entry stateは最大20件だけ返す。
- Tag Searchは`entry_tags(tag_id, entry_id)`から一致Entry IDを絞り、AND条件を`GROUP BY/HAVING`で解決する。
- TagなしSearchへjoinやTransactionを追加せず、既存30万件性能を維持する。
- `EXPLAIN ANALYZE BUFFERS`でTag 1件、10件、Tagのみ、名前＋Tag、共有＋Tag、MISSING＋Tag、Page後半を測定する。
- Raspberry Pi相当の30万FileEntry、10 User、最大Tag条件で代表Queryのwarm p50／p95／最大、cold、CPU、Memory、DB connection、Index sizeを記録する。
- 通常2秒以内を満たさない場合はQuery／Indexを修正して再測定し、未達のまま完了しない。

## テスト戦略

### Domain／Application unit test

- Empty ID、UTC、Tag trim／NFC、Rune長、control character、NameKey、case-insensitive重複。
- Tag 200件、Entry Tag 20件の境界と、既存関連への冪等再送。
- Favorites page、Tag filter 0／1／10／11、重複、不正UUID、他Filterとの組合せ。
- Repository結果から全Error codeへの変換。
- お気に入り、Tag、Searchの認可・Validation境界Line Coverage 95%以上。

### PostgreSQL／API integration test

- Migration Up／Down／再Up、constraint、Index、Cascade、model snapshot。
- Owner、Viewer、Contributor、Editor、Manager、未共有、Admin、直接／継承／複数経路。
- Favorite add／remove、Tag CRUD／attach／detachの冪等性と同時実行。
- Tag名同時作成、上限到達競合、Tag削除対attach、Share失効対favorite／attach、Move対attach。
- Favorites／organization state／Searchの状態別動作。
- Rename、Move、Share解除・再取得、Trash、Restore、Purge、MISSING二段階、索引削除、User削除。
- repeated `tagId` binding、bodyなし契約、Client state混入拒否、OpenAPI contract。
- Tag名、Search query、File名、User名、Path、TokenのLog非漏えい。

### Android test

- DTO strict mapping、未知値、UUID、時刻、Page、重複結果、Tag上限。
- 401 Refresh、204、400、404、409、429、5xx、network結果不明とreconcile。
- Favorites／Tag ViewModelのLoading、Empty、Paging、Refresh、古い応答、Session切替。
- Home導線、Favorites画面、Tag管理Dialog、Entry selector、Search Tag filterのCompose UI。
- 二重Tap、回転、狭い画面、Scroll、Keyboard、権限失効、`MISSING`のfail-closed表示。

### E2E／性能／回帰

- Raspberry Pi、PostgreSQL、実Storage、署名AndroidでLAN／ZeroTierを確認する。
- 30万件代表Tag Searchが通常2秒以内で、意図したIndexを使用する。
- Favorite／TagのUser分離、共有失効・再取得、状態遷移、再起動、同時操作を確認する。
- Search、Recent、Personal／Shared、Upload、Download、Rename、Move、Trash、Restore、Purge、MISSINGを回帰確認する。
- 限定識別子のTest dataだけを清掃し、孤立organization行、未完了操作、active Upload Sessionが0件であることを記録する。

## 依存ライブラリ

新しい依存Libraryは追加しない。Serverは既存.NET／EF Core／Npgsql、Androidは既存Kotlin Coroutines／Retrofit／Composeを使用する。

## ディレクトリ構造

```text
server/
├── src/
│   ├── KuraStorage.Domain/Files/
│   │   ├── FavoriteEntry.cs
│   │   ├── Tag.cs
│   │   └── EntryTag.cs
│   ├── KuraStorage.Application/
│   │   ├── Abstractions/OrganizationAbstractions.cs
│   │   ├── Organization/
│   │   │   ├── OrganizationContracts.cs
│   │   │   ├── OrganizationService.cs
│   │   │   └── TagNameNormalizer.cs
│   │   └── Search/                       # TagIdsを追加
│   ├── KuraStorage.Infrastructure/
│   │   └── Persistence/
│   │       ├── Configurations/           # 3 entity mapping
│   │       ├── Migrations/
│   │       └── Queries/
│   │           ├── PostgreSqlOrganizationRepository.cs
│   │           └── PostgreSqlSearchRepository.cs
│   └── KuraStorage.Api/Program.cs
└── tests/
    ├── KuraStorage.Domain.Tests/
    ├── KuraStorage.Application.Tests/
    └── KuraStorage.IntegrationTests/

apps/android/
├── core-model/.../OrganizationModels.kt
├── core-network/.../                     # DTO・OrganizationApi・Search tagId
├── core-data/.../OrganizationRepository.kt
├── feature-search/.../
│   ├── OrganizationViewModels.kt
│   ├── OrganizationScreens.kt
│   ├── SearchViewModels.kt
│   └── SearchScreens.kt
├── feature-files/.../                    # organization callback/action
└── app/.../
    ├── MainActivity.kt
    └── ServiceContainer.kt

contracts/openapi/kurastorage-api.yaml
docs/
├── functional-design.md
├── architecture-design.md
├── repository-structure.md
├── development-guidelines.md             # 恒久規約が増える場合だけ
├── operations/
└── testing/
```

実際の既存file分割に合わない場合は責務単位を維持して配置し、将来用の空DirectoryやProjectを作らない。

## Rollout／rollback

1. 対応するPostgreSQLとStorage RootのBackup、Storage ID、Service状態を確認する。
2. Migrationを明示適用し、3 Table、constraint、Index、既存schemaを確認する。
3. Server APIを配置し、health、認証、既存Search、Favorites／Tag smoke testを実施する。
4. 署名Androidを配置し、Version、署名、Root CA、Hostnameを確認する。
5. LAN／ZeroTier、実機E2E、30万件性能、Log非漏えいを確認する。

RollbackはAndroid、Server、Migrationの逆順とする。新Tableに本番Userデータが作成された後のMigration Downはデータを失うため、Backup確保と明示承認なしに実行しない。Serverだけを先に旧版へ戻す場合、新Tableは未参照のまま保持できるが、旧Serverと新Androidの契約不一致を避けるためAndroidも対応版へ戻す。

## 実装の順序

### PR1: Server APIと検索基盤

1. Domain entity、normalizer、Application contractの失敗Testを追加する。
2. EF mapping、Migration、Up／Down／Cascade Testを実装する。
3. RepositoryのFavorite、Tag CRUD、organization stateをTest firstで実装する。
4. lock後再認可、件数上限、競合Testを実装する。
5. Search contractとPostgreSQL QueryへTag filterを追加する。
6. API endpoint、Error mapping、OpenAPI、Contract Testを追加する。
7. 30万件Query plan、Coverage、必須CI、Security、Migrationを検証する。
8. Server関連の正式文書・運用文書を更新し、PR1を作成して停止する。

### PR2: Android UIと実機E2E

1. Android model、network、repositoryの失敗Testと実装を追加する。
2. Favorites、Tag管理、Entry organization ViewModel／Compose UIを実装する。
3. Home、Navigation、DI、feature-files callbackを接続する。
4. SearchへTag filterを追加し、古い要求とSession分離をTestする。
5. Android unit／Compose／connected Test、全必須CIを実行する。
6. Backup、Migration、Server、署名Androidを本番相当順序で配置する。
7. Raspberry Pi性能、LAN／ZeroTier、Android実機、Security、回帰、清掃を完了する。
8. 全正式文書・Test記録を整合し、Release BuildとPR2を完了する。

## 将来の拡張性

Application contractとAPIはAndroid固有型へ依存しないため、将来のWeb UIも同じお気に入り・Tag APIを再利用できる。共有Tag、色、階層、自動Tag、Smart Folderを追加する場合は、個人Tagの所有・非公開性を暗黙変更せず、別のvisibility／ownership model、Migration、認可、UIとして設計する。

外部Search engineは30万件規模でPostgreSQL目標を満たせない実測とADRがある場合だけ検討する。今回のTag関連TableとAPIを将来機能のために汎用Metadata engineへ拡張しない。
