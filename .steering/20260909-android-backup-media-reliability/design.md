# Androidバックアップ・メディア信頼性改善 設計書

## アーキテクチャ概要

既存のServer File/Trash API、Backup Compare/Upload、永続Media JobとAndroidのRepository/ViewModel/Composeを拡張する。新規の広域削除API、端末内動画変換、無制限のretryは追加しない。

```text
Android File一覧 ── 選択対象ごとの既存Trash command ──> Server Trash契約
Android Backup Worker ── Compare（再関連付け候補を含む） ──> Receipt / File Version
Android Photo Viewer ── View context + current index ──> zoom状態を破棄して前後Fileへ
Android Thumbnail UI ── summary / retry decision ──> Media Job retry（有界・冪等）
Android Video Player ── Media3 codec error分類 ──> 再生継続 / Download・外部アプリ導線
```

## コンポーネント設計

### 1. 複数選択削除

**責務**:

- File browserのselection stateへ削除actionを追加し、選択済みのFile/Folderを各IDごとの既存Trash commandへ変換する。
- 確認dialog、実行中状態、項目単位outcome、完了後のselection解除と一覧更新を一貫して管理する。

**実装の要点**:

- Server側は既存の単一Trash APIと認可・競合・冪等契約を再利用する。複数IDを受け取る新しい一括削除Endpointは作らない。
- Clientはbounded concurrencyで実行し、各結果を成功・失敗に分ける。失敗した項目は一覧を再取得して現状態を正とする。
- 一覧に見えても選択不可能な状態（Trash/MISSING/権限消失）はaction実行前に除外し、最終認可はServerが判定する。

### 2. 再インストール後のBackup再関連付け

**責務**:

- Compare requestに、通常Receiptを持たない新規端末文書を既存Fileと安全に照合するための限定された候補情報を追加する。
- Server Application層で認証済みUser、現在Device、保存先Folder権限、同一相対Path、File状態、Size/更新時刻/checksumを検証し、候補が一意な場合だけ再関連付けoutcomeを返す。
- Upload確定時にも同じFile/Version/Folder権限を再検証し、receiptをDB一意制約で収束させる。

**実装の要点**:

- Client指定のUser ID、Device ID、Remote File IDを同一性・認可の根拠にしない。checksumは必要な候補に対してのみ要求・計算する。
- 同一内容は`ALREADY_UPLOADED`相当の結果と新Device/`localDocumentKey` Receiptの確定へ収束させ、本文Uploadを開始しない。
- 複数候補・checksum不一致・不正状態・権限不足では再関連付けせず、既存の`NEW`/`CHANGED`/利用者操作待ちへ安全に戻す。FileやReceiptを削除しない。

### 3. 拡大写真からの前後移動

**責務**:

- Photo Viewerの閲覧Contextを、閲覧可能File ID列と現在indexとして明示する。
- zoom/pan gestureと前後navigation actionを競合させず、navigationが決定した時点で対象Fileに紐づく表示状態を初期化する。

**実装の要点**:

- 前後ボタン、accessibility action、必要なら端末Back/gestureの優先順位を既存Viewerの操作契約と揃える。zoom中のpanは前後移動として解釈しない。
- image request/cache keyはSession scope、File ID、Version、Variantを含める。非同期完了時にはcurrent File ID/version tokenを照合し、古い結果を捨てる。
- Full screenは写真surfaceを隠さない下部overlay action barを持つ。通常Previewの`EntryOrganizationViewModel` callbackを再利用してFavorite/Tagを操作し、既存Original download coordinatorとfull screen解除callbackを同じ小型Iconで公開する。
- Iconの描画サイズは控えめにしてもtouch targetはAndroid accessibility要件を満たし、content description、状態、tooltipを持たせる。操作中・error時の表示は通常Previewと同一stateを使う。

### 3.1 フォルダ一覧の先頭移動

**責務**:

- `LazyListState`のscroll位置から先頭移動buttonの表示可否を導出し、Folder一覧のみに表示する。
- button actionは現在の`LazyListState`をcancel可能なanimationでindex 0へ移動し、Folder遷移時の既存scroll anchor保存/復元と別のユーザー明示操作として扱う。

**実装の要点**:

- visualは小型の上向きIcon/FAB相当とするが、touch target、TalkBack label、contrastを確保する。Top app bar、Upload、selection action、一覧itemを覆わない安全な位置に置く。
- 先頭または短い一覧では非表示とし、scroll中の頻繁な再CompositionやFolder ID変更時の古いanimationをcurrent navigation generationでcancelする。

### 4. Thumbnail失敗のdismissと有界retry

**責務**:

- `ThumbnailFailureNoticeState`をSession scopeと失敗summaryの世代で保持し、dismiss済みの同一失敗群を画面再訪時に再表示しない。
- `ThumbnailRetryCoordinator`がretry可能Jobだけをkeyごとにcoalesceし、上限回数・指数backoff・Server指定Retry-Afterを守って明示Retry APIを呼ぶ。

**実装の要点**:

- dismissはSession scope内のUI状態であり、Logout、Session失効、File Version変更、`failedCount=0`で失効する。新規失敗は通知可能にする。
- retry状態はJob/File名をLogやUI summaryへ漏らさない。retry上限に到達したkeyはterminal suppressionとして保持し、明示操作だけが再開できる。
- ServerのJob冪等性と既存の一意制約を利用し、Clientの再Composition・rotation・複数一覧画面から重複retryを作らない。Originalへの自動fallbackはしない。

### 5. 端末Codec非対応MP4

**責務**:

- Media3のdecoder初期化・playback errorを既存のネットワーク/Range/認証エラーと分離してtyped `CodecUnsupported`へ変換する。
- `CodecUnsupported`ではPlayerを停止・解放し、再試行を自動開始せず、Downloadまたは外部対応アプリに明示的に渡すactionを表示する。

**実装の要点**:

- APIの動画契約はOriginal Range配信のまま維持し、Clientでトランスコードやソフトウェアdecoder追加をしない。
- 外部actionは一時的・認可済みの既存Download結果だけを対象にし、Token付きURLや認証Headerを外部へ渡さない。利用可能なhandlerがない場合は説明だけを表示する。
- MP4 Container MIMEと内部Codecは別に扱い、互換端末では既存Media3 Range再生経路を維持する。

## データフロー

### 再インストール後の同一内容Backup

```text
1. Androidがsourceを再走査し、Receipt未登録の候補をCompareへ送る。
2. Serverが現在のUser/Device/Folder権限と一意候補を照合し、必要時だけchecksumを要求する。
3. checksum一致なら、transaction内で新Device/localDocumentKeyのReceiptを作成または既存値へ収束させる。
4. Androidは本文を送らず、Room itemをALREADY_UPLOADEDとして確定する。
5. それ以外は既存のNEW/CHANGED/blocked経路へ進む。
```

### Thumbnail失敗

```text
1. summaryまたはthumbnail responseが失敗を示す。
2. UIはdismiss keyとretry keyを照合し、通知またはplaceholderを表示する。
3. retry可能かつ残回数があればCoordinatorが1件にcoalesceして待機後Retryする。
4. 成功またはfailedCount=0で失敗世代を解消する。上限/恒久失敗は自動処理を停止する。
```

## エラーハンドリング戦略

- Bulk Trashは項目単位のtyped outcomeを集計し、通信・認可・競合を一般失敗に丸めない。
- Backup照合で曖昧さがある場合は既存Fileを操作せず、再アップロードを省略するための推測をしない。
- Photo navigationの通信・decode・権限失敗は対象Fileだけに表示し、前後Fileの状態を汚染しない。
- Full screen actionは通常Previewと同じ認可済みcallbackだけを使い、DownloadはOriginalのStreaming契約を変えない。
- 先頭移動はUIのscroll stateだけを変更し、Folder位置、Server query、sort/filter、保存済みanchorを勝手に変更しない。
- Thumbnail retryはretryable/terminalをServer responseの型から判定し、例外文字列を判定根拠にしない。
- Codec非対応は端末内コンテンツ取得の失敗ではないため、認証更新、Range retry、Server Media Job retryを誘発しない。

## テスト戦略

### Unit / Contract Test

- 複数選択削除の対象filter、bounded outcome集計、partial failure表示。
- Backupの一意候補、checksum一致/不一致、複数候補、権限・状態変更、Receipt idempotency。
- Photo navigation token、zoom reset、rapid navigation時の古い非同期結果破棄。
- Full screen action barのFavorite/Tag/Download/exit、処理中・失敗・TalkBack semanticsとsystem Backの優先順位。
- Folder先頭移動buttonの表示閾値、scroll animation、short list非表示、Folder切替/anchor復元との競合。
- dismiss世代、retry回数/backoff/coalescing、terminal stopとexplicit retry。
- Media3 error分類とCodecUnsupported時のaction選択。

### Integration / Instrumented / E2E Test

- Server APIとAndroid Room/Workerで再インストール相当の新Device/新localDocumentKeyを再現し、本文転送0・Receipt確定を確認する。
- Emulatorと可能な実機で複数選択Trash、Photo zoom中navigation、thumbnail failure dismissal/retry、対応MP4を確認する。
- codec非対応の再現可能fixtureは実機（OPPOを含む場合は対象端末）で検証し、対応端末（Pixel相当）では既存再生を回帰確認する。
- `verify-server.sh`、`verify-android.sh`、関連E2Eを変更範囲ごとに早期実行し、最後に全対象gateを1回実行する。

## Fixture清掃

- 実機/実Server検証の開始前に、read-only baselineを取り、run IDと今回作成したexact IDだけをRepository外manifestへ記録する。
- fixtureは最小数を再利用可能な1セットに集約して高速化する。既存User、File、Folder、Backup Rule、Media Job、端末データはfixture候補にしない。
- 清掃は派生データ/Job、File、Folder、Backup Rule、test User、端末private dataの依存関係逆順で実行し、各exact IDの不存在とbaseline一致を確認する。

## 正式文書への反映

- `docs/product-requirements.md`、`docs/functional-design.md`、`docs/architecture-design.md`、`docs/development-guidelines.md`へ、複数選択Trash、再インストール照合、Viewer navigation、thumbnail retry/dismiss、codec非対応導線の仕様を矛盾なく反映する。
- 上記正式文書へ、全画面写真の小型action barとFolder一覧のaccessibilityを満たす先頭移動buttonも反映する。
- `docs/repository-structure.md`は追加・変更したServer/Android componentの配置に変更がある場合だけ更新する。

## 実装の順序

1. 作業開始前の差分・baseline確認、既存仕様の整合更新、fixture manifest準備。
2. Server/Android Backup再関連付けをTest firstで実装する。
3. Androidの複数選択TrashとPhoto navigationを実装する。
4. Thumbnail dismiss/retryとCodecUnsupported導線を実装する。
5. 変更近傍テストを各段階で実行し、統合/E2E/実機検証、fixture清掃、最終gateを行う。
6. 差分をreviewし、全タスクが完了した後に1本だけPull Requestを作成して完了記録を残す。

## セキュリティ・性能考慮事項

- 全File操作、Backup、Media取得は現在の認証・Folder/File権限・Session scopeを再評価する。
- 生のFile名、相対Path、User/Device識別子、Token、Codecの例外詳細をLog、metric label、dismiss keyに出さない。
- Bulk Trashとretryは上限付き並列実行にし、画面再Composition/Worker再実行で同一操作を重複実行しない。
- checksumは照合候補が絞れた場合だけstreaming計算し、全Fileの無条件hash計算をしない。
