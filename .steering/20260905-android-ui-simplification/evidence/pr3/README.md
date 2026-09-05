# PR 3 Favorites・Routing・横断UI検証記録

## 検証環境

- 実行日: 2026-09-05
- 実機: OPPO CPH2333 / Android API 33 / 360dp幅
- Emulator: Android API 33 / 360dp相当
- 接続: ZeroTier経由の実Server
- 対象Build: `0.14.0-pr3-e2e1` / versionCode 21（release validation build）
- APK: Signature Scheme v3、non-debuggable相当。既存検証Installを維持するためAndroid Debug証明書で署名した検証専用Buildであり、本番配布署名ではない。

Token、Server address、SAF URI、端末の絶対Path、個人File名、個人画像は本記録へ残していない。

## 自動Test・静的検証

- `./scripts/ci/verify-android.sh`: 1,387タスク成功。Build、Unit Test、Coverage、ktlint、detekt、Lint、APK生成を含む。
- 対象Unit Test: 334件成功。Destination判定、同種Media context順序、process loss、認証・Server変更時のclearを含む。
- API 33 Emulator Connected Test: `app` 9件、`feature-search` 14件、`feature-media` 20件成功。
- API 33物理端末 Connected Test: `feature-search` 14件、`feature-media` 20件成功。
- 最初の物理端末`feature-media`実行はUSB切断で16/20時点に中断したが、再接続後の全20件再実行は0失敗で成功した。
- `git diff --check`: 成功。

## Favorites・Destination・Context

- Favoritesの写真2件で実Thumbnailを表示し、非Thumbnail種別と取得失敗は種類別Fallback iconを使用することをCompose Testで確認した。
- 行本体と独立した詳細overflowをSemantics上で確認した。行本体からFile detailsを経由せず写真Viewerへ直接遷移した。
- Favoritesから開いた写真は`1 / 2`から`2 / 2`へ一覧順に移動し、先頭・末尾でPrevious/Nextの有効状態が切り替わった。
- FilesのPDF ThumbnailからPDF用確認を経てViewerを開き、実PDFの1ページ目を描画した。
- Photo、Video、Audio、PDF、Text、Folder、未知/MissingのDestinationは共通ResolverのUnit Testで網羅した。Video/Audioは同種だけを抽出した順序付きContextを使用する。
- Sharedは実Serverで画面更新と権限別summaryを確認し、Read only/Editor境界と各Destinationは既存`feature-files`回帰Testおよび共通Resolver Testで確認した。
- Contextは認証成功、Logout、Server置換、認証失効でclearされ、process loss時は単一Entryへ安全にFallbackする。

## 実Server操作と状態復元

- Favoriteを解除し、Semanticsが`Add to favorites`へ変化した後に再追加し、`Remove from favorites`へ戻ることを確認した。
- 一時Tagを作成し、写真へ追加・解除した。検証後に一時Tagを削除し、Tagなしの初期状態へ戻した。
- Lowを表示後、MediumとOriginalを選択し、各request開始とOriginal size取得を確認した。
- `Download original`からSAFへ14,666,750 bytesを保存し、画面上の元サイズ14.7 MBと整合することを確認した。検証Fileは確認後に削除し、検証用保存先を空へ戻した。
- Upload、Folder作成、Rename、Move、Share、Trash、Restore、Missing/RecoveryはPR 1のCompose/Navigation回帰、PR 2の実機・実Server確認、および今回の全Android検証で回帰がないことを確認した。PR 3はこれらのRepository/API契約を変更していない。

## 検証データのCleanup

- PR 3ではTest用User、Server上のFile、Folderを作成していない。
- 一時Tagは写真から解除後に削除し、Favoriteは元の登録状態へ復元した。
- SAFへ保存した検証Fileと、端末・Localの`kurastorage-pr3-*`一時Captureを削除した。
- 検証前から存在したUser、File、Folder、保存先Folderには変更を加えていない。

## Responsive・Accessibility

- 物理端末のPortrait/Dark/360dpでFavorites、Files、PDF Viewer、Photo Viewer、Tag sheet、SAFを確認し、System bar、Top/Bottom navigation、Viewer、Bottom sheetの重大な重なりや到達不能はなかった。
- FavoritesではBack/Refresh、Thumbnail、行本体、詳細overflowを順に取得でき、Icon操作はcontent descriptionを持つ。実機Semantics上の主要Icon buttonは48dp以上だった。
- API 33のCompose fixtureで360dp、文字100%/200%、Portrait/Landscape、Light/Darkを確認した。Favorites固有表示は320dp幅のCompose TestでもThumbnail、行本体、詳細overflow、Missing fallbackを操作した。
- 端末メーカー制限によりADBからfont scaleと画面回転の変更は`WRITE_SETTINGS`で拒否されたため、200%文字、Landscape、Lightは同じAPI 33系のCompose fixtureとEmulator Connected Testで補完した。実機設定は変更されていない。
- 生のenum値を利用者向けLabelへ置換し、Missingでも詳細overflowを到達可能に維持した。

## Screenshot比較

変更前基準は`docs/ui/android/mockups/home-navigation/010-my-files.png`、`012-shared-files.png`、PR 1/PR 2の比較結果とした。変更後は同じ実ServerデータでFavorites、Files、Photo Viewer、PDF Viewerを実機Captureし、次を目視比較した。

- Favoritesへ実Thumbnailが追加され、File名だけの一覧より内容を識別しやすくなった。
- 一覧全体を遷移対象にしながら詳細overflowを独立させ、操作の競合を避けた。
- 360dpの初期表示でFavoritesを2件確認でき、Top app barと行操作に重なりがない。
- 写真ViewerはFavoritesの件数だけを示し、一覧順のPrevious/Nextを維持した。
- Back/Refreshを共通Icon配置へ揃え、文字Button由来の幅崩れを避けた。

実データCaptureは個人画像とFile名を含むためPull Requestへ添付せず、この比較結果だけを記録した。
