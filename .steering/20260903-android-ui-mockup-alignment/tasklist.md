# Android UIモックアップ整合 タスクリスト

## 対象要件

- `docs/product-requirements.md` 8章「UI・UX要件」および各Android機能の受け入れ条件。
- `docs/functional-design.md` 10章「画面遷移図」、11章「UI設計」。
- `docs/ui/android/mockups/`の`001`〜`036`。
- 正式画面に必要な実装が欠ける場合は、モックアップではなく`docs/`を正とし、必要最小限のServer、API、永続状態、Android実装を同じ作業で完成させる。

## タスク完全完了の原則

- 全タスクを最終的に`[x]`にし、親タスクはすべての子タスク完了後だけ完了にする。
- 1回の実装で1つのPull Request単位を選び、実装、テスト、表示検証、文書、Commit、Push、英語Pull Request、必須CI、steeringモード3-Aの完了記録まで完了して停止する。
- 各Pull Requestは先行Pull Requestが`main`へMergeされ、必須CIが成功した後に、最新`main`から短命Branchを作成して開始する。
- 実装と対応するCompose test、手動表示確認、参考UIとの意図的差分の記録を同じPull Requestに含める。
- 画像を背景に貼り付けて完了とせず、Composeの実コンポーネント、Semantics、Responsive layoutとして実装する。
- 「時間の都合」「難しい」「別タスク」を理由にスキップしない。技術的に不要となった場合だけ、理由と代替実装をタスクとPull Request完了記録へ明記する。

## 確定済みスコープ境界

- [x] Android 10（API 29）以上のJetpack Composeアプリを対象とする。
- [x] 機能、表示項目、状態、権限、文言は正式文書を優先し、配色、余白、形状、情報階層は参考UIを基準とする。
- [x] `005-vpn-connection.png`と`035-quality-network-settings.png`のLegacyなVPN表記はZeroTier表記に置き換え、KuraStorage内から接続・切断・Member認可を行わない。
- [x] 画面下部の木々は使用せず、過剰な和風装飾を追加しない。
- [x] 参考UI内のサンプル写真、File名、件数、日時、User名をProductionの固定データにしない。
- [x] 正式画面に不足する実装は`docs/`を根拠に追加し、UIと無関係な機能拡張を混在させない。
- [x] キャッシュ管理は正式仕様の管理者用状態取得と「今すぐ清掃」を実装し、モックアップにだけある「失敗項目を一括再試行」は追加しない。
- [x] Web UI、オフライン閲覧、Tablet専用構成、ピクセル単位の完全一致は対象外とする。

---

## フェーズ0: 計画承認

- [x] `requirements.md`を作成し、User承認を得る。
- [x] `design.md`を作成し、User承認を得る。
- [x] 欠落実装は正式文書を参照して実装範囲へ含める方針のUser確認を得る。
- [x] 本`tasklist.md`を承認済み`requirements.md`と`design.md`に照らしてUserが確認し、Pull Request境界と実装順序を承認する。

---

## PR1: UI監査・Design system基盤

### 1.1 開始条件・現行UI監査

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0が完了している。
  - [x] `git status`と既存差分を確認し、最新`main`から短命Branchを作成する。
  - [x] `requirements.md`、`design.md`、正式文書のUI節、既存`core-ui`、各Screen、Compose testを再確認する。
- [x] `001`〜`036`のMockup追跡表を`docs/testing/`PR1記録に作成する。
  - [x] 番号、Path、正式画面・節、Production Composable、UiState/ViewModel、Navigation、Testの有無を記録する。
  - [x] Loading、Empty、Content、Error、Processing、Permission、Offlineの必要状態と現行の欠落を記録する。
  - [x] ZeroTier表記、木々装飾除外、実データ表示、Responsive再配置等の意図的差分を記録する。
  - [x] 欠落Destination、不足API、未使用の正式状態を追跡し、対応PRを割り当てる。

### 1.2 Theme・Design token

- [x] `core-ui`のKuraStorage Themeを実装する。
  - [x] 深い藍のBrand色、温かみのある背景、Surface、Outline、Divider、Primary/Secondary textをLight/Dark color schemeに定義する。
  - [x] Success、Warning、Error、Info、Categoryの意味tokenを定義し、状態表示を色だけに依存させない。
  - [x] Typography、Shape、4dp基準Spacing、最小Elevationを定義し、任意な数値の重複を避ける。
  - [x] Dynamic colorを既定で無効にし、System Dark theme切替で可読性を保つ。
- [x] Logo、Icon、File type表示の基盤を実装する。
  - [x] ロゴAssetの出典・利用可否を確認し、Vector/Compose描画または利用可能Assetを採用する。
  - [x] 装飾LogoをSemantics treeから除外し、意味が必要な場合はテキストを併記する。
  - [x] MIME/Entry typeから、Folder、Photo、Video、Audio、PDF、Text、Document、Unknownの共通Icon variantへ変換する。

### 1.3 共通Component・状態表示

- [x] 共通の画面枠と表示Componentを`core-ui`に実装する。
  - [x] `KuraAppScaffold`、`KuraTopAppBar`、`KuraScreenContent`、`KuraSectionHeader`にWindow inset、最大幅、見出しSemanticsを実装する。
  - [x] `KuraCard`、`KuraListRow`、`KuraStatusBadge/Panel`の通常、選択、警告、無効variantを実装する。
  - [x] Primary、Secondary、Destructive button、Icon button、Text/Password/Selection field、Segmented controlの最小基盤を実装する。
  - [x] Loading、Empty、Recoverable error、Blocking error、Progressの共通状態を、理由・次操作・request IDを受け取れる形で実装する。
  - [x] 対象、影響範囲、取消し、実行を持つ通常/危険操作の共通Confirmation dialogを実装する。
- [x] 共通Componentのアクセシビリティ基盤を実装する。
  - [x] 操作可能領域を48dp相当以上とし、Icon-only buttonにContent descriptionを必須化する。
  - [x] Heading、Selected、Progress、Error、Live regionのSemantics helperを必要最小限実装する。
  - [x] 200%文字拡大時に水平Button/Segmentが縦配置へ変形できる共通layoutを実装する。

### 1.4 PR1テスト・検証・完了

- [x] `core-ui` Compose instrumented testを追加する。
  - [x] Light/Dark、通常/警告/失敗、有効/無効、Loading/Error/Progressを検証する。
  - [x] 360dp幅、200%文字、48dp操作領域、Heading、Content description、Selected/Progress semanticsを検証する。
  - [x] 決定的Fixtureで`captureToImage()`でき、ProductionへScreenshot用処理を含めない。
- [x] PR1の自動・手動検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`と`git diff --check`が成功する。
  - [x] Android 13実機またはAPI 33 Emulatorで`:core-ui:connectedDebugAndroidTest`が成功する。
  - [x] ロゴ・Iconの権利、APK/SBOM、不要依存、大きなBitmap非混入を確認する。
  - [x] `docs/testing/YYYYMMDD-android-ui-pr1-foundation.md`に対応表、テスト、キャプチャ、意図的差分を記録する。
- [x] PR1を完了する。
  - [x] 差分をself-reviewし、無関係な変更、debug code、秘密情報、実環境値がない。
  - [x] Commit、Push、英語Pull Request、必須CI、steeringモード3-AのPR1完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR2: App shell・Home・グローバルNavigation

### 2.1 開始条件・App構成

- [x] PR2の開始条件を満たす。
  - [x] PR1が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、`MainActivity.kt`、`AppDestination`、`HomeScreenTest`とNavigation testを確認する。
  - [x] Userの明示指示により、PR1 Merge前はPR1先端から積み上げた一時BranchでPR2実装を先行し、Merge連絡後に最新`main`へ載せ替える。
- [x] App shellを構築する。
  - [x] `KuraAppScaffold`にTop bar、Bottom navigation、Snackbar、FAB、Window insetを統合する。
  - [x] 認証後の主要Destinationをホーム、ファイル、共有、検索、設定の5項目に固定する。
  - [x] 認証前、フルスクリーンViewer、DialogでBottom navigationを表示しない。
  - [x] `launchSingleTop`、`popUpTo`、state restorationを用い、同一TabのBack stack重複を防ぐ。
  - [x] Session/Route/User変更時に保護画面とUI stateを破棄し、別Contextへ再利用しない。
  - [x] `MainActivity.kt`の巨大Composableを、挙動変更に必要なApp shell、Home、Navigation helperのみへ分割する。

### 2.2 Home（`009-home.png`）

- [x] Homeを参考UIと正式仕様に整合する。
  - [x] Logo、接続状態、自動バックアップ状態の要約カードを表示する。
  - [x] 自分のファイル、家族共有、最近開いたファイルの主要導線をカード内に表示する。
  - [x] 写真、動画、音声、文書のCategory導線を表示し、サンプル件数を固定しない。
  - [x] 最近のFileをThumbnail/File type Icon、名前、更新日時、Sizeとともに有界表示する。
  - [x] Favorites、Tags、Activity、Trash、Media settings、Backup settings、Logoutの現行導線をメニューまたは適切なSectionに保つ。
  - [x] AdminにだけStorage/Trash/Cache警告導線を表示し、Memberに管理状態を公開しない。
- [x] HomeのAdaptive layoutを実装する。
  - [x] 360dp幅でカードが1列、十分な幅で状態/Categoryが有界に2列化する。
  - [x] 200%文字と横画面で主要導線とLogoutへスクロールで到達できる。
  - [x] Loading、部分Error、Empty recent、接続変化を画面全体の汎用Errorにまとめない。

### 2.3 PR2テスト・検証・完了

- [x] App NavigationとHomeのテストを更新する。
  - [x] 5項目のBottom navigation、選択状態、再選択、Back、認証前/後の表示を検証する。
  - [x] Homeの接続、Backup、Category、Recent、Admin/Memberの主要状態とCallbackを検証する。
  - [x] 360dp、200%文字、Light/Dark、Heading、navigation semanticsを検証する。
- [x] PR2の自動・手動検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [x] Android 13実機相当のAPI 33 EmulatorでHomeの縦/横、通常/200%文字、Light/Dark、Bottom navigationを確認する（物理端末が未接続のため決定的Compose fixtureで代替し、物理端末の最終確認はPR10で実施する）。
  - [x] `docs/testing/YYYYMMDD-android-ui-pr2-app-shell-home.md`に`009`のCapture、意図的差分、Navigation検証を記録する。
- [x] PR2を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR2完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR3: 起動・接続・認証UI（`001`〜`008`）

### 3.1 開始条件・起動画面

- [x] PR3の開始条件を満たす。
  - [x] PR2が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、Connection/AuthのUiState、ViewModel、Navigation、Testを確認する。
- [x] `001-splash.png`に対応する起動UIを実装する。
  - [x] Androidの起動Theme/System SplashとCompose初期画面のLogo、背景色、App名を一貫させる。
  - [x] 不要な固定待機を追加せず、接続判定へ遅滞なく遷移する。
  - [x] 起動時のチラつき、Dark theme、API 29/API 31以上の差を確認する。

### 3.2 Connection（`002`〜`005`）

- [x] Connection画面を状態ごとに再構築する。
  - [x] `002-connection-check.png`を基準に、確認中の処理内容、Progress、待機説明を表示する。
  - [x] `003-local-connection-status.png`を基準に、LOCAL_DIRECT、基盤Network、Server到達、HDD状態と次操作を表示する。
  - [x] `004-disconnected-status.png`を基準に、ZeroTier未接続、Server到達不可、TLS/Hostname失敗、HDD利用不可を別理由で表示する。
  - [x] `005-vpn-connection.png`の情報階層を使いつつ、REMOTE_SECUREをZeroTierの別アプリ案内と「再確認」に置き換える。
  - [x] LOCAL_DIRECTとREMOTE_SECUREが同時可能な場合はLOCAL_DIRECTを表示し、SSIDでRouteを推測しない。
  - [x] Connection状態のIcon、label、state description、再確認ButtonをTalkBackで理解できる。

### 3.3 Auth・Device登録（`006`〜`008`）

- [x] Auth画面を再構築する。
  - [x] `006-login.png`を基準にLogo、タイトル、説明、User名、Password、表示切替、Loginボタンを配置する。
  - [x] IME action、Focus順、Password隠蔽、二重送信防止、入力保持、Inline errorを実装する。
  - [x] Security lock、認証期限切れ、Device失効、汎用認証失敗を過度な存在差なく表示する。
- [x] 初回Device登録画面を再構築する。
  - [x] `007-initial-setup.png`を基準に、LOCAL_DIRECTでの初回登録中、対象Device名、処理中状態を表示する。
  - [x] `008-device-registration-error.png`を基準に、REMOTE_SECUREからの登録不可、Device上限、失敗、再確認/戻るを表示する。
  - [x] Device登録がLOCAL_DIRECT専用である制約をUIで弱めず、REMOTE_SECUREで登録Callbackを有効にしない。

### 3.4 PR3テスト・検証・完了

- [x] Connection/Auth Compose testを更新する。
  - [x] `001`〜`008`の主要状態、操作、Error、ZeroTier文言、Semanticsを検証する。
  - [x] VPNの接続/切断操作がなく、REMOTE_SECUREで新規Device登録できないことを検証する。
  - [x] PasswordがSemantics・Screenshot fixture・Logへ平文混入しないことを確認する。
- [x] PR3の自動・実機相当検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`、`:feature-connection:connectedDebugAndroidTest`、`:feature-auth:connectedDebugAndroidTest`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [x] Android 13実機相当のAPI 33 EmulatorでLOCAL_DIRECT、REMOTE_SECURE、未接続、Login、初回Device登録を確認する（物理端末が未接続のため決定的Compose fixtureで代替し、物理端末の最終確認はPR10で実施する）。
  - [x] `docs/testing/YYYYMMDD-android-ui-pr3-connection-auth.md`に`001`〜`008`のCapture、状態、意図的差分を記録する。
- [x] PR3を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR3完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR4: File browser・Detail・Transfer・Trash・Missing

### 4.1 開始条件・File共通表示

- [x] PR4の開始条件を満たす。
  - [x] PR3が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、`FileBrowserScreen`、各Dialog、Transfer、Admin storage、Thumbnail slot、Testを確認する。
- [x] File/Folderの共通表示を実装する。
  - [x] Thumbnail/File type Icon、名前、更新日時、Size/項目数、共有、権限、MISSING、overflowの表示を共通化する（項目数は現行API契約に存在しないため、Folderでは取得不能であることを明示する）。
  - [x] List/Gridで情報量を切り替え、Thumbnailがない場合もレイアウトを安定させる。
  - [x] 1,000件でLazy描画、安定key、Thumbnailの元File非取得、Scroll中の不要な再composeを確認する。

### 4.2 File一覧・詳細（`010`、`021`〜`023`）

- [x] `010-my-files.png`を基準にFile browserを再構築する。
  - [x] Top bar、現在位置/Breadcrumb、Folder section、File section、Search、overflow、Upload FABを配置する。
  - [x] Folder作成、Upload、Open、Detail、Rename、Move、Share、Organization、Trashを権限に応じて表示する。
  - [x] Root/下位Folder、Paging、Refresh、Loading、Empty、Error、Recovery requiredを明確に表示する。
- [x] `021-unsupported-file.png`を基準に非対応File表示を実装する。
  - [x] File type、MIME、Size、非対応理由、Download/外部操作を正式仕様の許可範囲で表示する。
  - [x] Unknown MIME/statusで危険操作を有効にしない。
- [x] `022-file-details.png`と`023-folder-details.png`を基準にDetailを実装する。
  - [x] Header card、metadata card、action cardに分け、名前、MIME/type、Size、日時、Owner、共有元、権限、保存場所、状態を表示する。
  - [x] FileのOpen/DownloadとFile/FolderのMove/Rename/Share/Trashを対象と権限に応じて表示する。
  - [x] 実行不可理由を不明にしたまま無効化せず、必要な説明を表示する。

### 4.3 Destination・Transfer・Trash・Missing（`026`〜`029`）

- [x] `026-server-folder-selection.png`を基準にServer Folder pickerを再構築する。
  - [x] Breadcrumb、作成可能Folder、現在の選択、選択確定、Loading/Errorを表示する。
  - [x] 所有/直接共有/継承共有の現在権限を使い、作成権限のないFolderを確定できない。
- [x] `027-transfer-status.png`を基準にTransfer状態を再構築する。
  - [x] 待機、Hash計算、Upload、Download、一時停止、再開、完了、失敗、取消しの状態とProgressを表示する。
  - [x] 通信結果不明時は成功を合成せず、同一Session/Idempotency keyの再取得・再開へ案内する。
- [x] `028-trash.png`を基準にTrashを再構築する。
  - [x] Trash日時、Server算出の保持期限、Restore、完全削除、管理者容量警告を表示する。
  - [x] File/Folderの完全削除で対象と不可逆性を表示し、二重送信と通信結果不明を安全に処理する。
- [x] `029-missing-files.png`を基準にMissing項目を再構築する。
  - [x] `MISSING_CANDIDATE`を「Fileを確認中」、`MISSING`を「Fileが見つかりません」、Unknownを更新必要として表示する。
  - [x] File名、検出日時、最終確認日時を表示し、物理Pathを表示しない。
  - [x] 再確認の二重送信を防ぎ、確定Missingだけに対象・削除範囲を明記した索引削除を提供する。

### 4.4 PR4テスト・検証・完了

- [x] File画面群のJVM/Compose testを更新する。
  - [x] `010`、`021`〜`023`、`026`〜`029`の主要状態、権限、操作、Dialog、Error、Semanticsを検証する。
  - [x] List/Grid、Thumbnail fallback、長いFile名、360dp、200%文字、横画面を検証する。
  - [x] Member/Owner/Manager/Viewer/Unknownの操作可否、完全削除、Missing索引削除を回帰テストする。
- [x] PR4の自動・実機相当検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`、`:feature-files:connectedDebugAndroidTest`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [x] Android 13実機相当のAPI 33 EmulatorでList/Grid、Folder遷移、Detail、Upload/Download、Rename/Move、Trash/Restore/Purge、Missingを確認する（物理端末が未接続のため決定的Compose fixtureで代替し、物理端末の最終確認はPR10で実施する）。
  - [x] `docs/testing/20260904-android-ui-pr4-files.md`に対象MockupのCapture、操作、意図的差分、大量一覧の結果を記録する。
- [x] PR4を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR4完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR5: Recent・Sharing・Category・Search・Organization・Activity

### 5.1 開始条件・共通Entry連携

- [x] PR5の開始条件を満たす。
  - [x] PR4が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、Sharing/Search/Organization/ActivityのScreen、UiState、Navigation callback、Testを確認する。
- [x] PR4のEntry表示パターンをSharing/Search/Recent/Favorites/Activityで再利用する。
  - [x] Feature間の直接依存を追加せず、IDとApp callbackだけでFile/Folderへ遷移する。
  - [x] Owner、Permission/Source、共有元、更新日時、MissingをSearch共通metadataから表示する。

### 5.2 Recent・Shared・Category・Search（`011`〜`014`）

- [x] `011-recent-files.png`を基準にRecentを再構築する。
  - [x] 開いた時刻ごとの階層、Thumbnail/type、metadata、Paging、Refresh、Empty/Errorを表示する。
  - [x] 古いPage/Session応答で現在一覧を上書きしない既存挙動を保つ。
- [x] `012-shared-files.png`を基準にShared一覧を再構築する。
  - [x] 所有/受信Filter、File/Folder、Owner、Permission、継承元、Paging、Empty/Errorを表示する。
  - [x] 開く時に最新詳細と権限を再取得するNavigation境界を保つ。
- [x] `013-category-browser.png`を基準にCategory browserを実装する。
  - [x] 既存Search契約のMIME categoryを再利用し、Photo、Video、Audio、Documentを一覧化する。
  - [x] 新しいServer APIやFeature間直接依存を作らず、HomeのCategory選択から対応Filterで開く。
  - [x] Paging、Refresh、Empty/Error、Unknown MIMEを表示する。
- [x] `014-search.png`を基準にSearchを再構築する。
  - [x] Query、type、owner、date、size、status、TagのFilterと検索/クリア操作を階層化する。Favoriteは正式Search APIにfilterがないため、server-backed Favorites導線で代替する。
  - [x] 不正Inputを対象Fieldの近くに表示し、結果、Paging、Refresh、Loading、Empty、Errorと混同しない。
  - [x] 200%文字と360dp時にFilterと結果の主操作へ到達できる。

### 5.3 Sharing設定（`024`〜`025`）

- [x] `024-sharing-settings.png`を基準にSharing settingsを再構築する。
  - [x] 対象Entry、Owner、適用範囲、Member、Permission、継承、追加、変更、解除を表示する。
  - [x] Folder配下適用、File単体適用、共有全体解除の影響範囲をConfirmation dialogに表示する。
  - [x] Owner/Managerのみ変更可能とし、処理中の二重送信を防ぐ。
- [x] `025-share-permissions.png`を基準にMember/Permission選択を再構築する。
  - [x] 共有候補検索、選択済みMember、Permissionの説明、現在値、保存中/Errorを表示する。
  - [x] File共有に`CONTRIBUTOR`を表示せず、Unknown Permissionで保存できない。

### 5.4 専用Mockupのない追加Feature

- [x] Favorites、Tags、Entry organizationをDesign systemに統一する。
  - [x] FavoritesはSearch/Recentと同じEntry row、Paging、Refresh、Empty/Errorを使う。
  - [x] TagsはSettings/Formパターンを使い、作成、Rename、Delete、validation、上限を表示する。
  - [x] Entry organizationはFavorite状態、本人Tag、処理中、通信結果不明後の再取得を表示する。
- [x] ActivityをDesign systemに統一する。
  - [x] Type filter、時刻、操作、対象のSnapshot、Paging、Refresh、Empty/Errorを表示する。
  - [x] 現在アクセス可能なTargetだけにNavigationを提供し、監査内部値を表示しない。

### 5.5 PR5テスト・検証・完了

- [x] Sharing/Search/Organization/ActivityのJVM/Compose testを更新する。
  - [x] `011`〜`014`、`024`〜`025`と専用Mockupのない画面の主要状態、操作、Error、Semanticsを検証する。
  - [x] Sharing権限、Tag validation、Search filter、Unknown state、Targetアクセス不可を回帰テストする。
  - [x] Category browserが既存Search APIを使用し、専用APIを作らないことを確認する。
- [x] PR5の自動・実機相当検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`、`:feature-sharing:connectedDebugAndroidTest`、`:feature-search:connectedDebugAndroidTest`、`:feature-activity:connectedDebugAndroidTest`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [x] Android 13実機相当のAPI 33 EmulatorでRecent、Shared、Category、Search、Share設定、Favorites、Tags、Activityを決定的Compose fixtureで確認する（物理端末が未接続のため、物理端末の最終確認はPR10で実施する）。
  - [x] `docs/testing/20260904-android-ui-pr5-discovery-sharing.md`にCapture、権限、Navigation、意図的差分を記録する。
- [x] PR5を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR5完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR6: Photo・Video・Audio・PDF UI（`016`〜`019`）

### 6.1 開始条件・Viewer共通枠

- [x] PR6の開始条件を満たす。
  - [x] PR5が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、Photo/PDF/Player、Media route、Controller、Testの既存パターンを確認する。
- [x] Viewer共通枠を実装する。
  - [x] Back、Title、Detail、Content領域、Status、主要操作を通常/フルスクリーンで一貫させる。
  - [x] System bar/Cutout、回転、バックグラウンド/復帰で操作領域とController lifecycleを保つ。
  - [x] 200%文字と横画面で主要操作が隠れず、スクロールまたは再配置で到達できる。

### 6.2 Photo（`016-photo-viewer.png`）

- [x] Photo viewerを参考UIに整合する。
  - [x] 画像領域、ピンチZoom、Pan、前後移動、現在位置、Detailを表示する。
  - [x] Low/Medium/Original、現在のNetwork、Size/推定転送量、Original確認、Download品質を表示する。
  - [x] Loading、生成中、Error、通信断、非対応MIME、前後Entry消失を安全に表示する。

### 6.3 Video・Audio（`017`〜`018`）

- [x] Video playerを`017-video-player.png`に整合する。
  - [x] Video surface、再生/一時停止、Seek、3秒/10秒移動、0.5〜3.0倍速、現在/総時間を表示する。
  - [x] Low/Medium/Original、転送量確認、Queue待ち、変換中/進捗/失敗、待機/バックグラウン/明示Originalを表示する。
  - [x] 品質変更で再生位置を保ち、未生成Low/MediumからOriginalへ自動切替しない。
- [x] Audio playerを`018-audio-player.png`に整合する。
  - [x] Artwork/type Icon、再生/一時停止、Seek、3秒/10秒移動、速度、現在/総時間、通信断/再接続を表示する。
  - [x] AudioにVideo用の品質選択と変換Jobを表示せず、OriginalのSize/転送量確認を行う。
  - [x] 非対応Codecを無限retryせず、別操作へ戻れる失敗状態とする。

### 6.4 PDF（`019-pdf-viewer.png`）

- [x] PDF viewerを参考UIに整合する。
  - [x] Download前のMIME/Size/Range確認、推定転送量、進捗、上限超過案内を表示する。
  - [x] Page表示、現在/総Page、前後移動、1〜4倍Zoom、Loading/Errorを表示する。
  - [x] PDF全体をMemoryに読み込まず、既存のTemporary file、Lease、TTL、Session cleanupを保つ。

### 6.5 PR6テスト・検証・完了

- [x] MediaのJVM/Compose/Android testを更新する。
  - [x] `016`〜`019`の主要状態、品質、転送確認、Job、Player、PDF、Error、Semanticsを検証する。
  - [x] 回転、background/foreground、通信断/再接続、非対応Codec、資源上限を回帰テストする。
  - [x] 主要Player buttonの48dp領域、Content description、progress semanticsを検証する。
- [x] PR6の自動・実機相当検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`、`:feature-media:connectedDebugAndroidTest`、`:core-data:connectedDebugAndroidTest`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [x] Android 13/API 33 EmulatorでPhoto/PDF/Video/Audio、フルスクリーン、200%文字、回転/復帰状態、通信断を決定的fixtureで確認し、既存のAndroid 13物理端末LOCAL_DIRECT/REMOTE_SECUREのMemory/Frame/Fatal event実測を再確認する（物理端末未接続のため、変更後の最終実機確認はPR10で実施する）。
  - [x] `docs/testing/20260904-android-ui-pr6-media.md`に`016`〜`019`のCapture、操作、資源実測、意図的差分を記録する。
- [x] PR6を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR6完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR7: Text editor・Version history UI（`020`）

### 7.1 開始条件・Editor

- [x] PR7の開始条件を満たす。
  - [x] PR6が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、Text editor/history、dirty/compare/restore状態、Navigation、Testを確認する。
- [x] `020-text-editor.png`を基準にText editorを再構築する。
  - [x] Back、File名、閲覧/編集、Save、dirty、encoding、version、history導線を表示する。
  - [x] ViewerとEditorの文字コントラスト、行間、キーボード/IME、Focus、長文Scrollを整える。
  - [x] Viewer/Editor利用中のLoading、Read-only、Saving、Saved、Error、Session喪失を判別できる。
  - [x] 未保存で離脱する場合は保存/破棄/取消しを確認し、回転/Process recreationの承認済み上限内で下書きを保つ。

### 7.2 競合・履歴・復元

- [x] Text conflict UIを再構築する。
  - [x] 409競合で最新再読込、有界行比較、別名Uploadを表示し、強制上書きを提供しない。
  - [x] 比較上限超過、最新版再取得失敗、権限低下を操作可能な文言で表示する。
- [x] Version historyとpreview/restore UIをDesign systemに統一する。
  - [x] 50件Paging、version、作成日時、Size、作成者情報の許可範囲、Loading/Empty/Errorを表示する。
  - [x] 選択中1件だけのpreview、取消し、権限再評価、最新Version再取得後のRestore確認を表示する。
  - [x] Session/File/refresh/preview generationの古い応答で現在画面を上書きしない。

### 7.3 PR7テスト・検証・完了

- [x] TextのJVM/Compose testを更新する。
  - [x] `020`の閲覧、編集、dirty、save、離脱確認、read-only、error、Semanticsを検証する。
  - [x] 競合、行比較、別名保存、履歴Paging、preview、restore、古い応答の無視を回帰テストする。
  - [x] 長文、360dp、200%文字、横画面、IME表示時の主操作到達性を検証する。
- [x] PR7の自動・実機検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`、`:feature-text:connectedDebugAndroidTest`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [x] Android 13実機相当のAPI 33 Emulatorで編集、回転、dirty離脱、競合、比較、別名保存、履歴、Restoreを決定的fixtureで確認する（物理端末が未接続のため、物理端末の最終確認はPR10で実施する）。
  - [x] `docs/testing/20260904-android-ui-pr7-text.md`に`020`と履歴/競合のCapture fixture、操作、意図的差分を記録する。
- [x] PR7を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR7完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR8: Server側Cache管理契約・永続Cleanup run

### 8.1 開始条件・正式文書

- [x] PR8の開始条件を満たす。
  - [x] PR7が`main`へMerge済みで必須CIが成功している。
  - [x] 最新`main`から短命Branchを作成し、`IMediaCleanupService`、`PostgreSqlMediaCleanupRepository`、`MediaCleanupWorker`、Admin authorization、冪等Endpointの類似実装を確認する。
- [x] Cache管理契約を正式文書へ追加する。
  - [x] `docs/product-requirements.md`にAdmin状態取得、非同期手動清掃、重複要求、Worker復旧の受け入れ条件を追加する。
  - [x] `docs/functional-design.md`にEndpoint、DTO、Run状態、transaction、lease、Error、Android pollingを定義する。
  - [x] `docs/architecture-design.md`、`docs/repository-structure.md`、`docs/development-guidelines.md`へAPI/Worker/DB境界、配置、検証を反映する。
  - [x] `contracts/openapi/kurastorage-api.yaml`へ`GET /api/v1/admin/media-cache`と`POST /api/v1/admin/media-cache/cleanup-requests`を追加する。

### 8.2 Cache status・Cleanup run永続化

- [x] Cache status queryをTest firstで実装する。
  - [x] Low/MediumのREADY合計、Image/Video内訳、10GB上限、6GB目標、Queued/Running件数、Failed件数を集計する。
  - [x] Thumbnail/PDF thumbnailを10GB対象に合算せず、生成中またはLease中の個別情報を公開しない。
  - [x] File名、物理Path、User名、Job入力、内部Error詳細をResponse・Logに含めない。
- [x] `MediaCleanupRun`とMigrationをTest firstで実装する。
  - [x] Run ID、scheduled/manual trigger、pending/running/completed/failed、Idempotency key hash、requesting Admin、lease、日時、件数、解放Byte、失敗件数を保持する。
  - [x] 同一Admin/Idempotency keyの同一要求を1Runへ収束させ、異なるpayload再利用を拒否する。
  - [x] pending/running manual runを不要に並列化せず、scheduled/manualの同時Cleanupを既存advisory lockに収束させる。
  - [x] MigrationのUp/Down/再Up、既存Media data保持、一意制約、pending modelなしをPostgreSQLで検証する。

### 8.3 Admin API・Worker復旧

- [x] Admin Cache APIをTest firstで実装する。
  - [x] GETはCache statusと最新Runを返し、POSTはUUID `Idempotency-Key`を必須として`202 Accepted`とRun状態を返す。
  - [x] Adminだけを許可し、Member、未認証、失効Device/Sessionを既存Error envelopeで拒否する。
  - [x] HTTP Request内でCleanup全体を実行せず、永続runの登録だけ行う。
  - [x] 通信結果不明後の同一Key再送とGET再取得で同じRunへ収束させる。
- [x] `MediaCleanupWorker`がmanual runを処理できるようにする。
  - [x] 未処理runを有界間隔でclaimし、既存`IMediaCleanupService`とadvisory lockを再利用する。
  - [x] 定期Cleanupもrun結果を記録し、最終清掃日時をAPI ProcessのMemoryに依存させない。
  - [x] Worker停止、Process kill、DB中断後のrunning leaseを回収し、冪等な清掃と最終状態へ収束させる。
  - [x] Storage unavailable、一部File削除失敗、lock取得不可を区別し、元Fileに影響させず再試行可能性を保つ。

### 8.4 PR8テスト・検証・完了

- [x] ServerのDomain/Application/Integration testを完了する。
  - [x] Cache集計、上限対象、Run状態遷移、冪等性、並列、lease回収、部分失敗、Worker再起動を検証する。
  - [x] Admin/Member/未認証/Device失効、不正Key、同一Key再送、通信結果不明のAPI結合テストを追加する。
  - [x] 生成中/使用中Cacheを削除せず、元FileとThumbnailへ影響しない実Storage/PostgreSQLテストを行う。
- [x] PR8の自動・運用検証を完了する。
  - [x] `./scripts/ci/verify-server.sh`、`verify-config.sh`、`verify-security.sh`、`verify-deployment.sh`、format、Migration、OpenAPI、`git diff --check`が成功する。
  - [x] API/Worker/PostgreSQLを使った実際のmanual cleanupでpending→running→completed/failed、解放Byte、重複防止を確認する。
  - [x] Log/Response/MetricにFile名、物理Path、User入力、Token、Idempotency key平文が漏れないことを確認する。
  - [x] `docs/testing/YYYYMMDD-android-ui-pr8-cache-server.md`にAPI、Migration、Worker復旧、実測結果を記録する。
- [x] PR8を完了する。
  - [x] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR8完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR9: Settings・Backup・Cache UI（`015`、`030`〜`036`）

### 9.1 開始条件・Settings（`015`）

- [ ] PR9の開始条件を満たす。
  - [ ] PR8が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`から短命Branchを作成し、Backup/Quality、Admin storage、ServiceContainer、Navigation、Testを確認する。
- [ ] `015-settings.png`を基準にSettings画面を実装する。
  - [ ] Account、Connection、Auto backup、Trusted Wi-Fi、Quality/Data usage、Cache、Activity、LogoutをSection化する。
  - [ ] Cache導線をAdminだけに表示し、Memberに管理数値を表示しない。
  - [ ] 各項目に現在状態、説明、次画面の意味を表示する。

### 9.2 Backup状態・Rule（`030`〜`032`）

- [ ] `030-backup-status.png`を基準にBackup overviewを再構築する。
  - [ ] 最終成功、保留、Upload中、成功、失敗の件数と、Rule/Taskごとの状態・Progress・理由を表示する。
  - [ ] 今すぐ実行、一時停止/再開、失敗retryを一意WorkとRoom transactionの既存処理へ接続する。
  - [ ] Network、Battery/充電、認証、HDD、Source権限の待機理由を区別する。
- [ ] `031-backup-rules.png`を基準にRule一覧を再構築する。
  - [ ] Source、Server保存先、有効状態、Network mode、Battery、権限Error、最終状態を表示する。
  - [ ] Add/Edit/Enable/Disable/Deleteを表示し、DeleteがServer Fileを削除しないことをConfirmationに明記する。
- [ ] `032-backup-rule-editor.png`を基準にRule editorを再構築する。
  - [ ] MediaStore/SAF Source、Server Folder、Network mode、最低Battery、初回充電、有効状態、保存を表示する。
  - [ ] SAF/Server Folder pickerをApp callbackの境界で使い、権限喪失と再選択を表示する。
  - [ ] 一方向Backup、端末削除非反映、強制停止後の制約を表示する。

### 9.3 Trusted Wi-Fi（`033`〜`034`）

- [ ] `033-trusted-wifi.png`を基準にTrusted Wi-Fi一覧を再構築する。
  - [ ] 表示名、SSID、BSSID制限、従量制、有効状態、現在接続中との一致を表示する。
  - [ ] 権限未許可、拒否、恒久拒否、情報取得不能をfail-closedで表示し、端末設定へ案内する。
- [ ] `034-trusted-wifi-editor.png`を基準にWi-Fi editorを再構築する。
  - [ ] 現在接続中Wi-Fiの読み取り、表示名、SSID、任意BSSID制限、従量制、有効状態を表示する。
  - [ ] 別Confirmation dialogで登録を明示確定し、SSID/BSSIDがServer identityの代替でないことを説明する。
  - [ ] SSID/BSSIDをScreenshot fixture、Log、Metric、Crash記録に実値で残さない。

### 9.4 Quality・Cache（`035`〜`036`）

- [ ] `035-quality-network-settings.png`を基準にQuality/Data usage設定を再構築する。
  - [ ] LOCAL_DIRECT、登録済み外部Wi-Fi+ZeroTier、未登録Wi-Fi+ZeroTier、Mobile+ZeroTierごとにLow/Medium/Originalを表示する。
  - [ ] 現在値、説明、Save、Reset、saving/errorを表示し、Viewer中の手動品質選択を制限しない。
  - [ ] Mobile通信の自動Backup禁止を変更可能な設定にしない。
- [ ] PR8のServer契約をAndroidに実装する。
  - [ ] `core-model`にCache status/run、`core-network`にDTO/API、`core-data`にAdmin Cache repositoryを追加する。
  - [ ] strict mapping、Unknown run status、Admin 403、401 refresh、通信結果不明後のGET/同一Key再送を実装する。
  - [ ] Session/Route/User変更時にCache状態とpollingを破棄する。
- [ ] `036-cache-management.png`を基準にAdmin Cache画面を実装する。
  - [ ] Low/Medium READY使用量/10GB、6GB清掃目標、Image/Video内訳、最終清掃、生成中、失敗を表示する。
  - [ ] 今すぐ清掃の対象と再生成可能性を確認し、pending/running/completed/failedをpollingで表示する。
  - [ ] モックアップ内のThumbnailを10GB使用量に合算せず、正式仕様にない一括失敗retryを表示しない。
  - [ ] MemberからRouteを直接開いてもServer 403を安全に表示し、管理操作を実行しない。

### 9.5 PR9テスト・検証・完了

- [ ] Settings/Backup/CacheのJVM/Contract/Compose testを更新する。
  - [ ] `015`、`030`〜`036`の主要状態、Form、保存、Permission、Progress、Error、Semanticsを検証する。
  - [ ] Cache DTO strict mapping、Admin/Member、Idempotency key、polling停止、通信結果不明を回帰テストする。
  - [ ] 360dp、200%文字、横画面、Light/DarkでFormと主操作へ到達できることを検証する。
- [ ] PR9の自動・実機検証を完了する。
  - [ ] `./scripts/ci/verify-android.sh`、`:feature-settings:connectedDebugAndroidTest`、`:feature-backup:connectedDebugAndroidTest`、`:app:connectedDebugAndroidTest`、`git diff --check`が成功する。
  - [ ] Android 13実機でSettings、Backup overview/rule、Wi-Fi権限/登録、Quality保存、Admin Cacheのmanual cleanupを確認する。
  - [ ] `docs/testing/YYYYMMDD-android-ui-pr9-settings-backup-cache.md`に`015`、`030`〜`036`のCapture、操作、意図的差分を記録する。
- [ ] PR9を完了する。
  - [ ] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR9完了記録、記録Commitの再Pushを完了して報告・停止する。

---

## PR10: 全体Adaptive・Accessibility・実機E2E

### 10.1 開始条件・36画面追跡

- [ ] PR10の開始条件を満たす。
  - [ ] PR9が`main`へMerge済みで必須CIが成功している。
  - [ ] 最新`main`から短命Branchを作成し、PR1〜PR9の完了記録、`docs/testing/`、残存未対応を確認する。
- [ ] `001`〜`036`のMockup追跡表を最終化する。
  - [ ] すべての番号にProduction owner、対象状態、自動Test、手動Capture、意図的差分が記録されている。
  - [ ] 参考UIのないFavorites、Tags、Entry organization、Activity、確認DialogのDesign system整合も記録する。
  - [ ] サンプル値固定、木々装飾、VPN操作、背景画像化、未実装ボタンが残っていない。

### 10.2 Adaptive・Accessibility最終検証

- [ ] 全主要画面のAdaptive layoutを最終確認する。
  - [ ] 360dp幅、一般的な縦画面、横画面、System bar/Cutoutで欠落、重なり、意図しない横切れがない。
  - [ ] 通常文字と200%文字拡大で、主要操作、Error、危険操作の取消しへ到達できる。
  - [ ] IME、Dialog、Snackbar、FAB、Bottom navigationがContentと操作不能な形で重ならない。
- [ ] TalkBackと非色依存を最終確認する。
  - [ ] 画面Title、Section heading、現在状態、List item、Form、Error、主/補助操作を意味のある順序で読み上げる。
  - [ ] Icon-only operationにContent description、選択にselected/state description、Progressにprogress semanticsがある。
  - [ ] Success、Warning、Error、Permission、Offlineを色だけでなく、labelとIcon/形状で識別できる。
  - [ ] 主要操作のタップ領域が48dp相当以上で、Focus trapや不要な連続読み上げがない。
- [ ] 文字とコントラストを最終確認する。
  - [ ] User向け用語、ZeroTier表記、File/Folder/Backupの文言が画面間で一貫している。
  - [ ] Light/Darkの本文、補足、Button、Status、Outlineが必要なコントラストを持つ。
  - [ ] Internal code、物理Path、Token、SSID/BSSIDの非公開値をUser向け表示とSemanticsに出さない。

### 10.3 全自動テスト・実機E2E

- [ ] Repository全体の自動検証を完了する。
  - [ ] `./scripts/ci/verify-android.sh`、`./scripts/ci/verify-server.sh`、`verify-config.sh`、`verify-security.sh`、`verify-deployment.sh`、`git diff --check`が成功する。
  - [ ] Android 13実機/API 33 Emulatorで`./apps/android/gradlew -p apps/android connectedDebugAndroidTest --max-workers=1`が全Moduleで成功する。
  - [ ] Coverage、ktlint、detekt、Android Lint、SBOM、Debug/Release相当Build、AndroidTest APK assemblyの必須gateを満たす。
- [ ] Android 13物理端末で主要E2Eを完了する。
  - [ ] 起動→LOCAL_DIRECT/REMOTE_SECURE→Login/Device登録→Homeの表示と遷移を確認する。
  - [ ] Bottom navigation、File/Folder、Upload/Download、Detail、Share、Search/Recent/Category/Favorite/Tag/Activityを確認する。
  - [ ] Photo/PDF/Video/Audio/Text、品質変更、競合、回転、background/foreground、通信断/再接続を確認する。
  - [ ] Settings、Backup rule/Wi-Fi/Progress、Quality、Admin Cacheのmanual cleanupを確認する。
  - [ ] 360dp相当、横画面、200%文字、TalkBack、Light/Darkの対象表を完了する。
  - [ ] Frame、Memory、ANR、Crash、StrictMode、Fatal log、ネットワークの明らかな回帰がない。

### 10.4 文書・最終完了

- [ ] `docs/testing/YYYYMMDD-android-ui-pr10-final-e2e.md`を作成する。
  - [ ] 36画面対応表、端末/API、方向、文字倍率、Theme、TalkBack、E2E手順、スクリーンショットを記録する。
  - [ ] 意図的差分、計画と実装の差分、実測値、残存不具合がないことを記録する。
- [ ] 正式文書を最終実装へ整合する。
  - [ ] `product-requirements`、`functional-design`、`architecture-design`、`repository-structure`、`development-guidelines`に矛盾、古いPath、実装と異なる契約が残っていない。
  - [ ] `docs/ui/`の参考画像自体をProduction assetとして複製していない。
- [ ] PR10を完了する。
  - [ ] self-review、Commit、Push、英語Pull Request、必須CI、モード3-AのPR10完了記録、記録Commitの再Pushを完了して報告・停止する。

- [ ] PR10 Merge後に全体完了処理を行う。
  - [ ] PR1〜PR10がすべて`main`へMergeされている。
  - [ ] 本ファイル全体に未完了タスク`[ ]`がないことを確認する。
  - [ ] steeringモード3-Bで「全体振り返り」を記録する。

---

## 各Pull Request完了記録

> 各Pull Request作成後にsteeringモード3-Aで追記する。後続Pull Requestに未完了タスクが残っていても、完了したPull Requestの記録は行う。各記録には完了日、Pull Request番号/URL、Test/Build/静的解析/手動確認、計画と実装の差分、追加タスクと理由、技術的に不要となったタスクと代替実装、後続への引継ぎを含める。該当なしは「なし」と記録する。

### PR1: UI監査・Design system基盤

- 完了日: 2026-09-03
- Pull Request: [#49 Add Android UI design-system foundation](https://github.com/ry825/Kura_Storage/pull/49)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Debug/Release APK、AndroidTest APK、Coverage、CycloneDX SBOMを含む）。
  - Android 13 / API 33 Emulatorで`:core-ui:connectedDebugAndroidTest`成功（5/5）。360dp、200%文字、Light/Dark、48dp操作領域、Heading、Content description、Selected/Error/Progress semantics、決定的Captureを確認した。
  - `git diff --check`成功。ロゴとFile iconは第三者Assetを使わないCompose描画で、Bitmap非混入、追加依存がCompose UI test用途だけであることを確認した。
  - GitHub必須CIのAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - Instrumented testの初回実行で、Theme fixtureが再composeされない問題とIcon buttonのSemantics領域が40dpになる問題を検出した。observable stateへの変更と明示的な48dp最小Sizeで修正し、同一PR内で再検証した。
  - それ以外のPR1範囲、PR境界、正式文書との優先関係に差分はない。
- 追加タスクと理由: 上記2件の検証時修正を追加した。受け入れ条件を満たすために必要であり、後続PRへの先送りはしていない。
- 技術的に不要となったタスクと代替実装: なし。第三者Logo Assetの権利確認は、独自のCompose `Canvas`描画を採用し出典依存をなくす形で完了した。
- 後続への引継ぎ:
  - PR2〜PR9では本PRのTheme、共通Component、状態表示、Semantics helper、Logo/File type iconを各Featureへ適用する。
  - 36画面監査で特定した不足Destination・契約は割当済みPRで解消し、PR10で物理端末、TalkBack、回転、200%文字、全画面Evidenceを最終確認する。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告であり、SBOM生成と必須CIは成功している。

### PR2: App shell・Home・グローバルNavigation

- 完了日: 2026-09-04
- Pull Request: [#50 Add Android app shell and Home dashboard](https://github.com/ry825/Kura_Storage/pull/50)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Build、JVM test、Lint、ktlint、Detekt、Coverage、Debug/Release APK、AndroidTest APK、CycloneDX SBOMを含む）。
  - Android 13 / API 33 Emulatorで`:app:connectedDebugAndroidTest`成功（8/8）、`:feature-settings:connectedDebugAndroidTest`成功（4/4）。5項目Navigation、選択・再選択・Back、認証前非表示、Homeの状態・Callback、Admin/Member、360dp、横画面、200%文字、Light/Dark、Heading、Navigation semantics、決定的Captureを確認した。
  - `git diff --check`成功。Self-reviewで全保護画面のViewModel keyを`SessionServices.sessionId`に紐付け、Route/User再接続時に旧RepositoryとUI stateを再利用しないことを確認した。
  - GitHub必須CI run `33827693590`のAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - 物理Android 13端末が未接続で、既知の無線Debug endpointも接続拒否だったため、PR2の表示確認はAPI 33 Emulator上の縦横・通常/200%文字・Light/Dark fixtureで代替した。物理端末での全画面最終確認は計画済みのPR10で実施する。
  - 固定5項目Navigationで既存のMedia settings、Backup settings、Logout導線を維持するため、PR9の詳細Settings再構築に先立って最小Settings hubを追加した。Cacheは契約未実装のためAdmin専用の無効行とし、正式な状態・清掃操作はPR8/PR9に残した。
- 追加タスクと理由:
  - LazyColumnの未構成Nodeを決定的に検証する`performScrollToNode`用Semantics tag、Settingsの200%文字・横画面Logout fixture、全保護ViewModelのSession key監査を追加した。Adaptive到達性とSession隔離の完了条件を満たすためである。
- 技術的に不要となったタスクと代替実装: なし。物理端末確認は削除せずPR10の最終実機E2Eに引き継ぎ、PR2では同一API levelの決定的Emulator fixtureを追加実行した。
- 後続への引継ぎ:
  - PR3では接続・認証画面を再構築し、今回追加したShell外RouteとSession破棄境界を維持する。
  - PR4〜PR7ではFiles、Shared/Search系、Viewer、Textの個別画面を更新し、Top-level以外ではBottom navigationを出さない現在のRoute分類を維持する。
  - PR8/PR9でAdmin cache契約と完全なSettings UIを実装し、現在の無効Cache行を正式状態・清掃操作へ置き換える。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告で、SBOM生成とCIは成功している。

### PR3: 起動・接続・認証UI

- 完了日: 2026-09-04
- Pull Request: [#51 Rebuild Android connection and authentication flow](https://github.com/ry825/Kura_Storage/pull/51)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Build、JVM test、Lint、ktlint、Detekt、Coverage、Debug/Release APK、AndroidTest APK、CycloneDX SBOMを含む）。
  - API 29、API 33、API 36 Emulatorで`:feature-connection:connectedDebugAndroidTest`（各5/5）、`:feature-auth:connectedDebugAndroidTest`（各6/6）、`:app:connectedDebugAndroidTest`（各8/8）が成功した。
  - API 29、API 33、API 36でLight/DarkのCold startを目視確認した。API 33では320 x 640 dpのCompact表示も確認し、最終Dark captureでLogo、見出し、説明、進捗、接続順序が判読可能であることを確認した。
  - `git diff --check`成功。Self-reviewでLOCAL_DIRECT優先、REMOTE_SECUREでのDevice登録無効化、Password semantics、送信重複防止、入力保持、ZeroTier操作非搭載を確認した。
  - GitHub必須CI run `33832746455`のAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - 物理Android 13端末が未接続だったため、同一API levelのAPI 33 Emulatorによる状態fixture、320 x 640 dp表示、Light/Dark Cold startで代替した。物理端末での最終確認は計画済みのPR10へ引き継ぐ。
  - API 31以上はSystem Splashの制約に合わせてLogoと背景色をNative resourceで統一し、App名は固定待機なしの最初のCompose frameに表示した。API 29は同じ意匠のWindow backgroundを使用した。
- 追加タスクと理由:
  - API境界と最新環境の回帰を同時に確認するため、計画したAPI 33に加えてAPI 29とAPI 36でも全19件のconnected testとLight/Dark Cold startを実行した。
  - 手動Dark確認で初期Compose frameの文字色不整合を検出したため、Connection/Auth rootへMaterial `Surface`を追加し、再Build・再Install・再Capture・全検証を実行した。
- 技術的に不要となったタスクと代替実装: なし。物理端末確認は削除せずPR10へ引き継ぎ、PR3では決定的な同一API level Emulator検証を完了した。
- 後続への引継ぎ:
  - PR4はPR3 Merge後に最新`main`から開始し、File browser・Detail・Transfer・Trash・Missingを再構築する。
  - Connection/AuthはShell外Routeのまま維持し、保護画面へ遷移する条件をStorage availableかつ認証済みに限定する。
  - PR10でAndroid 13物理端末、TalkBack、回転、200%文字、全画面E2Eを最終確認する。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告で、SBOM生成とCIは成功している。

### PR4: File browser・Detail・Transfer・Trash・Missing

- 完了日: 2026-09-04
- Pull Request: [#52 Rebuild Android file management screens](https://github.com/ry825/Kura_Storage/pull/52)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Build、JVM test、Lint、ktlint、Detekt、Coverage、Debug/Release APK、AndroidTest APK、CycloneDX SBOMを含む）。
  - Android 13 / API 33 Emulatorで`:feature-files:connectedDebugAndroidTest`成功（23/23）、`:app:connectedDebugAndroidTest`成功（8/8）。List/Grid、権限別操作、Detail、非対応File、Move、Transfer、Trash/Purge、Missing、360dp、200%文字、Dark、横画面、1,000件Lazy表示の決定的fixtureを確認した。
  - `git diff --check`成功。Self-reviewでBreadcrumb、Search境界、direct/inherited権限、Unknown状態のfail-closed、索引削除範囲、Dialog scroll、Thumbnail fallbackを確認した。
  - GitHub必須CI run `33835560354`のAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - 物理Android 13端末が未接続だったため、同一API levelのAPI 33 Emulatorと決定的Compose fixtureで代替した。物理端末での最終確認は計画済みのPR10へ引き継ぐ。
  - 現行`FileEntry`/一覧APIにFolder項目数がないため、値を合成せず「取得不能」と明示した。Searchは読み込み済みPageだけを絞り込まず、既存のServer-backed Search Routeへ接続した。
  - 320 x 640 dpでの初回fixtureによりDetail上部の優先情報が画面外になる問題を検出し、状態/MIME/非対応理由を上位へ移動してDetail本文をscroll可能にした後、23件を再実行した。
- 追加タスクと理由:
  - Browserの階層位置を正確に維持する`BrowserBreadcrumb` stateとJVM回帰テストを追加した。Mockupの現在位置要件を、下位Folderでも安定して満たすためである。
  - Backup destination pickerにも同じowner・permission source・writable guard・Folder作成表示を適用した。`026`のServer Folder pickerがMoveとBackupで異なる安全性を持たないようにするためである。
- 技術的に不要となったタスクと代替実装:
  - 非対応Fileの自動外部Openは安全なURI/Intent契約がないため追加せず、現在権限で許可されたDownloadを安全な代替操作とした。
  - Folder項目数のServer/API拡張はPR4のUI範囲外かつ現行契約にないため追加せず、取得不能表示で代替した。
- 後続への引継ぎ:
  - PR5では本PRのEntry metadata/type fallback/permission表示をFeature間直接依存なしでRecent、Shared、Category、Search、Favorites、Activityへ展開する。
  - PR6/PR7ではDetailから既存のMedia/Text Routeへ渡す境界と、非対応/Unknownでのfail-closedを維持する。
  - PR10でAndroid 13物理端末、TalkBack、回転、200%文字、実Serverを使うUpload/Download/Move/Trash/Missing E2Eを最終確認する。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告で、SBOM生成とCIは成功している。

### PR5: Recent・Sharing・Category・Search・Organization・Activity

- 完了日: 2026-09-04
- Pull Request: [#53 Align Android discovery and sharing UI](https://github.com/ry825/Kura_Storage/pull/53)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Build、JVM test、Lint、ktlint、Detekt、Coverage、Debug/Release APK、AndroidTest APK、CycloneDX SBOMを含む）。
  - Android 13 / API 33 Emulatorで`:feature-search:connectedDebugAndroidTest`成功（11/11）、`:feature-sharing:connectedDebugAndroidTest`成功（6/6）、`:feature-activity:connectedDebugAndroidTest`成功（2/2）、`:app:connectedDebugAndroidTest`成功（8/8）。320 x 640、200%文字、Dark、決定的Capture、Filter、権限、Error、Navigation到達性を確認した。
  - `git diff --check`成功。Self-reviewでFeature間直接依存の非追加、既存Search APIの再利用、最新権限のNavigation境界、Unknown/Missingのfail-closed、stable key、古い応答の破棄を確認した。
  - GitHub必須CI run `33838311301`のAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - Favoriteは正式Search APIにfilter契約がないため、読み込み済みPageの不完全な絞り込みを作らず、server-backed Favorites画面への導線で代替した。
  - Categoryは専用APIやFeatureを追加せず、既存Search Route/RepositoryにMIME categoryを設定し、共通Entry rowで表示した。
  - 物理Android 13端末が未接続だったため、同一API levelのAPI 33 Emulatorと決定的Compose fixtureで代替した。物理端末の最終確認はPR10へ引き継ぐ。
- 追加タスクと理由:
  - RecentとShared一覧にrequest generation guardを追加した。Filter変更やRefresh後に古い応答が現在の一覧を上書きしない完了条件を明示的に保証するためである。
  - 共通`KuraFileEntryRow`とdisabled semanticsを`core-ui`に追加した。Search/Recent/Shared/Favorites間でmetadata表示とfail-closed操作を一致させるためである。
  - 200%文字の初回fixtureでActivityのError操作がCompact画面の表示範囲外になる問題を検出し、scroll可能な状態表示に変更して再検証した。
- 技術的に不要となったタスクと代替実装:
  - Search内のFavorite filterは対応するServer契約がないため実装せず、全件をserver-backedで取得するFavorites画面への導線で代替した。
  - Category専用Server APIとFeature moduleは不要と判断し、正式Search契約とApp callbackの再利用で代替した。
- 後続への引継ぎ:
  - PR6/PR7では共通Entry rowからApp callbackで既存Media/Text Routeへ渡す境界と、Unknown/Missing/権限喪失時のfail-closedを維持する。
  - PR8/PR9のCache/Settings実装では、共有権限や状態の内部値をUIで合成せずServer応答を権威とする。
  - PR10でAndroid 13物理端末、TalkBack、回転、200%文字、実ServerのRecent/Shared/Search/Sharing/Organization/Activity E2Eを最終確認する。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告で、SBOM生成とCIは成功している。

### PR6: Photo・Video・Audio・PDF UI

- 完了日: 2026-09-04
- Pull Request: [#54 Align Android media viewer interfaces](https://github.com/ry825/Kura_Storage/pull/54)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Build、JVM test、Lint、ktlint、Detekt、Coverage、Debug/Release APK、AndroidTest APK、CycloneDX SBOMを含む）。
  - Android 13 / API 33 Emulatorで`:feature-media:connectedDebugAndroidTest`成功（14/14）、`:core-data:connectedDebugAndroidTest`成功（10/10）、`:app:connectedDebugAndroidTest`成功（8/8）。320 x 640、200%文字、Dark、フルスクリーン、Photo/PDFの品質・転送確認、Video/Audioの再生操作とError表示を決定的fixtureで確認した。
  - 既存のAndroid 13物理端末LOCAL_DIRECT/REMOTE_SECURE記録でPSS 117,912 KiB、RSS 259,820 KiB、343 frames、janky 55（16.03%）、median/p90/p95/p99 13/38/53/125 ms、Fatal/ANRなしを再確認した。
  - `git diff --check`成功。Self-reviewでSession/Route lifecycle、品質変更時の再生位置、Original明示確認、PDF temporary file/Lease/cleanup、非対応Codecのfail-closedを確認した。
  - GitHub必須CI run `33841801468`のAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - 物理Android 13端末が未接続のため、変更後の表示・操作は同一API levelのEmulatorで確認し、資源実測は同じController/Data pathを使用した承認済みの物理端末記録を再確認した。変更後の物理端末・TalkBack最終確認はPR10へ引き継ぐ。
  - Audio参考UIの5秒戻しではなく、上位の正式仕様に従い3秒/10秒移動を維持した。VideoのLow/Medium/Originalは解像度を合成せず、Server契約の品質名とサイズを表示した。
- 追加タスクと理由:
  - Photoの現在位置/総数、明示的なZoom in/out操作、各Viewerの型付き状態Panel、VideoのCompact・200%文字・Dark・フルスクリーンfixtureを追加した。参考UIの位置情報とAccessibility/状態判別の完了条件を決定的に満たすためである。
- 技術的に不要となったタスクと代替実装:
  - 新規API、依存、Controller/Data構成、WorkManager、装飾Assetは不要だった。既存の認証済みMedia source、変換Job、Player lifecycle、PDF temporary-file管理を維持し、Compose UIと状態mappingの更新で代替した。
- 後続への引継ぎ:
  - PR7ではMedia routeと同じSession/Back/Detail境界を維持し、Text editor/historyの未保存・競合・復元状態を実装する。
  - PR10でAndroid 13物理端末のPhoto/PDF/Video/Audio、回転、background/foreground、通信断/再接続、資源、TalkBack、200%文字を最終確認する。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告で、SBOM生成とCIは成功している。

### PR7: Text editor・Version history UI

- 完了日: 2026-09-04
- Pull Request: [#55 Align Android text editor and version history UI](https://github.com/ry825/Kura_Storage/pull/55)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-android.sh`成功（1,387 tasks、Build、JVM test、Lint、ktlint、Detekt、Coverage、Debug/Release APK、AndroidTest APK、CycloneDX SBOMを含む）。
  - Android 13 / API 33 Emulatorで`:feature-text:connectedDebugAndroidTest`成功（8/8）、`:app:connectedDebugAndroidTest`成功（8/8）。閲覧/編集、dirty離脱、保存、read-only、競合、有界比較、別名保存、履歴、preview、Restore、200%文字、長文の決定的fixtureを確認した。
  - `git diff --check`成功。Self-reviewでSave and leaveの成功後のみの離脱、競合時の強制上書き非提供、権限喪失のfail-closed、preview/refreshの古い応答無視、Restoreの多重実行防止を確認した。
  - GitHub必須CI run `33845834384`のAndroid、Server、Config、Securityがすべて成功した。
- 計画と実装の差分:
  - 物理Android 13端末が未接続のため、同一API levelのAPI 33 Emulatorと決定的Compose fixtureで表示・操作を代替した。物理端末、TalkBack、回転、IME、通信断、実Serverの最終確認はPR10へ引き継ぐ。
  - `020`の固定sample値と装飾は取り込まず、正式仕様の実File情報、UTF-8/1 MiB上限、64 KiB下書き復元上限、権限再評価、楽観的version確認を維持した。
- 追加タスクと理由:
  - Save and leaveの明示的な成功待ち状態、View/Edit切替後の下書き保持、比較上限超過表示、最新version再取得失敗、Restore多重実行防止のテストを追加した。未保存data保全と回復操作を完了条件どおり決定的に検証するためである。
- 技術的に不要となったタスクと代替実装:
  - 強制上書き、自動merge、新規API、新規依存、装飾Assetは不要だった。既存の認証済みText repository、別名Upload、有界行比較、generation guard、Kura design systemの再利用で代替した。
- 後続への引継ぎ:
  - PR8/PR9のCache/Settings実装では、PR7と同様にServer応答を権威とし、permission/session/request generationのfail-closedを維持する。
  - PR10でAndroid 13物理端末、TalkBack、回転、200%文字、IME、通信断/再接続、実ServerのText editor/history E2Eを最終確認する。
  - CycloneDX生成時の`androidx.media3:media3-ui-compose:1.11.0` effective-POM warningは既存の非fatal警告で、SBOM生成とCIは成功している。

### PR8: Server側Cache管理契約・永続Cleanup run

- 完了日: 2026-09-04
- Pull Request: [#56 Persist server media cache cleanup runs](https://github.com/ry825/Kura_Storage/pull/56)
- Test・Build・静的解析・手動確認:
  - `./scripts/ci/verify-server.sh`成功（Release build警告0、Domain 135、Application 353、Integration 229）。`dotnet format --verify-no-changes`を含む。
  - `./scripts/ci/verify-config.sh`、`verify-deployment.sh`、`verify-security.sh`、OpenAPI YAML parse、`OpenApiContractTests`、`git diff --check`が成功した。ホストにない`nginx`/`shellcheck`を含む構成・配備検証は使い捨てUbuntu Docker環境で実行した。
  - PostgreSQL 17でMigrationのUp/Down/再Up、既存Media data保持、manual一意制約、4 Index、pending modelなしを確認した。同一Admin/keyの並列登録、異なるfingerprint拒否、manual優先claim、active lease拒否、期限切れrunning lease回収、旧worker token拒否も確認した。
  - 実`MediaCleanupWorker`、`MediaCleanupService`、`PostgreSqlMediaCleanupRepository`、`DerivativeStore`を接続し、manual runの`PENDING -> RUNNING -> COMPLETED`、1件/10 bytes解放、元File・有効Delivery lease・Thumbnail保持を確認した。Storage unavailable、部分削除失敗、advisory lock競合、取消後回収も自動Testで確認した。
  - API結合TestでAdmin成功、Member `403`、未認証/失効Device `401`、不正key `400`、同一key再送`202`と同一Run ID、GET再取得を確認した。Response/収集Log/MetricにFile名、物理Path、User入力、Token、Idempotency key平文を出さない境界を確認した。
  - Coverlet Line CoverageはDomain 84.57%、Application Unit/Integration統合88.57%。重要境界は`MediaCleanupRun.cs` 96.77%、`AdminMediaCacheService.cs` 98.98%、`MediaCleanupWorker.cs` 96.05%で基準を満たした。
  - GitHub必須CI run `33851958226`のConfig、Security、Android、Serverがすべて成功した。
- 計画と実装の差分:
  - 永続Runと5秒poll/15分leaseを運用設定として変更可能にするため、Server exampleだけでなくProduction template、environment example、Raspberry Pi config validation、deployment verificationへ設定を追加した。
  - 実Workerと実Storageを同一PostgreSQL統合Testで検証するため、Integration test projectからWorker projectを明示参照した。PR8のAPI/Worker/DB境界とPR9のAndroid UI非実装という範囲は計画どおり維持した。
- 追加タスクと理由:
  - Cleanup poll/lease設定の起動時範囲検証と配備template検証を追加した。誤設定でmanual受理遅延またはlease競合を拡大しないためである。
  - Domain/Application全体80%と重要状態境界95%を数値確認し、不正identity/counter/failure code、Worker取消/lease喪失/hosted lifecycleのテストを追加した。
- 技術的に不要となったタスクと代替実装: なし。
- 後続への引継ぎ:
  - PR9では本PRのOpenAPIに従い、AdminだけにCache状態・manual cleanup導線を表示し、POSTごとにUUID keyを生成する。通信結果不明時は同じkeyを再送し、GETのRun状態を権威としてpollする。
  - Android側でFile名、Path、User名、Job入力、内部Errorを推測または表示せず、Serverが返す集計・許可済みfailure codeだけを使用する。
  - PR10でAndroid 13物理端末と実Serverを使い、Settingsからmanual cleanup、通信断/再接続、pending/running/completed/failed表示、TalkBack、200%文字を最終確認する。

### PR9: Settings・Backup・Cache UI

未完了。

### PR10: 全体Adaptive・Accessibility・実機E2E

未完了。

---

## 全体振り返り

> PR1〜PR10のMerge、全タスク完了、各Pull Request完了記録を確認した後だけ、steeringモード3-Bで実装完了日、全体の計画と実績の差分、主な設計変更、技術的な学び、プロセス上の改善点、次回への改善提案を記録する。
