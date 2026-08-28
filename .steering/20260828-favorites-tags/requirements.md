# お気に入り・タグ 要求内容

## 概要

認証Userが閲覧できるFileとFolderを個人のお気に入りへ登録し、Home画面から素早く開けるようにする。あわせて、User個人の非公開TagをFileとFolderへ付与し、実装済みの権限対応SearchをTagで絞り込めるようにする。

## 背景

FileEntryが増えると、Folder階層、名前検索、最近使用だけでは、頻繁に使う項目や用途別に整理した項目へ継続的に到達しにくい。お気に入りは繰り返し開く項目への短い導線を提供し、TagはFolder階層を変更せず複数の観点で項目を分類できる。

本機能は実装済みのFileEntry索引、共有・実効権限、Search API、PostgreSQL検索Query、Android `feature-search`を再利用する。お気に入りとTagをFile本体や共有設定へ混在させず、Userごとの個人整理情報として管理することで、Viewerを含む閲覧可能Userが共有項目を自分用に整理でき、他Userへ分類名や利用状況を漏らさないようにする。

## 前提条件

- File／Folder共有、実効権限、外部変更追従、`MISSING`管理、Trash／Purge、権限対応Search、Recent APIが`main`へMerge済みで、必須CIが成功していること。
- Android Search／Recent UIが`main`へMerge済みで、既存Home、Navigation、File／Folder詳細への遷移を再利用できること。
- PostgreSQL 17、Raspberry Pi本番相当環境、実Storage Root、Android実機、LAN、ZeroTier、Release署名入力を検証に利用できること。
- Production MigrationはAPI起動時に自動実行せず、既存の配置手順に従いBackup後に明示適用すること。

## 用語と機能境界

- **お気に入り**: 認証UserとFileEntryの組に紐づく非公開の個人整理情報。同じUser・Entryは1件だけ保持する。
- **Tag**: 認証Userが所有する非公開の分類名。他User、対象EntryのOwner、共有管理者、Adminへ暗黙公開しない。
- **Tag付与**: 認証Userが所有するTagと、現在閲覧可能なFileEntryの関連。同じUser・Tag・Entryは1件だけ保持する。
- **閲覧可能**: 既存`AuthorizationService`と共有規則により、Owner、直接共有、または祖先Folder共有から有効なPermissionを持つ状態。Admin Roleだけでは他UserのFileEntryを閲覧可能にしない。
- **個人整理情報**: File名、配置、内容、共有設定、Owner、他Userの整理情報を変更しないUser単位のMetadata。閲覧可能な共有項目では`VIEWER`を含む全Permissionで操作できる。
- **Tag検索**: 認証User本人が所有するTagだけを条件に、既存Search対象を絞り込む処理。複数Tag指定はすべてのTagが付与された項目に一致するAND条件とする。

## 実装対象の機能

### 1. User単位のお気に入り登録・解除

- 認証Userが現在閲覧可能な`ACTIVE`のFileまたはFolderをお気に入りへ登録できる。
- 登録は同じUser・Entryに対して冪等とし、二重行を作らない。
- 認証Userが自分のお気に入りを解除できる。解除は対象が未登録でも冪等に成功する。
- 登録日時はServer UTCで確定し、ClientからUser ID、Owner ID、登録日時、物理Pathを受け取らない。
- お気に入り登録はFile内容、Folder階層、共有権限、他Userのお気に入りへ影響を与えない。

### 2. 権限・状態対応のお気に入り一覧

- Home画面からお気に入り一覧へ移動できる。
- 一覧は認証User本人のお気に入りだけを、登録日時の新しい順、同値時はFileEntry IDの昇順で安定Paginationする。
- Page sizeは1〜100、既定50とし、全件をAndroid Memoryへ一括保持しない。
- 一覧取得時に現在の閲覧権限をSQL段階で再評価し、共有解除またはPermission失効後の次要求から対象を返さない。
- `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`は状態を区別して表示し、`TRASHED`、未完了FileOperation、完全削除済み項目は通常のお気に入り一覧へ返さない。
- 一覧結果には既存Search結果と同じFile／Folder、Owner、Permission／Source、Share Target、状態、MIME、size、更新日時のMetadataと、お気に入り登録日時を含める。
- 一覧からFile／Folderを開く際は既存詳細APIで最新状態と権限を再取得し、古い一覧情報だけで操作を許可しない。

### 3. User個人Tagの管理

- 認証Userは自分のTagを作成、一覧、名前変更、削除できる。
- Tag名はtrimとUnicode NFC正規化後に1〜50 Unicode code pointとする。
- Tag名にUnicode control characterを許可しない。内部の空白は維持し、表示名として使用する。
- 同じUser内では正規化後のTag名を大文字小文字を区別せず一意とし、例として`Work`と`work`を別Tagにしない。
- User当たりのTagは最大200件とし、上限超過を検証可能な`400` Errorで拒否する。
- Tag一覧は正規化名の大文字小文字を区別しない昇順、同値時はTag ID昇順で返す。
- Tag名前変更は同じ正規化名への再実行を副作用のない成功とし、別Tagとの重複を`409`で拒否する。
- Tag削除は認証User本人のTagだけを対象とし、全FileEntryとの関連を同じDB Transactionで削除する。FileEntry本体や他UserのTagを削除しない。

### 4. File／FolderへのTag付与・解除

- 認証Userは自分が現在閲覧可能な`ACTIVE`のFileまたはFolderへ、自分が所有するTagを付与できる。
- 個人整理情報であるため、Owner、`VIEWER`、`CONTRIBUTOR`、`EDITOR`、`MANAGER`のいずれも、自分のTagだけを付与・解除できる。
- 同じUser・Tag・Entryへの付与は冪等とし、二重行を作らない。
- Entry当たりのTagはUserごとに最大20件とし、上限超過を検証可能な`400` Errorで拒否する。
- 認証Userは自分のTag付与を解除できる。解除は関連が存在しない場合も冪等に成功する。
- `MISSING_CANDIDATE`または`MISSING`に既に付与されたTagは保持・表示できるが、新しいTagは付与しない。既存Tagの解除はできる。
- `TRASHED`、未完了FileOperation、完全削除済み項目にはTagを付与せず、通常画面からTag関連を操作しない。
- Tag付与・解除はFile内容、名前、配置、`fileVersion`、共有権限、他UserのTagへ影響を与えない。

### 5. Tagによる検索・絞り込み

- 既存`GET /api/v1/search`へ認証User本人のTag ID条件を追加する。
- Tag filterは1〜10個の重複しないTag IDを受け付け、複数指定時はすべてのTagを持つEntryだけを返す。
- Tag filterだけでも検索を実行でき、名前、Entry種別、File category、更新日時、size、Owner、共有元、状態と組み合わせられる。
- 他User所有Tag、存在しないTag、不正UUID、重複、11個以上の指定を`INVALID_SEARCH_FILTER`の`400`で拒否し、Tagの存在可否を区別するErrorにしない。
- Search対象、実効権限、Permission Source、状態、並び順、Paginationは既存Search契約を維持する。
- 認可候補をApplicationまたはAndroidで後から隠さず、PostgreSQL Query内で認証User、Tag関連、閲覧権限、状態、他Filterを適用する。
- Tag filter後も同一データ状態でPage間の重複・取りこぼしを起こさない。

### 6. Rename・Move・共有・状態変更との整合性

- RenameまたはMoveではFileEntry IDを維持し、お気に入りとTag付与をそのまま維持する。
- 共有解除またはPermission失効時は、対象のお気に入りとTag付与をDBに保持するが、一覧、検索、候補へ返さない。
- 同じUserが閲覧権限を再取得した場合は、保持中のお気に入りとTag付与を再表示する。
- `MISSING_CANDIDATE`または`MISSING`では、お気に入りとTag付与を保持し、状態を明示して一覧・検索へ返す。通常FileとしてDownloadや変更操作を有効にしない。
- Trash移動ではお気に入りとTag付与を保持するが、通常のお気に入り一覧とSearchから除外する。Restore後は再表示する。
- Purge、`MISSING`の一覧削除、FileEntryのCascade削除、User削除では関連するお気に入りとTag情報を削除し、孤立行を残さない。
- Tag削除、Purge、共有変更、Tag付与・解除が競合しても、重複行、他User関連の削除、非認可表示へ収束しない。

### 7. Androidお気に入りUI

- Homeへ「お気に入り」の明確な入口を追加する。
- お気に入り一覧はLoading、Empty、Success、Pagination、Refresh、入力不要の認証更新、通信Error、権限失効を表示する。
- File／Folder、Owner、Permission／Source、共有元、更新日時、`MISSING`状態を既存共通Modelから表示する。
- 既存File／Folder一覧または詳細から、お気に入り登録・解除を実行できる。
- 二重Tap、画面再構成、401 Refresh再送で二重登録や誤った表示反転を起こさない。
- 通信結果不明、権限失効、対象消失では成功状態をローカル合成せず、Serverから状態を再取得する。

### 8. Android Tag UIとSearch統合

- User本人のTagを作成、一覧、名前変更、削除できる。
- File／Folderの既存画面から現在のTagを確認し、自分のTagを付与・解除できる。
- Tag名の空、長さ、control character、重複、上限Errorを操作箇所で理解できる形で表示する。
- 既存Search画面で1〜10個のTagを選択し、他のFilterと組み合わせて検索できる。
- Filter変更時はPageと古い結果を破棄し、取消済みまたは古い要求が新しい結果を上書きしない。
- Logout、User切替、接続先変更時に前UserのTag、お気に入り、検索条件、結果、処理中状態を再利用しない。
- Tag名、検索語、File名、User名、物理Path、TokenをAndroid Logへ残さない。

### 9. 契約・Security・運用・観測

- お気に入り、Tag管理、Tag付与・解除、Tag検索のAPIをOpenAPIへ記載し、Androidと同一契約を使用する。
- すべてのAPIを既存の認証、Device／Session状態、Request ID、共通Error、Rate Limit方針へ統合する。
- Clientから任意のUser ID、Owner ID、物理Path、登録日時、作成日時を受け取らない。
- お気に入り・Tag一覧、Search、候補件数を通じて、他Userの整理情報または未共有Entryの存在を推測できない。
- Search query、Tag名、File名、User名、物理Path、TokenをNginx／API Access Log、通常Log、Metric label、例外、E2E記録へ残さない。
- MigrationのBackup、適用順、Index作成時のLock／所要時間、Rollback制約、関連データ保護を文書化する。

## 受け入れ条件

### お気に入り

- [ ] Userごとに閲覧可能な`ACTIVE`のFileとFolderをお気に入り登録・解除できる。
- [ ] 同一User・Entryを複数回登録しても1行だけ保持し、解除を再送しても安全に成功する。
- [ ] Homeからお気に入り一覧へ移動できる。
- [ ] 本人のお気に入りだけを登録日時の新しい順で安定Paginationする。
- [ ] 他Userのお気に入り、未共有Entry、失効後のEntryを件数・Metadata・Error差分から推測できない。
- [ ] `MISSING_CANDIDATE`／`MISSING`は状態を区別して表示し、`TRASHED`、未完了操作、Purge済みEntryを返さない。
- [ ] 一覧からEntryを開くときに最新詳細と権限を再取得する。

### Tag管理・付与

- [ ] User本人の非公開Tagを作成、一覧、名前変更、削除できる。
- [ ] Tag名はtrim・NFC後1〜50 code point、control characterなし、User内case-insensitive一意である。
- [ ] User当たり200 Tag、Entry当たりUserごとに20 Tagの上限を超えない。
- [ ] Ownerと全共有PermissionのUserが、閲覧可能な`ACTIVE` Entryへ自分のTagだけを付与・解除できる。
- [ ] 同一User・Tag・Entryの付与が重複せず、解除の再送が安全に成功する。
- [ ] Tag削除時に本人の関連だけを削除し、FileEntry本体と他UserのTagへ影響を与えない。
- [ ] `MISSING_CANDIDATE`／`MISSING`の既存Tagは保持・解除できるが、新規付与できない。

### Tag検索

- [ ] 本人のTagを1〜10個指定し、単独または既存Filterとの組合せで検索できる。
- [ ] 複数Tag指定時は全Tagを持つEntryだけが一致する。
- [ ] 他UserTag、存在しないTag、不正・重複・上限超過の条件を情報漏えいのない`400`で拒否する。
- [ ] Owner、直接共有、継承共有、複数経路の実効PermissionとSourceを既存Searchどおり返す。
- [ ] Tag filter後も非認可、`TRASHED`、未完了操作、Purge済みEntryを結果と件数へ含めない。
- [ ] 同一データ状態でPaginationの重複・取りこぼしがない。

### ライフサイクル整合性

- [ ] Rename／Move後もFileEntry ID、お気に入り、Tag付与を維持する。
- [ ] 共有解除中は個人整理情報を非表示にし、同じUserの権限再取得時に再表示する。
- [ ] Trash中は非表示、Restore後は再表示し、Purge／索引削除／User削除後は関連を削除する。
- [ ] 同時更新や失敗後に重複、孤立、他User関連の削除、非認可表示が残らない。

### Android

- [ ] Home、お気に入り一覧、File／Folder操作からお気に入りの主要Flowを完了できる。
- [ ] Tag管理、File／Folderへの付与・解除、Tag条件Searchの主要Flowを完了できる。
- [ ] Loading、Empty、Pagination、Refresh、Validation、競合、認証更新、通信Error、権限失効を表示できる。
- [ ] 二重Tap、画面再構成、取消、401再送、古い応答で状態が重複・逆転しない。
- [ ] User／Session／接続先をまたいで個人整理情報が漏れない。

### 性能・品質

- [ ] 30万FileEntry、家族User 10名、User当たり最大200 Tag、Entry当たり最大20 Tagを含む代表条件で、Tag検索が通常2秒以内を満たす。
- [ ] Tag検索とお気に入り一覧がHDD走査、Page内N+1、無制限再帰、全件Memory保持、Client後Filterを行わない。
- [ ] `EXPLAIN ANALYZE BUFFERS`で意図したIndexと有界な認可・Tag Queryを使用する。
- [ ] Domain／Application全体のLine Coverage 80%以上、今回追加する認可・Validation境界95%以上を満たす。
- [ ] Raspberry Pi、LAN、ZeroTier、署名Android実機でお気に入り、Tag、権限失効、`MISSING`、Trash／Restore／Purge、既存機能回帰E2Eが成功する。
- [ ] 必須CI、署名Release Build、Migration Up／Down／再Up、機密情報検査が成功する。

## 成功指標

- お気に入り登録・解除とTag付与・解除の冪等再送で重複行が0件である。
- 他UserのTag、お気に入り、未共有Entryに関する情報漏えいが0件である。
- 30万FileEntryの代表Tag検索で通常2秒以内を維持する。
- Page size 100以下の段階表示とし、お気に入りまたは検索結果をAndroid Memoryへ全件保持しない。
- Rename、Move、共有変更、`MISSING`、Trash、Restore、Purge後に古い表示または孤立関連が残らない。
- 既存Search、Recent、Personal／Shared一覧、Upload、Download、Rename、Move、Trash、Restore、Purge、MISSINGに回帰がない。

## スコープ外

以下はこの作業では実装しない。

- 他Userと共有するTag、Ownerが配布するTag、家族共通Tag。
- Tagの色、アイコン、説明、階層、別名、並び替え、結合、一括編集。
- AI／ruleによるTag自動付与、推薦、ranking、Smart Folder。
- お気に入りFolderへの自動同期、固定順、手動並び替え、最近順以外のranking。
- OCR、本文、画像内文字、音声、動画字幕の全文検索。
- 検索候補、入力補完、検索履歴、保存済み検索。
- Trash専用のお気に入り一覧またはTag検索。
- Adminによる他Userのお気に入り・Tag閲覧、管理、横断検索。
- Web UI、外部Search engine、外部SaaS、別Search cluster。
- 操作履歴、File version、自動Backup、Media派生データの変更。

## 参照ドキュメント

- `docs/product-requirements.md` 7.12.2「お気に入り」、7.12.3「タグ」
- `docs/functional-design.md` 8.11「MVP後: 検索」、10.2「ホームからの主要ナビゲーション」、11.2「ホーム画面」
- `docs/architecture-design.md` 8.1〜8.3、13.7、14.1〜14.3、20.2、21.5
- `docs/repository-structure.md` Server Application／Infrastructure／API、Android Feature Module、Tests構造
- `docs/development-guidelines.md` 2.2「Securityを既定値にする」、2.4「TDD」、7.2「Schema変更」、7.3「Query」
- `.steering/20260824-search-recent-files/requirements.md`
- `.steering/20260824-search-recent-files/design.md`
- `.steering/20260828-favorites-tags/tasklist.md`
