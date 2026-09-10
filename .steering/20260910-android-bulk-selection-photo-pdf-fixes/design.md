# Android 一括選択・写真全画面・PDF表示修正 設計書

## アーキテクチャ概要

既存のAndroid Compose画面、ViewModel、`MediaRepository`、`TemporaryPdfStore`、`PdfDocumentController`の責務境界を維持して修正する。Server API、認可、削除状態遷移、写真の一覧順序、PDFの保存上限は変更しない。原因の確定前に新規依存やfallbackの恒久保存を加えず、失敗を最小fixtureで再現して該当層だけを変更する。

```text
Bulk selection screen ── responsive selection action component ── existing bulk-trash confirmation/use case

PhotoViewerScreen ── fullscreen presentation state ── PhotoViewerViewModel navigation state
        │                         │
        └─ gesture arbitration ── PhotoCanvas (zoom/pan or one-item navigation)

PDF route ── PdfViewerViewModel ── MediaRepository (HEAD / content)
                  │
                  └─ TemporaryPdfStore ── PdfDocumentController ── Android PdfRenderer
```

## 現行実装で確認した調査点

- `FileBrowserScreen`の選択中サマリーは横方向の`Row`に選択件数、Clear、Move to trashを同居させている。文字拡大、狭幅、inset時の幅不足を自動テストで再現し、アクション領域をresponsiveに組み替える。
- `PhotoViewerScreen`の全画面時は操作Rowが写真へ直接重なり、背景surface・scrim・safe drawing insetを持たない。`PhotoCanvas`には通常画面と同じ`onPrevious`/`onNext`が渡されるため、gesture処理が全画面stateを変更しないことを確認して修正する。
- PDFはすでにprivate cacheへのstreaming、`PdfRenderer`、失敗分類を持つ。実機/MockWebServerで、metadata検査、content download、temporary file、renderer open、renderの各境界を観測し、実際に失敗する境界を特定して最小限に修正する。`Accept-Ranges`の扱い、Content-Type parameter、read/close lifecycle、routeのdisposeを重点確認する。

## 主要設計決定

### 1. 一括選択は状態ではなく表示コンテナだけを適応化する

選択ID、bulk operation、confirmation、既存のViewModel actionは変更しない。選択件数とaction群を狭幅・文字拡大では複数行または縦並びへ安全に折り返し、広幅では既存どおりコンパクトに並べる。各actionは48dp以上のtouch target、明確なcontent description、progress中の二重実行防止を保つ。

対象画面を検索して同一選択action構造が複数あれば、`core-ui`の小さな再利用可能コンテナへ寄せる。Filesだけの構造であれば抽象化せず、`FileBrowserScreen`内を最小変更する。両方の場合とも、選択解除、確認Dialog、対象IDの送信、認可・状態検査は既存use caseへ委譲する。

### 2. 写真の全画面は独立presentationを保ち、gestureと状態遷移を分離する

`fullscreen`は選択中Photoの表示presentation stateであり、前後写真へ遷移しても保持する。PhotoのID、file version、表示source変更だけが別Photoの状態を初期化し、Swipe自体は`fullscreen = false`を変更しない。

全画面用のbottom action containerは、下端のsafe drawing insetを取り込む半透明のsurface/scrimに置く。写真内容から独立したcontrastとelevationを持たせ、横幅不足時はFlowRowまたは2行layoutへ折り返す。Backだけは全画面を解除し、Photo Viewer routeからは離脱しない。

PhotoCanvasのgesture arbitrationは、zoomが等倍でpanを扱っていない場合だけ、閾値を超えた水平gestureを一度だけ前/次のcallbackに変換する。zoom中のpan、垂直gesture、edge、loading/error、前後項目なしではnavigationを発火させない。遷移中は次のSwipeを抑止し、ViewModelの既存generation/request ticketで古い画像・prefetch結果を破棄する。

### 3. PDFの既存安全契約を保ったまま失敗境界を修正する

PDF openは次の状態遷移を維持する。

```text
LOADING_METADATA → CONFIRMING → DOWNLOADING → RENDERING → READY
                       │             │              │
                       └─────────────┴──────────────┴→ FAILED(typed failure)
```

- metadataでは`application/pdf`をparameterを除いて正規化し、0 byte、256MiB超過、必要なRange要件を既存契約に従って検証する。
- confirm後は`MediaRepository.openContent`の結果を64KiB bufferでscope別private temporary fileへstreamingし、Content-Length、PDF signature、空き容量、Session合計512MiBを検証してからだけ`PdfRenderer`へ渡す。
- download/open/renderの例外をauthentication、permission、not found、too large、storage、incomplete、corrupt、password protected、render unsupported、networkへ変換する。retry可能な失敗は`Retry open`を第一actionとし、`Save a copy`は独立した補助actionのままにする。
- Page、Bitmap、ParcelFileDescriptor、PdfFileLeaseは再試行、画面離脱、ViewModel clear、Session/logoutで確実にclose/releaseする。取消・失敗したpartial fileは削除する。既存のSession外File拒否とTTL cleanupを緩めない。

実装は観測した原因に限定する。例えばServerが`Accept-Ranges`を返すべき契約でないと分かった場合は、Client側で安全に扱える範囲に正規化するのではなく、正式仕様とAPI契約の矛盾として報告・文書修正を先行する。

### 4. テストfixtureはexact manifestで管理し、最後にだけ清掃する

作業開始前にread-onlyで既存データのID/件数/checksumを記録する。今回作成した資材だけを、作成直後にrun ID付きworkspace外manifestへexact ID/pathとして追加する。清掃はmanifestの各entryを再読込してidentityを照合した後に個別削除し、wildcard、名前部分一致、親Folder再帰、全件削除を禁止する。追加資材がない場合は「作成なし」を記録し、既存データへ清掃操作を行わない。

## コンポーネント設計

### 1. Bulk selection action presentation

**対象候補**:

- `apps/android/feature-files/.../FileBrowserScreen.kt`
- 必要な場合のみ `apps/android/core-ui/.../components/`
- 対応するCompose/Instrumented tests

**実装の要点**:

- `selectedForTrashIds`、`bulkTrashInProgress`、clear、confirmを入力として受ける表示だけを変更する。
- `BoxWithConstraints`、`FlowRow`、またはColumn/Row切替で、360dp・landscape・font scale 2.0でもactionが表示領域内に収まるようにする。
- destructive actionは視覚的に区別するが、danger colorだけに意味を依存しない。進行中は対象actionをdisableし、countと進行状態を読み上げ可能にする。

### 2. Photo full-screen controls and navigation

**対象候補**:

- `apps/android/feature-media/.../photo/PhotoViewerScreen.kt`
- PhotoCanvasとPhoto ViewerのUnit/Compose/Instrumented tests

**実装の要点**:

- 全画面専用controlsを通常画面のcontrolsから分離し、`navigationBarsPadding`/safe drawing inset、surface、最低48dp touch targetを適用する。
- 全画面stateのremember keyはPhoto遷移で不必要にfalseへ戻らないよう確認する。表示source更新によるstate resetが必要なら、presentation stateとimage transform stateのkeyを分離する。
- Swipeの開始条件と消費条件を明文化し、1 gestureにつき1 callback、callback発火後はgesture終了までlockする。
- Photo ViewModelのnavigation順序、canGoPrevious/canGoNext、request generation、prefetchを再利用し、route popやfullscreen解除を前後移動の副作用にしない。

### 3. PDF opening and rendering diagnostics

**対象候補**:

- `apps/android/core-data/.../media/TemporaryPdfStore.kt`
- `apps/android/feature-media/.../pdf/PdfViewerViewModel.kt`
- `apps/android/feature-media/.../pdf/PdfDocumentController.kt`
- `apps/android/feature-media/.../pdf/PdfViewerScreen.kt`
- App PDF routeとUnit/MockWebServer/Instrumented tests

**実装の要点**:

- 検査・download・renderer open・page renderをtestableな境界として維持し、failure mappingで情報を失わない。
- `PdfDocumentController.open`のpartial-open時はdescriptor、renderer、leaseを例外時にも逆順に閉じる。
- ViewModelはretry前に旧render/load jobと旧documentを閉じ、旧結果が新しいretry stateを上書きしないようgenerationまたは同値確認を適用する。
- UIは`CONFIRMING`でだけ通信量確認を表示し、正常PDFの`Open PDF`はSAF callbackへ進まずviewer openを開始する。失敗画面ではtyped failureとretryを可視化する。

## データフロー

### 一括選択してTrashへ移動

1. 利用者が既存のselection checkboxで対象Entry IDを選ぶ。
2. responsive summaryが選択件数、Clear、Move to trashを画面幅に応じて配置する。
3. 利用者がMove to trashを選び、既存confirm dialogで対象件数を確認する。
4. 既存ViewModel/use caseが選択済みexact ID、現在権限、状態を検証してtrash操作を実行する。
5. 結果を既存stateへ反映し、selectionを安全に更新する。

### 全画面写真をSwipeする

1. 利用者がPhoto Viewerで全画面を開始する。
2. 全画面layoutが写真canvasとcontrastを確保したbottom controlsを描画する。
3. 等倍で水平Swipeが閾値を超えた場合、gesture lockを取り`onPrevious`または`onNext`を1回だけ呼ぶ。
4. ViewModelが隣接photoのIDを選択し、既存request generationで画像を更新する。
5. `fullscreen` presentation stateを維持したまま新しい写真を表示する。

### PDFをアプリ内で開く

1. PDF routeが詳細とHEAD metadataを取得し、MIME/size/Range/上限を検査する。
2. 利用者が通信量確認の`Open PDF`を承認する。
3. `TemporaryPdfStore`がscope内private fileへstreamingし、Size/signature/capacityを検証する。
4. `PdfDocumentController`がlease付きFileDescriptorから`PdfRenderer`を開き、現在Pageをbackground renderする。
5. UIがpage/zoom/panを提供する。失敗時はtyped failureと`Retry open`を提示し、画面離脱等でresourceを解放する。

## エラーハンドリング戦略

- 一括選択: layout都合でactionを不可視・重畳にしない。既存の操作失敗表示、認可、confirmationを維持する。
- 写真Swipe: navigation不能時はno-opとし、写真を閉じたり全画面を解除したりしない。画像取得失敗は既存error stateを表示する。
- PDF: 安全検証を通らないFileをrendererへ渡さない。失敗理由をtyped stateで保持し、retryでpartial file、旧job、旧documentを残さない。
- cleanup: manifestにない、identity照合に失敗した、保護baselineと一致する対象は削除せずfail-closedで記録する。

## テスト戦略

### Unit・JVM test

- 選択actionの表示条件とbulk operation中のdisableを固定する。
- Photoの等倍Swipe、zoom pan、先頭/末尾、連続gesture、full screen presentation stateを固定する。
- PDF metadata正規化、failure mapping、retryの旧resource close、partial download cleanup、Size/signature/capacity/Session境界を固定する。

### Compose・Instrumented test

- 360dp、landscape、font scale 2.0、system insetで一括選択アクションのsemanticsと可視領域を確認する。
- 写真全画面のbottom controls、contrast surface、48dp target、Back、Swipe後も`photo-fullscreen`が存在することを確認する。
- `PdfRenderer`を使う正常PDF、retry、Activity lifecycleでPDF viewport、page、zoom、resource lifecycleを確認する。

### MockWebServer・実機確認

- PDFのHEAD/GETについてContent-Type parameter、Content-Length、Range、有効PDF、途中切断、401/403/404を再現する。
- Android実機で報告された一括選択、写真全画面Swipe、PDF openを確認する。実機/Serverが利用できない場合は、実行不能な理由と自動テスト証跡をtasklistへ記録し、成功扱いにしない。

### 迅速な実行順序

各修正では失敗テスト→該当moduleのtest/lint→該当Android testの順に実行し、全体buildを繰り返さない。最終段階で変更対象に適用されるformat、lint、build、testを一度実行する。並列実行可能で出力・キャッシュ競合のないread-only検査は並行化するが、Gradleの同一出力を競合させない。

## 依存ライブラリ

新規依存は追加しない。既存のCompose Material 3、Kotlin Coroutines、OkHttp、Coil、Android `PdfRenderer`を使用する。

## ディレクトリ構造

```text
apps/android/
├─ core-data/src/{main,test}/.../media/TemporaryPdfStore.kt
├─ core-ui/src/main/.../components/             # 共通化が必要な場合だけ
├─ feature-files/src/{main,androidTest}/.../FileBrowserScreen.kt
└─ feature-media/src/{main,test,androidTest}/.../
   ├─ photo/PhotoViewerScreen.kt
   └─ pdf/{PdfViewerScreen,PdfViewerViewModel,PdfDocumentController}.kt

.steering/20260910-android-bulk-selection-photo-pdf-fixes/
├─ requirements.md
├─ design.md
└─ tasklist.md
```

## 実装の順序

1. 既存変更と先行PRの状態を確認し、最新`main`を基点とした1本の作業Branchを用意する。
2. 最小fixtureとbaseline manifestで3不具合を再現し、PDF失敗境界を確定する。
3. 一括選択actionのresponsive layoutとテストを実装する。
4. 写真全画面controlsとSwipe state/gestureの修正およびテストを実装する。
5. 確定したPDF失敗境界の修正、typed failure/resource lifecycle test、Viewer UI確認を実装する。
6. 対象moduleから段階的に検証し、最後に必要な標準検証と実機確認を行う。
7. manifest対象のみ清掃し、baselineが不変であることを確認する。
8. self-review、Commit、Push、英語Pull Requestを1本作成し、PR完了記録と全体振り返りをtasklistへ追加する。

## セキュリティ・性能・互換性

- PDF/写真は既存の認証Session、TLS host検証、接続経路別clientを使用し、Tokenやphysical pathをUI、log、manifestへ出さない。
- 画像SwipeのためにOriginalを余計に取得せず、既存prefetchとrequest cancellationを利用する。
- PDFは全体をmemoryへ読まず、64KiB streamingと1 page/最大32MiB bitmapを維持する。
- 既存の削除・認可・status state、PDF容量上限、Session cleanup、写真のquality/source契約は後方互換に保つ。DB migrationと新規APIは不要とする。

## Pull Request運用

- 今回の全フェーズを1本のPull Request単位とする。中間Pull Requestは作成しない。
- 全実装、関連自動テスト、必要な実機確認、manifest清掃、self-review、tasklistの完了更新が済むまでPull Requestを作成しない。
- PRのタイトル・本文は英語とし、目的、対象タスク、変更内容、テスト結果、影響・未実施事項を記載する。
- PR作成後はSteeringモード3で同じtasklistに完了記録と全体振り返りを追加し、同じbranchへ反映して停止する。
