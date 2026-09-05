# Android UI簡素化・メディア操作改善 要求内容

## 概要

Androidアプリ全体の情報量と操作配置を整理し、既存機能・認可・データを維持したまま、内容を主役にしたシンプルで一貫性のあるUIへ改善する。特にFiles、Shared、Favorites、Tags、写真ビューアー、File detailsを優先し、写真一覧・閲覧・整理・保存までを迷わず操作できる状態にする。

併せて、実機で確認された写真Download失敗と、画質を変更しても表示結果が変わらない問題を既存契約内の不具合として診断・修正する。新しいServer機能やDatabase schemaは追加せず、既存APIと派生画像契約を正しく利用する。

## 背景

現行UIは、Back、Refresh、Search、List/Grid、Upload、属性、`Actions`などの操作と説明が同時に大きな領域を占め、肝心のフォルダー、ファイル、写真サムネイルを一度に確認できる範囲が狭い。操作位置や文言にも一貫性がなく、右側の属性や詳細Dialog内のボタンは狭い画面で読みづらく、押しづらい。

写真ビューアーでは前後移動ボタンが写真へ重なり、固定高さの表示領域より操作カードの存在感が大きい。Settingsに画質設定があるにもかかわらず、Original設定時に写真を開くたび確認Dialogが表示される。また、Low・Medium・Originalの選択と実際に表示された画像の対応が利用者に伝わらず、実機では画質差を確認できていない。

Favoritesは写真サムネイルを表示せず、項目選択後も写真ビューアーではなく汎用File detailsへ遷移する。FavoriteとTagの操作も写真閲覧中に見つけにくい。これらにより、保存した写真を視覚的に探して連続閲覧・整理する主要フローが分断されている。

## 実装対象の機能

### 1. アプリ全体の視覚階層と共通操作

- 既存の色、Typography、Spacing、Shape、共通Componentを再整理し、カード・見出し・説明・枠線の重複を減らす。
- 画面の主要内容、主要操作、補助情報、例外状態の優先順位を明確にし、通常状態では補助情報や管理情報を常時大きく表示しない。
- BackはTop app bar左端、Refreshや画面固有の補助操作は右端またはoverflowへ統一し、文字Buttonの横並びによる圧迫を解消する。
- アイコン操作には48dp以上の操作領域と理解可能なContent descriptionを持たせる。
- `Family shared`を`Shared`へ変更し、Home、Bottom navigation、画面Title、説明、Testで同じ用語を使う。
- `Actions`や生のEnum名など、意味を推測させる表記を避け、overflow iconと具体的な動詞を使用する。

### 2. Files・Shared一覧の内容中心レイアウト

- Top app bar、現在位置、表示切替、検索、Uploadをコンパクトに再配置し、一覧が画面の大部分を使用できるようにする。
- 写真・動画・PDFはサムネイルを主情報として確認できるAdaptive gridを提供し、狭い画面でも複数項目を一度に見渡せるようにする。
- List/Grid切替状態を画面再生成で不必要に失わず、現在のFolder操作とPaginationを維持する。
- FolderとFileの名前に`Folder:`・`File:`を重ねず、種別、更新日時、Size、Owner、共有、Permission、欠損状態を重要度順に短く表示する。
- 通常の個人領域ではOwnerやPermissionなど自明な属性を繰り返さず、Sharedまたは制限がある場合だけ必要な情報を表示する。
- 行右側の属性・Buttonで名前やサムネイルを圧迫せず、補助操作は一貫したoverflowから開く。
- Folder tapはFolderを開き、対応メディア・Text tapは直接Viewer/Editorを開くという既存の主操作を維持する。

### 3. File details・ファイル操作の再構成

- `File details`と操作群を、360dp幅や文字200%でもButton・Labelが欠けないBottom sheetまたはAdaptiveな詳細面へ再構成する。
- `Actions`という抽象見出しへ依存せず、`Open`、`Download original`、`Rename`、`Move`、`Share`、`Add to favorites`、`Manage tags`、`Move to Trash`など具体的な操作名を使用する。
- Metadataは名前、種類、Size、更新日時、Owner、共有元、Permission、状態を読みやすいLabel/Valueとして整理し、生の内部値をそのまま表示しない。
- 危険操作は通常操作と視覚的・意味的に分離し、既存の確認、認可、通信結果不明、Recovery/Missing処理を維持する。
- 実行できない操作で画面を埋めず、非表示または理由が必要な場合だけ無効状態と説明を示す。

### 4. 写真ビューアーのコンテンツ優先化

- 写真表示領域を固定360dpから利用可能領域に追従する構成へ変更し、縦・横画面とも写真が主役になるようにする。
- `Previous photo`・`Next photo`のButtonを写真へ重ねず、左右Swipe、画面外のNavigation control、または両方で前後移動できるようにする。
- Back、現在位置、Favorite、Tag、その他操作を一貫したViewer chromeへ配置し、写真を恒常的に覆う大きなButtonやCardを置かない。
- Zoom、Pan、Double tap、前後Context、生成中・失敗・再試行の既存挙動を維持する。
- 画質はSettingsの接続種別別設定を初期値として自動適用し、写真を開くたび`Load original photo?`を表示しない。
- Viewer内でLow・Medium・Originalを変更できる場合は、選択後に対応するvariantを再取得して表示し、現在適用中の画質を簡潔に確認できるようにする。
- Downloadは既存の正式契約に合わせて元ファイルをSAF選択先へ保存し、Viewerの表示画質と保存対象を混同させない。

### 5. Favorite・TagとViewerの接続

- 写真ビューアーから、現在の写真をFavoriteへ追加・解除できる明確な操作を提供する。
- 写真ビューアーから、現在の写真へTagを付与・解除できる明確な操作を提供する。
- Favorite/Tag変更中、成功、失敗、通信結果不明を既存Organization APIの状態に従って表示し、ローカルだけで成功を合成しない。
- Favorites一覧で写真・動画・PDFのサムネイルを表示し、ファイル名だけに依存せず内容を判別できるようにする。
- Favoritesの写真をtapすると写真Viewerへ、動画・音声・PDF・Textは対応Viewer/Editorへ、FolderはFolder browserへ直接遷移する。
- Favoritesから開いたメディアでもFavorites一覧順のNavigation contextを構成し、対応する前後項目を連続閲覧できるようにする。
- 非対応Fileまたは操作不能状態だけ、適切な詳細・説明画面へ遷移する。

### 6. 写真Download不具合の修正

- 実Android端末と実Serverで現在のDownload失敗を再現し、SAF出力、Request variant、HTTP応答、Stream copy、完了判定のどこで失敗するかを特定する。
- 写真Viewerの`Download original`で、元ファイルを全体ByteArray化せずSAF URIへStreaming保存できるようにする。
- 成功時は保存完了を通知し、失敗時は作成途中の不完全Fileを可能な範囲で削除して、再試行可能な具体的表示を行う。
- Cancel、権限拒否、容量不足、通信切断、認証・共有権限失効を成功扱いにしない。
- File browser、PDF、Text copy等の既存SAF操作へ回帰を起こさない。

### 7. 画質切替不具合の修正

- Low・Medium・Original選択時に、Androidがそれぞれ正しいMedia variantをRequestしていることを自動Testで確認する。
- File ID、File version、variant、Session scopeを含むCache keyを維持し、別画質のCache結果を誤って再利用しない。
- 画質変更前の非同期応答や生成Jobが、変更後の表示を上書きしない。
- 実ServerでLow・Medium・OriginalのResponse、Content type、寸法またはByte sizeを比較し、生成可能な写真では選択したvariantが実際に切り替わることを確認する。
- 端末画面上で差が小さい場合も、現在の選択と実際のRequest variantが一致していることを検証可能にし、偽の画質差をUI加工で作らない。

### 8. Responsive・Accessibility・視覚検証

- 360dp幅、通常文字、200%文字、Landscape、Dark themeで、主要内容と操作が欠けたり重なったりしない。
- Viewerの写真、Files/Favoritesのサムネイル、Top app bar、Bottom navigationがSystem barやDisplay cutoutと重ならない。
- TalkBack順序を内容優先にし、Back、Refresh、Favorite、Tag、overflow、表示切替、前後移動の意味と状態を読み上げる。
- 色だけでFavorite、選択画質、共有、欠損、Error状態を表現しない。
- 変更前後の実データを使ったScreenshotまたは画面Captureを記録し、情報密度、表示領域、Button崩れ、サムネイル表示を比較できるようにする。

## 受け入れ条件

### 全体UI・用語

- [ ] 認証後の主要画面でBackとRefreshの位置・Icon・操作領域が統一され、360dp幅と文字200%でも押下できる。
- [ ] Production UIとTestから`Family shared`表記がなくなり、利用者向け名称が`Shared`へ統一されている。
- [ ] 通常状態で複数段の操作Buttonや冗長なCardが主要内容を圧迫せず、補助操作はTop app barまたはoverflowから到達できる。
- [ ] 既存のHome、Files、Shared、Search、Recent、Favorites、Tags、Activity、Trash、Settings、BackupへのNavigationを失わない。

### Files・詳細

- [ ] FilesとSharedの写真一覧で実サムネイルが表示され、名前・最小限Metadata・overflowが重ならない。
- [ ] 360dp幅の初期表示で、Top領域のために一覧が操作不能または極端に狭くならない。
- [ ] Folder/File属性は意味のある表記で重要度順に表示され、右側の長い文字列や`Actions` Buttonが内容を圧迫しない。
- [ ] 詳細・操作面は文字200%でもButtonが切れず、主要操作、補助操作、危険操作を区別できる。
- [ ] 既存のUpload、Folder作成、Download、Rename、Move、Share、Favorite/Tag、Trash、Restore、Missing処理を権限どおり実行できる。

### 写真Viewer・画質

- [ ] 写真の上に`Previous photo`・`Next photo`の大きなButtonが重ならない。
- [ ] Swipeまたは明確な非重畳操作で前後の写真へ移動でき、現在位置を確認できる。
- [ ] Viewerを開くたび`Load original photo?` Dialogが表示されず、Settingsの現在接続向け画質が自動適用される。
- [ ] Low・Medium・Originalの変更が正しいvariant Requestと表示へ反映され、古い画質の応答が新しい選択を上書きしない。
- [ ] 写真の表示領域が利用可能な画面領域へAdaptiveに広がり、操作Cardより写真が視覚的に優先される。
- [ ] Zoom、Pan、Double tap、生成待ち、Error、Retry、前後Prefetchの既存機能が維持される。

### Favorites・Tags

- [ ] 写真Viewer上でFavorite状態を確認して追加・解除でき、Tagを確認して付与・解除できる。
- [ ] Favorites一覧の写真・動画・PDFにサムネイルが表示される。
- [ ] Favoritesの写真tapでFile detailsを経由せず写真Viewerが開く。
- [ ] FavoritesのFolder、動画、音声、PDF、Text、非対応Fileがそれぞれ適切な既存Destinationへ遷移する。
- [ ] Favoritesから写真を開いた場合も、対応する一覧Context内で前後移動できる。

### Download

- [ ] 実機の写真Viewerから`Download original`を実行し、選択したSAF保存先に元ファイルと同じ内容をStreaming保存できる。
- [ ] 保存されたFileが対応アプリで開け、SizeまたはSHA-256がServerの元ファイルと一致する。
- [ ] 失敗・Cancel・容量不足・通信切断を完了表示せず、不完全な保存先を成功Fileとして残さない。
- [ ] File browserとPDFの既存Download、およびUploadへ回帰がない。

### 品質・検証

- [ ] 変更対象のViewModel、Navigation、Compose UI、Media Request、SAF Downloadに自動Testがある。
- [ ] `./scripts/ci/verify-android.sh`、関連Server Test、`git diff --check`が成功する。
- [ ] Android 13実機と実ServerでFiles、Shared、Favorites、写真Viewer、Favorite/Tag、3画質、Downloadを確認する。
- [ ] 360dp幅、文字200%、Landscape、Dark theme、TalkBackで重大な重なり・切れ・到達不能がない。
- [ ] 変更前後の画面と、意図した情報階層・残した例外表示を検証記録へ残す。

## 成功指標

- 写真を開いてから閲覧開始までに、画質確認Dialogを操作する回数が0回になる。
- 写真Viewer内で前後移動、Favorite、Tag、元ファイルDownloadへそれぞれ直接到達できる。
- Favorites内の対応メディアにサムネイルが表示され、写真tapから1回でViewerへ遷移する。
- 360dp幅・文字200%で、主要Buttonの文字切れ、Button同士の重なり、写真へのNavigation Button重畳が0件になる。
- 実機でLow・Medium・OriginalのRequest variant一致率が100%となり、写真Download成功率が正常な接続・権限・容量条件で100%となる。
- 既存の認証、認可、共有、File操作、Media generation、Session分離に重大な回帰が0件となる。

## スコープ外

以下はこの作業では実装しない。

- 新しいServer公開API、Database table、Migration、ファイル保存方式の追加。
- 既存のRole、共有Permission、File認可、Device・Session、Local Direct・ZeroTier境界の変更。
- Web、iOS、DesktopクライアントのUI。
- 顔認識、Album自動分類、Timeline、Map等の新しい写真管理機能。
- 画像へ人工的なBlurや劣化を加えてLow qualityに見せる処理。
- 既存Server派生生成自体に不具合があると判明した場合の大規模なMedia pipeline再設計。既存契約内の局所修正が不可能なら、証拠と影響を記録して別途承認を得る。
- すべての文言の日本語化。今回触れる利用者向け英語表記は短く一貫させるが、Localization基盤追加は行わない。

## 参照ドキュメント

- `docs/product-requirements.md` - Android UI・File・Download・共有・Favorites/Tags・Media受け入れ条件
- `docs/functional-design.md` - Android画面遷移、Files/Viewer/Quality/Favorites/Tags、SAF Download設計
- `docs/architecture-design.md` - Android Module境界、Media派生、Range Download、Cache・Session境界
- `docs/repository-structure.md` - `app`、`core-ui`、`feature-files`、`feature-media`、`feature-search`の配置
- `docs/development-guidelines.md` - Compose、Accessibility、SAF Streaming、Test、Pull Request規約
- `.steering/20260903-android-ui-mockup-alignment/` - 現行Design system・Navigation・画面実装の履歴
- `.steering/20260829-android-media-viewers-players/` - 画質、写真Viewer、Media Request・Downloadの履歴
- `.steering/20260828-favorites-tags/` - Favorites、Tags、Organization API・Navigationの履歴
- `.steering/20260823-file-folder-sharing-permissions/` - Shared一覧・権限表示の履歴
