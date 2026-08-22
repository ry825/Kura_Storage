# 外部変更追従・MISSING管理 タスクリスト

## 対象

- 正式要件: `docs/product-requirements.md` 7.10.3「MVP後: ファイル索引の外部変更追従」および7.10.4「MVP後: MISSING状態」
- 目的: HDD上の実ファイル・内容・物理階層を正とし、inotifyと定期再スキャンでPostgreSQL索引を継続的に修復できる状態を確立する。
- 完了条件: 外部追加・更新・名前変更・移動・削除、監視停止・イベント欠落・HDD切断を安全に処理し、`MISSING_CANDIDATE`、`MISSING`、再確認、索引削除をServer・Android・運用環境で一貫して扱える。

## 作業開始前の前提

- [x] 同じ作業ディレクトリの`requirements.md`が作成・承認されている。
- [x] 同じ作業ディレクトリの`design.md`が作成・承認されている。
- [x] 承認済み設計と本タスクリストに差分がある場合、本タスクリストを実装前に更新して承認内容へ合わせる。
- [x] `docs/product-requirements.md`、`docs/functional-design.md`、`docs/architecture-design.md`、`docs/repository-structure.md`、`docs/development-guidelines.md`の関連節と矛盾がない。
- [x] 未Mergeの依存Pull Requestがなく、最新`main`から最初の作業Branchを作成できる。（PR #15のMergeとCI成功を確認し、最新`main`から`feat/external-change-missing-rescan`を作成）

## タスク完全完了の原則

**このファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止する。**

- フェーズとPull Requestは上から順に実施する。
- 実装と対応する自動Test・手動確認・文書更新を同じPull Requestに含める。
- 選択したPull Request単位に未完了タスクを残したまま作業を終了しない。
- 後続Pull Requestは、依存元Pull Requestが`main`へMergeされた後に最新`main`から開始する。
- 技術的理由で不要になったタスクだけ、取消理由と代替実装を明記して完了扱いにできる。
- 検索、共有、派生データ生成そのものは追加しない。ただし、将来の関連データを安全に無効化・削除できるApplication境界は維持する。

---

## PR1: 索引整合性モデル・全件再スキャン・管理CLI

### 1.1 要件対応表と既存境界の確定

- [x] PR1の実装範囲を承認済み要件・設計へ対応付ける。
  - [x] 外部追加、内容更新、名前変更、移動、削除ごとの索引収束結果を列挙する。
  - [x] `ACTIVE`、`MISSING_CANDIDATE`、`MISSING`、`TRASHED`、Root、Upload一時領域、Trash Containerの走査対象・除外規則を確定する。
  - [x] `users/{ownerUserId}/files`から所有者を解決し、未知User、管理外Path、不正な階層を通常索引へ公開しない規則を確定する。
  - [x] 外部Rename・Moveで既存File IDを維持できる条件と、同一性が曖昧な場合に誤結合しない規則を確定する。
  - [x] Folderの外部移動・削除時に子孫のPathと状態を一貫して更新する規則を確定する。
  - [x] KuraStorage API操作、FileOperation Recovery、Upload確定、Trash・Restore・Purgeと再スキャンが競合する場合のLock順序と再試行規則を確定する。

### 1.2 Domainモデル・DB Schema・Migration

- [x] `FileEntryStatus`と状態遷移を拡張する。
  - [x] `MISSING_CANDIDATE`と`MISSING`を追加する。
  - [x] Status列の最大長を32へ拡張し、状態と`missing_*`列の整合をDB Check Constraintで保証する。
  - [x] HDDが`AVAILABLE`のときだけ`ACTIVE`から`MISSING_CANDIDATE`へ遷移できる。
  - [x] 異なるObservationかつ既定5分以上後の再確認で不存在の場合だけ`MISSING_CANDIDATE`から`MISSING`へ遷移できる。
  - [x] 再発見時に`MISSING_CANDIDATE`または`MISSING`から承認済み状態へ復帰し、欠損情報を消去できる。
  - [x] `TRASHED`、Root、処理中のFileOperationを誤って欠損遷移させない。
- [x] 索引照合に必要なMetadataを`FileEntry`へ追加する。
  - [x] HDD上の更新日時、補助的なファイル同一性情報、欠損初回検知日時、最終確認日時を保持する。
  - [x] 補助的なinode・File IDだけを同一性の唯一の根拠にしない。
  - [x] 内容変更時だけ`fileVersion`を増分し、配置変更だけでは増分しない。
  - [x] Size、MIME、更新日時、Checksumの取得・更新規則を承認済み設計へ合わせる。
- [x] 再スキャン実行状態を永続化する。
  - [x] Scan ID、種別、開始・完了日時、状態、走査件数、差分件数、Error分類を保持する。
  - [x] APPLY Scanは永続StagingへBatch保存し、正常完了後または保持期限超過時に清掃する。
  - [x] DRY_RUNは専用Connectionの一時Stagingだけを使用し、Scan Runを含む永続DB変更を残さない。
  - [x] 同時全件再スキャンをDB Lockまたは同等の仕組みで1実行に制限する。
  - [x] 中断・失敗したScanを成功扱いにせず、途中結果から`MISSING`を確定しない。
- [x] EF Core設定とMigrationを追加する。
  - [x] `file_entries`のStatus制約、一覧用部分Index、一意制約を新状態と両立させる。
  - [x] `MISSING`項目が同一Pathの再発見や新規索引作成を妨げない一意性規則を実装する。
  - [x] Migration Up/Down、既存`ACTIVE`・`TRASHED`行、Rollback条件を確認する。

### 1.3 安全なFilesystem走査境界

- [x] 全件再スキャン用Filesystem abstractionを追加する。
  - [x] Storage Rootから相対Path、種類、Size、更新日時、補助同一性情報をStreaming列挙する。
  - [x] Storage IDとMount状態を走査開始前・走査中・確定直前に検証する。
  - [x] Storage Root外、絶対Path、Path Traversal、Symbolic Link、循環、特殊Fileを拒否または隔離する。
  - [x] Userの正式`files`領域だけを索引化し、`.storage-identity`、一時Upload、Trash内部、派生・内部管理領域を規則どおり除外する。
  - [x] 個別Pathの権限・I/O ErrorとHDD全体利用不可を区別する。
  - [x] 30万件規模でも全件をMemoryへ保持せず、Batch処理とCancellationを行う。

### 1.4 全件再スキャンApplication Service

- [x] HDD SnapshotとDB索引を冪等に照合する。
  - [x] 外部追加されたFile・Folderを所有Userと親Folderへ対応付けて索引へ追加する。
  - [x] 親Folderから順に処理し、孤児、未知User、不正Path、同名曖昧項目を通常一覧へ公開しない。
  - [x] 既存FileのSize、MIME、更新日時、内容変更を反映し、内容変更時だけ`fileVersion`を増分する。
  - [x] 外部Rename・Moveを承認済み同一性規則で一意に判定できる場合だけ既存IDのPath・Parent・Nameへ反映する。
  - [x] 同一性が曖昧な項目を誤った既存IDへ結合せず、新規発見と不存在候補へ分離する。
  - [x] Folder配置変更時に子孫Pathを同一Transactionまたは再試行可能なBatchで整合させる。
- [x] 不存在の二段階判定を実装する。
  - [x] 完走したScanで未発見の対象だけを`MISSING_CANDIDATE`にする。
  - [x] 別時刻の完走したScanまたは明示再確認でも不存在の場合だけ`MISSING`へ確定する。
  - [x] HDD全体利用不可、Storage ID不一致、Scan中断、列挙不完全、権限Error時は対象を`MISSING`へ進めない。
  - [x] 欠損候補が再発見された場合は通常状態へ復帰させる。
  - [x] 走査開始後に作成・移動・削除された項目を誤確定しないよう、Scan世代と確定時再検証を行う。
- [x] 既存操作との競合を制御する。
  - [x] FileOperationが`PENDING`または`FILESYSTEM_DONE`のPathを確定対象から除外または延期する。
  - [x] Rename、Move、Upload、Trash、Restore、Purge、Recoveryと同じ索引行を更新する際に条件付きUPDATE・Version確認を行う。
  - [x] 競合時は古いSnapshotで上書きせず、再照合対象へ戻す。
  - [x] 1件の失敗でScan全体を即座に破棄する条件と、個別隔離して継続できる条件を区別する。

### 1.5 管理CLI・観測性

- [x] `KuraStorage-admin index rescan`を実装する。
  - [x] Application Serviceを呼び出し、CLIから直接SQLを実行しない。
  - [x] dry-runで追加・更新・移動・候補化・欠損確定・隔離予定件数を表示し、DBを変更しない。
  - [x] 通常実行でScan IDと集計結果を表示し、二重起動時は明確に拒否する。
  - [x] HDD利用不可、Storage ID不一致、取消、部分失敗を成功終了にしない。
- [x] 低CardinalityのMetric・構造化Log・Auditを追加する。
  - [x] Scan時間、走査件数、追加、更新、再発見、候補化、欠損確定、隔離、失敗件数を記録する。
  - [x] User ID、File ID、File名、相対・絶対Path、内容、SecretをMetric Labelへ含めない。
  - [x] LogとCLI通常出力へ物理絶対Pathや個人情報を不要に出さない。

### 1.6 PR1自動Test

- [x] Domain・Application Testが完了している。
  - [x] 全状態遷移、禁止遷移、別時刻再確認、再発見、内容Version更新をTestする。
  - [x] 外部追加・内容更新・Rename・Move・削除・Folder子孫更新をTestする。
  - [x] 曖昧な同一性、同名競合、孤児、未知User、特殊Fileを誤索引しないことをTestする。
  - [x] Scan世代、条件付き更新、通常File操作との競合をTestする。
- [x] Infrastructure・Integration Testが完了している。
  - [x] Migration Up/Down、Status制約、部分Index、一意制約、Scan LockをPostgreSQLでTestする。
  - [x] APPLYの中断Staging保持・期限清掃と、DRY_RUNの一時Staging破棄・永続DB非変更をTestする。
  - [x] 実Filesystemで大文字小文字差、Unicode、深い階層、空Folder、Symbolic Link、列挙中変更をTestする。
  - [x] HDD未Mount、Storage ID不一致、read-only、権限Error、I/O Error、Scan中断で誤って`MISSING`にしない。
  - [x] 30万件相当をBatch走査し、Memory使用量が件数比例で無制限に増えないことを確認する。
  - [x] dry-runがDBとFilesystemを変更せず、通常実行と同じ差分分類になることを確認する。
- [x] PR1の標準検証が成功している。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-deployment.sh`が既存配置へ回帰しない。
  - [x] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [x] `git diff --check`が成功する。

### 1.7 PR1文書・セルフレビュー・完了

- [x] PR1の文書と実装を整合する。
  - [x] 正式文書へ確定した状態遷移、Schema、全件Scan、CLI、競合規則を反映する。
  - [x] Migration、dry-run、本実行、失敗時再実行、Rollback手順を運用文書へ記載する。
  - [x] OpenAPIやAndroid契約はPR1で不要に変更していない。
- [x] PR1差分をセルフレビューする。
  - [x] HDDを正とし、DB Snapshotだけで存在や欠損を断定していない。
  - [x] `MISSING_CANDIDATE`を経由せず`MISSING`へ確定する経路がない。
  - [x] 検索、共有、派生データ本体や不要なPackageを先行追加していない。
  - [x] Credential、実環境情報、物理Path、生成物が差分にない。
- [x] PR1を完了する。
  - [x] 1.1〜1.7のPR1対象項目がすべて`[x]`である。
  - [x] Commit、Push、英語のPull Request作成、CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR1完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR2: inotify追従・定期再スキャン・Worker運用

### 2.1 Linux inotify監視Adapter

- [x] Storage Rootの変更イベントを監視する。
  - [x] Linux libcへの限定P/Invokeでinotifyを使用し、正式User領域の作成、更新、Close Write、Rename、Move、削除を取得する。
  - [x] Move cookieを設定済みWindow内で対応付け、片側だけのMoveは個別Path再照合へ変換する。
  - [x] 監視開始前後の隙間を起動時再スキャンで補完する。
  - [x] 新規Folderへ監視を追加し、削除・移動済みFolderの監視を解放する。
  - [x] Cookie等で対応付け可能なMoveを組にし、未対応イベントは個別再照合へ収束させる。
  - [x] Burstをdebounce・coalesceし、同じPathへの大量イベントで無制限にQueueを増やさない。
  - [x] Queue overflow、watch limit、イベント欠落、監視停止を検出し、全件再スキャンを要求する。
  - [x] KuraStorage内部の一時・Trash・派生・管理領域イベントを除外しつつ、正式配置後の状態は取りこぼさない。
  - [x] Symbolic Link、Storage Root外へ解決されるPath、特殊Fileをイベント経由でも通常索引へ公開しない。

### 2.2 Index Event処理

- [x] `IndexEventWorker`のApplication処理を実装する。
  - [x] EventをHintとして扱い、処理時にHDDの現在状態とStorage状態を再確認する。
  - [x] 作成・更新・Rename・Move・削除をPR1の共通照合処理へ委譲し、別の状態遷移を重複実装しない。
  - [x] 外部更新とKuraStorage内部操作の競合時にFileOperation・DB Versionを再確認する。
  - [x] Event順序逆転、重複、欠落、同一Path再作成を冪等に処理する。
  - [x] 単一イベントの不存在だけで`MISSING`へ確定せず、候補化後の再確認を予約する。
  - [x] HDD利用不可時はEventを根拠に欠損状態を更新せず、利用可能復帰後の再スキャンへ収束させる。

### 2.3 定期・起動時再スキャン

- [x] `FullRescanWorker`を既存`KuraStorage.Worker`へ追加する。
  - [x] Worker起動時、設定周期、inotify overflow・監視異常時にPR1の全件再スキャンを実行する。
  - [x] DB Lockで複数Worker・管理CLIとの全件Scan重複を防ぐ。
  - [x] `Enabled`、再スキャン周期、起動時実行、Batch Size、debounce、Move pairing、Queue上限、欠損確認間隔、再試行Backoff、Staging保持を型付きOptionsにする。
  - [x] 既定値と安全な上下限を設定し、起動時に不正設定を拒否する。
  - [x] 1件のPoison Event、監視再作成失敗、DB一時障害でWorker全体が永久停止しない。
  - [x] API、TrashPurgeWorker、Upload Cleanup、FileOperation Recoveryと不要に直列化しない。
- [x] Storage状態遷移と監視Lifecycleを連携する。
  - [x] `UNAVAILABLE`またはStorage ID不一致で監視・再確認を停止する。
  - [x] `AVAILABLE`復帰時に監視を作り直し、全件再スキャン完了後に通常イベント処理へ戻る。
  - [x] 再接続後も別HDDの内容を既存索引へ結合しない。
  - [x] Worker停止・再起動中のイベント欠落を起動時再スキャンで修復する。

### 2.4 配置・Security・観測性

- [x] Raspberry Pi配置を更新する。
  - [x] Workerのsystemd Unit、権限、HDD read access、Restart、停止Timeoutをinotify・再スキャンへ対応させる。
  - [x] inotify watch上限の必要値と確認方法を文書化し、無制限なsysctl変更を行わない。
  - [x] `appsettings.example.json`とdeployment設定へ監視・再スキャンOptionsを追加し、PR3のProtocol対応完了までは`Indexing.Enabled=false`を既定にする。
  - [x] Install、Upgrade、Rollback、Verify ScriptへWorker状態と設定検証を追加する。
- [x] 運用上必要な状態を観測できる。
  - [x] Watcher稼働、最終Event時刻、Queue長、Overflow、最終成功Scan、Scan遅延、候補・欠損件数を低Cardinalityで計測する。
  - [x] HDD利用不可と個別File欠損を別のLog・Metricとして識別できる。
  - [x] Path、File名、User ID、File IDをMetric Labelにせず、Logも必要最小限の識別子に限定する。
  - [x] 異常時の手動dry-run、再スキャン、Worker再起動、watch上限確認手順を記載する。

### 2.5 PR2自動Test

- [x] inotify・Event処理Testが完了している。
  - [x] 外部作成、連続更新、Rename、同一Folder内Move、Folder間Move、削除、再作成をTestする。
  - [x] Event重複、順序逆転、対応しないMove、Burst、debounce、Queue上限をTestする。
  - [x] overflow、watch limit、監視停止から全件再スキャンへ収束することをTestする。
  - [x] Linux native descriptorを停止時に解放し、read loopをCancellationで終了できることをTestする。
  - [x] 内部操作のEventと外部Eventが競合しても二重索引、誤Version増分、誤欠損を作らない。
- [x] Worker・Integration Testが完了している。
  - [x] 起動時・周期・異常時Scan、Global Lock、Backoff、取消、graceful shutdownをTestする。
  - [x] Worker停止中に加えた変更が再起動後のScanで索引へ反映される。
  - [x] HDD切断中に全Fileが`MISSING`にならず、同一HDD再接続後に正しく収束する。
  - [x] 別Storage ID、read-only、権限Error、DB一時障害から安全に復帰する。
  - [x] 30万件相当とEvent BurstでQueue・Memory・DB負荷が設定上限内に収まる。
- [x] PR2の標準検証が成功している。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-deployment.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が既存Android実装に対して成功する。
  - [x] `git diff --check`が成功する。

### 2.6 Raspberry Pi実機確認

- [x] 実HDD・Linux inotifyで外部変更追従を確認する。
  - [x] 配置前にPostgreSQLとStorage RootのBackupを取得し、復元可能性を確認する。
  - [x] MigrationとWorkerを配置し、API、Nginx、PostgreSQL、HDD、Storage IDを既存Verify手順で確認する。
  - [x] HDD上でFile・Folderを外部作成、更新、Rename、Move、削除し、DB索引が設計時間内に収束する。
  - [x] 大量変更と意図的な監視停止でイベント欠落を発生させ、再スキャンで修復する。
  - [x] HDD取外し中に全件欠損化せず、同一HDD再接続後に差分を回収する。
  - [x] Worker再起動、API操作との同時変更、DB一時停止から誤索引なく復旧する。
- [x] 資源・性能を実機確認する。
  - [x] 30万件相当の基準データまたは再現可能な縮尺データで走査時間、CPU、RSS、DB負荷、HDD I/Oを測定する。
  - [x] Event Burst・全件Scan中も一覧、Download、Upload、認証更新が許容範囲で応答する。
  - [x] 測定条件、件数、変更数、所要時間、最大Memory、HDD条件を`docs/testing/`へ記録する。

### 2.7 PR2文書・セルフレビュー・完了

- [x] PR2の文書と実装を整合する。
  - [x] 正式文書へinotify、Event Queue、overflow、起動時・定期Scan、Storage復帰手順を反映する。
  - [x] 設定値を実測で変更した場合は根拠と影響を記録する。（既定値は変更せず、実測結果とwatch上限の警告を記録）
  - [x] Watcher停止、全件Scan失敗、HDD交換時の運用Runbookを更新する。
- [x] PR2差分をセルフレビューする。
  - [x] inotifyを唯一の正とせず、再スキャンで欠落を修復できる。
  - [x] Event Handlerが長時間I/OやDB処理を直接抱えず、boundedな処理境界になっている。
  - [x] systemd権限、sysctl、設定値が必要最小限で、秘密情報や実環境値を追跡していない。
  - [x] 検索、共有、派生データ本体を追加していない。
- [x] PR2を完了する。
  - [x] 2.1〜2.7のPR2対象項目がすべて`[x]`である。
  - [x] Commit、Push、英語のPull Request作成、CI成功確認を完了する。
  - [x] `steering`スキルのモード3-AでPR2完了記録を追記し、同じBranchへCommit・Pushする。
  - [x] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## PR3: MISSING API・索引削除・Android表示と再確認

### 3.1 File一覧・詳細・Content access契約

- [x] File APIで欠損状態を一貫して扱う。
  - [x] 一覧・詳細Responseへ`MISSING_CANDIDATE`と`MISSING`を追加し、Healthの`protocolVersion`を2へ更新して非互換ClientをFile APIの前で更新要求へ止める。
  - [x] 通常File、候補、確定欠損をClientが区別でき、欠損理由と検知日時を表示できる情報を返す。
  - [x] 一覧はDB索引から取得し、要求ごとにHDD全走査を行わない。
  - [x] Download・内容Open直前にHDD存在とStorage状態を確認する。
  - [x] Open時に不存在を検出しても要求内だけで`MISSING`を確定せず、候補化・再確認へ収束させる。
  - [x] `MISSING`項目へのDownload、Rename、Move、Trash等の禁止操作を安定したError Codeで拒否する。
  - [x] 他Userの欠損項目の存在、Path、Metadataを漏えいしない。

### 3.2 明示再確認API

- [x] `MISSING`項目の再確認Use CaseとEndpointを実装する。
  - [x] 所有Userの`MISSING`または`MISSING_CANDIDATE`項目だけをIDで再確認できる。
  - [x] HDDが`AVAILABLE`であることを確認して対象Pathを安全に再照合する。
  - [x] 再発見時はMetadata・Parent・状態を更新し、通常一覧へ復帰させる。
  - [x] 不存在継続時は状態と最終確認日時を冪等更新する。
  - [x] HDD利用不可、Storage ID不一致、権限Errorでは欠損継続を確定せず、明確な再試行可能Errorを返す。
  - [x] 再確認と外部再作成、全件Scan、索引削除の競合を条件付き更新で処理する。

### 3.3 「一覧から削除」APIと関連情報整理

- [x] `MISSING`索引削除Use CaseとEndpointを実装する。
  - [x] 所有Userの確定`MISSING`項目だけを明示操作で削除できる。
  - [x] `ACTIVE`、`MISSING_CANDIDATE`、`TRASHED`、Root、他User項目を拒否する。
  - [x] 削除処理はHDDの削除・移動・作成を一切行わない。
  - [x] FileEntryと現時点で実装済みの関連管理情報を同一DB Transactionで削除する。
  - [x] DB管理情報だけを扱う`IFileIndexDeletionParticipant`を物理完全削除境界から分離し、将来の共有、検索補助、Recent、派生データが同じ整理処理へ参加できる構造にする。
  - [x] Folder索引削除時の欠損子孫、部分再発見、親子関係を承認済み規則で一貫して処理する。
  - [x] 同じ要求の再送、全件Scanとの競合、削除直前の索引上の再発見で誤削除しない。
  - [x] 監査ログへ明示操作と結果を記録し、物理PathやFile名を不要に残さない。

### 3.4 Android Data・Domain・UI

- [x] AndroidのAPI ContractとDomainモデルを拡張する。
  - [x] 未知Statusを安全に扱い、旧Server・旧ClientをProtocol不一致時の更新要求へ止める。
  - [x] 再確認と一覧から削除をRepository・UseCase境界へ追加する。
  - [x] 通信結果不明時に再確認成功や索引削除成功を推測せず、Server一覧を再取得する。
- [x] 一覧・詳細で`MISSING`を表示する。
  - [x] 通常Fileと区別できる状態、理由、検知日時、利用不可操作をAccessibility対応で表示する。
  - [x] `MISSING_CANDIDATE`は誤確定を避ける表示規則に従い、通常項目との違いを必要な範囲で示す。
  - [x] `MISSING`項目から「再確認」を実行し、処理中の二重送信を防ぐ。
  - [x] 「一覧から削除」はHDD上のFile削除ではないことを明記して確認を求める。
  - [x] 再発見成功後と索引削除成功後にServerから一覧を再取得する。
  - [x] HDD利用不可、再試行可能Error、再発見、欠損継続、競合を明確に表示する。

### 3.5 PR3自動Test

- [x] Server Unit・Integration・API Testが完了している。
  - [x] 一覧・詳細・Download直前存在確認・禁止操作の状態別契約をTestする。
  - [x] 再確認の再発見、不存在継続、Storage利用不可、競合、冪等性をTestする。
  - [x] 索引削除でHDD操作が0回であり、FileEntryと実装済み関連情報だけが削除されることをTestする。
  - [x] Folder子孫、部分再発見、同時Scan、同時再作成、二重削除をTestする。
  - [x] 他User、無効Token、非`MISSING`、Rootを所有関係を漏えいしないErrorで拒否する。
  - [x] OpenAPI、Fixture、API Response、共通Error Codeが一致する。
- [x] Android Unit・Compose Testが完了している。
  - [x] Status mapping、未知Status、Repository、UseCase、ViewModel状態遷移をTestする。
  - [x] Health Protocol 1・2の不一致、未知Statusの`UNKNOWN`変換、非互換時にFile APIへ進まないことをTestする。
  - [x] 通常・候補・欠損表示、再確認、確認Dialog、二重Tap、成功後再取得、Error表示をTestする。
  - [x] 画面回転、Back、通信結果不明、再発見、削除済み競合をTestする。
  - [x] Accessibility Semanticsと日本語文言が「実ファイル削除」と誤認させないことを確認する。
- [x] PR3の標準検証が成功している。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./scripts/ci/verify-deployment.sh`が成功する。
  - [x] `./scripts/ci/verify-android.sh`が成功する。
  - [x] `./apps/android/gradlew -p apps/android connectedDebugAndroidTest --max-workers=1`が成功する。
  - [x] `git diff --check`が成功する。

### 3.6 Raspberry Pi・Android実機E2E

- [x] 外部削除からMISSING操作までを実機確認する。
  - [x] Androidで表示中のFileをHDD上から外部削除し、`MISSING_CANDIDATE`から別時刻再確認後の`MISSING`へ遷移する。
  - [x] HDDを外しただけでは全Fileが`MISSING`にならず、AndroidにStorage利用不可として表示される。
  - [x] `MISSING`のPathへ実Fileを戻し、「再確認」で同じ索引項目が通常状態へ復帰する。
  - [x] 不存在のまま「一覧から削除」し、HDD操作なしで一覧・詳細・Downloadから到達不能になる。
  - [x] 外部Rename・Move・内容更新・Folder削除を行い、Android表示とDB索引が実体へ収束する。
  - [x] 監視停止、API・Worker再起動、Event Burst、HDD再接続後も同じ結果へ収束する。
  - [x] 他Userから再確認・索引削除できず、対象の存在やMetadataが漏えいしない。
- [x] 既存機能を実機回帰確認する。
  - [x] Upload、Resume、Download、Range、Folder作成、Rename、Move、Trash、Restore、Purgeが監視・再スキャン導入後も動作する。
  - [x] 外部変更追従中もAPI応答とAndroid操作性が正式な性能目標を満たす。
  - [x] E2E結果、状態遷移、所要時間、失敗注入条件を`docs/testing/`へ記録する。

### 3.7 文書整合・最終セルフレビュー

- [x] 正式文書と実装を整合する。
  - [x] 5つの正式文書、Steering文書、OpenAPI、Migration、Server、Worker、Android、配置・運用手順の名称と状態が一致する。
  - [x] `MISSING_CANDIDATE`、`MISSING`、再確認、索引削除、HDD非操作境界を運用文書へ反映する。
  - [x] Backup・Restore後の全件再スキャン、HDD交換・Storage ID不一致時の復旧手順を更新する。
  - [x] 検索・共有・派生データ追加前に守る索引Consumer境界と索引削除契約を明記する。
  - [x] Android Protocol 2配布、Migration、Server・Worker配置、dry-run、`Indexing.Enabled=true`、本Scanの順でProduction rollout手順を記載する。
- [x] 全体差分をセルフレビューする。
  - [x] HDD上の存在・内容・階層を正とする原則を全経路で維持している。
  - [x] HDD利用不可と個別File不存在を混同する経路がない。
  - [x] Event欠落、Worker停止、再起動、競合後に全件再スキャンで収束できる。
  - [x] 「一覧から削除」がHDD操作を行わず、将来関連情報を残さない拡張境界を持つ。
  - [x] 物理Path、個人情報、Credential、実環境情報、生成物が差分にない。
  - [x] 検索、共有、派生データ生成、自動バックアップを実装範囲へ混入させていない。

### 3.8 PR3完了

- [ ] PR3を完了する。
  - [ ] 3.1〜3.8のPR3対象項目がすべて`[x]`である。
  - [ ] Commit、Push、英語のPull Request作成、CI成功確認を完了する。
  - [ ] `steering`スキルのモード3-AでPR3完了記録を追記し、同じBranchへCommit・Pushする。
  - [ ] Pull Request URLと検証結果をユーザーへ報告して停止する。

---

## 各Pull Request完了記録

各Pull Request作成後に`steering`スキルのモード3-Aを使用して追記する。対象Pull Request内のタスクがすべて完了するまで記録しない。

### PR1: 索引整合性モデル・全件再スキャン・管理CLI

- 完了日: 2026-08-22
- Pull Request: [#16 Add external index reconciliation foundation](https://github.com/ry825/Kura_Storage/pull/16)
- 実施したTest・Build・静的解析: `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-server.sh`（Domain 33件、Application 72件、Integration 70件、失敗0件）、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`./scripts/ci/verify-android.sh`、EF Core `migrations has-pending-model-changes`、`git diff --check`を実施し、すべて成功した。Pull RequestのGitHub Actions（Android、Config、Security、Server）もすべて成功した。
- 手動確認・実機確認: 実HDDとRaspberry Piへの配置はPR1の対象外として未実施。PostgreSQL実DBと一時領域上の実Filesystemを使用したIntegration Testで、Migration、Scan Lock、APPLY・DRY_RUN、状態直列化、競合、Symlink・特殊File隔離を確認した。
- 計画と実装の差分: 承認済みPR1範囲どおり実装した。Linuxの補助同一性情報は追加Packageを使わず`statx`限定P/Invokeで取得し、`MissingCandidate`のDB表現は正式名称`MISSING_CANDIDATE`へ明示変換した。OpenAPIとAndroid契約は変更していない。
- 実装中に追加したタスクと理由: タスクリストへの追加タスクはなし。セルフレビューで列挙途中の権限・I/O Errorを不完全Scanとして扱う安全なEnumerator終了処理と、状態名の明示変換を補強した。
- 技術的に不要になったタスク・理由・代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: PR #16のMergeとCI成功を確認してから最新`main`よりPR2 Branchを作成する。PR2ではPR1の共通照合Service・Global Lock・Storage検証を再利用し、inotifyをHintとして起動時・周期・overflow時の全件再スキャンへ収束させる。PR3のProtocol対応完了までは`Indexing.Enabled=false`を維持する。

### PR2: inotify追従・定期再スキャン・Worker運用

- 完了日: 2026-08-22
- Pull Request: [#17 Add external index watcher and rescan workers](https://github.com/ry825/Kura_Storage/pull/17)
- 実施したTest・Build・静的解析: `./scripts/ci/verify-config.sh`、`./scripts/ci/verify-server.sh`（Domain 34件、Application 95件、Integration 75件、失敗0件）、`./scripts/ci/verify-security.sh`、`./scripts/ci/verify-deployment.sh`、`./scripts/ci/verify-android.sh`（656タスク）、`dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`、`git diff --check`を実施し、すべて成功した。Pull RequestのGitHub Actions（Android、Config、Security、Server）もすべて成功した。
- 手動確認・実機確認: 配置前のPostgreSQL・Storage Root Backupと復元可能性を確認し、Raspberry Pi 4と実exFAT HDDへMigration・Workerを配置した。外部作成・内容更新・Rename・Folder移動・削除・再発見、監視停止中の変更、Worker再起動、inotify overflow、HDD取外し・同一HDD再接続、PostgreSQL一時停止からの復旧を確認した。10,000件の再現可能な縮尺データでEvent収束、Dry-runのCPU・RSS・DB負荷・HDD I/Oを測定し、全件Scan中の一覧・Upload・Download内容一致・認証更新も成功した。測定結果は`docs/testing/20260822-external-indexing-e2e.md`へ記録し、専用試験データを削除して`Indexing.Enabled=false`、実行中Scan 0件、未完了FileOperation 0件、全Service activeを確認した。
- 計画と実装の差分: 承認済みPR2範囲どおり、inotifyをHintとしてPR1の共通照合と全件Scanへ収束させた。実測で既定設定値を変更する必要はなく、PR3完了までは`Indexing.Enabled=false`を維持した。実機の`fs.inotify.max_user_watches=61621`は推奨65536未満のため、配置Scriptは自動変更せず警告し、運用文書へ測定に基づく変更手順を記載した。
- 実装中に追加したタスクと理由: 実機のFolder移動で再配置後のwatchを`IN_MOVE_SELF`により失う問題を修正し、配下変更の回帰Testを追加した。10,000件試験でEvent追加とScan追加の一意制約競合、Worker中断後に残る`RUNNING` Scan、既存EntryごとのDB照会による負荷を検出したため、一意制約競合の再試行正規化、中断Runの`FAILED/WORKER_INTERRUPTED`回復、Path・Move候補・未完了操作のBatch照会とPostgreSQL回帰Testを追加した。
- 技術的に不要になったタスク・理由・代替実装: なし。
- 後続Pull Requestへの引継ぎ事項: PR #17のMergeとCI成功を確認してから最新`main`よりPR3 Branchを作成する。PR3でProtocol 2、MISSING API、索引削除、Android表示をまとめて配置するまでは`Indexing.Enabled=false`を維持する。本番有効化前に実Directory数と運用余裕を測定し、現在のwatch上限61621を推奨65536以上へレビュー済みsysctl設定で調整する。PR3実機回帰ではPR2の性能記録を基準に、既存Upload・Download・File操作と外部変更追従を再確認する。
### PR3: MISSING API・索引削除・Android表示と再確認

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析: 未実施
- 手動確認・実機確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したタスクと理由: 未記録
- 技術的に不要になったタスク・理由・代替実装: 未記録
- 後続作業への引継ぎ事項: 未記録

---

## 全体振り返り

PR1〜PR3および本ファイルの全タスクが完了した後にだけ、`steering`スキルのモード3-Bを使用して記録する。

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
