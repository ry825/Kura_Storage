# Android Viewer・一覧・Navigation UX修正 設計書

## アーキテクチャ概要

既存のAndroid multi-module構成、MVVM、Repository境界、Compose Navigation、Media3、`PdfRenderer`、Search/Organization API、Text versioningを維持する。今回は不具合の原因となっているUI stateとNavigation stateの所有境界を整理し、同一の情報を複数画面で独自計算しない構成にする。

```text
Compose screen
  ├─ ViewModel / screen state
  │    ├─ File / Search / Organization repository
  │    ├─ Media repository + authenticated route-bound OkHttp
  │    └─ Text repository
  ├─ core-ui
  │    ├─ common file-size formatter
  │    ├─ content-first entry row
  │    └─ settings row / semantic colors
  └─ app navigation
       ├─ entry destination resolver
       ├─ tag-filtered search route
       └─ folder navigation event coordinator

Server API
  ├─ HEAD/GET media content by variant
  ├─ existing Search API with tagIds filter
  └─ broadened text document contract + existing version history
```

新しいAndroid Module、Server Project、DB Table、Workerは追加しない。実装は1つの作業Branchと1本のPull Requestにまとめるが、差分reviewと検証が可能なように依存順のフェーズで進める。

## 現行実装で確認した主な原因候補

- `MediaPlayerScreen`は`fullscreen = true`でも、Header、Details、Status、Controlsを含む親`Column`の`verticalScroll`を維持している。
- 動画の1回tapは`videoControlsVisible`を切り替えるが、実際の操作群は動画のoverlayではなく、scrollするColumnの後続要素に配置されている。
- `PdfViewerScreen`は画面全体が`verticalScroll`で、PDF領域が420dpに固定されている。失敗時の主要actionが再試行ではなく`Download instead`になっている。
- `FileBrowserViewModel.open()`は読込中の同一Folderに対する再入を防ぐ前に、`folderStack`と`breadcrumbs`へ値を追加する。非同期の`refresh()`と`loadCurrentFolder()`にも世代管理がない。
- `KuraFileEntryRow`はThumbnailと複数行metadataを同じRowに配置し、FavoritesのThumbnailは72dpに固定されている。Searchは既存の`leading`差し替えを活用していない。
- Tagsは作成・改名・削除のactionだけを持ち、TagからSearchの既存`tagIds`条件へ遷移するactionがない。
- Size formatterが`feature-media`と`core-ui`に分散し、1,000基準と1,024基準、`bytes`と`B`の表記が混在している。
- Media metadataは`inspectOriginal()`のみを公開し、選択中のLow/Medium variantの`Content-Length`を画面stateとして保持していない。
- TextはServerとAndroidの両方で6 MIMEと厳密なUTF-8に限定されている。

## 主要設計決定

### 1. 表示中Sourceを画質とSizeの正とする

Media UIは「選択された画質」と「実際に表示・再生中のSource」を区別する。Size表示は常に後者の`variant`とmetadataを使う。画質変更中は旧Sourceと旧Sizeを維持し、新Sourceと新Sizeが確定した1回のstate updateで切り替える。

### 2. Full screenは独立した非scroll Layoutにする

Full screen時は通常PlayerのColumnを再利用せず、画面全体の`Box`に動画Surfaceと操作overlayだけを配置する。Android system barはimmersive表示にし、操作overlay表示中だけsafe insetを適用する。Backは最初にFull screenを解除し、Viewer自体からは離脱しない。

### 3. PDFの内部取得とUser保存を別actionにする

PDFのViewer用取得は`Open PDF`とし、利用者が選ぶDownload/SAF保存と表示上もstate上も分離する。失敗時の第一actionは同じアプリ内閲覧の`Retry`とし、`Save a copy`は副次actionにする。

### 4. Folder Navigationはrequest generation付きの単一state machineにする

Folder遷移の正は`FolderLocation(id, entry, breadcrumbs)`とし、stackとパンくずを別々に先行更新しない。各open/backに単調増加のgenerationを付け、最新generationの成功応答だけをcommitする。同一目的Folderへの連打は1つの進行中requestに収束させる。

### 5. Tag別一覧は既存Search契約を再利用する

新しいTag専用Server endpointやDB queryは追加しない。Tags画面の項目tapで`tagId`と表示用Tag名をSearch routeへ渡し、Search ViewModelを1 TagのAND filterで初期化する。表示名はUI用として扱い、queryの認可と存在確認はServerの`tagId`判定に従う。

### 6. Textは安全な自動判定と明示的な破壊的保存確認を分ける

`ACTIVE`なFileで、raw Sizeが1 MiB以下の場合は、MIMEや拡張子だけでText APIから拒否しない。Serverは次の順でdecodeする。

1. UTF-8 BOMがあればBOMを除いたUTF-8。
2. UTF-16LE/UTF-16BE BOMがあれば対応encoding。
3. BOMがなければ厳密なUTF-8。
4. 厳密decodeが失敗した場合はUTF-8 replacement decodeによるlossy preview。

Responseに`encoding`と`decodeStatus` (`EXACT` / `LOSSY`)を含める。`LOSSY`の原本は読取り直後に編集画面を表示できるが、保存前に内容がUTF-8として置換されることを警告する。Save requestは`acknowledgeLossySource = true`を明示しない限りServerで拒否する。認識できたUTF-16は元encodingを保って保存し、lossy previewの保存はUTF-8に正規化する。変更前のraw bytesは既存version historyに保持する。

バイナリ誤検出を減らすため、NUL比率と制御文字比率の上限を契約化する。上限を超えるFileは自動でEditorへ直接routeせず、File detailsの`Open as text`から警告と明示確認を経て開く。数値は実装前のfixture調査で固定し、正式文書とContract Testに反映する。

### 7. Size表記は`core-ui`の純粋関数へ集約する

`formatFileSize(bytes: Long?): String`を`core-ui`に置き、すべての利用者向けSize表示で使う。基数は1,024とし、`B`は整数、`KB`・`MB`・`GB`は小数1桁までとして末尾の`.0`を除く。`null`と負値は`Unknown`、TB以上もGB値として安全に表示する。内部API、Log、永続データのbyte値は変換しない。

## コンポーネント設計

### 1. Media metadata・画質state

**対象**:

- `core-model/media/MediaModels.kt`
- `core-network/media/MediaApi.kt`
- `core-data/media/MediaRepository.kt`
- `feature-media/MediaViewerController.kt`
- Photo/Video ViewModel・Screen

**実装の要点**:

- `inspectOriginal(fileId)`を内包する`inspectContent(fileId, variant)`を追加する。
- HEADの200は`MediaVariantMetadata(variant, byteCount, mimeType, acceptsRanges)`、202は既存`MediaJobSnapshot`に変換する。
- `MediaViewerState`へ「requested variant」、「displayed/playing variant」、「variant metadata」を持たせる。
- generationまたはrequest tokenで古いHEAD/GET/job poll応答を破棄する。
- Originalの通信確認契約と、Low/Medium失敗時にOriginalを自動取得しない契約を維持する。

### 2. Video player・Full screen

**対象**:

- `feature-media/player/MediaPlayerScreen.kt`
- `feature-media/player/MediaPlayerViewModel.kt`
- `feature-media/player/AndroidMediaPlayerController.kt`
- `feature-media/player/MediaVideoSurface.kt`
- `app/MediaPlayerRoute.kt`

**実装の要点**:

- 通常LayoutとFull screen LayoutをComposable境界で分ける。
- Full screenは`Box(fillMaxSize)`内にVideo Surface、buffer/error状態、操作overlayを重ね、`verticalScroll`を持たない。
- `PlayerSurface`の前面かつ操作overlayの背面に透明なtap layerを置いてoverlayをtoggleし、再生中は無操作時に自動非表示、一時停止・buffering・error中は必要な操作を表示する。
- seek操作中は自動非表示timerを停止する。
- `BackHandler`/system BackはFull screen解除を優先する。回転時もPlayer instanceを1 itemに保ち、再生位置・速度・`playWhenReady`を復元する。
- Player errorを認証、HTTP/Range、通信切断、codec非対応、派生生成失敗へ変換する。

### 3. PDF open state・Viewer Layout

**対象**:

- `core-data/media/TemporaryPdfStore.kt`
- `feature-media/pdf/PdfViewerViewModel.kt`
- `feature-media/pdf/PdfViewerScreen.kt`
- `feature-media/pdf/PdfDocumentController.kt`
- App側PDF routeとSAF save callback

**実装の要点**:

- `PdfLoadState`にmetadata、confirmation、private-cache download、open、render、typed failure、readyの遷移を明示する。
- Viewer取得用callbackを`onConfirmOpen`/`onRetryOpen`、User保存用callbackを`onSaveCopy`と命名・責務分離する。
- `Scaffold`のtop barを固定し、PDF viewportを残り高さの`Box(weight(1f))`へ拡張する。page操作とzoomはcompact overlayまたはadaptive bottom controlsへ置く。
- `PdfRenderer.Page`とParcelFileDescriptorは画面遷移、page切替、再試行、Session変更で必ずcloseする。Composeへ未公開の破棄Bitmapだけを即時recycleし、公開済みBitmapは描画display listとのraceを避けるため参照解放後のheap管理へ委ねる。
- 部分Fileはcancel・IOException・Size不一致・PDF signature不正で削除する。既存の容量・TTL・Session分離を維持する。

### 4. Content-first entry row・Thumbnail

**対象**:

- `core-ui/components/KuraFileEntryRow.kt`
- `feature-media/thumbnail/FileThumbnail.kt`
- `feature-search/SearchScreens.kt`
- `feature-search/OrganizationScreens.kt`
- App側Thumbnail注入

**実装の要点**:

- `KuraFileEntryRow`へcompact/visualの表示variantを追加するか、同じsemanticsを使う`KuraVisualFileEntryRow`を追加する。実装前のAPI影響reviewで小さい差分になる方を選ぶ。
- 写真・動画のThumbnailは幅に応じて少なくとも96dp相当の視覚領域を確保する。タブレットへの過度な拡大はしない。
- 名前は最大2行、補助metadataは原則1〜2行の`bodySmall`/`labelMedium`とし、状態・共有元など必要な情報だけ追加表示する。
- Favorites、Search、Tag別Searchで同じ表示ComponentとThumbnail fallbackを使う。
- Thumbnailは既存のSession scope、file version、request tokenをcache keyに維持し、失敗時にOriginalを取得しない。

### 5. Tagsからの絞込みNavigation

**対象**:

- `feature-search/OrganizationScreens.kt`
- `feature-search/SearchViewModels.kt`
- `feature-search/SearchScreens.kt`
- `app/MainActivity.kt`のSearch/Tags route

**実装の要点**:

- Tag cardの本体tapを「対象を表示」、改名・削除をtrailing actionとして分離する。
- Search routeへURL encode済み`tagId`を渡す。Tag名をroute identityや認可に使わない。
- Search ViewModelは初期`SearchInput(tagIds = setOf(tagId))`を一度だけ適用し、同じTag routeの再composeで不要に再初期化しない。
- 一覧から既存entry destination resolverを使い、Folderと各Viewerへ直接遷移する。

### 6. Settingsの視覚階層

**対象**:

- `core-ui/components/KuraComponents.kt`
- `core-ui/ThemeTokens.kt`
- `feature-settings/`
- `feature-backup/`のSettingsから到達する画面
- Admin cache・Connection設定画面

**実装の要点**:

- Settings共通rowはIconを装飾または補助要素として`onSurfaceVariant`系の色と小さめの固定領域に配置する。actionable iconは3:1以上のcontrastと48dp操作領域を保つ。
- headlineは`onSurface`、現在値とsupporting textは検証済み`onSurfaceVariant`を使い、alphaの重ね掛けでcontrastを下げない。
- 大きな装飾Icon、全rowの強いcontainer color、重複する長文説明を除き、Section、項目名、現在値、必要な短い説明の順に整理する。
- Light/Darkとdynamic colorの各paletteでcontrastを計測する。

### 7. Text document contract・Editor

**対象**:

- `server/src/KuraStorage.Application/Files/TextFileContracts.cs`
- `server/src/KuraStorage.Application/Files/TextFileService.cs`
- File version store/repositoryと関連Test
- `contracts/openapi/kurastorage-api.yaml`
- Android `core-model`/`core-network`/`core-data`のText契約
- `feature-text/`
- App側entry destination resolver/File details

**実装の要点**:

- MIME allowlistだけでの415を廃止し、File state、権限、raw Size、decode結果で判定する。
- `TextDocument`へ`decodeStatus`を追加し、Save requestへ`acknowledgeLossySource`と必要なencoding情報を追加する。OpenAPI、Server、Android DTO/modelを同時に変更する。
- 読取りと保存の両方でraw/encoded Size 1 MiB上限を強制する。
- `expectedVersion`、`operationId`、mutation lock、journal、immutable version、復元契約は変更しない。
- 既存対応MIMEは今までどおり直接Editorへrouteする。その他の明らかなText MIME/拡張子も直接route対象に追加し、それ以外はFile detailsの`Open as text`から開く。

### 8. File browser Navigation coordinator

**対象**:

- `feature-files/FileBrowserViewModel.kt`
- `feature-files/FileBrowserScreen.kt`
- `app/MainActivity.kt`のFile browser route

**実装の要点**:

- `folderStack`と`breadcrumbs`を`FolderLocation`のstackへ統合する。UIはそのsnapshotからパンくずを表示し、`currentFolder`を後付けでappendしない。
- open/backは共通`navigateTo()`を通し、進行中の目的IDとgenerationを記録する。
- 同一目的IDはignoreし、異なる目的IDは前requestをcancelして最新操作を採用する。cancelできない応答もgeneration不一致で破棄する。
- 目的Folderのdetailと一覧取得が必要条件を満たした後にだけstackをcommitする。失敗時は元snapshotに留まる。
- Top app bar Backとsystem Backは同じ`viewModel.back()`を呼び、rootで`false`の場合だけApp Navigationへ委譲する。
- initial shared folderのroot labelと親境界を保ち、personal rootへ誤って戻さない。

### 9. 検証fixture manifest・清掃

**責務**:

- 今回作成した外部状態だけを正確に追跡する。
- 既存データを削除対象にできない清掃guardを設ける。

**実装の要点**:

- 実機・実Server検証の開始前に、作業固有prefixとrun IDを決定する。
- 作成したUser ID、File/Folder ID、Tag ID、Share/Favorite等の関連ID、物理fixtureのchecksumをworkspace外の一時manifestへ即時追記する。認証情報やTokenは記録しない。
- 清掃はexact IDとmanifest membershipを必須とし、名前の部分一致、wildcard、親Folder全体、DB全件を対象にしない。
- 削除は通常のApplication/API境界で子関連から順に行い、直接SQL/物理削除が必要な場合は、対象と回復可否を再確認して検証手順に記録する。
- 清掃前後にmanifest IDの存在確認と、作業開始前から存在する保護対象のID・checksum確認を行う。
- Committed evidenceには手順、件数、成否、匿名化したID/checksumだけを残す。

## データフロー

### 動画を選択画質で開く

```text
1. Entryとnetwork contextから初期quality/variantを決める。
2. ViewModelがrequest generationを更新し、対象variantのHEADを実行する。
3. 202なら既存job poll、200ならvariant metadataを取得する。
4. Media3が同じvariantをRange取得し、READYになったらSourceとSizeを同時commitする。
5. Full screenは同じPlayer stateを非scroll Layoutへ表示する。
```

### PDFをアプリ内で開く

```text
1. OriginalのHEADでMIME、Size、Range supportを検証する。
2. Userへ推定通信量とprivate temporary storageの利用を表示する。
3. Open PDFの確認後にTemporaryPdfStoreへstreamingする。
4. signatureとSizeを検証し、atomic rename後のFileだけをPdfRendererへ渡す。
5. viewportに合わせてcurrent pageをbackground renderする。
6. 失敗時はtyped errorを示し、Retry openと必要な場合のSave a copyを分ける。
```

### Tagから対象一覧を開く

```text
1. Tags画面でTag本体をtapする。
2. App NavigationがtagId付きSearch routeへ遷移する。
3. Search ViewModelがtagIds = [tagId]で既存Search APIを呼ぶ。
4. ServerがTag ownership、現在権限、ACTIVE状態、paginationをSQLで確定する。
5. Androidが共通visual rowとThumbnailで結果を表示する。
```

### Folderを連打する

```text
1. 最初のopenでgeneration Nとtarget IDを記録する。
2. 同じtargetの再openはignoreする。別targetならgeneration N+1として前requestをcancelする。
3. detailと一覧の成功応答は、最新generationと一致する場合だけcommitする。
4. FolderLocation stackからcurrent folder、parent、パンくずを一度に更新する。
```

### lossyなDocumentを編集する

```text
1. Serverがraw bytesとSizeを検証し、decodeを試みる。
2. 厳密decode失敗時はLOSSYとしてreplacement previewを返す。
3. Androidは常時警告と保存時の破壊的変更確認を表示する。
4. Userが確認したSaveだけacknowledgeLossySource = trueを送る。
5. ServerがexpectedVersion、権限、operationIdを再検証し、旧raw bytesをversion化後に新内容をpublishする。
```

## エラーハンドリング戦略

### Media・PDF

- 既存`MediaUiError`の分類をUIのactionに対応させ、認証失効は再接続、一時的通信失敗はRetry、codec・破損・暗号化は対応不可の案内を出す。
- PDF固有は`TOO_LARGE`、`INSUFFICIENT_STORAGE`、`INCOMPLETE`、`INVALID_OR_CORRUPT`、`PASSWORD_PROTECTED`、`RENDER_FAILED`、`DISCONNECTED`、`AUTHENTICATION_REQUIRED`に整理する。
- Error messageにToken、File名、物理Path、SSID/BSSIDを含めない。request IDがある場合だけsupport用に表示する。

### Navigation

- request失敗時は元の`FolderLocation`を維持し、失敗したtargetをstack/パンくずへ追加しない。
- Back中のopenとopen中のBackはgenerationで最新intentへ収束させる。
- App routeの`popBackStack()`はFile browser rootのときだけ実行する。

### Text

- 1 MiB超過は413、File state・version・operation競合は409、読取不能なstorageは503とし、既存error codeの意味を保つ。
- lossy原本への確認なしSaveは新しい種類付き422で拒否する。
- decode/encodeは読み込みたraw bytesの範囲内で行い、失敗時に新versionや一時Fileをpublishしない。

## テスト戦略

### JVM・Server Unit Test

- File Size formatterの0、1,023、1,024、1 MiB前後、1 GiB前後、`null`、負値、`Long.MAX_VALUE`。
- Media ViewModelのrequested/displayed variant、metadata、生成中、画質切替競合、旧response破棄。
- Player overlayの表示・自動非表示条件とFull screen Back。
- File browserの同一Folder連打、異なるFolder連続tap、open中Back、失敗、shared root。
- SearchのTag初期filter、pagination、権限失効、route再compose。
- UTF-8/UTF-8 BOM/UTF-16LE/UTF-16BE、invalid byte、NUL/制御文字、1 MiB境界、lossy確認の有無、version競合、復元。

### Contract・MockWebServer・Server Integration Test

- variant付きHEADの200/202/401/403/404、`Content-Length`、`Content-Type`、`Accept-Ranges`。
- Videoの初回Range、206、seek、不正`Content-Range`、途中切断、派生生成完了。
- PDFのHEAD後GET、Size不一致、中断、空き容量不足、signature不正、Session分離、TTL。
- Text APIの新契約、認可、lossy save guard、immutable version、journal/recovery、OpenAPI fixture整合。
- Search `tagIds`の他User Tag存在秘匿、現在権限、非`ACTIVE`、安定pagination。

### Compose・Instrumented Test

- Full screenにscroll containerがなく、tapで操作overlayが表示され、BackでFull screenだけが解除される。
- PDF viewportが画面残り高さを使い、page、zoom、pan、Retry openが操作できる。
- Favorites、Search、Tag別結果のThumbnail・fallback・文字行数・直接route。
- SettingsのLight/Dark、360dp、Landscape、文字200%、semantics、touch target、contrast。
- Textのexact/lossy表示、保存確認、競合、再読込み。
- `PdfRenderer`とMedia3のActivity lifecycle、回転、background/foreground、resource close。

### 実機・実Server E2E

- Android 13実機と実Serverで、修正前の各問題を再現する手順と修正後の結果を記録する。
- 保証動画MIME、Low/Medium/Original、Full screen、seek、回転、復帰を確認する。
- 正常・破損・暗号化・容量境界PDFの開く/再試行/中断/清掃を確認する。
- ローカル直接Wi-Fiと登録済み外部Wi-Fi＋ZeroTierで起動、認証、File一覧、Video、PDFを確認する。必須条件不成立も確認する。
- Folder連打、異なるFolder連続tap、読込中Back、system Backを連続操作し、実在Pathと一致することを確認する。
- manifestのexact IDだけを清掃し、保護した既存データのID/checksumが不変であることを確認する。

## 依存ライブラリ

新規依存は追加しない。既存のCompose Material 3、Navigation Compose、Media3、Coil、OkHttp、Kotlin Coroutines、`PdfRenderer`、ASP.NET Core、EF Core/Npgsqlを使う。

## ディレクトリ構造

```text
apps/android/
├─ app/src/{main,test}/kotlin/com/kurastorage/app/
│  ├─ MainActivity.kt
│  ├─ MediaPlayerRoute.kt
│  └─ navigation-related tests
├─ core-model/src/{main,test}/.../
│  ├─ media/MediaModels.kt
│  └─ TextFileModels.kt
├─ core-network/src/{main,test}/.../
│  ├─ media/MediaApi.kt
│  └─ text contracts
├─ core-data/src/{main,test,androidTest}/.../
│  ├─ media/MediaRepository.kt
│  ├─ media/TemporaryPdfStore.kt
│  └─ TextFileRepository.kt
├─ core-ui/src/{main,test}/.../
│  ├─ components/KuraFileEntryRow.kt
│  ├─ components/KuraComponents.kt
│  └─ formatting/FileSizeFormatter.kt
├─ feature-files/src/{main,test,androidTest}/.../
├─ feature-media/src/{main,test,androidTest}/.../
├─ feature-search/src/{main,test,androidTest}/.../
├─ feature-settings/src/{main,test,androidTest}/.../
├─ feature-backup/src/{main,test,androidTest}/.../
└─ feature-text/src/{main,test,androidTest}/.../

server/src/
└─ KuraStorage.Application/Files/
   ├─ TextFileContracts.cs
   └─ TextFileService.cs

contracts/openapi/kurastorage-api.yaml
docs/
.steering/20260905-android-viewer-navigation-ux-fixes/
├─ requirements.md
├─ design.md
├─ tasklist.md
└─ evidence/
```

## 実装の順序

1. 現行不具合の再現、作業Branch作成、fixture manifestと既存データ保護baselineの用意。
2. 正式文書とOpenAPIにText拡張・variant metadata・UI/Navigationの契約変更を反映。
3. 共通File Size formatterとvariant metadata stateをTest firstで実装。
4. Video再生不具合とFull screen overlayを実装・検証。
5. PDFのOpen/Retry/Save境界とadaptive Viewerを実装・検証。
6. Content-first rowをFavorites、Search、Tag別一覧へ適用し、TagsからSearch routeを接続。
7. Settingsと下位画面の視覚階層・contrast・responsiveを修正。
8. Text decode/save契約とAndroid Editorの警告・対象routeをTest firstで実装。
9. File browser Navigation state machineと共通Back処理をTest firstで実装。
10. Android/Server自動検証、実機・実Server・Wi-Fi経路E2E、Accessibility・視覚確認を実施。
11. manifest対象だけを清掃し、既存データ不変を確認。
12. 差分をself-reviewし、Commit・Push・英語Pull Request作成、`tasklist.md`のPR完了記録を同じPRへ追加。

## 正式ドキュメント更新方針

- `docs/product-requirements.md`: Text対象/decode・保存警告、Tag別一覧、画質別Size、Full screen/Backの受入条件を更新する。
- `docs/functional-design.md`: Text APIのrequest/response/error、variant HEAD、Tag Navigation、Folder state machine、PDF open flowを更新する。
- `docs/architecture-design.md`: Text decode・version保護、Media metadata、非scroll Full screen、Navigation generationの境界を更新する。
- `docs/repository-structure.md`: 新しいformatterと変更コンポーネントの配置を反映する。
- `docs/development-guidelines.md`: File Size表記、lossy Text guard、Full screen、Folder遷移競合、fixture清掃のルールを追加する。

## セキュリティ考慮事項

- Media、PDF、Thumbnail、Textは現在の`SessionServices`、TLS host検証、route-bound client、User/Device/Session認証を必須とする。
- 登録済みWi-Fiは自動Backup policyの候補条件にとどめ、SSID/BSSIDをServer identityや権限の代替にしない。
- Tag結果はServer SQLでTag ownershipと現在権限を評価し、Client後filterで隠さない。
- Text対象を拡張しても、File存在秘匿、現在権限、Size上限、version競合、idempotencyを維持する。
- Token、パスワード、SSID/BSSID、File名、物理Path、Text本文、Tag名をLog、Metric label、Crash report、fixture manifestへ出力しない。
- テスト清掃はexact IDとmanifestで対象を制限し、既存データを含み得る破壊的操作は行わない。

## アクセシビリティ・Responsive考慮事項

- 操作領域は48dp以上とし、Iconには目的を表すcontent descriptionを持たせる。装飾Iconはsemanticsから除外する。
- 通常文字は4.5:1以上、大きな文字と操作に必要な非Text UIは3:1以上のcontrastを確保する。
- 写真・動画Thumbnailが大きくなっても名前、状態、overflow actionの操作領域を重ねない。
- 360dp、Landscape、文字200%ではRowの高さを固定せず、必要に応じてメタデータを折り返す。
- Player/PDFのoverlayはTalkBack focus中に自動非表示せず、現在の再生・page・zoom状態をsemanticsで通知する。

## パフォーマンス考慮事項

- 一覧Thumbnailは画面用派生データだけをCoilで読み、Originalをfallback取得しない。
- variant SizeはHEADで取得し、Size表示のためにGET本体を重複取得しない。Serverの既存job一意性でHEAD/GETの生成requestを収束させる。
- PDFは64 KiB bufferでstreamingし、current pageだけをbackground renderする。1 Bitmap 32 MiB・長辺4096pxの上限を維持する。
- Folder Navigationの古いrequestはcancelし、応答が返ってもUI stateを更新しない。
- Textは1 MiB上限内でdecodeし、無制限なcharset探索や複数回の全文複製を行わない。

## 互換性・Migration

- DB schema migrationは不要とする。
- Text APIのresponse/request field追加はOpenAPIとAndroidを同じPull Requestで更新する。旧クライアントが必須変更で壊れないよう、`acknowledgeLossySource`は既存のexact UTF-8 Fileでは省略可能な既存振る舞いを保つ。
- 既存の6 MIME・UTF-8 Fileの閲覧、編集、version history、復元を回帰Testで保護する。
- Navigation routeを追加する場合は、既存のSearch/Tags deep linkとback stackを維持する。

## 将来の拡張性

- Text decode契約は将来のcharset追加を可能にするが、今回はBOMで明示できるUTF-8/UTF-16とlossy UTF-8 previewに限定する。
- visual entry rowはRecent、Shared、Categoryに再利用可能にするが、今回の適用先はFavorites、Search、Tag別結果だけとする。
- Full screen overlayは将来のsubtitle、picture-in-picture、cast用actionを追加できる構造にするが、今回は実装しない。
- File Size formatterは将来TB表記を追加できるよう単位テーブル化するが、今回はGBまでを表示する。

## Pull Request運用

- 実装開始時に現在の作業BranchとPull Request状態を確認し、未Mergeの先行UI変更がある場合はそのMerge後の最新`main`から新しい短命Branchを作る。
- 今回の全フェーズは1つのPull Request単位であり、中間PRは作らない。
- 全タスク、自動・手動検証、正式文書、清掃、self-reviewが完了するまでPull Requestを作成しない。
- Pull Request作成後は`steering`スキルのモード3で完了記録を追加し、同じBranchへCommit・Pushして停止する。
