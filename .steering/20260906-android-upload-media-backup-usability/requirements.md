# Androidアップロード・メディア・バックアップ操作性改善 要求内容

## 概要

Androidアプリのアップロード、File browser、写真・動画・PDF Viewer、Search Navigation、Trusted Wi-Fi、Settings、自動Backup、Thumbnail生成状況に関する不具合と操作性をまとめて改善する。

今回の実装、検証、正式文書更新、テストデータ清掃は1本のPull Requestにまとめる。検証は変更箇所に近い自動Testから実行し、必要な統合Test・実機確認へ段階的に広げる。

## 背景

現在のAndroidアプリでは、次の問題が確認されている。

- Upload完了後も`Transfer status`が表示され続け、現在処理中の転送があるように見える。
- Upload pickerでFileを1件しか選択できず、Folder単位または複数FileをまとめてUploadできない。
- File browserのパンくずが現在Pathの表示だけで、上位Folderへ直接移動できない。
- 写真を全画面表示できず、表示時に縦横比または元画像を超えて引き伸ばされて見える。
- Search画面からBottom navigationのHomeを押してもHomeへ戻らない。
- Thumbnail生成の待機数・処理中数が分からず、処理の進行状況を判断できない。
- Thumbnail生成が直列処理のため、対象が多いと表示可能になるまで時間がかかる。
- 動画のLow・Medium品質生成がServer負荷と待ち時間を増やしている。
- モバイル通信で大容量の元動画を再生するとき、通信量を確認する導線が不足している。
- Trusted Wi-Fi設定で現在接続中のWi-Fiを検出して登録する操作が期待どおり動かない。
- Settingsの下位画面で背景色と文字色の組み合わせにより文字を読めない場合がある。
- 通常表示の動画Player操作が画面下部に偏り、再生中に操作しにくい。
- 動画再生がカクつき、視聴しにくい場合がある。
- 自動BackupのJobを1件ずつ実行するため、大量の対象があると完了まで時間がかかる。
- PDFを開けない場合がある。
- File一覧のHeaderが大きく、一覧に利用できる縦方向の領域が狭い。
- File一覧をscrollしてFileを開いた後、一覧へ戻ると先頭へ戻り、元のFileを探し直す必要がある。

前回のSteering作業で完了扱いとなっているSettings、Trusted Wi-Fi、PDF、Media Viewer関連についても、今回報告された現象を基準に再現確認し、残存原因または回帰を修正する。

## 正式仕様との整合が必要な変更

### 動画は元動画のみを再生する

動画のLow・Medium品質選択と動画派生生成を無効化し、動画ViewerはOriginalのみを再生する。写真の品質選択および写真派生生成は維持する。既存の動画派生データを今回の機能変更だけを理由に一括削除せず、新しい動画品質派生Jobを作成しない状態へ移行する。

Wi-Fiまたは有線接続では通常の確認フローでOriginalを再生する。モバイル通信ではOriginalのSizeを事前取得し、1 MiB以上の場合にSizeと大量通信の可能性をDialogで示し、利用者が`元動画を再生`を選択した場合だけ再生を開始する。Sizeを取得できない場合は安全側の確認を行い、確認前に動画本体を取得しない。

### Backup Jobの並列処理

自動Backupは順序と冪等性を維持しながら複数Fileを並列処理できるようにする。同一Fileの重複実行を防ぎ、設定可能な同時実行上限を設ける。手動Uploadと自動Backupは同一端末内の共通転送枠を共有し、利用者が開始した手動Uploadを優先する。Raspberry Pi、Network、端末の負荷を並列数1・2・4で測定し、改善が続き安全性に余裕があれば6・8も測定して、安定する最大の初期値を決める。失敗した1件が他の独立したFileの処理を停止させない。

### Thumbnail生成状況の可視化

Thumbnail Jobの状態をServer側で集計し、少なくとも待機中と生成中の件数をAndroidアプリから確認できるようにする。件数は権限境界を越えたFile情報を開示せず、Polling負荷を制限する。

### Thumbnail生成の並列処理

写真・動画・PDFのThumbnail生成は、Raspberry Piの資源上限内で複数Jobを並列実行できるようにする。Job claimとLeaseにより同一Jobの重複実行を防ぎ、設定可能な同時実行上限を設ける。並列数1・2・4を測定し、改善が続き安全性に余裕があれば6・8も測定する。動画Low・Medium派生生成は無効化するため、この並列枠へ含めない。

並列数はUploadまたはThumbnail単独の最短時間では決めず、両者と通常のFile一覧・Thumbnail取得・動画Range再生を同時に行う混合負荷で決める。CPU、メモリ、swap、I/O wait、温度、API応答、動画rebufferを監視し、Foreground操作用の余力を残す。上限到達時は新規処理をQueueで待機させ、無制限なTask・Process生成を行わない。

## 実装対象の機能

### 1. Transfer statusの完了後非表示

- Upload中、再試行中、失敗して利用者の操作が必要なTransferだけを表示対象とする。
- 全Transferが成功し、継続して知らせるべき失敗もない場合は`Transfer status`領域を自動的に消す。
- 完了直後の必要なFeedbackは短時間のSnackbar等で示し、常設領域として残さない。

### 2. 複数File Upload

- System file pickerから複数Fileを一度に選択できるようにする。
- 選択した各Fileを独立したUploadとしてQueueへ登録し、Fileごとの進捗、成功、失敗、再試行を扱う。
- 同名競合、選択取消、読取権限喪失、途中失敗が他のFileの結果を破壊しない。

### 3. Folder Upload

- System folder pickerからFolderを選択し、配下のFolder構造とFileを再帰的にUploadできるようにする。
- 空Folder、深い階層、同名項目、読取不能項目、途中取消を明示的に扱う。
- Path traversalや選択範囲外のDocumentを取り込まず、作成済みの親子関係を維持する。

### 4. パンくずNavigation

- Rootから現在Folderまでの各パンくず要素をLinkとして操作可能にする。
- 深い階層と長いFolder名でも途中要素を切り捨てず、全階層を折り返し表示で確認・操作できるようにする。
- 要素を押すと、そのFolder IDに基づいて対象階層へ直接移動する。
- 連打、読込中の別要素選択、権限失効、削除済みFolderでも架空Pathを作らない。

### 5. 写真Viewerと全画面表示

- 写真Viewerから全画面表示へ切り替え、再操作で通常表示へ戻せるようにする。
- 写真は通常表示・全画面表示・回転・zoom中を含むすべての状態で元の縦横比を絶対に変更せず、画面を埋めるための引き伸ばしやCropを行わない。
- 画面と写真の比率が異なる場合は余白を許容して`Fit`で収め、元Bitmapより不必要に拡大しない。
- Portrait、Landscape、回転、zoom、panで画像が歪まず、画面外操作やsystem UI復帰で状態が破綻しない。

### 6. SearchからHomeへのNavigation

- Search表示中にBottom navigationのHomeを1回押すとHomeへ移動する。
- Search query、結果一覧、Navigation back stackがHome表示を妨げない。
- 他のBottom navigation項目と再選択時の挙動も既存Navigation契約に合わせる。

### 7. Thumbnail生成状況

- Thumbnail Jobの待機中、生成中、必要に応じて失敗数を利用者が確認できる。
- 未解消の失敗数は案内しつつ、確認済みの失敗バナーは利用者が閉じられるようにする。失敗数が0に戻った場合は閉じた状態を解除する。
- Job完了や再試行に応じて件数を更新し、古い応答で新しい状態を上書きしない。
- 状況取得に失敗してもFile一覧やThumbnail表示そのものを利用不能にしない。

### 8. Thumbnail生成の制御付き並列化

- 独立したThumbnail Jobを設定上限内で並列生成する。
- 同一Jobまたは同一File/Version/Thumbnail種別の重複生成を防ぐ。
- 1件の失敗が別の独立Jobを停止させず、失敗Jobだけを既存retry契約で再実行できるようにする。
- Worker再起動、Lease失効、元Fileの変更・削除でも、部分Fileや不正なREADYデータを公開しない。
- 並列数はCPU、メモリ、I/O、Job所要時間を実測し、安全な初期値を正式仕様へ記録する。

### 9. 動画品質選択の無効化とモバイル通信確認

- 動画Viewerから品質選択UIを外し、OriginalのみをRange再生する。
- 動画Low・Medium派生Jobを新規作成しない。
- モバイル通信で1 MiB以上の動画を再生するとき、`この動画は4.1 GBです。現在の接続はモバイル通信のため、大量のデータ通信が発生する可能性があります。`と同等のDialogを表示する。
- `キャンセル`では動画データを取得せず、`元動画を再生`でのみ再生する。
- Wi-Fiまたは有線接続では、このモバイル通信専用Dialogを表示せず再生を開始できる。

### 10. Settingsの視認性

- Settingsの各下位画面でTheme tokenを一貫して使用し、背景と文字を同色にしない。
- Light/Dark theme、Dialog、Dropdown、Text field、disabled/error状態で文字を判読できる。
- 360dp幅、Landscape、OS文字200%でも主要な説明と操作が欠落しない。

### 11. Trusted Wi-Fiの現在接続情報による登録

- 現在Wi-Fiに接続している場合、必要なAndroid権限とOS APIを使ってSSIDおよび利用可能なBSSIDを取得し、登録Formへ自動入力する。
- 権限未許可、位置情報Service無効、SSID取得不可、Wi-Fi未接続を区別して案内する。
- 利用者の明示操作で登録を確定し、検出しただけでは自動Backup許可へ追加しない。
- SSID/BSSIDをServer本人確認、User認証、TLS検証、`LOCAL_DIRECT`判定の代替にしない。

### 12. 動画Player操作Overlay

- 通常表示と全画面表示で同じ操作Overlayを使用する。
- 動画は通常表示・全画面表示・回転中を含むすべての状態で元の縦横比を絶対に変更せず、画面を埋めるための引き伸ばしやCropを行わない。画面と動画の比率が異なる場合は余白を許容して`Fit`で収める。
- 動画表示をtapすると、再生・一時停止、巻き戻し、早送り、seek、再生速度、時間、全画面切替を動画上へ半透明で表示する。
- 再度tapまたは一定時間の無操作でOverlayを消し、操作中は意図せず非表示にしない。
- TalkBack、Portrait、Landscapeで各操作を識別して利用できる。

### 13. 動画再生のカクつき改善

- カクつきの原因をNetwork、Range request、Buffer、Compose再構成、Player lifecycle、端末decode能力に分けて測定する。
- Original再生に適したBufferとCacheを、メモリ上限と通信種別を考慮して設定する。
- 不要なPlayer再作成、同じSourceの再読込、Main thread上の重い処理をなくす。
- 基準動画と対象端末でframe drop、再buffer回数、再生開始時間を修正前後で比較する。

### 14. 自動Backupの制御付き並列化

- 独立したFileを固定上限内で並列Uploadする。
- 同一File、同一Queue項目、同一Upload sessionの重複処理を防ぐ。
- Network切替、アプリ終了、WorkManager再実行、部分失敗後も正しく再開する。
- 同時実行数を設定またはBuild時の構成で制御でき、端末とServerに過負荷を与えない。
- 手動Uploadと自動Backupの合計同時数が共通転送上限を超えず、枠が競合する場合は手動Uploadを優先する。

### 15. PDF Viewer修正

- 正常な対応PDFを通信量確認後にアプリ内Viewerで開けるようにする。
- 認証、接続、HTTP、空き容量、取得中断、破損、暗号化、Renderの失敗を区別する。
- 一時FileのSize上限、Session分離、取消時清掃を維持する。
- 前回修正済みの経路を含め、実際に開けない入力と端末条件を再現Testへ固定する。

### 16. File一覧Headerの省スペース化

- File browserのTop app bar、Path、操作領域の高さと余白を縮め、File一覧の表示領域を広げる。
- Upload、検索、並べ替え等の主要操作を失わず、touch targetとsystem bar insetを維持する。
- 360dp幅、Landscape、OS文字200%で重なりや切れが発生しない。

### 17. File一覧のscroll位置復元

- File一覧からFile/Folderの詳細またはViewerを開き、Backで同じ一覧へ戻った場合、離脱前に見ていた位置とitem内offsetを復元する。
- 位置は表示indexだけでなく、先頭可視Fileの安定IDをanchorとしてFolder、並び順、Filterごとに保持する。
- 一覧内容が更新されてもanchorが存在すれば同じFileを基準に戻し、anchorが削除・非表示なら最も近い有効位置へ安全に補正する。
- 別Folder、別Sort、別Filterの位置を混同せず、明示Refreshだけを理由に不要な先頭移動を行わない。
- Process recreationでも保存可能な範囲で位置を復元し、大量一覧で全項目をmemoryへ保持しない。

### 18. 検証とテストデータの安全な清掃

- 不具合ごとに失敗を再現する自動Testまたは明確な手動検証手順を用意する。
- 変更箇所に近いUnit/UI Testを先に実行し、成功後に関連Module、Repository標準検証、必要な実機E2Eへ広げる。
- テスト用User、File、Folder、Upload、Backup、Thumbnail/Media Job、派生データ、一時Fileに作業固有prefixまたはmanifestを付ける。
- 清掃前にmanifestと実IDを照合し、今回作成した対象だけを個別に削除する。
- 作業開始前から存在するUser、File、Folder、DB row、物理File、派生データ、端末データを削除しない。
- 清掃後に今回の対象が残っていないことと既存データが維持されていることを確認する。

## 受け入れ条件

### UploadとTransfer status

- [x] Upload完了後、継続表示すべきTransferがなければ`Transfer status`が自動的に消える。
- [x] 複数Fileを一度に選択し、各Fileの結果を確認できる。
- [x] Folderを選択し、配下のFolder構造とFileを維持してUploadできる。
- [x] 複数選択またはFolder Uploadの一部失敗が、成功済みの別Fileを失敗扱いにしない。

### File browserとNavigation

- [x] パンくずの任意の上位Folderを1回押すと、そのFolderへ直接移動する。
- [x] 360dp幅とOS文字200%の深い階層でも、パンくずの全階層名と祖先Linkが表示・操作できる。
- [x] Search画面でHomeを1回押すとHomeへ戻る。
- [x] File browser Headerが縮小され、主要操作とaccessibilityを維持したまま一覧の表示行数または表示面積が増える。
- [x] scrollした一覧からFileを開いてBackで戻ると、離脱前のFile位置とoffsetが復元される。
- [x] Folder・Sort・Filterごとに位置が分離され、anchor Fileが消えた場合も有効な近傍位置へ復元される。

### 写真

- [x] 写真を全画面表示し、通常表示へ戻せる。
- [x] 縦長、横長、小さい画像、大きい画像の通常表示・全画面表示・回転・zoom中に元の縦横比が常に維持され、引き伸ばしやCropが発生せず、不必要に元画像以上へ拡大されない。

### Thumbnail

- [x] Thumbnail生成の待機中数と生成中数が実際のJob状態と一致して表示される。
- [x] 完了、失敗、再試行で件数が更新され、権限外のFile情報を開示しない。
- [x] 独立したThumbnail Jobが設定上限内で並列実行され、同一Jobの重複生成や部分File公開が起きない。
- [x] 直列baselineより待ち時間または総処理時間が改善し、Raspberry PiのCPU・メモリ・I/Oが設定した安全域内に収まる。

### 動画

- [x] 動画の品質選択UIが表示されず、新しいLow・Medium動画派生Jobが作成されない。
- [x] Wi-Fi接続時はOriginal動画を通常再生できる。
- [x] モバイル通信で1 MiB未満の動画は大容量警告なしで再生できる。
- [x] モバイル通信で1 MiB以上の動画はSizeを含む警告が出て、許可前に動画本体を取得しない。
- [x] 通常表示と全画面表示の両方で、tapにより動画上の操作Overlayを表示・非表示にできる。
- [x] 縦長・横長・正方形に近い動画の通常表示・全画面表示・回転中に元の縦横比が常に維持され、引き伸ばしやCropが発生しない。
- [x] 基準動画のframe drop、再buffer、または体感上の停止が修正前より改善し、測定結果を記録する。

### SettingsとTrusted Wi-Fi

- [x] Settingsの各下位画面をLight/Dark themeで判読できる。
- [x] 現在接続中のWi-Fi情報を検出して登録Formへ反映できる。
- [x] 権限不足、位置情報無効、取得不能、未接続の各状態で適切な案内が表示される。
- [x] 検出したWi-Fiは利用者が保存操作を行うまで登録済みにならない。

### Backup

- [x] 複数の独立したBackup対象を設定上限内で並列処理できる。
- [x] 同一Fileの重複Upload、データ破損、Queue取りこぼしが発生しない。
- [x] 大量Fixtureで直列処理より所要時間が改善し、Server/端末負荷が設定した安全域内に収まる。
- [x] Upload・Thumbnail・一覧取得・動画再生の混合負荷でもServerが過負荷にならず、Foreground操作の応答性を維持する。
- [x] 失敗、取消、Network切替、Process再起動後に未完了項目だけを安全に再開できる。

### PDF

- [x] 再現対象の正常なPDFをアプリ内で開ける。
- [x] 破損・暗号化・取得失敗時に原因別の案内が出て、部分Fileやresourceを残さない。

### 検証・清掃・Pull Request

- [x] 対象Unit/UI/Integration Test、関連Build・Lint、必要な実機E2Eが成功する。
- [x] 性能Testは再利用可能なFixtureと測定条件を固定し、修正前後を比較できる。
- [x] 今回追加したテストデータだけがmanifestまたはID照合により削除される。
- [x] 既存データが維持され、今回のテストデータが残っていないことを清掃後に確認する。
- [ ] 全変更、検証結果、正式文書更新、清掃記録を最後に1本の英語Pull Requestへまとめる。

## 成功指標

- 報告された18項目すべてについて、再現条件、修正内容、検証結果を1つの`tasklist.md`で追跡できる。
- 複数File・Folder Uploadと並列Backupが、データ整合性を維持しながら直列操作より短時間で完了する。
- 動画の不要な品質変換Jobが新規発生せず、モバイル通信時の大容量取得が利用者の明示許可なしに始まらない。
- 既存データを1件も削除せず、今回作成したテストデータだけを清掃できる。

## スコープ外

- 動画の新しいLow・Medium品質変換方式の開発
- Adaptive bitrate streamingの導入
- Backup同時実行数を無制限にすること
- Wi-Fi検出だけで自動的にTrusted Wi-Fiへ登録すること
- 既存の動画派生データを一括削除するMigrationまたは運用処理
- 今回報告されていない画面の全面的なUI redesign
- Pull Requestの分割およびPull RequestのMerge

## 参照ドキュメント

- `docs/product-requirements.md` - Media品質、Upload、Backup、Trusted Wi-Fiの要求
- `docs/functional-design.md` - Media Job、Viewer、Backup、接続判定の機能設計
- `docs/architecture-design.md` - Worker同時実行、Media配信、Android Network・Player構成
- `docs/repository-structure.md` - Android、Server、Testの配置規則
- `docs/development-guidelines.md` - Kotlin/C#、非同期処理、Media、Testの実装規約
- `.steering/20260905-android-viewer-navigation-ux-fixes/` - 前回のViewer、Settings、PDF、Navigation修正と検証記録
- `.steering/20260902-android-auto-backup/` - 自動BackupとTrusted Wi-Fiの既存設計・実装記録
- `.steering/20260829-thumbnail-derivative-worker-infrastructure/` - Thumbnail/派生Jobの既存設計・実装記録
