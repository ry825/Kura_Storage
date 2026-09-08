# 写真高速表示・Low派生永続化 要求内容

## 概要

写真の`IMAGE_LOW`派生データを必要時生成の短期Cacheから、Originalと対になる永続派生データへ変更する。新規・更新写真にはLow生成Jobを必ず登録し、既存の全写真は再開可能な一括生成コマンドで補完する。

写真のMedium品質は廃止する。AndroidではLowという技術名を主要UIに出さず、VPN経由の外部Wi-Fiで利用できる「高速表示（省通信）」として提供する。Local directでは従来どおりOriginalを初期表示する。

同じAndroid改善として、フォルダ一覧の連続スクロールを滑らかにし、写真Viewerから戻るときはViewer内で最後に表示していた写真の位置へ一覧を復帰させる。また、Home画面でKuraStorage volumeの使用量、総容量、空き容量を全ログイン利用者が確認できるようにする。

## 背景

VPNと外部Wi-Fiを使うとき、Original写真はファイル容量、HDD読取り、回線帯域、AndroidでのDecodeの影響を受け、初回表示が遅い。現行のLowは長辺最大1,280 px、WebP品質70であり通信量削減に有効だが、初回要求時に生成するため、未生成写真では変換待ちが発生する。また、24時間TTLと10 GiB→6 GiBのLRU清掃対象のため、一度生成しても後日再生成が必要になる。

2026-09-07時点の実Server集計は次のとおりである。集計はFile名、User名、物理Pathを取得せず、件数とSizeのみを対象とした。

- `ACTIVE`写真: 9,233件、Original合計92,175,206,455 bytes。
- 生成済み`IMAGE_LOW`: 11件、合計2,516,836 bytes、1件平均228,803 bytes。
- 現行の少数sampleからの全件Low推定: 約1.5〜3 GiB。実行前に実測と空き容量条件を確定する。

## 要求上の決定

### 1. Lowの内部契約と利用者向け名称

- Server内部の`IMAGE_LOW`、APIの`image-low`、現行のWebP生成器は再利用する。既存の完成済みLowが現在のSource VersionとProfile Versionに一致する場合は再生成しない。
- Androidの主要UIでは「Low品質」ではなく「高速表示」または「高速表示（省通信）」と表示する。
- 速さを認識できるIconとText labelを併用する。Icon候補は飛脚、稲妻、高速移動線等とし、IconだけでON/OFFや機能を表現しない。最終図形は既存のKuraStorage Icon体系とAccessibility要件に合わせる。

### 2. LowをOriginalと対になる永続派生にする

- 対応写真がUpload、Folder Upload、自動Backup、外部HDD変更検出のいずれかで新規追加または内容更新されたら、OriginalのDB確定後に対応するLow生成Jobを必ず永続Queueへ登録する。
- OriginalのUpload成功をLow変換の同期完了まで待たせない。Original確定とLow Job登録の取りこぼしを防ぎ、Workerが非同期に生成する。
- Lowは長辺最大1,280 px、WebP品質70、縦横比維持、拡大なしの現行Profileを維持する。Profileを変更する場合はVersionを更新し、旧データを新Profileとして誤用しない。
- 生成には現行の専用temporary workspace、出力検証、atomic publish、Job claim、Lease、retry、stale recoveryを再利用する。生成途中ファイルを永続保存先へ公開しない。
- 完成済みLowは現行のDerivative Rootへ保存し、temporary workspaceと明確に分離する。Derivative Rootが必要な容量、権限、mount identity、atomic rename境界を満たす限り、別の物理保存先は追加しない。
- Lowに24時間TTL、10 GiBのHigh watermark、6 GiBのLow watermark、LRU清掃を適用しない。
- RenameまたはMoveで写真内容とFile Versionが変わらない場合はLowを再利用する。内容更新でFile Versionが変わった場合は新Lowを生成し、旧Versionを配信しない。
- Trash中はOriginalとLowの対応を保持し、Restore時に同じVersionのLowを再利用する。Purgeまたは安全な索引管理情報削除でOriginalが完全削除されたときは、対応するLow、Job、Leaseも削除する。
- `MISSING`のLowをOriginalの代替として配信しない。Originalが同じ内容とVersionで復旧したときの再利用条件は実装前に正式設計で確定する。

### 3. 既存写真の一括Low生成

- Admin CLIまたは配置用Scriptとして、全対象写真のLowを補完するコマンドを提供する。DBへの手動SQL挿入や派生保存先の直接全件走査は通常手順としない。
- コマンドは実行前の`dry-run`を持ち、対象数、再利用数、新規Job数、推定容量、HDD空き容量、保護領域を個人情報なしで表示する。
- 現在のSource Version、Low Profile Version、完成状態が一致する既存Lowは必ず再利用する。同じコマンドを再実行しても重複派生、重複Job、重複物理ファイルを作成しない。
- 安定順の有界BatchでQueueへ登録し、中断・API/Worker/Pi再起動後に未完了分だけを再開できる。無制限のTask、Process、DB row lock、メモリ保持を発生させない。
- 既存のWorker並列上限とCPU/IO保護を使用し、Upload、Backup、Thumbnail、Original配信、一覧API等のForeground操作を優先する。必要な場合は一時停止と再開を行える。
- 最低空き容量を下回る、Storage identityが一致しない、HDDがread-only、またはWorkerが安全に生成できない場合は新規登録または生成を停止する。
- 完了後は、対象写真数、Low `READY`数、失敗数、合計容量、未完了数を照合できる。File名、User名、物理Pathを通常出力しない。

### 4. Medium品質の廃止

- Androidの写真品質選択、接続環境別初期品質、ViewModel、保存済み設定からMediumを削除する。旧設定がMediumの場合は、予期しないOriginal通信を発生させず、高速表示ONへ安全に移行する。
- Serverは新しい`IMAGE_MEDIUM`派生またはMedia Jobを作成しない。OpenAPI、Client契約、テスト、設定、管理集計からMediumを除く。
- 既存AndroidとServerの展開順序で写真Viewerを使用不能にしない互換期間または互換応答を設計する。最終状態でMedium用の新規変換や永続Medium保存は行わない。
- 既存のMedium行、物理ファイル、Job、Leaseは専用の安全な移行手順で削除する。元写真、Low、Thumbnail、PDF thumbnail、対象外データを削除しない。
- 写真Viewerの手動選択は「高速表示」と「Original」の2状態とする。

### 5. Server側Cache機能の廃止と永続派生管理

- Server側のLow/Mediumを対象としたTTL・LRU・10 GiB/6 GiB容量清掃を廃止する。Lowは再生成可能なCacheではなく、Originalのライフサイクルに追従する永続派生として管理する。
- 一般のCache cleanupを削除しても、temporary workspace、途中出力、stale Job、terminal Job、削除中状態、orphan派生の安全な復旧・清掃は維持する。
- 現行のAdmin Media Cache APIと手動Cleanup runは、利用者が必要な運用情報を失わないよう、「永続派生の件数・容量・欠落・失敗状態」へ再定義するか、廃止手順を正式設計で決定する。
- 写真Thumbnail、動画Thumbnail、PDF thumbnailの永続保持と生成契約は変更しない。

### 6. Android高速表示（省通信）設定

- `LOCAL_DIRECT`では設定にかかわらずOriginalを写真Viewerの初期表示とする。利用者はViewer内で高速表示へ切り替えられるが、通常はLocal directの帯域を活用する。
- VPN経由の登録済み・未登録外部Wi-Fiでは、接続設定に「高速表示（省通信）」のON/OFFを提供する。ONの場合はLow、OFFの場合はOriginalを初期表示する。
- VPN経由の外部Wi-Fiでは高速表示を既定ONとする。旧設定のLowとMediumはON、OriginalはOFFへ移行する。
- Mobile＋VPNは既存の省通信方針を維持し、Lowを初期表示する。Originalの明示選択と通信確認は従来どおり利用できる。
- Viewer内では現在が高速表示かOriginalかを明示し、写真単位で切り替えられる。Viewer内の一時的な切替は、別の写真または次回Viewerを開く際の接続環境別既定値を書き換えない。
- Lowが未生成、生成中、または失敗中の場合、サムネイルか明示的な処理中表示を維持し、利用者の確認なしにOriginalへ自動Fallbackしない。
- 360 dp幅、Landscape、OS文字200%、Light/Dark theme、TalkBackで高速表示の状態と操作を識別できる。

### 7. Android端末内Cacheの扱い

- ServerのLow/Medium Cache廃止と、Android端末内の画像Cacheを分離して扱う。
- Androidの有界memory/disk cacheは、同じ完成済みLowまたはOriginalを毎回VPN経由で再取得しないための転送・Decode最適化として維持する。
- 端末Cache keyにServer/Account scope、File ID、File Version、Variantを含め、異なるServer、User、Version、品質間で再利用しない。Logout、Account切替、接続先変更、OS容量回収では安全に消失可能なCacheとする。
- Android CacheがなくてもServerの永続Lowから再取得でき、変換待ちを発生させない。Android CacheはLowの永続性に対するSource of Truthにしない。

### 8. Security・信頼性・運用性

- Lowの配信はOriginalと同じUser、Device、Session、TLS、Server identity、File閲覧権限を要求する。高速表示は認証・認可の代替にしない。
- 別User、権限失効後、旧File Version、`MISSING`、Purge済み写真のLowを配信しない。
- 一括生成と自動生成は、Original、バックアップ、DB、既存Thumbnailを変更しない。1件の失敗で他の独立写真を失敗扱いにしない。
- Job失敗は件数と型付きErrorで管理でき、再試行可能な失敗は対象限定で再実行できる。無限自動Retryを行わない。
- 一括生成中にAPI、Worker、PostgreSQL、HDDの負荷を観測し、Foreground操作と自動Backupの性能を承認済み安全域の範囲外へ悪化させない。

### 9. フォルダ一覧の滑らかな連続スクロール

- List表示とGrid表示の両方で、通常のdragまたはflingを位置保存・復元処理が途中で打ち消さない。
- Scroll anchorはFolder、Trash状態、表示modeごとに維持するが、保存したanchorを利用者の操作中に繰り返し適用しない。画面再生成、Folderへ戻ったとき、Viewerからの明示的復帰時だけ一度復元する。
- 次pageがある場合は末尾へ到達する前に有界prefetchし、利用者が毎pageで停止して`Load more`を押さなくても連続して閲覧できる。
- Pagination requestは同時に1件だけとし、同じpageの重複取得、無限request loop、全件一括memory loadを行わない。
- Page取得に失敗した場合は現在位置と読込済み項目を保持し、末尾に再試行操作を表示する。一覧全体を先頭へ戻さない。
- Stable keyを維持し、追加page、Thumbnail更新、状態表示のrecompositionで見えている項目が不必要に移動しない。
- 360 dp幅、Landscape、OS文字200%、TalkBackでも連続閲覧と再試行ができる。

### 10. Photo Viewerから現在写真位置への復帰

- フォルダ一覧から写真Viewerを開き、Previous/Nextで別写真へ移動してからBackした場合、最初にタップした写真ではなく、Back直前に表示していた写真が見える一覧位置へ戻る。
- Viewerで移動しなかった場合は、従来どおり起点写真付近へ戻る。
- 復帰時は写真詳細sheetを自動的に開かず、対象写真を一覧内へ表示する。Viewerの「詳細」操作を選んだ場合だけ対象写真の詳細sheetを開く。
- 戻り先はViewerを開いたNavigation contextへ限定し、別Folder、別一覧、別Account、別Serverのscroll anchorへ誤適用しない。
- 対象写真が削除、移動、権限失効、filter変更等で一覧に存在しない場合はcrashせず、保存済みの最寄り位置を維持する。
- process再生成、画面回転、List/Grid切替と競合して先頭へ飛ばない。Navigation結果は一度消費し、後続の手動scrollを上書きしない。

### 11. Home画面のKuraStorage容量表示

- Home画面にKuraStorage storage volumeの使用量、総容量、空き容量を表示する。
- 表示は少なくとも「使用済み / 合計」とprogress表示を持ち、単位を読みやすくformatする。空き容量もTextで確認できる。
- 使用済み容量は`totalBytes - availableBytes`として計算し、同じmount上のOriginal、永続Low、Thumbnail、Database以外のfilesystem使用分も含むvolume実使用量であることをUIまたは正式文書で明示する。
- 全ログイン利用者が集計容量を閲覧できる。Trash件数、Purge履歴、File名、User名、path等のAdmin情報は公開しない。
- Serverは既存のStorage identity/read guardとcapacity取得処理を再利用した、認証必須の最小限APIを提供する。Anonymous health APIへ容量値を追加しない。
- Storage unavailableまたは容量取得失敗時は未知値を0として表示せず、「容量を取得できません」と再試行を表示する。Homeの他機能は継続利用できる。
- 画面表示時に取得し、手動refreshおよび適切な再取得契機を持つ。短い間隔の無限pollは行わない。
- 0 bytes、不正な`available > total`、非常に大きい値、容量警告域を安全に処理する。
- 360 dp幅、Landscape、OS文字200%、Light/Dark theme、TalkBackで使用量、総容量、空き容量を識別できる。

### 12. Pull Request方針

- 本要求に含まれるServer、Android、Database、CLI、文書、一覧UX、容量表示のSource変更は、実装と検証をすべて完了した後に1つのPull Requestとして作成する。
- 作業途中に機能別のPull Requestを作成しない。`tasklist.md`は1つの最終Pull Request単位として管理する。
- 実Serverへのmigration、Backfill、Medium purge、APK配布は、最終Pull RequestのMerge後に行う運用作業として分離し、追加のSource変更が不要な状態までPull Request内でcommandと検証手順を完成させる。

2026-09-08のユーザー承認により、release keystore・password file・署名fingerprint・version codeを必要とする正式署名APKの生成、固定名での配置、upgrade install確認、およびpaginationを発生させる十分な認証済み実データでの体感確認は、このMerge後運用に含める。Source PRでは、同じ経路を検証するdebug APKと自動テストを完了条件とし、正式APK・実データ確認は省略せずフェーズ9の実環境gate後に実施する。

## 正式文書との整合が必要な変更

- 物理端末で最新sourceをLocal direct検証する必要がある場合、debug APKは公開Root CAと接続先を明示指定して生成できる。ただしproduction署名APK、productionデータ、秘密鍵を置き換えない。

現行の正式文書は「写真Low/Mediumを必要時生成し、24時間TTLと10 GiB→6 GiB LRU清掃の再生成可能Cacheとする」ことを要求している。本要求の承認後、実装と同じ変更で次を更新する。

- `docs/product-requirements.md`: 必須Low永続化、高速表示、滑らかな一覧、Viewer復帰、Home容量表示の要求へ更新する。
- `docs/functional-design.md`: 永続Low、Job、Backfill、Medium移行、一覧pagination/anchor、Navigation return target、容量状態を更新する。
- `docs/architecture-design.md`: 永続派生lifecycle、Queue、保存境界、Foreground負荷保護、one-shot scroll復元、容量読取り境界を更新する。
- `docs/repository-structure.md`: CLI、Application Service、Worker、Android一覧/Viewer/Homeの実際の配置に合わせて更新する。
- `docs/development-guidelines.md`: 永続派生、backfill、pagination、Navigation target、容量APIの実装・移行検証規約を追加する。
- `contracts/openapi/kurastorage-api.yaml`とAPI Error/互換性文書: Medium/Cache廃止、派生status、認証済み容量APIを反映する。

## 受け入れ条件

### Lowの永続生成と再利用

- [ ] 対応写真をUpload、Folder Upload、自動Backup、外部追加すると、確定済みOriginalと同じFile ID/Versionに対応するLow Jobが取りこぼしなく登録される。
- [ ] Low生成待ちでOriginalのUpload完了を不要に遅らせず、Workerが有界並列で最終的に`READY`へ収束させる。
- [ ] 既存の現行Version/Profileに一致するLowは、Upload後・一括生成後・Worker/Pi再起動後も重複生成されない。
- [ ] LowにTTLとLRU容量清掃が適用されず、Trash/Restoreで再利用され、Original完全削除で対応Lowも削除される。
- [ ] 内容更新後に旧VersionのLowを配信せず、Rename/Moveだけで現行Lowを無駄に再生成しない。

### 既存写真のbackfill

- [ ] `dry-run`で対象数、再利用数、不足Low数、推定追加容量、安全な空き容量を確認できる。
- [ ] 実行コマンドを中断・再実行しても、現行Versionの各対象写真に有効なLowが正確に1件だけ存在する。
- [ ] 9,233件の実データは件数、`READY`、失敗、合計Sizeを集計照合し、不足と重複が0件になるまで安全に完了または型付き失敗として明示される。
- [ ] 一括生成中も一覧、Original配信、Upload、Backupが利用でき、安全域を超えたら一時停止・調整できる。

### Medium廃止と移行

- [ ] Androidの設定とViewerにMediumが表示されず、旧Medium設定が高速表示ONへ移行する。
- [ ] Serverが新規`IMAGE_MEDIUM`行、Job、物理ファイルを作成せず、古いAndroidを考慮した展開中の応答もOriginalを無確認取得させない。
- [ ] 専用移行の実行後、MediumのDB行、Job、Lease、物理ファイルが0件で、Original、Low、Thumbnailの件数とchecksumに意図しない変更がない。

### 高速表示（省通信）

- [ ] Local directで写真を開くとOriginalが初期選択される。
- [ ] VPN＋外部Wi-Fiで高速表示ONのときはLow、OFFのときはOriginalが初期選択される。
- [ ] VPN＋外部Wi-Fiの既定値はONで、Mobile＋VPNはLow初期表示を維持する。
- [ ] Viewerから写真単位で高速表示/Originalを切り替えられ、状態をTextとIconの両方で判別できる。
- [ ] 高速表示ではOriginalを自動取得せず、実通信量がLowファイルのSize範囲内である。
- [ ] Low未準備時は生成中または失敗を表示し、確認なしにOriginalへFallbackしない。
- [ ] 360 dp幅、Landscape、OS文字200%、Light/Dark theme、TalkBackで設定とViewer操作を利用できる。

### 一覧スクロールとViewer復帰

- [ ] List/Gridを連続してflingしてもscroll anchor復元に中断されず、次pageが自動で有界読込みされる。
- [ ] Pagination中も現在位置が変わらず、重複requestや無限requestが発生しない。
- [ ] Page取得失敗後も読込済み一覧と位置を保持し、末尾の再試行から継続できる。
- [ ] Viewerで20枚程度Nextした後にBackすると、最後に表示していた写真が見える位置へ戻る。
- [ ] Viewerの通常Backでは詳細sheetを開かず、「詳細」操作では現在写真の詳細sheetを開く。
- [ ] 対象写真消失、画面回転、process再生成、Folder/List/Grid context差異で誤ったanchorを適用しない。

### Home容量表示

- [ ] Admin以外を含む認証済み利用者がHomeで使用済み/合計/空き容量を確認できる。
- [ ] Anonymous利用者には容量値を返さず、Admin専用Trash/Purge情報を一般容量APIへ含めない。
- [ ] Storage unavailable、取得失敗、不正値で誤った0表示やcrashがなく、再試行できる。
- [ ] 360 dp幅、Landscape、OS文字200%、Light/Dark theme、TalkBackで容量状態を理解できる。

### 容量・性能・信頼性

- [ ] 全Low生成後の実使用量とOriginal比率を記録し、必要空き容量と将来増加の監視方法を正式運用文書に反映する。
- [ ] 事前生成済みLowは、承認済みVPN＋Wi-Fi基準環境でViewer表示開始p95 2秒以内または現行Original baseline比50%以上改善を満たす。
- [ ] Low一括生成と新規自動生成で、同一派生の重複生成、部分ファイル公開、Original変更、Queue取りこぼし、無制限Retryが0件である。
- [ ] API/Worker/PostgreSQL/Pi再起動、HDD切断・再接続、read-only、容量不足、変換失敗後に、Originalを損傷せず、Lowの欠落と状態が検出・復旧可能である。
- [ ] Android端末Cacheを温存しても、Server/Account/File Versionの境界を越えた写真が表示されない。

## 成功指標

- 一括補完完了時の対象`ACTIVE`写真に対する現行Version/ProfileのLow `READY`または明示的な非対応/型付き失敗の説明率: 100%。
- 新規対応写真のOriginal確定後、Low Job未登録のまま放置される件数: 0件。
- 新規`IMAGE_MEDIUM`生成数: 0件。
- 同一Source Version/ProfileのLow重複有効Job・重複`READY`・重複物理出力: 0件。
- VPN＋Wi-Fiの高速表示でOriginalを自動取得する件数: 0件。
- 生成途中または検証前のLow公開: 0件。
- Folder一覧のfling中断、重複pagination request、Viewer復帰先誤り: 0件。
- Home容量値にFile名、User名、path、Admin専用Purge情報が含まれる件数: 0件。

## スコープ外

以下は本作業では実装しない。

- Original写真の圧縮、置換、削除。
- 動画のLow/Medium変換の復活、HLS/DASH、動画高速表示。
- Thumbnailの512 px/WebP生成・永続保持方針の変更。
- PDF本文、音声本文、Textファイルの派生データ追加。
- Web・iOSクライアントの高速表示UI。
- Web・iOSクライアントの連続pagination、Viewer復帰位置、Home容量UI。
- AI画像最適化、複数サイズの端末別配信、CDN、Internet上の外部変換Service。

## 参照ドキュメント

- `docs/product-requirements.md` 7.11「MVP後: 派生データとキャッシュ管理」
- `docs/functional-design.md` 5.4、7.5、8.5、18.2「派生データ・Cache・配信・実装順」
- `docs/architecture-design.md` 8.6〜8.7、11.3〜11.4、15.2「派生識別・永続Job・Media生成」
- `docs/repository-structure.md`
- `docs/development-guidelines.md`
- `contracts/openapi/kurastorage-api.yaml`
- `.steering/20260829-thumbnail-derivative-worker-infrastructure/`
- `.steering/20260829-android-media-viewers-players/`
- `.steering/20260906-android-upload-media-backup-usability/`
