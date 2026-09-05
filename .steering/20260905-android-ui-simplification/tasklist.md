# Android UI簡素化・メディア操作改善 タスクリスト

## 🚨 タスク完全完了の原則

**このファイルの全タスクは最終的に完了させる。ただし、1回の実装では1つのPull Request単位を完了し、Pull Request作成後に停止してよい。**

### 必須ルール

- 全てのタスクを最終的に`[x]`にする。
- 「時間の都合により別タスクとして実施予定」「実装が複雑すぎるため後回し」は禁止する。
- 選択したPull Request単位に未完了タスクを残したまま作業を終了しない。
- 後続Pull Requestのタスクは、依存元がMergeされるまで`[ ]`のまま残してよい。
- タスクが大きい場合は実装中に具体的なサブタスクへ分割し、完了条件を弱めない。
- 技術的に不要になった場合だけ、取消理由と代替実装を該当項目およびPR完了記録へ明記する。

### Pull Request運用

- PR 1、PR 2、PR 3の順で進め、同時に複数PRへ着手しない。
- 各PRは原則として最新の`main`から短命Branchを作成する。
- 後続PRが前PRへ依存する場合、前PRが`main`へMergeされたことを確認してから開始する。
- 各PRで実装、テスト、文書更新、差分レビュー、Commit、Push、CI、英語PR作成、PR完了記録まで完了する。
- コーディングエージェントはPull RequestをMergeしない。Pull Request作成と完了記録のPush後に停止する。

---

## 計画文書

- [x] `requirements.md`を作成し、ユーザー承認を得る。
- [x] `design.md`を作成し、ユーザー承認を得る。
- [x] `tasklist.md`を作成し、ユーザー承認を得る。

---

## PR 1: Content-first navigation and file browsing

### 1. 実装前確認

- [x] PR 1の実装前状態を確認する。
  - [x] `tasklist.md`を読み、PR 1だけを今回の実装範囲として選択する。
  - [x] `git status`と既存差分を確認し、ユーザー変更を保護する。
  - [x] 最新の`main`と依存Pull Requestの状態を確認し、PR 1用Branchを用意する。
  - [x] `docs/`のAndroid UI、Files、Shared、File操作、Accessibilityに直接関係する節を再確認する。
  - [x] `HomeScreen`、`FileBrowserScreen`、共通UI component、関連Testの類似実装を確認する。

### 2. 共通Navigationと用語

- [x] 共通Top app barとIcon操作を整理する。
  - [x] Backを左端、Refreshと補助操作を右端またはoverflowへ統一する。
  - [x] Back、Refresh、overflowへ48dp以上のtap targetとContent descriptionを設定する。
  - [x] Refresh中の多重実行防止と状態表示を維持する。
- [x] 利用者向け`Family shared`表記を`Shared`へ統一する。
  - [x] Home、Navigation、画面Title、説明文を更新する。
  - [x] Production codeとTest fixtureに旧表記が残っていないことを確認する。

### 3. Files・Shared一覧

- [x] Files/Sharedの上部操作領域をcompact化する。
  - [x] 重複するBack/Refresh/Search/List/GridのButton列を整理する。
  - [x] 長い現在Pathを1行の省略可能なBreadcrumbとして表示する。
  - [x] Search、表示切替、Upload、New folderを権限と優先度に応じてTop app bar、compact field、Floating action、overflowへ再配置する。
- [x] Content-firstのadaptive grid/listを実装する。
  - [x] Gridを写真一覧が見やすい初期表示にする。
  - [x] 写真・動画・PDFで既存派生Thumbnailを表示し、失敗時は種別Iconへfallbackする。
  - [x] Folderと非画像Fileを同じLayout規則で欠けずに表示する。
  - [x] List/Grid状態を画面再生成後も保持する。
  - [x] Pagination、追加読込、Refreshで重複項目または重複Requestを発生させない。
- [x] Entryの情報階層と操作を整理する。
  - [x] 名前から`Folder:`/`File:`接頭辞を除去する。
  - [x] 個人Filesでは自明なOwner/Permissionを省略する。
  - [x] Sharedでは共有元、Permission、書込可否など判断に必要な情報だけを表示する。
  - [x] Missing、Recovery、Pending等をIconと短いLabelで表示する。
  - [x] Entry本体tapをOpen、縦overflowを補助操作として分離し、`Actions`文字Buttonを廃止する。
  - [x] Folder、Photo、Video、Audio、PDF、Textの既存Open動作を維持する。

### 4. Entry details・操作面

- [x] `File details`をadaptiveな詳細面へ再構成する。
  - [x] 縦scroll可能なBottom sheetを基本とし、360dp幅と文字200%で内容を欠けさせない。
  - [x] HeaderへThumbnail/Icon、名前、種別を配置する。
  - [x] Size、更新日時、Owner、共有元、Permission、状態をLabel/Valueで表示する。
  - [x] Raw enumを`Read only`、`Can edit`等の利用者向けLabelへ変換する。
- [x] 操作を具体的な動詞と権限に基づいて表示する。
  - [x] `Open`、`Download original`、`Rename`、`Move`、`Share`、Favorite、Tags、Trash等を既存Capabilityどおり表示する。
  - [x] 実行不能操作は原則非表示とし、必要時だけ理由付き無効表示にする。
  - [x] 危険操作を末尾へ分離し、既存確認DialogとServer認可を維持する。
  - [x] Upload、Folder作成、Rename、Move、Share、Trash、Restore、Missing/Recovery操作へ回帰を起こさない。

### 5. PR 1テスト・文書・検証

- [x] PR 1のUnit/Compose Testを追加・更新する。
  - [x] Metadata表示mapper、個人/Shared属性省略、表示Mode保持をUnit Testする。
  - [x] Back、Refresh、overflow、Thumbnail、Entry tap、details sheetのCompose Testを追加する。
  - [x] 360dp幅、文字200%、Empty、Loading、Error、Missing、Read onlyをTestする。
  - [x] 旧`Family shared`、`Actions`が対象Production UIへ残らないことをTestまたは検索で確認する。
- [x] PR 1に対応する正式文書を更新する。
  - [x] `docs/product-requirements.md`の`Shared`用語とFiles/Details受け入れ条件を更新する。
  - [x] `docs/functional-design.md`のTop app bar、一覧、details sheetを更新する。
  - [x] 必要な場合だけ`docs/architecture-design.md`、`docs/repository-structure.md`、`docs/development-guidelines.md`を更新する。（責務・構成・開発規約の変更がないため更新不要）
- [x] PR 1の自動検証を完了する。
  - [x] 変更対象のUnit/Compose Testが成功する。
  - [x] `./scripts/ci/verify-android.sh`が成功する。
  - [x] `git diff --check`が成功する。
- [x] PR 1の画面と主要操作を確認する。
  - [x] Files/SharedのList/Grid、Search、Refresh、Pagination、Upload、New folderを確認する。
  - [x] Entry detailsと権限別操作を確認する。
  - [x] 360dp、font 100%/200%、Portrait/Landscape、Light/Darkで重大な重なり・切れがない。
  - [x] TalkBackの読み上げ順、Content description、tap targetをSemantics treeとCompose操作で確認する。
  - [x] 変更前後のFiles/Shared/details Screenshotを同条件で保存し比較する。（`evidence/pr1/README.md`）

### 6. PR 1完了処理

- [x] PR 1の差分をセルフレビューする。
  - [x] PR 1の目的外変更、秘密情報、絶対Path、デバッグコード、不要依存がない。
  - [x] 実装、Test、正式文書、Steeringの対応が取れている。
- [x] PR 1をCommit、Pushし、英語のPull Requestを作成する。
  - [x] 英語本文へ目的、対象Task、変更、Test結果、影響、未実施事項を記載する。
  - [x] CI成功を確認し、Pull RequestはMergeしない。
- [x] `各Pull Request完了記録`へPR 1の実施結果を記載する。
  - [x] 完了日、PR番号/URL、Test、計画差分、追加Task、取消Task、PR 2への引継ぎを記載する。
  - [x] 記録をCommit・Pushし、Pull Requestへ反映されたことを確認する。

---

## PR 2: Adaptive photo viewer and reliable media actions

### 1. 実装前確認

- [x] PR 2の実装前状態を確認する。
  - [x] PR 1が`main`へMerge済みであることを確認する。
  - [x] `tasklist.md`を読み、PR 2だけを今回の実装範囲として選択する。
  - [x] 最新の`main`からPR 2用Branchを作成し、`git status`と既存差分を確認する。
  - [x] `docs/`のPhoto viewer、Quality、Media generation、SAF Download、Favorites/Tagsに関係する節を再確認する。
  - [x] `PhotoViewerScreen`、`MediaViewerController`、`KuraMediaFetcher`、`MediaContentDownloader`、Organization ViewModel、関連Testを確認する。

### 2. Adaptive photo viewer

- [x] 写真を主役にしたadaptive Layoutを実装する。
  - [x] 固定360dp viewportを廃止し、利用可能領域へ追従させる。
  - [x] System bar、cutout、Toolbarを考慮し、Portrait/Landscapeで写真を欠けさせない。
  - [x] 大きな常設Cardを除去し、Back、位置、Favorite、Tags、Download、Quality、その他操作をcompact toolbarへ整理する。
- [x] 写真へ重ならない前後Navigationを実装する。
  - [x] Previous/Nextの大きなoverlay Buttonを除去する。
  - [x] 等倍時の水平方向Swipeを実装し、1gestureで1項目だけ移動する。
  - [x] Zoom中のPanを前後Swipeとして誤判定しない。
  - [x] 写真外にTalkBackから操作可能なcompact previous/nextを用意する。
  - [x] Zoom、Pan、Double tap、現在位置、前後Prefetch、Details往復を維持する。

### 3. 写真画質State

- [x] Settingsの写真画質を確認Dialogなしで自動適用する。
  - [x] 写真用`Load original photo?` Dialogと確認待ちStateを削除する。
  - [x] 接続種別別のLow/Medium/Original設定をViewer初期variantへ適用する。
  - [x] Video/Audio等の既存転送確認Policyへ影響を与えない。
- [x] Viewer内Quality切替を実Requestと一致させる。
  - [x] Low、Medium、Original選択ごとに対応variantを再要求する。
  - [x] request generationで古い成功、生成完了、Errorによる巻き戻りを防ぐ。
  - [x] session scope、file ID、file version、variantを含む既存Cache keyを維持する。
  - [x] 希望画質ではなく実際に表示中のvariantとLoading先を簡潔に表示する。
  - [x] 生成中、Retry、失敗、File切替のStateを維持する。

### 4. Viewer内Favorite・Tags

- [x] Photo viewerへ既存Organization stateを接続する。
  - [x] appが現在File用の`EntryOrganizationViewModel`を保持し、表示StateとCallbackをViewerへ渡す。
  - [x] `feature-media`からOrganizationRepositoryを直接参照しない。
  - [x] File切替時にFavorite/Tag stateを正しいFileへ切り替える。
- [x] Favorite操作をViewer toolbarへ追加する。
  - [x] 追加/解除状態、pending、操作不能をIconとSemanticsへ反映する。
  - [x] Server成功応答後だけ表示Stateを更新し、結果不明を成功扱いしない。
- [x] Tags操作をViewer toolbarへ追加する。
  - [x] Bottom sheetで付与済み/利用可能Tagを表示する。
  - [x] Tag単位の追加/解除、pending、Error、Refreshを既存APIで処理する。
  - [x] 文字200%でもTag名と操作が欠けない。

### 5. Original download

- [x] 現在の写真Download失敗箇所を実機と実Serverで特定し、検証記録へ残す。（変更前BuildでSAF保存先作成後、Server original 10,047,953 bytesに対して3.84 KiBの不完全Fileが残ることを確認し、OutputStream open後からcopy・closeまでの区間と特定。`evidence/pr2/README.md`）
  - [x] SAF URI選択、OutputStream open、variant request、HTTP応答、copy、close、完了通知を段階的に確認する。
  - [x] Token、SAF URI、絶対Path、個人File名を診断Logへ記録しない。
- [x] Photo viewerのDownloadをOriginal固定のstreaming保存へ修正する。
  - [x] 表示中variantと保存variantを分離し、常に`MediaVariant.ORIGINAL`をRequestする。
  - [x] `CreateDocument`へ元File名と具体的なMIME typeを渡す。
  - [x] 全体ByteArray化せずOutputStreamへcopyし、close完了後だけ成功通知する。
  - [x] Cancel、open失敗、容量不足、通信切断、認証/認可失効、Server失敗を成功扱いしない。
  - [x] 途中失敗時は作成済みURIの削除を試み、削除不能時は不完全Fileの可能性を表示する。
  - [x] File browserとPDFの既存DownloadおよびUploadを維持する。

### 6. PR 2テスト・文書・検証

- [x] 写真Viewer、Quality、Organization、Downloadの自動Testを追加・更新する。
  - [x] Settings別初期variantと確認Dialog非表示をUnit/Compose Testする。
  - [x] Low/Medium/Original requestと古いgeneration破棄をUnit Testする。
  - [x] Cache keyのsession/file/version/variant分離を確認する。
  - [x] 非重畳previous/next、Swipe、Zoom/Pan競合、Toolbar semanticsをCompose Testする。
  - [x] Favorite/Tagの成功、pending、失敗、結果不明、File切替をTestする。
  - [x] Original固定、streaming、Cancel、open/copy/delete失敗をTestする。
  - [x] File browser/PDF DownloadとUploadの回帰Testを実行する。
- [x] MockWebServerでMedia契約を検証する。
  - [x] Low、Medium、Originalが正しいvariant requestになる。
  - [x] Response Content-Typeと生成中/Ready/Error処理が正しい。
  - [x] Request切替後に古い応答が表示Stateを上書きしない。
- [x] PR 2に対応する正式文書を更新する。
  - [x] `docs/product-requirements.md`の写真確認Dialog廃止、Viewer操作、Quality、Original download条件を更新する。
  - [x] `docs/functional-design.md`のViewer Layout、Swipe、Quality state、Favorite/Tags、SAF失敗処理を更新する。
  - [x] `docs/architecture-design.md`のgeneration、Cache/session、app調停を更新する。
  - [x] 必要な場合だけ`docs/repository-structure.md`と`docs/development-guidelines.md`を更新する。（構成・開発規約の変更がないため更新不要）
- [x] PR 2の自動検証を完了する。
  - [x] 変更対象のUnit/Compose/MockWebServer Testが成功する。
  - [x] `./scripts/ci/verify-android.sh`が成功する。
  - [x] Serverコードを変更した場合だけ対象`dotnet test`と`./scripts/ci/verify-server.sh`が成功する。（Serverコード変更なし）
  - [x] `git diff --check`が成功する。
- [x] PR 2を実機と実Serverで検証する。（`evidence/pr2/README.md`）
  - [x] Viewer表示領域、Swipe、非重畳Navigation、Zoom/Pan/Double tap、前後移動を確認する。
  - [x] Viewer内FavoriteとTagの追加/解除、pending、Errorを確認する。
  - [x] Low/Medium/Originalのrequest variant、HTTP status、Content-Type、decoded寸法、byte sizeを比較する。
  - [x] Original download後のFileが開け、sizeまたはSHA-256がServer originalと一致する。
  - [x] Cancel、通信失敗、書込失敗で成功表示や不完全Fileの放置がないことを可能な範囲で確認する。（Cancelは実機、通信・書込・削除失敗は自動Testで確認）
  - [x] 360dp、font 100%/200%、Portrait/Landscape、Light/Dark、TalkBackを確認する。（文字200%等はAPI 33実機上のCompose fixtureとSemanticsを併用）
  - [x] 変更前後のPhoto viewer Screenshotを同条件で保存し比較する。（公開基準と個人情報を含まない比較結果を`evidence/pr2/README.md`へ記録）

### 7. PR 2完了処理

- [x] PR 2の差分をセルフレビューする。
  - [x] PR 2の目的外変更、秘密情報、絶対Path、デバッグコード、不要依存がない。
  - [x] 実装、Test、実機記録、正式文書、Steeringの対応が取れている。
- [x] PR 2をCommit、Pushし、英語のPull Requestを作成する。
  - [x] 英語本文へ目的、対象Task、変更、Test結果、実機結果、影響、未実施事項を記載する。
  - [x] CI成功を確認し、Pull RequestはMergeしない。
- [x] `各Pull Request完了記録`へPR 2の実施結果を記載する。
  - [x] 完了日、PR番号/URL、Test、実機検証、計画差分、追加Task、取消Task、PR 3への引継ぎを記載する。
  - [x] 記録をCommit・Pushし、Pull Requestへ反映されたことを確認する。

---

## PR 3: Visual favorites and app-wide polish

### 1. 実装前確認

- [ ] PR 3の実装前状態を確認する。
  - [ ] PR 2が`main`へMerge済みであることを確認する。
  - [ ] `tasklist.md`を読み、PR 3だけを今回の実装範囲として選択する。
  - [ ] 最新の`main`からPR 3用Branchを作成し、`git status`と既存差分を確認する。
  - [ ] `docs/`のFavorites、Tags、画面Routing、Thumbnail、Accessibilityに直接関係する節を再確認する。
  - [ ] `OrganizationScreens`、`MediaNavigationContextStore`、既存media/text/folder routes、Thumbnail実装、関連Testを確認する。

### 2. Favoritesの視覚化

- [ ] Favorites一覧へMedia Thumbnailを追加する。
  - [ ] 写真・動画・PDFへ既存派生Thumbnailを表示する。
  - [ ] Folder、Audio、Text、非対応Fileへ分かりやすい種別Iconを表示する。
  - [ ] Thumbnail失敗を種別Iconへfallbackし、一覧全体をErrorにしない。
  - [ ] 名前、最小限Metadata、Thumbnail、overflowを狭い画面で重ねない。

### 3. Favorites直接RoutingとContext

- [ ] appへ再利用可能なEntry destination resolverを実装する。
  - [ ] Folder、Photo、Video、Audio、PDF、Textを適切な既存Destinationへ分類する。
  - [ ] Unsupported、Missing、操作不能項目だけEntry detailsへfallbackする。
  - [ ] Files/Shared/Favoritesの重複Routingをresolverへ統合し、既存遷移を維持する。
- [ ] FavoritesのMedia navigation contextを接続する。
  - [ ] Favorites結果からActiveな同種Mediaを表示順のまま登録する。
  - [ ] 写真tapでFile detailsを経由せずPhoto viewerを開く。
  - [ ] Favorites内のprevious/nextで一覧順に連続閲覧できる。
  - [ ] Logout、Server変更、認証失効でContextをclearする。
  - [ ] Process death等でContextがない場合も単体Viewerへ安全にfallbackする。

### 4. アプリ全体のUI仕上げ

- [ ] 今回対象の主要画面で視覚階層と用語を横断確認する。
  - [ ] Home、Files、Shared、Search、Recent、Favorites、Tags、Activity、Trash、Settings、BackupへのNavigationを確認する。
  - [ ] 対象Production UIに`Family shared`、抽象的な`Actions`、生の内部Enum、写真用`Load original photo?`が残っていない。
  - [ ] Back、Refresh、overflowの位置、Icon、tap target、Content descriptionが一貫している。
  - [ ] 冗長なCard、枠線、説明、余白が主要Contentを不必要に圧迫していない。
  - [ ] 既存Themeの色、Typography、Spacing、ShapeでLight/Dark双方のcontrastを維持する。

### 5. PR 3テスト・文書・総合検証

- [ ] FavoritesとRoutingのUnit/Compose Testを追加・更新する。
  - [ ] 種別ごとのdestination分類とfallbackをUnit Testする。
  - [ ] Favorites順のPhoto/Video/Audio contextをUnit Testする。
  - [ ] Thumbnail表示/fallback、Entry tap、overflowをCompose Testする。
  - [ ] Favoritesの写真tapが直接Photo viewerへ遷移するNavigation Testを追加する。
  - [ ] Context消失時、Logout/Server切替時の安全な挙動をTestする。
- [ ] PR 3に対応する正式文書を更新する。
  - [ ] `docs/product-requirements.md`のFavorites Thumbnailと直接Viewer条件を更新する。
  - [ ] `docs/functional-design.md`のEntry resolverとFavorites media contextを更新する。
  - [ ] `docs/architecture-design.md`のapp調停とContext lifecycleを更新する。
  - [ ] 必要な場合だけ`docs/repository-structure.md`と`docs/development-guidelines.md`を更新する。
- [ ] 全Android自動検証を完了する。
  - [ ] 変更対象のUnit/Compose/Navigation Testが成功する。
  - [ ] `./scripts/ci/verify-android.sh`が成功する。
  - [ ] `git diff --check`が成功する。
- [ ] 実機・実ServerでEnd-to-endフローを確認する。
  - [ ] Files、Shared、FavoritesでThumbnailを確認し、各種Fileを適切なDestinationへ開く。
  - [ ] Favorites写真を直接開き、一覧順の前後移動、Favorite解除、Tag変更を確認する。
  - [ ] Low/Medium/OriginalとOriginal downloadを再確認する。
  - [ ] File操作、共有、Upload、Download、Trash、Restore、Missing/Recoveryへ回帰がない。
- [ ] Responsive・Accessibility・視覚比較を完了する。
  - [ ] 360dp、font 100%/200%、Portrait/Landscape、Light/Darkで主要操作の重なり・切れ・到達不能がない。
  - [ ] TalkBackの順序、状態読み上げ、全IconのContent description、48dp tap targetを確認する。
  - [ ] System bar、cutout、IMEとTop/Bottom navigation、Viewer、Bottom sheetが重ならない。
  - [ ] 変更前後Screenshotを同じ実データで比較し、一覧表示量、Viewer表示領域、Button崩れの改善を記録する。
  - [ ] 認証、認可、共有、Session/Cache分離に重大な回帰がない。

### 6. PR 3完了処理

- [ ] PR 3の差分をセルフレビューする。
  - [ ] PR 3の目的外変更、秘密情報、絶対Path、デバッグコード、不要依存がない。
  - [ ] 実装、Test、実機記録、正式文書、Steeringの対応が取れている。
- [ ] PR 3をCommit、Pushし、英語のPull Requestを作成する。
  - [ ] 英語本文へ目的、対象Task、変更、Test結果、実機結果、影響、未実施事項を記載する。
  - [ ] CI成功を確認し、Pull RequestはMergeしない。
- [ ] `各Pull Request完了記録`へPR 3の実施結果を記載する。
  - [ ] 完了日、PR番号/URL、Test、実機検証、計画差分、追加Task、取消Task、全体完了への引継ぎを記載する。
  - [ ] 記録をCommit・Pushし、Pull Requestへ反映されたことを確認する。

---

## 各Pull Request完了記録

### PR 1

- 完了日: 2026-09-05
- Pull Request: [#60 Simplify Android file browsing navigation and details](https://github.com/ry825/Kura_Storage/pull/60)
- 実施したTest・Build・静的解析・手動確認:
  - `:feature-files:testDebugUnitTest`: 26件成功。
  - `:feature-files:connectedDebugAndroidTest`: API 33 Emulatorで25件成功。Back、Refresh、Search、List/Grid、Entry操作、権限別details、Upload、Rename、Move、Trash、Restore、Missingを確認。
  - `./scripts/ci/verify-android.sh`: JDK 17・API 36 SDKで1,387タスク成功。Build、Unit Test、ktlint、detekt、Lintを含む。
  - `git diff --check`: 成功。
  - GitHub Actions: Android、Server、Config、Securityの4jobが成功。
  - 360dp、font 100%/200%、Portrait/Landscape、Light/DarkのCompose fixtureとSemantics treeを確認し、`evidence/pr1/`へCaptureと比較結果を保存。
- 計画と実装の差分: PR 1の機能スコープに差分なし。実ServerでのMedia variant・Original download検証は計画どおりPR 2で行う。
- 実装中に追加したTaskと理由: 表示ルールをComposeから分離した`FileBrowserPresentation`とUnit Testを追加し、個人/Shared metadataと権限Labelを安定検証可能にした。また再現可能なUI Capture用Compose testと証跡記録を追加した。
- 技術的に不要になったTask、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ: PR #60の`main`へのMergeを確認するまでPR 2を開始しない。PR 2でPhoto viewerのadaptive layout、Quality自動適用、Favorite/Tags、Original downloadと実機・実Server検証を行う。

### PR 2

- 完了日: 2026-09-05
- Pull Request: [#61 Improve Android photo viewer and media actions](https://github.com/ry825/Kura_Storage/pull/61)
- 実施したTest・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`: JDK 17・API 36 SDKで1,387タスク成功。Build、Unit Test、ktlint、detekt、Lintを含む。
  - API 33実機のConnected Compose Test: `feature-media` 18件、`feature-search` 11件成功。API 33 Emulatorでは`core-data` 14件、`feature-files` 25件を含む関連回帰Testも成功。
  - `git diff --check`: 成功。
  - GitHub Actions: Android、Server、Config、Securityの4jobが成功。
  - OPPO CPH2333 / Android API 33 / 360dpで、実ServerへZeroTier接続し、Viewer、Swipe、Zoom/Pan/Double tap、Favorite、Tags、Quality、Original download、SAF Cancelを確認。
  - LowはHTTP 200・WebP・1280 x 853・137,516 bytes、MediumはHTTP 200・WebP・2560 x 1707・237,762 bytes、OriginalはHTTP 200・JPEG・5472 x 3648・10,047,953 bytesであることを確認。
  - 端末保存FileとServer originalが10,047,953 bytesおよびSHA-256 `835be98e71267845b7a4f66469fcf96e3e0888899972ecc48d72f98b50469f14`で一致。詳細は`evidence/pr2/README.md`へ記録。
- 計画と実装の差分: 機能スコープに差分なし。実データScreenshotは個人画像とFile名を含むためPRへ添付せず、公開モックを変更前基準として目視比較結果を記録した。実機メーカー制限でADB shellからfont scaleを変更できなかったため、文字200%は同じAPI 33実機上のCompose fontScale fixtureとSemanticsで確認した。
- 実装中に追加したTaskと理由: 実機Zoom/Pan検証で拡大画像がViewer外へ描画される問題を検出したため、Photo canvasを独立したclip layerで囲み、Top app barと操作領域への描画はみ出しを防止した。変更前Downloadの3.84 KiB不完全Fileと修正後の段階別検証を再現可能な証跡として追加した。
- 技術的に不要になったTask、理由、代替実装: なし。
- 後続Pull Requestへの引継ぎ: PR #61の`main`へのMergeを確認するまでPR 3を開始しない。PR 3ではFavoritesのThumbnail、直接Routing、Media navigation context、主要画面の横断UI・Accessibility仕上げを行う。

### PR 3

- 完了日: 未完了
- Pull Request: 未作成
- 実施したTest・Build・静的解析・手動確認: 未実施
- 計画と実装の差分: 未記録
- 実装中に追加したTaskと理由: 未記録
- 技術的に不要になったTask、理由、代替実装: 未記録
- 後続Pull Requestへの引継ぎ: 未記録

---

## 全体振り返り

> PR 1〜PR 3の全タスク完了と各PR完了記録を確認した後だけ記載する。

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
