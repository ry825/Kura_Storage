# Android写真・PDFビューアー／動画・音声プレイヤー 要求仕様書

## 1. 概要

Androidアプリへ一覧サムネイル、写真Viewer、PDF Viewer、動画・音声Player、接続環境別の品質・通信量設定を追加する。既存Serverが生成する写真・動画の低／中品質派生データ、一覧Thumbnail、永続Media Job、完成済みMP4、認証・認可付きRange配信を利用し、選択していない元画質の通信を発生させない。

## 2. 背景

現在のAndroidアプリはFile一覧、詳細、Download等を提供するが、写真、PDF、動画、音声をアプリ内で閲覧・再生できない。元ファイルを毎回Downloadすると、外部Wi-FiやMobile通信で待ち時間と通信量が大きくなる。

Server側では、写真・動画・PDFのThumbnail、写真Low／Medium、動画Low／Medium、生成Job状態、完成済み派生データのRange配信が利用可能になる。本作業では、そのServer機能をAndroidの閲覧体験へ安全に接続し、利用者が通信環境にかかわらず品質を選び、元画質の通信量を確認してから取得できるようにする。

## 3. 前提条件

- `.steering/20260829-thumbnail-derivative-worker-infrastructure/`の全Pull Requestが`main`へMerge済みである。
- Serverは認証・認可後に、次のContent Variantを提供する。
  - `thumbnail`
  - `image-low`
  - `image-medium`
  - `video-low`
  - `video-medium`
  - `original`
- Serverは未生成または生成中の派生データに`202 Accepted`、Media Job ID、状態URL、再試行待機秒を返す。
- Serverは完成・検証済みの写真派生WebPと動画派生MP4だけを公開する。
- Serverは元ファイルと完成済み派生データの単一Range Requestを処理する。
- Androidは既存の接続判定、TLS検証、認証Token更新、Network binding、File権限を再利用する。

## 4. 要求上の決定

### 4.1 品質選択の対象

- 低画質／中画質／元画質の選択対象は写真と動画とする。
- 音声は既存Serverに音声Low／Medium派生契約がないため、初回実装では元ファイルだけをRange再生する。
- 音声Playerにも元ファイルSizeまたは推定通信量を表示し、利用者の確認後に再生を開始する。
- 将来、音声Low／Mediumを追加する場合は、Serverの派生形式、Codec、Bitrate、Media Job、Cacheを別の要求・設計で定義してからAndroidへ追加する。

この決定は、`docs/product-requirements.md` 7.6.3の「動画・音声プレイヤー」に含まれる品質選択表現と、写真・動画だけを品質派生対象とする既存Server契約の曖昧さを解消する。要求承認後、関連する正式文書を同じ方針へ統一する。

### 4.2 元画質とPDFの通信確認

- 写真または動画で元画質を選択した場合、Content取得前に元ファイルSizeまたは推定通信量を表示し、利用者の明示確認を得る。
- 音声再生では元ファイルだけを利用するため、初回再生前に元ファイルSizeまたは推定通信量を表示し、利用者の明示確認を得る。
- PDF本文は元PDF全体をApp private一時領域へ取得するため、初回表示前にSizeまたは推定通信量を表示し、利用者の明示確認を得る。
- 一覧Thumbnailは小容量の一覧表示用派生データとして確認対象外とする。
- 確認前に元写真、元動画、元音声、PDF本文を先読みしない。

## 5. 利用者と利用条件

### 5.1 対象利用者

- 自分が所有するFileを閲覧できる認証済み利用者。
- 直接共有またはFolder共有の継承により`VIEWER`以上を持つ認証済み利用者。

### 5.2 認可条件

- Viewer／Playerを開く時点とContent／Media Jobを再取得する時点で、現在のFile閲覧権限が必要である。
- `ADMIN` Roleだけを理由に他UserのFileを閲覧できない。
- 権限消失、Logout、Device失効、Session失効、接続先変更後は表示・再生・Pollingを停止する。
- 他Userまたは以前の接続先のThumbnail、PDF一時File、Media Job状態、再生URLを再利用しない。

## 6. 実装対象の機能

### 6.1 一覧サムネイル

- File一覧のGrid表示で、写真、動画、PDFの一覧用Thumbnailを表示する。
- List表示でThumbnailを表示するか種類Iconを表示するかは承認済み設計に従う。
- Folder、Thumbnail非対象File、生成中、取得失敗、非対応形式、`MISSING`を区別する。
- Thumbnail取得では`thumbnail` Variantだけを要求し、元ファイルを代替取得しない。
- 未生成または生成中の場合はPlaceholderを表示し、Server指定の待機秒に従って再取得する。
- 画面外へScrollしたRequestを取消し、同一Thumbnailの重複要求と無制限並列取得を防ぐ。
- Rename／Moveだけでは同じSource VersionのThumbnailを不要に再取得しない。
- 元内容のVersion変更後は旧Thumbnailを表示しない。

### 6.2 写真Viewer

- 次のMIMEを初回保証対象とする。
  - `image/jpeg`
  - `image/png`
  - `image/webp`
  - `image/gif`
  - `image/bmp`
  - `image/heic`
  - `image/heif`
- 画面へのFit、Pinch zoom、Pan、拡大状態からの復帰を提供する。
- 同じ閲覧Context内で前後の閲覧可能な写真へ移動できる。
- 低画質、中画質、元画質を接続環境に関係なく選択できる。
- 低／中品質では選択した写真派生データだけを取得する。
- 未生成の場合は生成中状態を表示し、完了後に選択品質を自動再取得する。
- 生成失敗時はRetry可能性に応じたErrorと操作を表示する。
- 元画質はSizeまたは推定通信量の確認後にだけ取得する。
- 品質変更中に前のRequestが完了しても、現在選択していない品質で画面を上書きしない。
- 低／中品質の失敗時に元画質へ自動Fallbackしない。
- ViewerからFile詳細と品質指定Downloadへ移動できる。

### 6.3 PDF Viewer

- `application/pdf`を初回保証対象とする。
- PDFをアプリ内で表示できる。
- 前後Page移動とPage指定ができる。
- 現在Pageと総Page数を表示する。
- Pinch zoom、Pan、画面Fitを提供する。
- PDF本文の取得前にSizeまたは推定通信量を表示し、利用者の明示確認を得る。
- PDFはApp private一時領域へStreaming取得し、全体をMemoryへ保持しない。
- PDFの許容Size、一時領域の必要空き容量、一時File保持期間は設計で具体化し、上限超過または容量不足を取得前に操作可能なErrorとして表示する。
- 取消、途中切断、破損、暗号化、Page描画失敗時にCrashせず、部分FileやPage resourceを解放する。
- Logout、接続先変更、期限切れ後に一時PDFを限定削除する。
- PDF Viewerから元ファイルDownloadへ移動できる。
- 一覧用PDF ThumbnailをPDF本文表示の代替には使用しない。

### 6.4 動画Player

- 次の動画MIMEを初回保証対象とする。
  - `video/mp4`
  - `video/webm`
  - `video/3gpp`
- Container MIMEが対象でも、端末MediaCodecが内部Codecを再生できない場合は非対応形式として表示する。
- 再生、一時停止、Seek、3秒戻る／進む、10秒戻る／進むを提供する。
- 再生速度を0.5〜3.0倍の範囲で変更できる。
- 現在時間と総時間を表示する。
- 低画質、中画質、元画質を接続環境に関係なく選択できる。
- 低／中品質では、完成・検証済みの選択品質MP4だけをRange再生する。
- File全体のDownload完了を待たず、Range配信から再生を開始する。
- 元画質はSizeまたは推定通信量の確認後にだけRange再生する。
- Seek時に必要な範囲を再要求し、File全体をMemoryへ読み込まない。
- 品質変更時は現在位置と再生／一時停止状態を保持し、新品質がReadyになった後に可能な範囲で同じ位置から再開する。
- Mobile通信では先読み量を抑え、次動画を自動再生しない。
- 低／中品質の失敗時に元画質へ自動Fallbackしない。

### 6.5 動画変換Job状態

- 低／中品質が未生成の場合はServerの永続Media Jobを利用する。
- Queue待ち、変換中、進捗あり、進捗算出不能、Retry待ち、完了、失敗を区別して表示する。
- Queue位置が返る場合は表示し、省略された場合は未知値を推測しない。
- 利用者は変換中に次を選択できる。
  - 完了まで待つ。
  - バックグラウンドで生成を続けて画面を離れる。
  - Sizeまたは推定通信量を確認して元画質で再生する。
- 「完了まで待つ」場合はServer指定の待機秒に従って状態を再取得し、Ready後に選択品質を自動取得する。
- 「バックグラウンドで続ける」場合、Androidの画面離脱またはProcess終了をServer Jobの取消へ伝播しない。
- 同じFileを後から開いた場合、現在のFile Versionと権限に対する状態をServerから再取得する。
- Retry可能な失敗だけ利用者の明示操作で再試行する。
- Retry操作の連打で複数Jobを作成しない。
- 変換途中または検証前のMP4をPlayerへ渡さない。

### 6.6 音声Player

- 次の音声MIMEを初回保証対象とする。
  - `audio/mpeg`
  - `audio/mp4`
  - `audio/aac`
  - `audio/ogg`
  - `audio/opus`
  - `audio/flac`
  - `audio/wav`
  - `audio/3gpp`
  - `audio/amr`
  - `audio/amr-wb`
- 端末MediaCodecが内部Codecを再生できない場合は非対応形式として表示する。
- 元ファイルのSizeまたは推定通信量を確認後、Range再生を開始する。
- 再生、一時停止、Seek、3秒戻る／進む、10秒戻る／進むを提供する。
- 再生速度を0.5〜3.0倍の範囲で変更できる。
- 現在時間と総時間を表示する。
- 初回実装では低／中品質選択と音声変換Jobを表示しない。
- 画面回転、AppのBackground／Foreground、Audio focus、Headset切断、通話割込みを承認済み設計に従って処理する。

### 6.7 品質・通信量設定

- 接続環境別の初期品質を次の既定値とする。

| 接続環境 | 初期品質 |
| --- | --- |
| ローカル直接接続 | 元画質 |
| 登録済み外部Wi-Fi＋ZeroTier | 中画質 |
| 未登録Wi-Fi＋ZeroTier | 低画質 |
| Mobile通信＋ZeroTier | 低画質 |

- 利用者は接続環境ごとの初期品質を変更できる。
- 設定はViewer／Playerを開いた時点の初期値だけを決定する。
- 接続環境に関係なく、写真・動画の低／中／元品質を手動選択できる。
- 接続環境変更だけを理由に閲覧中の品質を自動変更しない。
- 設定値が未知または破損している場合は、安全な既定値へ戻す。
- 設定画面には各品質の意味と、実通信量がFileや形式により異なることを表示する。

### 6.8 共通再接続・Error処理

- 通信切断時は、現在の接続状態と再接続操作を表示する。
- 一時的な通信失敗の再試行は上限と待機を持ち、無限Loopにしない。
- `401`後のToken更新は既存の単一Flight処理を利用する。
- Token更新不能、Device失効、Session失効時は再生・表示を停止して再Login導線へ移動する。
- `403`、`404`、権限消失、File消失、`MISSING`、Trash、Version変更を、古いCacheを表示することで隠さない。
- 不正Range、短いResponse、Content-Length不一致、Decode失敗、Codec非対応を区別可能なErrorへ変換する。
- 未知のServer Statusは`UNKNOWN`として扱い、元画質取得、Retry等の追加通信を自動実行しない。

### 6.9 Navigationと画面状態

- File一覧、検索、最近使用、お気に入り、Tag、共有一覧、File詳細から対象Viewer／Playerを開ける。
- MIMEとFile状態に応じて写真、PDF、動画、音声、非対応表示へRoutingする。
- Viewer／PlayerからFile詳細またはDownloadへ移動できる。
- Back操作、画面回転、AndroidによるActivity再生成後も、現在File、Page、Zoom、再生位置、速度、品質を設計で許可した範囲で復元する。
- Logout、認証Session変更、接続先変更ではBack stackとMedia状態を破棄する。

### 6.10 Accessibilityと表示

- TalkBackでFile種別、品質、再生状態、現在時間、Page、生成状態、Error、主要操作を判別できる。
- Touch target、文字拡大、Dark theme、縦横画面で主要操作を利用できる。
- 色だけでLoading、Generating、Error、選択品質を区別しない。
- Byte size、通信量、時間、速度、Page数を利用者向け形式で表示する。
- Network ID、Managed IP、Node Identity、Access Token、物理Path、内部Process出力を表示しない。

## 7. 非機能要求

### 7.1 性能

- 1,000件以下のFolder一覧を通常2秒以内に段階表示する既存目標を維持する。
- Cache済み写真は、基準環境で通常2秒以内に表示を開始する。
- Cache済み動画・音声は、基準環境で通常3秒以内に再生を開始する。
- 未生成写真・動画は、通常1秒以内に生成中またはJob状態を表示する。
- 動画変換完了時間には固定SLAを設定しない。
- Thumbnail、写真、PDF PageのDecode／RenderとMedia読み込みでMain threadをBlockしない。
- 1,000件一覧、巨大写真、長大PDF、長時間MediaでOOM、ANR、FileDescriptor leakを発生させない。

### 7.2 通信量

- 一覧Thumbnail取得時に元ファイルを取得しない。
- 写真・動画は現在選択している品質だけを取得する。
- 低／中品質が未準備でも元画質を自動取得しない。
- 元写真、元動画、元音声、PDF本文は利用者の確認前にprefetchしない。
- Mobile通信では動画の先読みを承認済み上限へ抑える。
- 実際に受信したByte数と画面表示した推定通信量を実機検証で比較し、差を記録する。

### 7.3 SecurityとPrivacy

- Access TokenをURL、Query、File名、Log、Crash reportへ含めない。
- Media URLは現在の信頼済みKuraStorage Hostだけを許可し、Redirectで別Hostへ認証Headerを送信しない。
- PDF一時FileをApp private領域外へ保存せず、他Appへ無制限公開しない。
- File名を一時Fileの物理Pathとして使用しない。
- Thumbnail、PDF一時File、Player cache、Job状態をUser Sessionと接続先の境界を越えて再利用しない。
- 破損Media、巨大Dimension、異常Duration、悪意あるMIMEでCrash、無制限Memory確保、無限Retryを起こさない。

### 7.4 Reliability

- 画面離脱またはClient切断はServer Media Jobを取消さない。
- HTTP Request、Coroutine、Image request、Player、PDF Page、FileDescriptorは所有するLifecycleの終了時に解放する。
- 品質変更、前後移動、Page移動の競合では最後の利用者操作だけを画面へ反映する。
- File Version変更後に旧Thumbnail、旧派生データ、旧PDF、旧再生URLを表示・再生しない。

## 8. 受け入れ条件

### 8.1 一覧サムネイル

- [ ] 写真、動画、PDFの一覧ThumbnailがGridへ表示される。
- [ ] Thumbnail未生成時はPlaceholderから完成後のThumbnailへ更新される。
- [ ] Thumbnail取得中に元ファイルRequestが発生しない。
- [ ] 1,000件FolderのScrollでOOM、ANR、無制限並列Requestが発生しない。
- [ ] Version変更後に旧Thumbnailが表示されない。

### 8.2 写真Viewer

- [ ] 対象写真をFit、Pinch zoom、Panできる。
- [ ] 前後の閲覧可能な写真へ移動できる。
- [ ] 低／中／元を任意の接続環境で選択できる。
- [ ] 低／中では選択品質の派生画像だけを受信する。
- [ ] 生成中、完了、失敗、Retry状態を表示できる。
- [ ] 元画質のSizeまたは推定通信量を確認するまで元画像を取得しない。
- [ ] 低／中失敗時に元画質へ自動Fallbackしない。

### 8.3 PDF Viewer

- [ ] PDF本文のSizeまたは推定通信量を確認後に表示できる。
- [ ] 前後Page移動、Page指定、現在Page／総Page表示、Zoom、Panが動作する。
- [ ] PDF全体をMemoryへ読み込まずApp private一時Fileから表示する。
- [ ] 取消、途中切断、容量不足、破損、暗号化PDFでCrashせず、部分Fileを残さない。
- [ ] Logout、接続先変更、期限切れで一時PDFが削除される。
- [ ] PDF ViewerからDownloadへ移動できる。

### 8.4 動画Player

- [ ] 完成済み低／中MP4または明示選択した元動画をRange再生できる。
- [ ] 再生、一時停止、Seek、±3秒、±10秒、0.5〜3.0倍速が動作する。
- [ ] 現在時間と総時間を表示する。
- [ ] Queue待ち、変換中、進捗、完了、失敗、Retryを表示する。
- [ ] 完了待ち、バックグラウンド継続、確認後の元画質再生を選択できる。
- [ ] 変換途中のMP4を再生しない。
- [ ] 品質変更後に可能な範囲で再生位置を維持する。
- [ ] Mobile通信で次動画を自動再生しない。
- [ ] Codec非対応を無限Retryせず表示する。

### 8.5 音声Player

- [ ] 元音声のSizeまたは推定通信量を確認後にRange再生できる。
- [ ] 再生、一時停止、Seek、±3秒、±10秒、0.5〜3.0倍速が動作する。
- [ ] 現在時間と総時間を表示する。
- [ ] 音声Low／Mediumまたは音声変換Jobを誤って表示・要求しない。
- [ ] Codec非対応、通信切断、認証失効を操作可能なErrorとして表示する。

### 8.6 品質・通信量設定

- [ ] 4種類の接続環境へ既定品質が適用される。
- [ ] 利用者が接続環境別の初期品質を変更し、再起動後も保持できる。
- [ ] 接続環境が手動品質選択肢を制限しない。
- [ ] 元画質確認を取消した場合、Content Requestを開始しない。
- [ ] 接続先またはSession変更後に以前のFile固有品質・Job・URLを再利用しない。

### 8.7 回帰・品質

- [ ] File一覧、検索、最近使用、お気に入り、Tag、共有、Download、Trash、Restoreの既存導線が動作する。
- [ ] Android JVM Unit Test、MockWebServer Test、Compose Instrumented Testが成功する。
- [ ] 実ServerとAndroid 10以降の実端末で主要E2Eが成功する。
- [ ] `./scripts/ci/verify-android.sh`と関連する品質・Security検証が成功する。
- [ ] Token、物理Path、他User情報がUI、URL、Log、一時File名へ露出しない。

## 9. 成功指標

- 一覧Thumbnail表示で元ファイル通信が0件である。
- 低／中品質未準備時の意図しない元画質Requestが0件である。
- Cache済み写真が基準環境で通常2秒以内に表示を開始する。
- Cache済み動画・音声が基準環境で通常3秒以内に再生を開始する。
- 未生成写真・動画が通常1秒以内に生成状態を表示する。
- 1,000件Folderの一覧操作、巨大写真、長大PDF、長時間MediaでOOM、ANR、FileDescriptor leakが0件である。
- 品質変更後の再生位置が、新しいDuration内で利用可能な場合に維持される。
- 対象MIME、Codec非対応、通信切断、認証失効の各失敗がCrashまたは無限Retryにならない。

## 10. スコープ外

以下は本作業では実装しない。

- HLS、DASH、`.m3u8`、生成途中Segment配信。
- 音声Low／Medium派生生成、音声変換Job、音声波形表示。
- 次の動画または音声の自動再生。
- Picture-in-Picture、Cast、字幕編集、DRM対応。
- 写真編集、動画編集、PDF注釈、PDF編集、OCR、全文検索。
- Viewer用Mediaの共有Storageへの恒久保存。利用者が明示する既存Download機能は対象内とする。
- Web／iOS Viewer。
- Server上の元ファイルを派生データで置換する処理。
- Cache状態の管理者画面、自動Backup、Offline同期。

## 11. 参照ドキュメント

- `docs/product-requirements.md` 4.6、7.6.1〜7.6.3、7.11
- `docs/functional-design.md` 5.4、6.1.5、7.3〜7.5、8.5〜8.6、11.3、11.5〜11.6、11.10、14.2、18.2 Android Step 8
- `docs/architecture-design.md` 5.2、11.3〜11.4、14.2、15.2、21.3〜21.4
- `docs/repository-structure.md` 8.4〜8.6、9.2
- `docs/development-guidelines.md` 5.4、8.3、11
- `.steering/20260829-thumbnail-derivative-worker-infrastructure/requirements.md`
- `.steering/20260829-thumbnail-derivative-worker-infrastructure/design.md`
- `.steering/20260829-android-media-viewers-players/tasklist.md`
