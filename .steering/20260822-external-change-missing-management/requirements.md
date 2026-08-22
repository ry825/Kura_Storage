# 外部変更追従・MISSING管理 要求仕様

## 概要

KuraStorage管理HDD上の実ファイル、内容、物理階層を正とし、Linux inotifyによる変更検知と起動時・定期全件再スキャンを組み合わせてPostgreSQL索引を継続的に実体へ収束させる。

実ファイルが見つからない場合は、HDD全体の障害と個別項目の欠損を区別し、`MISSING_CANDIDATE`を経た別時刻の再確認後にだけ`MISSING`へ確定する。利用者はAndroidから欠損状態を確認し、再確認またはHDDを変更しない索引削除を実行できる。

## 背景

現行実装は、KuraStorage APIが行ったUpload、Folder作成、Rename、Move、Trash、Restore等に合わせてHDDとDB索引を更新する。一方、管理・緊急アクセス、媒体復旧、別ツール等によってHDDがKuraStorage外から変更された場合、DB索引は自動追従せず、一覧、詳細、Downloadの情報と実体が乖離し得る。

HDDはファイル存在、内容、物理階層の正であるため、DB索引の内容を実体より優先してはならない。ただし、HDD取外し、誤Mount、Storage ID不一致、読取障害を個別ファイル削除として扱うと、全索引を誤って`MISSING`にする危険がある。また、inotifyだけではProcess停止中の変更、Queue overflow、watch limit、イベント欠落を完全には回収できない。

検索、共有、最近使用、派生データ等を索引Consumerとして追加する前に、イベント検知と全件再スキャンが同じ照合規則へ収束し、欠損とストレージ障害を安全に区別できる基盤を確立する必要がある。

## 実装対象の機能

### 1. 管理対象とデータの正

- KuraStorage専用Storage Root配下の正式User領域を走査・監視対象とする。
- `users/{ownerUserId}/files`配下の実ファイルとFolderを、Path上のUser namespaceおよび既存User・Root情報に基づいて所有Userへ対応付ける。
- `.storage-identity`、Upload一時領域、Trash内部、派生・内部管理領域等は承認済み設計に従って通常索引対象から除外する。
- Storage Root外、絶対Path、Path Traversal、Symbolic Link経由、特殊File、未知User、不正なUser階層を通常索引へ公開しない。
- HDD上の存在、内容、物理階層を正とし、PostgreSQLは相対Path、所有者、種類、Size、MIME、日時、状態、内容Version、照合情報等の管理情報と索引を保持する。
- File一覧と詳細は主にDB索引から返し、要求ごとのHDD全体走査を行わない。
- File内容を開く直前にはStorage状態とHDD上の存在を確認する。

### 2. 全件再スキャン

- 管理CLI、Worker起動時、設定周期、inotify異常時に同じ全件再スキャンApplication Serviceを実行できる。
- 全件再スキャンはStorage ID、Mount、読取可否を開始前、走査中、確定前に検証する。
- 30万件規模を想定し、全項目をMemoryへ保持せず、取消可能なBatch処理で走査・照合する。
- 同時に複数の全件再スキャンを実行せず、管理CLIと複数Workerからの重複実行を防ぐ。
- Scanの開始、完了、失敗、取消、件数、差分集計を永続化または同等の追跡可能な状態として記録する。
- 走査が完走せず、Storage状態が途中で変化し、または全体の列挙完全性を保証できない場合、そのScan結果から欠損を確定しない。
- 管理CLIは変更予定を表示してDB・HDDを変更しないdry-runと、索引を更新する本実行を提供する。

### 3. 外部変更の索引反映

- 外部追加されたFile・Folderを所有Userと親Folderへ対応付け、親から順に通常索引へ追加する。
- 外部更新されたFileのSize、MIME、HDD更新日時等を反映し、内容が変化した場合だけ`fileVersion`を増分する。
- 名前変更または移動だけでは`fileVersion`を増分しない。
- 外部Rename・Moveは、既存項目との同一性を一意に判断できる場合だけFile IDを維持してName、Parent、相対Pathを更新する。
- inode等の補助情報を単独で同一性の保証に使用せず、同一性が曖昧な項目を誤った既存File IDへ結合しない。
- 同一性を安全に確定できない場合は、実体を新規発見として扱い、旧索引を独立した不存在候補として扱う。
- Folderの外部Rename、Move、削除では、配下項目の相対Path、親子関係、欠損状態が互いに矛盾しない状態へ収束する。
- 同名競合、孤児、未知User、不正Path、同一性の曖昧さを検出した項目は、誤った通常項目として公開せず、運用者が確認可能な形で隔離・記録する。

### 4. inotify変更検知

- Linux inotifyにより正式User領域の作成、更新、書込完了、Rename、Move、削除を検知する。
- 新規Folderを監視対象へ追加し、削除・移動されたFolderの不要なwatchを解放する。
- 対応付け可能なMoveイベントを組として処理し、対応付け不能または順序不明なイベントは現在のHDD状態の再照合へ収束させる。
- イベントは実体変更のHintとして扱い、処理時に現在のStorage状態と対象Pathを再確認する。
- 同一PathへのBurst、重複、順序逆転、同一Path再作成を冪等に処理し、Queueを無制限に増やさない。
- Queue overflow、inotify watch limit、監視停止、イベント欠落を検知し、全件再スキャンを要求する。
- Process停止中または監視開始前後の変更は、起動時再スキャンで回収する。
- KuraStorage内部操作に由来するイベントも安全に再照合し、同じ索引更新やVersion増分を重複実行しない。

### 5. `MISSING_CANDIDATE`と`MISSING`

- HDDが`AVAILABLE`で、正常に完走した走査または対象再確認により実ファイルが存在しない場合だけ`MISSING_CANDIDATE`にする。
- `MISSING_CANDIDATE`判定とは別時刻の独立した再確認でも不存在の場合だけ`MISSING`へ確定する。
- 単一の削除イベントだけを根拠として、項目を直接`MISSING`へ確定しない。
- HDD未Mount、Storage ID不一致、読取不能、走査中断、列挙不完全、権限Error等では欠損状態を新規確定または進行させない。
- HDD全体が利用不可になっても、全FileEntryを`MISSING_CANDIDATE`または`MISSING`へ変更しない。
- 欠損候補または欠損確定項目の実体が再発見された場合、Metadataを再照合して承認済み通常状態へ復帰させる。
- 走査開始後の同時作成、移動、削除や通常File操作を古いSnapshotで上書きせず、条件付き更新または再照合へ戻す。
- Root、`TRASHED`項目、処理中のFileOperationを通常の個別欠損判定で誤って状態変更しない。

### 6. 再確認と索引削除

- 認証済み利用者は、自分が所有する`MISSING_CANDIDATE`または`MISSING`項目をFile IDで明示再確認できる。
- 再確認時はHDDが`AVAILABLE`であること、Storage ID、対象Pathの安全性を再検証する。
- 実体が再発見された場合はMetadataと状態を更新し、同じ索引項目を通常利用可能な状態へ復帰させる。
- 不存在が継続する場合は、二段階判定規則に従って状態と最終確認日時を冪等更新する。
- Storage利用不可、Storage ID不一致、権限Error等では不存在継続を確定せず、再試行可能なErrorを返す。
- 認証済み利用者は、自分が所有する確定`MISSING`項目だけを「一覧から削除」できる。
- 「一覧から削除」はHDD上のFile・Folderを削除、移動、作成、変更しない。
- 索引削除時はFileEntryと実装済みの関連管理情報を同じDB Transaction内で削除する。
- 未実装の検索、共有、最近使用、派生データ等の空テーブルや仮機能は追加せず、将来の関連情報が削除処理へ参加できるApplication境界を維持する。
- Folder索引の再確認・削除では、欠損子孫、部分的に再発見された子孫、親子関係を一貫した規則で扱う。
- `ACTIVE`、`MISSING_CANDIDATE`、`TRASHED`、Root、他Userの項目を一覧から削除できない。

### 7. File APIとAndroid表示

- File一覧・詳細APIは`MISSING_CANDIDATE`と`MISSING`を返し、既存Protocolと非互換になる場合はHealthのProtocol VersionでClientを更新要求へ止める。
- `MISSING`項目は通常Fileと区別できる状態、理由、検知日時を表示できる。
- `MISSING`項目へのDownload、Rename、Move、Trash等の実体を必要とする操作を、安定したError Codeで拒否する。
- File Open直前に不存在を検出した場合は候補化・再確認へ収束させ、API要求内の1回の確認だけで`MISSING`を確定しない。
- Androidは一覧・詳細で`MISSING`項目を通常項目と区別し、「ファイルが見つかりません」と利用者に分かる形で表示する。
- Androidは`MISSING`項目に「再確認」と「一覧から削除」を提供し、処理中の二重送信を防ぐ。
- 「一覧から削除」の確認表示は、HDD上の実ファイルを削除する操作ではなく、KuraStorageの管理情報を消す操作であることを明示する。
- 再確認または索引削除の成功後はServerから一覧・詳細を再取得し、通信結果不明時にClientが成功を推測しない。
- AndroidはStorage利用不可、再試行可能Error、再発見、欠損継続、競合を区別して表示する。

### 8. 競合、安全性、観測性、運用

- 全件再スキャン、個別イベント処理、再確認、索引削除は、Upload、Folder作成、Rename、Move、Trash、Restore、Purge、FileOperation Recoveryと競合しても索引を古い状態へ戻さない。
- FileOperationが中間状態のPathを欠損確定または外部変更として誤処理せず、操作完了後の再照合へ収束させる。
- 同じイベント、Scan、再確認、索引削除の再実行は冪等であり、重複FileEntry、重複削除、状態巻き戻りを起こさない。
- Workerは起動時、設定周期、監視異常時に全件再スキャンを実行し、Storage利用不可中は欠損判定を停止する。
- 同一HDDが`AVAILABLE`へ復帰した場合は監視を再構築し、全件再スキャン後に通常イベント処理へ戻る。
- Storage IDが異なるHDDを既存索引へ自動結合しない。
- 再スキャン周期、Batch Size、Queue上限、debounce、再試行Backoff等を型付き設定で検証する。
- Watcher状態、最終成功Scan、Scan遅延、Queue長、Overflow、追加・更新・候補・欠損・隔離・失敗件数を低CardinalityのMetricと構造化Logで確認できる。
- Metric Label、通常Log、API Response、AuditへFile名、相対・絶対Path、内容、User ID、Token、Secretを不要に出力しない。
- Worker、管理CLI、Migration、dry-run、本実行、再試行、HDD交換、Rollbackの運用手順を文書化する。

## 受け入れ条件

### 管理対象・全件再スキャン

- [ ] 正式User領域のFile・Folderを所有User、親子関係、相対Pathと対応付けて索引化できる。
- [ ] Storage Root外、未知User、不正Path、Symbolic Link、特殊File、内部管理領域を通常索引へ公開しない。
- [ ] 全件再スキャンが外部追加、内容更新、Rename、Move、削除をHDDの現在状態へ収束させる。
- [ ] 内容変更時だけ`fileVersion`が増加し、名前変更または移動だけでは増加しない。
- [ ] 外部Rename・Moveの同一性が一意な場合だけ既存File IDを維持し、曖昧な項目を誤結合しない。
- [ ] Folderの外部変更後も、子孫のPath、親子関係、状態に矛盾が残らない。
- [ ] dry-runは実行予定差分を分類し、DBとHDDを変更しない。
- [ ] 同時全件再スキャンが1実行に制限され、失敗・取消・中断したScanを成功として記録しない。

### inotify・定期追従

- [ ] Linux inotifyが作成、更新、書込完了、Rename、Move、削除を検知し、共通照合処理へ渡す。
- [ ] 重複、順序逆転、Burst、対応付け不能Move、同一Path再作成を処理しても重複索引や状態巻き戻りが発生しない。
- [ ] Queue overflow、watch limit、監視停止を検出し、全件再スキャンへ自動的に収束する。
- [ ] Worker停止中および監視開始前後の変更を、起動時再スキャンで回収できる。
- [ ] 設定周期の正常な全件再スキャン完了後、走査対象に未反映のHDD・DB差分が残らない。
- [ ] Event Burstおよび全件再スキャン中も、Queue、Memory、CPU、DB、HDD I/Oが設定した上限内で処理される。

### `MISSING_CANDIDATE`・`MISSING`

- [ ] HDDが`AVAILABLE`で実体の不存在を確認した場合だけ`MISSING_CANDIDATE`へ遷移する。
- [ ] 初回不存在とは別時刻の独立した再確認でも不存在の場合だけ`MISSING`へ遷移する。
- [ ] 単一削除イベント、未完走Scan、走査中断、権限Errorから直接`MISSING`へ遷移しない。
- [ ] HDD未Mount、Storage ID不一致、読取不能時に全FileEntryを`MISSING_CANDIDATE`または`MISSING`へ変更しない。
- [ ] 欠損候補または欠損確定項目の実体を戻した場合、再確認またはScanで同じ索引項目を通常状態へ復帰できる。
- [ ] Root、`TRASHED`、処理中のFileOperationを通常の欠損項目として誤判定しない。

### 再確認・一覧から削除

- [ ] 所有Userが`MISSING_CANDIDATE`または`MISSING`項目を明示再確認できる。
- [ ] 実体が再発見された場合、Metadataと状態を更新して通常利用へ復帰できる。
- [ ] Storage利用不可時の再確認は欠損継続を確定せず、再試行可能Errorになる。
- [ ] 所有Userが確定`MISSING`項目を「一覧から削除」できる。
- [ ] 「一覧から削除」ではHDD操作が0回であり、FileEntryと実装済み関連管理情報だけがDBから削除される。
- [ ] `ACTIVE`、`MISSING_CANDIDATE`、`TRASHED`、Root、他User項目の索引削除を拒否する。
- [ ] 再確認、索引削除、全件Scan、同一Path再作成が競合しても、再発見済み実体の誤削除や重複索引を起こさない。

### API・Android

- [ ] File一覧・詳細で通常、`MISSING_CANDIDATE`、`MISSING`を区別できる。
- [ ] File内容を開く直前にHDD上の存在を確認し、不存在の実体を配信しない。
- [ ] `MISSING`項目への実体操作を安定したError Codeで拒否し、他Userには存在やMetadataを漏えいしない。
- [ ] Androidが`MISSING`項目へ欠損表示、再確認、「一覧から削除」を提供する。
- [ ] Androidが「一覧から削除」を実ファイル削除と誤認させない確認表示を行い、二重送信を防ぐ。
- [ ] Androidは成功後にServer一覧を再取得し、通信結果不明時に成功を推測しない。
- [ ] 旧Client、未知Status、旧Serverを承認済みProtocol契約で安全に扱い、非互換ClientをFile APIの前で更新要求へ止める。

### 競合・回帰・運用

- [ ] Upload、Folder作成、Download、Rename、Move、Trash、Restore、Purge、Recoveryが監視・再スキャン追加後も成功する。
- [ ] 内部File操作と外部変更が競合しても、DB・HDD不整合、二重索引、誤Version増分、誤欠損を残さない。
- [ ] Worker、API、DBの停止・再起動、イベント欠落、同一HDD再接続後に索引が実体へ収束する。
- [ ] 異なるStorage IDのHDDを既存索引へ自動結合しない。
- [ ] 30万件相当の走査とEvent Burstについて、時間、CPU、Memory、DB負荷、HDD I/Oを測定・記録する。
- [ ] Raspberry Pi、PostgreSQL、共有exFAT HDD、Android実機で外部変更、欠損二段階判定、再確認、索引削除を確認できる。
- [ ] Server・Worker・Androidの必須CI、Migration適用・Rollback、Security検査、実機E2Eが成功する。
- [ ] 正式文書、Steering文書、OpenAPI、設定例、配置・運用手順と実装が一致する。

## 成功指標

- HDD全体利用不可を原因とする誤った`MISSING_CANDIDATE`または`MISSING`遷移: 0件。
- `MISSING_CANDIDATE`を経由しない`MISSING`確定: 0件。
- 次回正常な全件再スキャン完了後に残る、走査対象HDDとDB索引の未分類差分: 0件。
- Event重複、欠落、順序逆転、Worker再起動による重複FileEntryまたは状態巻き戻り: 0件。
- 名前変更または移動だけを原因とする`fileVersion`増分: 0件。
- 「一覧から削除」によるHDD操作、再発見済み実体の誤削除、他User索引削除: 0件。
- Storage Root外またはSymbolic Link経由の索引化・読取り: 0件。
- File名、物理Path、内容、Token、SecretのMetric Label・API Responseへの漏えい: 0件。
- 30万件相当の全件再スキャンとEvent Burstが、承認済み設計で定める資源・時間上限内に完了する。

## スコープ外

以下はこの作業では実装しない。

- ファイル名検索、全文検索、OCR、Tag、最近使用、お気に入り。
- File・Folder共有、共有権限、共有一覧。
- Thumbnail、低・中画質派生データ、動画変換、Cache生成・清掃。
- Android自動バックアップ、MediaStore・SAF差分監視、Room、WorkManager。
- KuraStorage外の任意Path、他用途領域、Syncthing受入領域の索引化。
- File Browser、Syncthing等の外部ツールからKuraStorage正式領域へ安全に書き込むための連携Protocol。
- 複数HDD、複数Storage Root、Remote Object Storage、NASの横断索引。
- 異なるStorage IDのHDDを既存索引へ自動Migrationまたは自動統合する機能。
- 同一性が曖昧な外部Rename・MoveをChecksum全件計算等で強制的に自動結合する機能。
- `MISSING`実体のBackup、第二媒体、外部Cloudからの自動復元。
- Androidからの物理Path表示・指定、HDD上の実体を直接操作する管理File Browser。
- Public Internetへ公開する索引管理画面またはMetrics Endpoint。

## 制約・前提

- 現行の単一Raspberry Pi、単一PostgreSQL、単一共有exFAT HDD、単一Storage Root構成を前提とする。
- KuraStorage外のツールによる正式領域変更は可能だが、運用上は管理・緊急用途に限定し、File Browserは原則読取り専用を推奨する。
- exFATの補助的なinode・File ID・更新時刻は、単独で永続的な同一性を保証しない。
- Storage Root、`storageId`、固定UID・GID・Mask、Symbolic Link拒否、物理Path非公開の既存境界を維持する。
- DB Migrationは管理CLIで明示適用し、APIまたはWorker起動時に自動適用しない。
- Workerは既存`KuraStorage.Worker`へ独立した索引Jobとして追加し、API Endpoint処理内でHDD全件走査を行わない。
- ClientはFile IDまたはFolder IDだけを使用し、相対Pathまたは物理絶対Pathを指定しない。
- 本要求で使用する時間はServer UTCを正とし、Client時刻を欠損判定へ使用しない。

## 参照ドキュメント

- `docs/product-requirements.md` 2.2、4.5、6.2、6.4、7.10.3、7.10.4
- `docs/functional-design.md` 2、3、5.2、6.2.3〜6.2.5、変更検知、索引修復、MISSING関連節
- `docs/architecture-design.md` 6.4、8、9、11.6、16、21
- `docs/repository-structure.md` Server、Worker、管理CLI、Android、Test、配置構成の関連節
- `docs/development-guidelines.md` Storage安全境界、Test、Migration、Security、Pull Request運用の関連節
- `.steering/20260722-kurastorage-mvp/` - 既存Storage Guard、FileEntry、FileOperation Recovery、Android File UIの基礎
- `.steering/20260817-file-rename-move/` - 既存Rename・Move、Path・Lock・Recovery規則
- `.steering/20260820-trash-permanent-delete-retention/` - Worker、Batch処理、関連情報削除、実機運用の既存パターン
- `.steering/20260822-resumable-chunk-upload/` - Upload確定、Cleanup・Recovery競合、Worker配置の既存パターン
- `.steering/20260822-external-change-missing-management/tasklist.md` - 本要求の実装・検証タスクとPull Request分割
