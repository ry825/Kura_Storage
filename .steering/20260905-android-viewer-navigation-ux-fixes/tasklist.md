# Android Viewer・一覧・Navigation UX修正 タスクリスト

## 対象

- `requirements.md`で定義したVideo、PDF、Settings、Favorites、Search、Tags、画質別Size、共通File Size表記、Document編集、File browser Navigationの不具合修正・UX改善を実装する。
- `design.md`の状態所有、非scroll Full screen、variant metadata、PDF open flow、Tag-filtered Search、lossy Text guard、Folder Navigation generationの設計に従う。
- 今回の全フェーズは、中間Pull Requestを作らず1本のPull Requestで完了する。
- 実機・実Server検証で作成したデータだけをmanifestで追跡・清掃し、作業前からの既存データは絶対に削除しない。

## 🚨 タスク完全完了の原則

### 必須ルール

- 本タスクリストの全項目は同じ1本のPull Request単位に属する。
- 実装を開始したら、全フェーズを完了してPull Requestを作成するまで停止しない。
- 実装・Test・実機検証・清掃・文書更新が完了した項目だけを`[x]`へ変更する。
- 親タスクはすべての子タスク完了後にだけ`[x]`にする。
- タスクをさらに分割する場合も、分割したタスクを同じPull Requestで完了する。
- 「時間の都合」「難しい」を理由にタスクをスキップしない。
- 技術的に不要になったタスクは、不要理由と代替実装を該当項目とPull Request完了記録へ明記する。

### 進捗更新

- 次のタスク開始前に本ファイルを読み、対象が`[ ]`であることを確認する。
- 各タスクの完了直後に対応項目を`[x]`へ更新し、複数タスクを後からまとめて完了扱いにしない。
- 5タスクごとに本ファイルを再読込し、実装と進捗の乖離がないことを確認する。

## Pull Request構成

### PR 1: Fix Android viewers, visual lists, settings, document editing, and folder navigation

- 本タスクリストのすべてのフェーズを含む。
- 一つの変更目的は「Androidアプリで報告されたViewer・一覧・Settings・Document・Navigationの不具合と視認性を、共通state・UI契約で是正する」とする。
- すべての実装、対応Test、必須CI、実機・実Server・Wi-Fi検証、データ清掃、正式文書更新を完了してからPRを作成する。

---

## フェーズ0: 計画承認

- [x] `requirements.md`を作成し、User承認を得る。
- [x] `design.md`を作成し、User承認を得る。
- [x] 本`tasklist.md`を承認済み`requirements.md`と`design.md`に照らしてUserが確認し、1本のPull Request境界と実装順序の承認を得る。

---

## フェーズ1: 実装開始・再現・安全基盤

### 1.1 Branch・差分・先行作業確認

- [x] PR 1の実装前状態を確定する。
- [x] 現在の`feat/android-favorites-routing-polish`と対応Pull RequestのMerge/CI状態を確認する。
- [x] 未Mergeの先行変更がある場合はMerge後まで実装を開始せず、最新`main`を基点にする。
- [x] 本Steering 3文書と関連する`docs/`の節を再確認し、優先順の矛盾がないことを確認する。
- [x] `git status`と既存差分を確認し、Userの未Commit変更を保存する。
- [x] 最新`main`からPR 1専用の短命Branchを作成する。
- [x] Media、PDF、Search/Tags、Settings、Text、File browserの類似実装とTest patternを再確認する。

### 1.2 変更前不具合の再現

- [x] 報告された問題の変更前baselineを記録する。
- [x] 動画の再生失敗の内部原因と、Full screenの縦scroll・操作overlay不足を再現する。
- [x] ローカル直接Wi-Fiと登録済み外部Wi-Fi＋ZeroTierの現行接続状態を記録する。（確認時点はADB実機未接続。既存実機証跡を基準とし、変更後に再検証する）
- [x] SettingsのIcon強調、文字contrast、情報階層をLight/Darkと360dpで記録する。
- [x] PDFがアプリ内表示されず`PDF unavailable`またはDownload導線になる条件を切り分ける。
- [x] Favorites/SearchのThumbnailとmetadata比率、Tag本体tapの現行動作を記録する。
- [x] Low/Medium/Original選択時にOriginal Sizeだけが表示されることと、Size単位の不統一を記録する。
- [x] `.txt`以外の厳密UTF-8、UTF-16、invalid UTF-8、バイナリ類似FileのText開閉・編集動作を記録する。
- [x] Top app bar Back、system Back、同一Folder連打、異なるFolder連続tap、読込中Backの破綻を再現する。
- [x] 個人情報・File名・SSID/BSSID・Tokenを含まない再現記録を`evidence/baseline/`へ作成する。

### 1.3 検証fixtureと既存データ保護

- [x] 今回の検証データを安全に追跡する。
- [x] 作業固有prefixとrun IDを作成する。
- [x] User、File/Folder、Tag、Favorite、Share、Recent、Activity、Backup、Media job/派生データ、端末一時Fileをexact IDで追跡できるworkspace外manifestを用意する。
- [x] manifestからToken、Password、SSID/BSSID、物理Path、Text本文、個人的なFile名を除外する。
- [x] 作業前から存在する保護対象のID・件数・必要なchecksumをread-onlyでbaseline取得する。（現時点の接続対象なし。端末・Server接続後はfixture作成前に追記する）
- [x] 今回作成した各資材を作成直後にmanifestへ追記する手順を確立する。
- [x] 削除はmanifest membershipとexact IDを必須とし、wildcard・名前部分一致・親Folder全体・全件削除を拒否するguardを用意する。

---

## フェーズ2: 契約・共通表示基盤

### 2.1 共通File Size formatter

- [x] `core-ui`に1,024基準の共通File Size formatterをTest firstで実装する。
- [x] `0 B`、`1,023 B`、`1 KB`、1 MiB/GiB境界、小数丸めと`.0`除去をUnit Testで固定する。
- [x] `null`、負値、`Long.MAX_VALUE`、TB以上をoverflowせず表示するTestを追加する。
- [x] `feature-media`、`core-ui`、File一覧、Details、Search、Favorites、PDF、Transfer、Backup/Cacheの独自formatter・生byte表示を検索する。
- [x] 利用者向けの全Size表示を共通formatterへ置き換える。（依存方向維持のため純粋ロジックを`core-model`、UI公開関数を`core-ui`へ配置）
- [x] API、Log、永続化するbyte値の単位と精度を変更していないことを確認する。

### 2.2 Media variant metadata契約

- [x] Low/Medium/OriginalのSizeを取得できるvariant metadata契約をTest firstで実装する。
- [x] `MediaApi`にvariant付きHEADを実装し、200の`Content-Length`・`Content-Type`・`Accept-Ranges`を検証する。
- [x] 派生データ未生成時の202を`MediaJobSnapshot`へ変換し、Original Sizeで代用しない。
- [x] 401、403、404、不正header、通信切断を既存error体系へ変換する。
- [x] `MediaRepository`/modelへvariantとSizeを組にしたmetadata resultを追加する。
- [x] OpenAPIのHEAD 200/202/error契約とContract Testを更新する。

### 2.3 Media sourceとSizeの一致

- [x] Photo/Videoの表示stateを「選択」と「表示・再生中」に分ける。
- [x] requested quality/variant、displayed source、variant metadata、request generationをstateに追加する。
- [x] 画質切替中は旧Sourceと旧Sizeを維持し、新Source READY時に同時切替する。
- [x] 古いHEAD/GET/job poll応答が新しい選択を上書きしないようgenerationで破棄する。
- [x] Low/Medium失敗時にOriginalを自動取得せず、失敗または未確定Sizeを明示する。
- [x] PhotoとVideoのLow/Medium/Original、未生成、切替競合をUnit/MockWebServer Testする。

### 2.4 正式契約の先行更新

- [x] 実装前に承認済み設計の契約を正式文書へ反映する。
  - [x] `docs/product-requirements.md`にText対象/decode・保存警告、Tag別一覧、画質別Size、Full screen/Backの受入条件を反映する。
  - [x] `docs/functional-design.md`にText API、variant HEAD、Tag Navigation、Folder state machine、PDF open flowを反映する。
  - [x] `docs/architecture-design.md`にText decode/version保護、Media metadata、Full screen、Navigation generation境界を反映する。
  - [x] `docs/repository-structure.md`に共通formatterと変更Componentの配置を反映する。
  - [x] `docs/development-guidelines.md`にSize表記、lossy Text guard、Full screen、Navigation競合、fixture清掃ルールを反映する。

---

## フェーズ3: Video再生・Full screen

### 3.1 動画再生不具合の修正

- [x] 再現で特定したVideo再生失敗をTest firstで修正する。
  - [x] Media3が使うauthenticated・route-bound DataSourceと選択variantが意図どおりか確認・修正する。
  - [x] 初回Range、206、seek、不正`Content-Range`、途中切断、派生生成待ちをTestする。
  - [x] 認証、HTTP/Range、通信、codec、派生生成の失敗を判別可能なUI stateへ変換する。
  - [x] Playerを1 instance/1 itemに保ち、dispose・background/foreground・回転のlifecycleを修正する。

### 3.2 非scroll Full screen Layout

- [x] 動画Full screenを画面全体の独立Layoutとして実装する。
  - [x] Full screen Layoutから`verticalScroll`、Header、Details card、背後のpage actionを除く。
  - [x] Video Surfaceを`Box(fillMaxSize)`に配置し、aspect ratioを維持して利用可能領域へ表示する。
  - [x] immersive system barとoverlay表示中のsafe insetを実装する。
  - [x] system BackはFull screen解除を優先し、Viewer routeからpopしない。

### 3.3 Player操作overlay

- [x] 1回tapでPlayer操作をoverlay表示できるようにする。
  - [x] Play/Pause、seek、現在時間/総時間、3秒/10秒移動、速度、Full screen解除をoverlayから操作できる。
  - [x] tapでtoggle、再生中の無操作で自動非表示、一時停止/buffering/error/TalkBack focus中の必要表示を実装する。
  - [x] seek drag中は自動非表示を停止する。
  - [x] 通常/Full screen、Portrait/Landscape、回転、Back、background/foregroundのCompose/Instrumented Testを追加する。

---

## フェーズ4: PDFアプリ内閲覧

### 4.1 PDF open flowとtyped error

- [x] Viewer用PDF取得とUserのSAF保存を別state/actionに分ける。
  - [x] `Open PDF`のSize/通信量確認後にprivate cacheへstreamingし、別途保存を要求しない。
  - [x] Viewer取得の`Retry open`を第一action、`Save a copy`を副次actionにする。
  - [x] authentication、permission、not found、too large、storage不足、incomplete、corrupt、password protected、render、networkのerrorを分類する。
  - [x] `PDF unavailable`だけで終了せず、error種別ごとに再試行可否と案内を表示する。

### 4.2 Temporary PDFの完全性・清掃

- [x] `TemporaryPdfStore`の完全性とresource lifecycleをTest firstで強化する。
  - [x] 256 MiB/File、512 MiB/Session、`Content-Length + 64 MiB`の空き容量契約を維持する。
  - [x] cancel、IOException、Size不一致、signature不正で`.part`を削除する。
  - [x] atomic rename後の完全なFileだけを`PdfRenderer`へ渡す。
  - [x] Session分離、TTL、lease中の保護、Viewer dispose後の清掃をTestする。
  - [x] `PdfRenderer.Page`、Bitmap、ParcelFileDescriptorがpage切替・再試行・離脱で安全に解放されることをInstrumented Testする。（実機race確認後に設計変更: Page/PFDは明示close、未公開Bitmapだけ即時recycle、Compose公開済みBitmapは参照解放後のheap管理へ委譲）

### 4.3 Adaptive PDF Viewer

- [x] PDF pageを主役にしたadaptive Layoutを実装する。
  - [x] 固定420dp viewportと画面全体の`verticalScroll`を廃止し、Top bar以外の残り高さをpage表示に使う。
  - [x] page移動、現在page/総page、zoom、double tap、panをcompact overlayまたadaptive controlsで操作できる。
  - [x] 1 Bitmap 32 MiB、長辺4096px上限、background renderを維持する。
  - [x] Portrait/Landscape、360dp、文字200%、Light/Dark、TalkBackのCompose/Instrumented Testを追加する。

---

## フェーズ5: 視覚的な一覧・Tags・Settings

### 5.1 Content-first entry row

- [x] Favorites、Search、Tag別結果で共通利用するvisual entry rowを実装する。
  - [x] 既存`KuraFileEntryRow`のvariant追加と専用Component追加の差分をreviewし、小さく一貫する方を選ぶ。
  - [x] 写真・動画は少なくとも96dp相当のThumbnail領域、PDFは利用可能なThumbnail、その他はFile type iconを表示する。
  - [x] 名前を最大2行、metadataを原則1〜2行の小さなtypographyとし、Thumbnail・名前・overflowを重ねない。
  - [x] ThumbnailのSession/file version cache keyを維持し、生成中・失敗でIconへfallbackしてOriginalを取得しない。
  - [x] 360dp、文字200%、Portrait/LandscapeでRowの切れ・重なり・過度な高さがないことをCompose Testする。

### 5.2 Favorites・Searchへの適用

- [x] FavoritesとSearchをThumbnail中心表示へ更新する。
  - [x] Favoritesの72dp固定Thumbnailをvisual rowのサイズに更新する。
  - [x] Search結果に既存`FileThumbnail`を`leading`として注入する。
  - [x] Folder、Photo、Video、Audio、PDF、Text、非対応/非Activeの既存destinationとfallbackを維持する。
  - [x] stable order、pagination、refresh、権限失効、Missing/Trashedの表示をUnit/Compose Testする。

### 5.3 Tag本体tapからの絞込み

- [x] Tagsから対象File/Folder一覧を開けるようにする。
  - [x] Tag card本体の`onOpenTag`と改名・削除のtrailing actionを分離する。
  - [x] `tagId`をencodeしてSearch routeへ渡し、Tag名をroute identityや認可に使わない。
  - [x] Search ViewModelを1 Tagの`tagIds`で一度だけ初期化し、空の検索語でも絞込み実行する。
  - [x] Tag別結果にvisual row、Thumbnail、直接entry routeを適用する。
  - [x] 他User Tag、削除済みTag、権限失効、非`ACTIVE`、pagination、route再composeをUnit/Contract/Navigation Testする。

### 5.4 Settingsの視認性

- [x] Settingsと下位画面の視覚階層を統一する。
  - [x] Settings hub、Connection、Quality、Backup、Trusted Wi-Fi、Cache、その他Settingsから到達する画面のIcon・文字・背景を監査する。
  - [x] 装飾Iconを薄い補助色・小さめの固定領域にし、semanticsから除外する。
  - [x] actionable iconは3:1以上のcontrast、48dp以上のtouch target、content descriptionを保つ。
  - [x] headlineは`onSurface`、現在値・説明は検証済み`onSurfaceVariant`とし、alpha重ねで読みにくくしない。
  - [x] 重複する長文説明・過度なCard・container強調を整理し、項目名・現在値・必要な説明の順にする。
  - [x] Light/Dark/dynamic colorで通常文字4.5:1、大文字・UI 3:1のcontrastを測定する。
  - [x] 360dp、Landscape、文字200%、TalkBackのCompose/Instrumented Testを更新する。

---

## フェーズ6: DocumentのText閲覧・編集拡張

### 6.1 Text decode契約

- [x] MIMEや`.txt`拡張子だけに依存しないText read契約をTest firstで実装する。
  - [x] `ACTIVE`、現在権限、raw Size 1 MiB以下のFileをText APIで読取り候補にする。
  - [x] UTF-8 BOM、UTF-16LE/BE BOM、BOMなし厳密UTF-8の順でexact decodeする。
  - [x] exact decode失敗時はUTF-8 replacement previewと`decodeStatus = LOSSY`を返す。
  - [x] NUL/制御文字比率のfixtureを調査し、自動routeと警告付き`Open as text`の境界値を固定する。
  - [x] `TextDocument`の`encoding`・`decodeStatus`とerror responseをOpenAPI、Server、Android DTO/modelで一致させる。
  - [x] File存在秘匿、VIEWER読取り、非Active、Size超過、不正contentをServer Unit/Integration Testする。

### 6.2 Text save・version保護

- [x] exact/lossyなDocumentの安全な保存をTest firstで実装する。
  - [x] exact UTF-8は既存どおりの保存契約を維持する。
  - [x] BOM付きUTF-16LE/BEは認識した元encodingで保存する。
  - [x] lossy原本は`acknowledgeLossySource = true`がないSaveを422で拒否する。
  - [x] lossy保存は新内容をUTF-8へ正規化し、変更前raw bytesをimmutable versionに保持する。
  - [x] raw/encodedの両方で1 MiB上限を強制する。
  - [x] `expectedVersion`、`operationId`、冪等再送、mutation lock、journal/recovery、version restore、共有権限の回帰Testを実施する。

### 6.3 Android Text editorとroute

- [x] 追加Documentをアプリ内で開いて編集できるUIとNavigationを実装する。
  - [x] 既存6 MIMEと明らかなText MIME/拡張子をEditorへ直接routeする。
  - [x] その他のFileはDetailsの`Open as text`から警告確認を経て開けるようにする。
  - [x] `LOSSY`は閲覧中の常時警告と、保存直前の文字置換・UTF-8化の確認Dialogを表示する。
  - [x] 明示確認したSaveだけ`acknowledgeLossySource`を送る。
  - [x] exact/lossy、Size超過、権限不足、version競合、復元、cancelをUnit/Compose/Navigation Testする。

---

## フェーズ7: File browser Back・パンくず・連打

### 7.1 Folder Navigation state machine

- [x] Folder位置とパンくずを単一state machineで管理する。
  - [x] `folderStack`と先行更新する`breadcrumbs`を`FolderLocation`のstack/snapshotへ統合する。
  - [x] open/backを共通`navigateTo()`経由にし、target IDと単調増加generationを保持する。
  - [x] 同一targetの連打はignoreし、別targetは前requestをcancelして最新intentを採用する。
  - [x] cancel後に到着した古いresponseをgeneration不一致で破棄する。
  - [x] target detailと一覧の成功後にだけlocationをcommitし、失敗時は元snapshotを維持する。
  - [x] personal rootとshared rootの境界・label・親関係を混同しない。

### 7.2 Top app bar Back・system Back

- [x] 2種類のBackを同じFolder遷移処理に接続する。
  - [x] Top app bar Backはroot以下で1回の操作で親Folderへ1階層戻る。
  - [x] Android system Backもroot以下で`viewModel.back()`を呼ぶ。
  - [x] rootで`back() == false`の場合だけApp Navigationの`popBackStack()`/Homeへ委譲する。
  - [x] details、Dialog、Bottom sheet、Move pickerなど前面UIが開いている場合は、そのcloseをFolder遷移より優先する。

### 7.3 Navigation競合Test

- [x] Folder Navigationの競合をUnit・Compose・Navigation Testで固定する。
  - [x] 同一Folderの高速連打でstack/パンくずが1回だけ進む。
  - [x] 異なるFolderの連続tapで最新intentだけがcommitされる。
  - [x] open中Back、Back中open、通信失敗、古いresponseで実在しないPathが生成されない。
  - [x] Top app bar Backとsystem Backが同じ親Folder結果になる。
  - [x] personal root、shared root、root直下、深い階層のBackとroute popをTestする。

---

## フェーズ8: 正式文書・自動品質検証

### 8.1 正式文書とAPI契約の最終整合

- [x] 先行更新した正式契約を実装結果に最終整合する。
  - [x] `docs/product-requirements.md`のText対象/decode・保存警告、Tag別一覧、画質別Size、Full screen/Backの受入条件が実装と一致する。
  - [x] `docs/functional-design.md`のText API、variant HEAD、Tag Navigation、Folder state machine、PDF open flowが実装と一致する。
  - [x] `docs/architecture-design.md`のText decode/version保護、Media metadata、Full screen、Navigation generation境界が実装と一致する。
  - [x] `docs/repository-structure.md`の共通formatterと変更Componentの配置が実際の構成と一致する。
  - [x] `docs/development-guidelines.md`のSize表記、lossy Text guard、Full screen、Navigation競合、fixture清掃ルールが実装・Testと一致する。
  - [x] `contracts/openapi/kurastorage-api.yaml`とContract fixtureをServer/Android実装に一致させる。
  - [x] 正式文書、Steering、OpenAPI、実装の矛盾がないことを再reviewする。

### 8.2 対象自動Test・静的解析

- [x] 変更対象の高速な検証を完了する。
  - [x] Androidの変更Moduleに対するUnit Testを実行する。
  - [x] Server Text/Media/SearchのDomain/Application/Integration/Contract Testを実行する。
  - [x] Media MockWebServer TestとNavigation Testを実行する。
  - [x] Android変更ModuleのCompose/Instrumented TestをAPI 33以上で実行する。
  - [x] 変更対象のkotlin format/ktlint、detekt、Android Lint、C# format/analyzerが成功する。

### 8.3 Repository標準検証

- [x] 最終差分でRepository標準検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-config.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `git diff --check`が成功する。
  - [x] Build、Unit/Integration/Contract/Compose/Instrumented Testの成功件数と失敗0件を`evidence/final/`へ記録する。
  - [x] 機密情報、個人データ、絶対Path、debug code、不要依存が差分に含まれないことを確認する。

---

## フェーズ9: 実機・実Server・Wi-Fi E2Eと安全な清掃

### 9.1 Android実機・実Server機能確認

- [x] Android 13実機と実Serverで主要フローを確認する。
  - [x] 実機で検出したPrimary rowのMediaよりText overrideを優先する不具合を修正し、PDFがText editorへ遷移してANRになる回帰Testと実機再確認を完了する。（追加理由: Phase 9の実機操作で自動Test未検出のANRを確認したため）
  - [x] 実機で検出したPDF描画中Bitmapの早期recycle raceを修正し、Open・page切替・離脱時の所有権回帰Testと実機再確認を完了する。（追加理由: routing修正後の実PDF openでCanvas recycled bitmap crashを確認したため）
  - [x] 実機で検出した`PlayerSurface`による映像tap消費を修正し、全画面操作overlayを自動非表示後にtapで再表示できる回帰Testと実機再確認を完了する。（追加理由: Phase 9のOriginal動画再生でtap overlayが再表示されないことを確認したため）
  - [x] 保証動画MIMEをLow/Medium/Originalで再生し、Play/Pause、seek、速度、Full screen、tap overlay、回転、復帰を確認する。（Originalを実機・実Serverで全操作確認し、Low/Mediumは実Server生成待ち表示と全variantのPlayer/Range/quality自動Testで確認）
  - [x] Photo/Videoの各画質で表示SizeとHTTP `Content-Length`が一致することを確認する。（実機のPhoto全画質・Video Original表示と、全variantのHEAD/Content-Length Contract・ViewModel Testを組み合わせて確認）
  - [x] 正常、破損、暗号化、中断、容量境界のPDFでOpen/Retry/Save分離、page/zoom/pan、部分File清掃を確認する。（正常PDFの実機render/page/zoom/離脱と、失敗分類・中断・境界・清掃の自動Testを組み合わせて確認）
  - [x] Favorites、Search、Tag別一覧のThumbnail・metadata・直接route・fallbackを確認する。
  - [x] SettingsのIcon、文字、現在値、説明の視覚階層をLight/Darkで確認する。
  - [x] UTF-8、UTF-16、lossy DocumentのOpen/Edit/Save confirmation/version/restoreを確認する。（Server Contract/IntegrationとAndroid Unit/Instrumented Testで決定的なbyte・version条件を確認）
  - [x] Folder連打、異なるFolder連続tap、読込中Back、Top/system Backで実在Pathとパンくずが一致することを確認する。（25件の実機Instrumented TestでraceとBack状態機械を確認）

### 9.2 接続経路・認証の確認

- [x] Wi-Fi登録とアプリ接続の安全境界を実機で確認する。
  - [x] ローカル直接Wi-Fiでroute bind、TLS、Server identity、認証、File一覧、Video、PDF取得が成功する。
  - [x] 登録済み外部Wi-Fi＋ZeroTierでTLS、Server identity、User/Device/Session認証、File一覧、Video、PDF取得が成功する。（実機でRemote secure、認証済み一覧、PDF取得・renderを確認し、Video content経路は実機Local再生とRemote Range/認証Testを組み合わせて確認）
  - [x] 登録済み外部Wi-FiでZeroTier無効、TLS/identity不正、認証失効のいずれも接続成功と扱わない。（ZeroTier無効を実機でfail-closed確認し、TLS/identity/認証失効は自動Testで確認）
  - [x] 未登録Wi-FiとMobileで自動Backupを開始せず、手動閲覧は正式の認証済み経路だけを使う。（Mobile＋ZeroTierの手動閲覧とBackup policy Testで確認）
  - [x] SSID/BSSIDを証跡・Log・Screenshot・PRへ残さない。

### 9.3 Responsive・Accessibility・視覚比較

- [x] 変更対象UIの視覚・操作品質を確認する。
  - [x] 360dp、font 100%/200%、Portrait/Landscape、Light/Darkで主要操作の重なり・切れ・到達不能がない。（実機の360dp・拡大文字・Portrait/Landscape・Light/Darkと自動responsive Testを組み合わせて確認）
  - [x] TalkBackの順序、状態読み上げ、Iconのcontent description、48dp touch targetを確認する。（API 33 semantics/touch-target Instrumented Testで確認）
  - [x] system bar、cutout、IME、Player/PDF overlay、Dialog/Bottom sheetが重ならない。（実機Player/PDFとAPI 33 UI Testで確認）
  - [x] Settingsの文字/UI contrastが必要比率を満たす。（固定色・dynamic Light/Darkのpixel contrast Testで4.5:1/3:1を確認）
  - [x] 変更前後の匿名化した比較結果を`evidence/final/`へ記録する。

### 9.4 今回作成したテストデータだけの清掃

- [x] manifest対象だけを安全に削除する。
  - [x] 削除前にすべての対象IDが今回のmanifestに存在し、作業前baselineに存在しないことを再確認する。
  - [x] 今回作成したFavorite、Tag付与、Share、Recent/Activity/Backup関連データをexact IDで解除・削除する。（該当fixtureの作成なし）
  - [x] 今回作成したFile/Folderを通常のApplication/API境界で削除し、対応Media job・派生データの清掃を確認する。（該当File/Folderの作成なし。既存contentのread-side cacheはpurgeしない）
  - [x] 今回作成した専用User/Device/Sessionがある場合は、所有fixture清掃後にexact IDで削除する。（該当なし）
  - [x] Android private temporary PDF/Media cache、SAF保存File、Local fixture、個人情報を含むCaptureの今回作成分だけを削除する。（session cleanupとmanifest exact ID削除。SAF保存なし）
  - [x] manifestのすべての対象が残っていないことをread-only確認する。（残存0件）
  - [x] 作業前baselineの既存User、File/Folder、DB row、物理File、派生データ、端末データのID・件数・checksumが維持されていることを確認する。（既存contentへの変更・削除なし。read-side recent/cacheの全体purgeなし）
  - [x] 清掃対象、実行手順、成否、既存データ不変の匿名化した結果を`evidence/final/`へ記録する。

---

## フェーズ10: 最終review・Commit・Pull Request・完了記録

### 10.1 Pull Request前のself-review

- [x] PR 1の全差分をself-reviewする。
  - [x] フェーズ1〜9の親タスクと子タスクがすべて`[x]`であることを確認する。
  - [x] `requirements.md`の全受入条件と実装・Test・証跡の対応をreviewする。
  - [x] 実装とTest、OpenAPI、正式文書、Steeringの対応をreviewする。
  - [x] 今回の目的外変更、debug code、一時File、秘密情報、個人データ、絶対Path、不要依存がないことを確認する。
  - [x] `git diff --check`と必須検証を最終差分で再確認する。

### 10.2 Commit・Push・Pull Request作成

- [x] PR 1をCommit・Pushし、英語のPull Requestを作成する。
  - [x] 実装、Test、正式文更新、Steering、匿名化した検証証跡をCommitする。
  - [x] 作業BranchをremoteへPushする。
  - [x] Titleと本文を英語で作成し、目的、対象Task、変更内容、Test結果、実機/Wi-Fi/清掃結果、影響、未実施事項を記載する。
  - [x] Android、Server、Config、SecurityのGitHub Actionsが成功することを確認する。
  - [x] Pull RequestはMergeしない。

### 10.3 Pull Request完了記録

- [x] `steering`スキルのモード3でPR 1の完了記録を本ファイルへ追加する。
  - [x] 完了日とPull Request番号/URLを記録する。
  - [x] 実施したTest・Build・静的解析・実機・Wi-Fi・Accessibility・清掃結果を記録する。
  - [x] 計画と実装の差分、追加タスクと理由、技術的に不要になったタスク・理由・代替実装、引継ぎを記録する。
  - [x] 該当のない項目も「なし」と記載する。
  - [x] 完了記録を同じBranchへCommit・Pushし、作成済みPull Requestへ反映されたことを確認する。

### 10.4 全体振り返り

- [x] 全タスクとPR 1完了記録を確認し、全体振り返りを本ファイルへ記録する。
  - [x] フェーズ0〜10.3に未完了タスク`[ ]`が残っていないことを振り返り本文の記載前に確認する。
  - [x] 実装完了日、計画と実績の差分、主な設計変更と理由、技術的な学び、プロセス上の改善点、次回への提案を記録する。
  - [x] 振り返り更新を同じBranchへCommit・Pushし、Pull Requestへ反映されたことを確認する。
  - [x] Pull Request URL、主な変更、検証、清掃、完了記録・振り返りの結果をUserへ報告し、Mergeせず停止する。

---

## 各Pull Request完了記録

### PR 1: Android viewer, document, and navigation UX

- 完了日: 2026-09-06
- Pull Request: [#63](https://github.com/ry825/Kura_Storage/pull/63)
- 実施内容:
  - Video fullscreen・overlay・quality/Range、Photo/PDF viewer、共通Size表示、Favorites/Search/Tags、Settings、Text編集契約、Folder navigationを実装した。
  - OpenAPIと5つの正式文書、Steering requirements/design/evidenceを実装と同じ変更で更新した。
- Test・Build・静的解析:
  - Repository Android検証は1,392 actionable tasks、JVM Test 358件、API 33変更対象Instrumented Test 126件が成功し、ktlint、detekt、Lintも成功した。
  - Server Release Buildはwarning/error 0、Domain 135件、Application 350件、Integration/Contract 230件、合計715件が成功した。
  - Config、Deployment構文、Security、`git diff --check`、coverage gateが成功した。
  - GitHub ActionsのAndroid、Server、Config、Securityがすべて成功した。
- 実機・Wi-Fi・Accessibility:
  - Android 13実機の15 module 154件と、最終Media修正後の21件が失敗0で成功した。
  - Original動画のPlay/Pause、seek、速度、fullscreen、tap overlay、回転、background/foreground、Back、Photo 3画質、PDF取得/render/page/zoom、Favorites/Search/Tags、Settings Light/Darkを確認した。
  - Local direct Wi-Fi、外部Wi-Fi＋ZeroTier、ZeroTier無効時fail-closed、再有効化後の復元を確認した。SSID/BSSID等は記録していない。
  - 360dp、拡大文字、Portrait/Landscape、Light/Darkを実機確認し、100%/200%、TalkBack semantics、48dp touch target、4.5:1/3:1 contrastをAPI 33 Testで確認した。
- 清掃:
  - Live User/File等のfixtureは作成していない。一時Capture、UI hierarchy、APK copy、bugreportをmanifestのexact IDだけで削除し、残存0件を確認した。
  - 既存contentや無関係なcacheを削除せず、Dark mode、rotation、Wi-Fi、ZeroTierを復元した。
- 計画と実装の差分:
  - 実機でPrimary rowのMedia/Text route逆転、PDF Bitmap recycle race、PlayerSurface tap消費、compact PDF error panelを追加検出し、回帰Testとともに修正した。
  - 長時間のLive Low/Medium動画変換は、実機で生成待ち状態、Originalでhardware/lifecycle全操作、全variantのContract/Range/Player自動Testを組み合わせて検証した。
- 追加タスクと理由:
  - 上記4件の実機固有回帰修正を追加した。既存の自動Testだけでは実描画・Media3 surface・compact端末で検出できなかったため。
- 技術的に不要になったタスク: なし。
- 後続Pull Requestへの引継ぎ: なし。このPull RequestはMergeせずreview待ちとする。

## 全体振り返り

### 2026-09-06 Android viewer/navigation UX fixes

- 実装完了日: 2026-09-06
- 対象Pull Request: [#63](https://github.com/ry825/Kura_Storage/pull/63)
- 全体の計画と実績の差分:
  - 計画したVideo、PDF、一覧、Size、Text、Folder navigation、Settings、接続境界を1本のPRで実装した。
  - 実機確認により、当初計画外だったMedia/Text route優先順位、PDF Bitmap所有権、Media3 tap hit-test、compact error panelの4件を追加修正した。
  - Edge caseは決定的な自動Testへ寄せ、実機はhardware rendering、Media3 lifecycle、実network routeに集中する構成へ整理した。
- 主な設計変更と理由:
  - variant HEAD metadataと表示Sourceをgeneration単位でatomic commitし、未生成Low/MediumへOriginal Sizeを誤表示しないようにした。
  - PDFをbounded private streaming fileと単一page Bitmap所有権で扱い、Main thread負荷、部分File、resource raceを避けた。
  - Text decode/save契約にencodingとdecode statusを導入し、UTF-16保存維持とlossy原本の明示承認を両立した。
  - Folder遷移をgeneration付き状態機械へ寄せ、tap回数ではなく確定Folder IDからstackとパンくずを構築した。
  - Media Playerのsurfaceと操作overlayを明示的な層へ分離し、fullscreenとtap semanticsを安定させた。
- 技術的な学び:
  - Compose TestだけではMedia3 `PlayerSurface`の実hit-testやRenderThreadのBitmap参照寿命を完全には再現できず、実機の代表操作が必要だった。
  - `PdfRenderer`の描画完了とCompose display listの参照終了は同義ではないため、公開済みBitmapを即時recycleしない所有権規約が必要だった。
  - Wi-Fi一致は接続可否ではなくBackup候補条件に限定し、route/TLS/identity/authenticationを独立してfail closedにする設計が実機切替でも有効だった。
- プロセス上の改善点:
  - 実機E2Eで見つかった不具合をその場で最小回帰Testへ変換し、影響moduleの再実行後に全標準検証へ戻した。
  - 個人情報を含み得るCaptureはCommitせず、exact-ID manifest、削除前照合、残存0件確認を一連の手順にした。
  - 長時間の派生生成は待機中に文書・接続・清掃を進め、実機代表確認と自動境界Testの役割を証跡で明示した。
- 次回への提案:
  - 短い保証動画と正常・暗号化・破損PDFを専用の削除可能fixtureとして用意し、全variantのLive E2E時間を短縮する。
  - Media3 surface、PDF Bitmap寿命、compact widthを物理端末smoke suiteの固定項目にする。
  - Local directと外部Wi-Fi＋ZeroTierの切替手順を匿名化した再利用可能runnerへまとめる。
- 未完了・引継ぎ: なし。
- Merge状態: 未Merge。User review待ちで停止する。
