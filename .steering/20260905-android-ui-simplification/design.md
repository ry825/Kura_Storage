# Android UI簡素化・メディア操作改善 設計書

## アーキテクチャ概要

既存の単方向データフロー、Navigation Compose、Feature module境界、Repository/API契約を維持し、UIの視覚階層と画面間Routingを整理する。新しいServer公開API、Database schema、永続形式は追加しない。

画面固有の状態と通信は既存ViewModelが所有し、Composableは表示用StateとCallbackだけを受け取る。複数Featureにまたがる「項目をどのViewerで開くか」「前後移動の一覧Context」は`app` moduleが調停する。共通のTop app bar、Icon操作、Metadata、Overflow、Thumbnail surfaceは`core-ui`へ置き、Feature間で見た目とAccessibilityを揃える。

```text
MainActivity / NavHost (app)
├── Entry destination resolver
│   ├── Folder       -> File browser
│   ├── Photo        -> Photo viewer + photo context
│   ├── Video/Audio  -> Player + media context
│   ├── PDF/Text     -> Viewer / Editor
│   └── Unsupported  -> Entry details
├── MediaNavigationContextStore
│   └── Files / Shared / Favorites の順序付きFile ID
├── feature-files
│   ├── content-first browser
│   └── adaptive entry details sheet
├── feature-media
│   ├── adaptive photo viewer
│   ├── quality selection state
│   └── original download request
├── feature-search
│   ├── Favorites / Tags thumbnail list
│   └── EntryOrganizationViewModel
├── core-ui
│   └── app bar / icon action / metadata / thumbnail shell
└── core-data
    ├── existing MediaRepository + Coil fetcher
    ├── existing OrganizationRepository
    └── streaming MediaContentDownloader
```

## 設計原則

1. 通常時は内容を優先し、補助操作と詳細情報はTop app bar、overflow、Bottom sheetへ段階的に開示する。
2. 項目本体のtapは`Open`、overflowは補助操作とし、同じ場所に複数の主操作を競合させない。
3. Text labelを並べたNavigationを避け、48dp以上のIcon操作とContent descriptionを使用する。
4. 内部Enumや権限値は利用者向けLabelへ変換し、自明なMetadataは省略する。
5. Viewerの状態は「設定された希望画質」と「実際に表示中のvariant」を区別し、UIへ後者を表示する。
6. Favorite、Tag、Downloadの成功はRepositoryまたは書き込み完了を確認した後だけ表示する。
7. 狭い画面、文字200%、横画面、Dark themeを例外ではなく標準Layout条件として扱う。

## コンポーネント設計

### 1. 共通Top app barとIcon操作

**責務**:

- Backを左端、Refreshと画面固有操作を右端へ配置する。
- 画面名、現在位置、操作の視覚的な優先順位を統一する。
- Iconの意味、状態、利用不能理由をTalkBackへ伝える。

**実装の要点**:

- 既存`KuraTopAppBar`を基礎に、共通のBack、Refresh、overflow Icon actionを使用する。
- Navigation iconとActionは最小48dpのtap targetを持たせる。
- File pathはTop app barとは別の1行Breadcrumbとし、長い場合は末尾側を優先して省略する。
- Refresh中は多重実行を防ぎ、Iconの無効状態と進捗表示を重複させない。
- Icon assetは既存Compose/Vector資産を再利用し、Icon library追加だけを目的とした依存追加は行わない。

### 2. Files・SharedのContent-first browser

**責務**:

- FolderとFileを、名前、Thumbnail、最小限Metadataを中心に表示する。
- List/Grid、検索、Upload、Folder作成、Pagination、各Entry操作を維持する。
- 個人領域と共有領域で、必要な属性だけを表示する。

**実装の要点**:

- `Scaffold`のTop app bar、内容領域、必要時のFloating actionへ操作を分離する。
- Gridを写真が見やすい初期表示とし、`LazyVerticalGrid(GridCells.Adaptive(...))`で幅に応じた列数にする。Folder、非画像Fileも同じcell寸法内でIconと名前を表示する。
- List/Grid選択は`rememberSaveable`または既存Screen stateで画面再生成後も保持する。Process終了を越える永続設定は今回追加しない。
- Searchと表示切替は常時2段のButton列にせず、compact search fieldとTop app bar/overflowへ配置する。
- UploadとNew folderは権限がある場合だけ表示し、主要内容を隠さないFloating actionまたはoverflow actionとする。
- Entry card/row全体をOpen targetとし、右上または末尾の縦overflowから詳細・補助操作を開く。`Actions`文字Buttonは廃止する。
- 個人FilesではOwnerと通常Permissionを省略する。Sharedでは共有元、Permission、書込不可など操作判断に必要な情報だけを短いLabelで示す。
- Missing、Recovery、Pending等は色だけで表さず、Iconと短い状態Labelを併用する。
- Paginationは既存の追加読込を維持し、Grid化によって同じpageを重複要求しない。

### 3. Entry details Bottom sheet

**責務**:

- Metadataと実行可能操作を読みやすく提示する。
- 通常操作、整理操作、共有操作、危険操作を区別する。
- 文字拡大時にもButtonやLabelを欠けさせない。

**実装の要点**:

- 固定幅AlertDialog内のButton群を、縦scroll可能な`ModalBottomSheet`を基本とした詳細面へ置き換える。大画面では同じContentを制約幅のPanel/Dialogへ適応可能な構造にする。
- HeaderにはThumbnail/Icon、名前、種別を置き、MetadataはLabel/Valueの縦配置にする。横1行へ無理に詰めない。
- 操作名は`Open`、`Download original`、`Rename`、`Move`、`Share`、`Add to favorites`/`Remove from favorites`、`Manage tags`、`Move to Trash`等の具体的な動詞にする。
- 操作はFlowRowへ詰めず、幅全体のrowまたはlist itemとして縦に並べる。実行不能な操作は通常非表示とし、状態理解に必要な場合だけ理由付き無効表示にする。
- Trash等の危険操作は末尾の区切られたDanger sectionへ置き、既存確認Dialogと認可判定を維持する。
- Raw enumは表示用mapperで`Read only`、`Can edit`、`Unavailable`等へ変換する。

### 4. Adaptive photo viewer

**責務**:

- 利用可能領域の大半を写真表示に使用する。
- 前後移動、画質変更、Favorite、Tag、Download、詳細へ直接到達させる。
- Zoom/Pan/Double tap、生成待ち、Retry、Prefetchを維持する。

**実装の要点**:

- 固定`360.dp` viewportを廃止し、`Scaffold`内の写真Canvasへ`weight(1f)`と安全な最小寸法を与える。LandscapeではToolbarを横またはcompact配置へ適応させる。
- Previous/Nextの大きなButtonを写真上から除去する。Zoom倍率が等倍のときは水平方向Swipeで前後移動し、Accessibilityと操作発見性のため写真外のcompact previous/next操作も提供する。
- Zoom中のPanを前後Swipeとして誤判定しない。Swipeは閾値と方向判定を持ち、1gestureにつき1項目だけ移動する。
- 上部はBack、項目位置、overflow、下部はFavorite、Tags、`Download original`、Qualityをcompact toolbarに配置する。大きな常設Cardは置かない。
- Toolbar Iconは選択状態をIcon形状とContent descriptionの両方で示す。通信中は対象操作だけを無効化する。
- DetailsはViewerを閉じずに詳細面へ遷移でき、戻るとZoom以外の一覧Contextと選択項目を保つ。

### 5. 写真画質State machine

**責務**:

- Settingsの接続種別別画質をViewer初期値へ適用する。
- 利用者が選んだLow、Medium、Originalと実Request/表示を一致させる。
- 派生生成中、失敗、再試行、古い非同期応答を安全に扱う。

**実装の要点**:

- 写真を開いたとき、`MediaViewerController`はQuality policyが返したvariantを確認Dialogなしで直ちに要求する。`Load original photo?`と写真用の確認待ちStateを削除する。
- Video/Audio等の転送確認Policyは今回変更せず、写真の初期読込だけを対象にする。
- Quality menu選択時はrequest generationを増加させ、`fileId + fileVersion + variant + session scope + generation`に対応する現在要求を識別する。
- Coilの永続Cache keyは既存の`scopeId:fileId:fileVersion:variant`を維持する。generationは古い表示結果を破棄するために使用し、同一variantの有効Cacheを不必要に分断しない。
- UIは希望画質ではなく、`Ready`となって現在Canvasが表示しているvariantを`Low`、`Medium`、`Original`として示す。Loading中は移行先も短く示す。
- 古いgenerationの成功・生成Job完了・Errorは現在Stateを上書きしない。
- Low/Medium/Originalの違いを端末側のBlurや拡大処理で偽装しない。実Server検証ではRequest path、Content-Type、decoded pixel寸法、byte sizeを比較する。

### 6. Viewer内Favorite・Tag操作

**責務**:

- 現在の写真のFavorite状態とTagを表示・更新する。
- 通信結果不明や権限失効を成功に見せない。

**実装の要点**:

- `app`が現在のFile IDに対応する既存`EntryOrganizationViewModel`を生成・保持し、Photo viewerへ表示StateとCallbackを渡す。
- `feature-media`はOrganizationRepositoryを直接参照せず、`isFavorite`、Tag一覧、pending/errorと操作Callbackだけを受け取る。
- Favorite Icon tapで追加/解除し、pending中は同一操作を抑止する。成功後にServer応答Stateを反映する。
- TagsはBottom sheetで付与済みと利用可能Tagを一覧表示し、各Tag単位のpending状態を示す。新しいTag管理APIは追加しない。
- Network失敗は「結果不明」の可能性を含む既存Organization errorを表示し、Refresh可能にする。

### 7. Favorites thumbnailと直接Routing

**責務**:

- Favorites内のMediaを見た目で判別可能にする。
- Entry type/MIMEに応じた適切なDestinationへ直接開く。
- Favorites順の前後Navigation contextを構成する。

**実装の要点**:

- FavoritesのEntry row/cardへ既存`FileThumbnail`相当のthumbnail slotを追加し、写真・動画・PDFは派生Thumbnailを表示する。
- `app`にEntry destination resolverを設け、Files、Shared、Favoritesから同じ判定を再利用する。
- Photo/Video/Audioを開く前に、現在のFavorites結果から同種かつActiveな項目を順序どおり`MediaNavigationContextStore`へ登録する。
- PDF、Textは各Viewer/Editor、Folderは該当Folder browserへ直接Routingする。非対応形式、Missing、操作不能項目だけEntry detailsを開く。
- Thumbnail失敗時はファイル種別Iconへフォールバックし、一覧全体をErrorにしない。

### 8. Original download coordinator

**責務**:

- Photo viewerから元ファイルをSAF選択先へstreaming保存する。
- Cancel、失敗、不完全File、通信結果を正しく扱う。
- 表示variantと保存variantを分離する。

**実装の要点**:

- Photo viewerのDownload requestは表示中画質にかかわらず`MediaVariant.ORIGINAL`を固定し、Action名も`Download original`とする。
- `CreateDocument`へ元File名と可能な限り具体的なMIME typeを渡す。URI未選択はCancelとして通知不要または短いCancel表示にし、成功にはしない。
- `ContentResolver`のOutputStreamへ`MediaContentDownloader.download`でcopyし、全体ByteArray化しない。
- Stream closeまで完了した後だけ成功Snackbarを表示する。例外時は作成済みURIの削除を試み、削除不能なら不完全Fileの可能性を明示する。
- Errorは認証/認可、Network、容量/書込、Server応答、生成未完了へ表示用に分類するが、Token、絶対Path、個人File名をLogへ出さない。
- File browser/PDFの既存Download coordinatorと共通化できる範囲を確認する。ただし共通化のためだけの大規模Refactorは行わず、各既存回帰Testを維持する。

## データフロー

### Files・Favoritesから写真を開く

```text
1. 利用者がEntry本体をtapする。
2. appのEntry destination resolverがentryType、status、MIMEを判定する。
3. 写真の場合、表示中一覧からActiveな写真だけを順序維持で抽出する。
4. MediaNavigationContextStoreへID列を登録し、contextIdを得る。
5. photo/{contextId}/{fileId}へ遷移する。
6. ViewerはcontextIdから現在位置・前後IDを取得し、選択写真を表示する。
```

### 写真を開いて画質を適用する

```text
1. Viewerが現在の接続種別とSettingsのQuality preferenceを読む。
2. Controllerが対応variantと新しいrequest generationを確定する。
3. Repository/Coil fetcherへfileId、fileVersion、variant、session scopeを渡す。
4. 派生生成中ならgenerationに紐づけてpoll/retry状態を表示する。
5. 現在generationのReadyだけをCanvasへ反映し、表示中variant Labelを更新する。
6. 前のgenerationの応答は破棄する。
```

### ViewerからFavorite・Tagを変更する

```text
1. Viewer toolbarのFavoriteまたはTagsを操作する。
2. EntryOrganizationViewModelが対象操作をpendingにする。
3. OrganizationRepositoryが既存APIへ更新要求する。
4. Server応答後にFavorite/Tag stateを置換する。
5. 失敗時はpendingを解除し、結果不明を含むErrorとRefresh導線を表示する。
```

### 元写真をDownloadする

```text
1. 利用者がDownload originalを選ぶ。
2. appがFile名/MIMEを指定してSAF CreateDocumentを起動する。
3. 選択URIをOutputStreamとして開く。
4. MediaContentDownloaderがORIGINAL responseをstreaming copyする。
5. close完了後に保存成功を通知する。
6. 途中失敗時はURI削除を試み、具体的なErrorと再試行導線を表示する。
```

## Navigation・状態保持

- Destination route形式は既存を維持し、公開Deep linkは追加しない。
- List/Grid、検索入力、選択Folderは`rememberSaveable`または既存ViewModel stateに保持し、回転等で不用意に初期化しない。
- Media contextはSession内の一時状態とし、Logout、Server変更、認証失効時に既存どおりclearする。
- Favorites更新後も開いたViewerのcontextは安定したsnapshotとして扱う。一覧へ戻ってRefreshした時点で新しい順序を使用する。
- Process deathで一時contextが失われた場合は、現在File単体でViewerを復元し、前後操作を無効にする。Crashさせない。

## エラーハンドリング戦略

### 表示用Error分類

新しいDomain例外階層は原則追加せず、既存`KuraStorageException`、Organization error、Media生成状態、I/O例外を画面用Errorへ変換する。

| 分類 | 表示 | 再試行 |
|---|---|---|
| 認証失効 | 再ログインが必要であることを表示 | Session flowへ委譲 |
| 認可/共有失効 | 操作できない、または共有が変更されたことを表示 | Refresh |
| Network/結果不明 | 完了を断定せず、状態確認を促す | Refresh / Retry |
| 派生生成中 | 生成中と表示し現在generationだけpoll | 自動 + 手動Retry |
| SAF Cancel | 成功表示しない | 再度Download |
| SAF書込/容量不足 | 保存できなかったことを表示 | 保存先確認後Retry |
| Thumbnail失敗 | 種別Iconへfallback | Entry本体は操作可能 |
| Missing/Recovery | 状態Labelと許可された既存操作を表示 | Refresh / Recovery |

### エラーハンドリングパターン

- 一覧全体、Entry単体、操作単体のError scopeを分離し、1 thumbnailの失敗で画面全体を置換しない。
- Snackbarだけでは操作不能理由が失われる場合、Bottom sheet内へinline errorを併用する。
- Throwable本文をそのまま利用者へ表示しない。request IDが既存Errorにある場合だけ診断情報として安全に表示する。
- Download失敗時の削除処理自体が失敗しても元の例外を保持し、不完全Fileの可能性を追加情報として示す。

## テスト戦略

### ユニットテスト

- Entry destination resolverがFolder、Photo、Video、Audio、PDF、Text、非対応、Missingを正しいDestinationへ分類する。
- Favoritesから写真を開くと写真だけが元の順序でMedia contextへ登録される。
- Settingsの接続別Qualityが写真の初期variantへ確認Dialogなしで適用される。
- Low、Medium、Original選択が対応variantを要求し、古いgenerationの応答が現在表示を上書きしない。
- Cache keyがsession scope、fileId、fileVersion、variantで分離される。
- Download selectionが表示画質にかかわらずOriginalとなる。
- Favorite/Tag pending、成功、Network結果不明、認可失効のState遷移を確認する。
- Metadata表示mapperがRaw enumを利用者向けLabelへ変換し、自明な個人領域属性を省略する。

### Compose UIテスト

- Production UIに`Family shared`、`Actions`、`Load original photo?`が残っていない。
- Back、Refresh、overflow、Favorite、Tags、Quality、previous/nextにContent descriptionと状態説明がある。
- Files/GridとFavoritesにthumbnail semanticsがあり、Entry tapとoverflow tapが混線しない。
- Photo viewerのprevious/next controlが写真Nodeへ重ならず、Swipeと非重畳操作の両方で移動できる。
- Entry detailsが狭い幅と大きいfont scaleでscroll可能で、危険操作が区別される。
- Loading、Generating、Error、Empty、Missing、Read onlyの各状態で主操作が到達可能である。

### Repository・統合テスト

- MockWebServerでLow、Medium、Originalが異なる既存endpoint/variantへRequestされる。
- Coil fetcherがvariant別Content-TypeとCache keyを維持する。
- MediaContentDownloaderがOriginalをchunked streamingし、途中失敗を成功扱いしない。
- SAF coordinator相当Test doubleで成功、Cancel、open失敗、copy失敗、delete成功/失敗を確認する。
- File browserとPDFの既存Download/Upload Testを回帰実行する。

### 実機・実Server検証

- Android 13実機でFiles、Shared、Favorites、Tags、Viewer、Favorite/Tag、3画質、Original downloadを確認する。
- Low、Medium、OriginalそれぞれのRequest variant、HTTP status、Content-Type、decoded寸法、byte sizeを機密情報なしで記録する。
- Download後のFile sizeまたはSHA-256をServer originalと比較し、対応アプリで開く。
- 360dp、font scale 100%/200%、Portrait/Landscape、Light/Dark、TalkBackで重なり、切れ、読み上げ順、tap targetを確認する。
- 変更前後Screenshotを同じデータ・画面条件で取得し、一覧表示量とViewer表示領域を比較する。

### 検証Command

```bash
./scripts/ci/verify-android.sh
git diff --check
```

Serverコードへ局所修正が必要になった場合だけ、影響Moduleの`dotnet test`と`./scripts/ci/verify-server.sh`を追加する。

## 依存ライブラリ

新規外部ライブラリは追加しない。既存のJetpack Compose Material 3、Navigation Compose、Coil、Coroutines、Android SAFを使用する。Swipeは既存Compose gesture/Foundation APIで実装し、Accompanist等を追加しない。

## ディレクトリ構造

実装時の主な変更候補を示す。正確なFile分割は既存責務を確認して決定する。

```text
apps/android/
├── app/src/main/kotlin/com/kurastorage/app/
│   ├── MainActivity.kt                       # destination調停、SAF、Viewer連携
│   └── MediaNavigationContextStore.kt        # 一覧Context
├── core-ui/src/main/kotlin/com/kurastorage/core/ui/
│   └── components/                           # app bar、icon action、metadata等
├── core-data/src/main/kotlin/com/kurastorage/core/data/media/
│   ├── KuraMediaFetcher.kt                   # variant/cache契約維持
│   └── MediaContentDownloader.kt             # streaming契約維持
├── feature-files/src/main/kotlin/com/kurastorage/feature/files/
│   └── FileBrowserScreen.kt                  # content-first一覧、details sheet
├── feature-media/src/main/kotlin/com/kurastorage/feature/media/
│   ├── MediaViewerController.kt              # 写真Quality state
│   └── photo/PhotoViewerScreen.kt            # adaptive viewer/toolbars
├── feature-search/src/main/kotlin/com/kurastorage/feature/search/
│   ├── OrganizationScreens.kt                # Favorites thumbnail
│   └── OrganizationViewModels.kt             # Favorite/Tag state再利用
└── */src/test, */src/androidTest             # unit/Compose/integration tests

docs/
├── product-requirements.md
├── functional-design.md
├── architecture-design.md
├── repository-structure.md
└── development-guidelines.md
```

## 正式ドキュメント更新方針

- `docs/product-requirements.md`: `Shared`用語、写真Viewerの確認Dialog廃止、Favorites直接Viewer、Original downloadの受け入れ条件を反映する。
- `docs/functional-design.md`: Content-first Files、details sheet、Viewer toolbar/Swipe、Quality state、Favorites Routing、SAF失敗処理を更新する。
- `docs/architecture-design.md`: appによるdestination調停、Media context、variant generation、Cache/session境界を更新する。
- `docs/repository-structure.md`: 新規Component/Fileを追加した場合だけ配置を反映する。
- `docs/development-guidelines.md`: 今回確立した共通UI/Accessibility規則が一般化できる場合だけ追記する。

## 実装・Pull Requestの順序

### PR 1: Content-first navigation and file browsing

1. 共通Top app bar/Icon actionと`Shared`用語を整える。
2. Files/Sharedをadaptive grid/listとcompact操作へ再構成する。
3. Entry detailsをadaptive Bottom sheetへ変更する。
4. 関連Unit/Compose Testと正式ドキュメントを更新し、Android検証を行う。

### PR 2: Adaptive photo viewer and reliable media actions

1. 写真viewport、非重畳Navigation、Viewer toolbarを再構成する。
2. 写真のQuality確認Dialogを廃止し、generationを考慮したvariant切替を修正する。
3. ViewerへFavorite/Tag操作を接続する。
4. Original downloadと失敗処理を修正する。
5. MockWebServer、Compose、SAF、実Server/実機検証と正式ドキュメント更新を行う。

### PR 3: Visual favorites and app-wide polish

1. FavoritesへThumbnailを追加し、Entry destination resolverで直接Routingする。
2. Favorites順のMedia contextを接続する。
3. 主要画面の冗長なCard/Label/`Actions`残存を横断確認する。
4. Responsive、font 200%、Landscape、Dark、TalkBack、Screenshot比較と全Android回帰検証を行う。

各PRは、対象Task、Test、文書更新、Commit、Push、英語のPull Request作成、`tasklist.md`のPR完了記録までを完了して停止する。後続PRはユーザーの継続指示後に開始する。

## セキュリティ考慮事項

- 認証Token、Session secret、SAF URI、絶対保存Path、個人File名をLogへ出さない。
- UIで操作を隠すだけに依存せず、既存Server認可を最終判断として維持する。
- SharedのRead only/Editor境界、Owner操作、Trash/Recovery制約を変更しない。
- Thumbnail、Low/Medium、OriginalのCacheをsession scopeで分離し、Logout/Server変更時のclearを維持する。
- Downloadは利用者がSAFで選択したURIだけへ書き込み、任意Pathを組み立てない。
- Error本文やrequest IDの扱いは既存sanitize方針に従い、Server内部情報を表示しない。

## Accessibility・Responsive考慮事項

- Iconのみの操作は48dp以上、明確なContent description、選択/無効/pending状態を持つ。
- 文字Buttonを横方向へ固定配置せず、縦Layout、scroll、adaptive widthを用いる。
- 写真上のgestureだけを唯一の操作手段にせず、TalkBackから到達できるprevious/next操作を写真外に置く。
- Favorite、Quality、Shared、Missing、Errorを色だけで区別しない。
- Semantics順はTop navigation、主要Content、主要操作、補助/危険操作の順とする。
- Safe drawing insetとIME insetを適用し、System bar、cutout、keyboardとの重なりを避ける。

## パフォーマンス考慮事項

- 一覧はLazy componentを維持し、viewport外Thumbnailを一括decodeしない。
- Thumbnailは既存派生variantを使用し、Grid表示のためにOriginalを取得しない。
- Media prefetchの並列数制限を維持し、Quality変更時は不要な古いJob/UI反映を停止する。
- Original downloadはstreamingし、File全体をMemoryへ保持しない。
- Metadata mapperとdestination判定は純粋関数とし、Compose再構成ごとのNetwork要求を発生させない。
- FavoritesのOrganization state取得は現在写真単位とし、Toolbar再構成で再作成しない。

## 互換性・Migration

- Database migration、Server migration、API version変更はない。
- 既存Role、Share、Device、Session、File storage layoutは変更しない。
- 既存Settings値をそのまま使用するため、利用者の画質設定Migrationは不要である。
- `Family shared`は表示Labelだけを`Shared`へ変更し、内部route名やAPI名は互換性のため維持できる。
- 現行の写真Original確認を前提とするUI Testと正式仕様は、新しい自動適用仕様へ同じPRで更新する。

## 将来の拡張性

- Entry destination resolverにより、将来の新しいViewerを一覧ごとに重複実装せず追加できる。
- Details contentをBottom sheetと大画面Panelで共有できるため、Tablet対応時に操作契約を維持できる。
- Viewer toolbarは操作State/Callbackを受け取るため、Album等を追加してもMedia repositoryへUIが直接依存しない。
- 今回はProcessを越える表示Mode永続化や新規Localization基盤を追加しない。利用者要求が確認できた時点で独立仕様として扱う。

## 設計上の非対象

- Serverの派生画像Algorithmを変更して見かけの差を強制すること。
- 新しい共有、Album、Timeline、顔認識、Map機能。
- Android以外のClient UI変更。
- 全画面の文言翻訳またはLocalization基盤導入。
- API/Databaseを伴う大規模Media pipeline再設計。
