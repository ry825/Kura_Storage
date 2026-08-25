# 検索・最近使用したファイル 要求内容

## 概要

閲覧可能な個人・共有ファイルを、名前、種類、更新日時、サイズ、所有者、共有元から検索できるようにする。あわせて、Userごとに最近開いたファイルを記録し、権限変更、共有解除、`MISSING`、Trash、完全削除を次回表示へ即時反映する。

## 背景

FileEntryが増えるとFolder階層だけでは目的の項目へ到達しにくい。共有と外部変更追従の導入後は、検索結果や最近使用履歴にも所有者・直接共有・継承共有・複数経路の実効権限と`MISSING`状態を正しく反映しなければ、存在や名前の漏えい、失効後の表示、誤った操作導線につながる。

本作業は、実装済みのFile索引、`AuthorizationService`、共有権限、`MISSING_CANDIDATE`／`MISSING`、Trash・Purge整合性を再利用し、PostgreSQL段階で閲覧可能範囲を絞り込む。HDD全体走査やAndroidだけの結果非表示は使用しない。

## 前提条件

- FileEntry索引、外部変更追従、`MISSING`管理、共有・権限制御を含むPR #19〜#22が`main`へMerge済みで、必須CIが成功していること。
- PostgreSQL 17、Raspberry Pi本番相当環境、実Storage Root、Android実機、LAN、ZeroTier、Release署名入力を検証に利用できること。
- Production MigrationはAPI起動時に自動実行せず、既存の配置手順に従ってBackup後に明示適用すること。

## 用語と機能境界

- **検索対象**: `FILE`と`FOLDER`。通常検索は`ACTIVE`、`MISSING_CANDIDATE`、`MISSING`を対象にし、`TRASHED`と完全削除済み項目は対象外とする。
- **ファイル種類**: `IMAGE`、`VIDEO`、`AUDIO`、`DOCUMENT`、`ARCHIVE`、`OTHER`。FolderはEntry種別Filterで扱い、拡張子だけでなく保存済みMIMEを基準に分類し、未知・欠落値は`OTHER`とする。
- **共有元**: 実効権限の説明に使用する`shareTargetId`。所有者の個人領域では共有元を持たない。
- **最近開いたファイル**: AndroidがFileをユーザーに表示できたことを確認した後、明示的な冪等APIで記録した`FILE`。Folder閲覧、一覧表示、Background refresh、単なる検索結果表示は履歴へ記録しない。
- **開いた時刻**: Server時刻を使用する。Clientが任意の`userId`、`openedAt`、所有者を指定できない。

## 実装対象の機能

### 1. 権限対応ファイル検索

- 認証Userが所有する項目、直接共有された項目、閲覧可能な共有Folder配下だけを検索する。
- 複数共有経路は既存認可規則で最強権限を採用し、同値時は直接共有、最も近い祖先の順で権限元を返す。
- File・Folder名を大文字小文字を区別せず検索する。空白を正規化し、入力長とPage sizeへ上限を設ける。
- Entry種別、ファイル種類、更新日時範囲、サイズ範囲、所有者、共有元、状態で絞り込む。
- 絞り込み条件は組合せ可能とし、矛盾した範囲、不正enum、不正UUID、不正日時、過大入力を`400`で拒否する。
- 安定したPaginationと並び順を提供し、Page間で同一項目を重複させない。
- 結果にOwner、実効Permission、Permission Source、Share Target、状態、MIME、サイズ、更新日時を含める。
- `MISSING_CANDIDATE`と`MISSING`を`ACTIVE`と区別し、既存のOwner限定再確認・索引削除規則を維持する。

### 2. PostgreSQL検索索引と性能

- `pg_trgm`をMigrationで有効化し、正規化した`lower(name)`へGIN trigram Indexを追加する。
- 短い検索語は無制限な全件走査へ退化させず、検証可能な最小長またはPrefix用Indexを正式文書とAPI契約で確定する。
- 所有者、直接共有、祖先Folder共有、状態、Filter、Paginationを1つの有界Queryまたは固定回数のBatch Queryへ統合する。
- 認可候補を取得後にApplicationやAndroidで隠す方式、Page内N+1、HDD走査、無制限再帰を禁止する。
- FileEntry 30万件、家族User 10名、代表検索20種でIndex利用と通常2秒以内の目標をRaspberry Pi相当環境で測定する。

### 3. 最近使用したファイル履歴

- `recent_files`でUserとFileEntryごとの最終`openedAt`を保持し、同じFileを再度開いた場合は重複行を作らずServer時刻を更新する。
- File表示成功後の明示APIだけが履歴を更新する。GET一覧、検索、認証再送、画面再構成だけでは更新しない。
- 最近使用一覧はUser本人の履歴だけを新しい順・IDの安定順でPaginationする。
- 取得時に現時点の閲覧権限をSQL段階で再評価し、共有解除・Permission失効後の次要求から対象を返さない。
- `MISSING_CANDIDATE`と`MISSING`は履歴を保持して状態を表示し、`TRASHED`は通常一覧から除外する。
- 完全削除・MISSING索引削除でFileEntryが削除された場合は履歴も削除する。User削除時も対象Userの履歴を残さない。
- 権限を再取得した場合、過去履歴を復活させるか削除するかを一貫させる。本作業では権限失効中もDB行は保持し、再取得時に過去履歴を再表示する。ただし完全削除時はCascadeで消去する。

### 4. Android検索画面

- Homeから検索画面へ遷移し、検索語、Entry種別、ファイル種類、更新日時、サイズ、所有者／共有元、状態を指定できる。
- 入力確定または明示検索でServerへ要求し、入力中の古い要求をCancelまたは世代管理して新しい結果を上書きさせない。
- Loading、空、Pagination、Refresh、入力Error、認証更新、Storage/API Error、通信結果不明、権限失効を表示する。
- 検索結果にFile／Folder、名前、Owner、Permission／Source、共有元、サイズ、更新日時、`MISSING`状態を表示する。
- Folder結果は既存File browser、File結果は既存詳細・Download導線へ遷移し、権限別操作可否を既存共通modelから導出する。
- 検索語やFilterをToken、物理Path、実User識別情報とともにLogへ残さない。

### 5. Android最近使用画面と履歴記録

- Homeから最近使用画面へ遷移し、User本人の最近開いたFileを新しい順に表示する。
- File詳細をユーザーへ表示できた場合だけ履歴記録APIを呼ぶ。Folder、一覧、検索結果の表示だけでは記録しない。
- 履歴記録の401 Refresh後再送は同じFileへの冪等更新とし、二重行やClient時刻による逆転を起こさない。
- 権限を失ったFileを表示せず、Share解除後またはServerの拒否後は一覧を再取得する。
- `MISSING_CANDIDATE`／`MISSING`を表示し、通常FileとしてDownloadや変更操作を有効にしない。
- 履歴からFileを開く際もServerの最新詳細と権限を取得し、古いClient状態だけで操作を許可しない。

### 6. 契約・運用・観測

- OpenAPI、正式設計文書、Repository構造、開発規約、運用文書を実装と同じ変更で更新する。
- Search query、File名、User名、共有元名、物理PathをAccess Log、Metric label、例外、E2E記録へ出さない。NginxとAPIのRequest記録がQuery stringを保存しないことを確認する。
- 検索・最近使用APIは既存の安定Error code、Request ID、認証Refresh、Rate Limit方針に従う。
- MigrationのBackup、適用順序、Index作成時のLock／所要時間、Rollback制約、履歴データ保護を文書化する。

## 受け入れ条件

### ファイル検索

- [ ] 自分に閲覧権限がある範囲だけを検索でき、未共有の他User項目を件数・名前・Owner・Filter候補から推測できない。
- [ ] File名とFolder名を検索できる。
- [ ] Entry種別とファイル種類で絞り込める。
- [ ] 更新日時の開始・終了で絞り込める。
- [ ] サイズの最小・最大で絞り込める。
- [ ] 所有者または共有元で絞り込める。
- [ ] 状態で絞り込め、`MISSING_CANDIDATE`／`MISSING`を通常結果と区別できる。
- [ ] 検索結果からFolderまたはFileを直接開き、最新権限に対応する操作だけを実行できる。
- [ ] 共有解除、Permission変更、Moveによる継承元変更、Trash、Restore、Purge、MISSING索引削除が次の検索へ反映される。
- [ ] Paginationが安定し、同じSnapshot条件で重複・取りこぼしがない。

### 最近使用したファイル

- [ ] 最近開いたFileをUser単位で表示する。
- [ ] 同一User・Fileは1行だけ保持され、再度開くとServer時刻で先頭へ更新される。
- [ ] 別Userの履歴を取得・更新できない。
- [ ] 権限を失ったFileは次の一覧から表示されない。
- [ ] `MISSING_CANDIDATE`／`MISSING`は状態を反映し、`TRASHED`と完全削除済みFileは表示されない。
- [ ] Background処理や検索結果表示だけでは履歴が更新されない。

### 性能・品質

- [ ] 30万件・代表検索20種で権限条件込みの名前検索が通常2秒以内を満たし、`EXPLAIN ANALYZE`で意図したIndexを使用する。
- [ ] Search／Recent一覧でHDD走査、N+1、無制限再帰、`SELECT *`がない。
- [ ] Domain/ApplicationのLine Coverage 80%以上、検索・認可境界95%以上を満たす。
- [ ] Raspberry Pi、LAN、ZeroTier、Android実機で検索・最近使用・失効・`MISSING`・回帰E2Eが成功する。
- [ ] 必須CI、署名Release Build、Migration検証、機密情報検査が成功する。

## 成功指標

- 30万FileEntry環境の代表検索20種で通常2秒以内。
- Page size 100までを上限とする段階表示で、結果全件をAndroidメモリへ一括保持しない。
- 権限失効、Trash、Purge後の検索・最近使用に旧表示が残らない。
- 検索・最近使用に起因するFile名、検索語、物理Path、他User履歴の情報漏えいが0件。
- 既存の個人File操作、共有、MISSING、Upload、Download、Trash、Restore、Purgeに回帰がない。

## スコープ外

以下はこの作業では実装しない。

- File本文、OCR、画像内文字、音声、動画字幕の全文検索。
- 検索候補、入力補完、検索履歴、保存済み検索、タグ、星印、お気に入り。
- Elasticsearch、OpenSearch、外部SaaS検索、別Search cluster。
- Group、Deny ACL、公開Link、家族外共有。
- 最近使用履歴の手動削除、固定、端末別履歴、閲覧回数、推薦・ランキング。
- Trash専用検索、Adminによる他User横断検索、監査ログ検索。
- Thumbnail、品質別Preview、Media変換、自動Backup、Web UI。

## 参照ドキュメント

- `docs/product-requirements.md` 7.7「MVP後: 検索と整理」
- `docs/functional-design.md` 5.7、5.8.2、8.11、Server Step 5、Android Step 6
- `docs/architecture-design.md` 8.1〜8.3、13.7、14.1〜14.3、20.2、21.5
- `docs/repository-structure.md` Server Application／Infrastructure／API、Android Feature Module、Tests構造
- `docs/development-guidelines.md` 7.2「スキーマ変更」、7.3「Query」
- `.steering/20260823-file-folder-sharing-permissions/` 共有・実効権限・Android UIの実装前提
- `.steering/20260822-external-change-missing-management/` `MISSING`索引・外部変更追従の実装前提
