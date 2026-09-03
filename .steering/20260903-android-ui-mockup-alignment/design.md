# Android UIモックアップ整合 設計書

## アーキテクチャ概要

現在のCompose、ViewModel、Repository、App Navigationの境界を維持し、「全画面で使う視覚言語」を`core-ui`、「複数Featureの画面枠と遷移」を`app`、「画面固有の情報階層と操作」を各`feature-*`に配置する。モックアップはデザインの入力として使い、機能、文言、状態、セキュリティ境界は正式文書を優先する。

```text
docs/ui/android/mockups + docs/functional-design.md
                         |
                         v
core-ui
  theme / tokens / icons / components / state / accessibility
                         |
          +--------------+------------------+
          |                                 |
          v                                 v
app: AppScaffold / Navigation        feature-*: Screen / UiState mapping
          |                                 |
          +---------------+-----------------+
                          v
              既存ViewModel / Repository / API
```

UI刷新は表示層の変更を主とし、既存ComposableのCallbackとUiStateは原則維持する。ただし、正式画面に必要な取得・操作契約がない場合は、正式文書の更新とともにServer API、Application処理、永続状態、Android Repository/ViewModelを最小限追加する。現状では`036-cache-management.png`に対応する管理者用キャッシュ状態と手動清掃契約が不足している。

## 設計原則

1. **正式仕様優先**: モックアップと正式文書が異なる場合、機能・文言・操作は正式文書、情報階層・配色・余白・形状はモックアップを基準とする。
2. **実コンポーネント**: モックアップ全体を画像表示せず、Text、Button、List、Form、Semanticsを持つCompose UIとして構築する。
3. **単一の視覚言語**: 同じ意味の色、カード、ボタン、状態、リスト項目を各Featureで個別実装しない。
4. **状態と操作の保持**: UI変更によって既存のLoading、Error、Retry、Permission、競合、通信結果不明、危険操作確認を落とさない。
5. **Adaptive first**: 特定画像サイズの固定座標ではなく、Window inset、画面幅、高さ、向き、文字拡大に応じて再配置する。
6. **PRごとの完結**: 各PRは対象画面の実装、Compose test、スクリーンショット確認、対応表更新までを含む。

## ビジュアルDesign system

### Color token

実装時にモックアップから相対関係を抽出し、最終値は実機のコントラスト確認後に固定する。色を直接使わず意味tokenを介する。

| Token群 | 用途 |
| --- | --- |
| `brandPrimary` / `onBrandPrimary` | 深い藍の主要ボタン、選択状態、主要アイコン |
| `background` / `surface` / `surfaceSubtle` | 温かみのある明るい背景、カード、補助面 |
| `outline` / `divider` | 細いカード境界線、リスト区切り |
| `textPrimary` / `textSecondary` / `textDisabled` | 見出し、補足、非活性文字 |
| `success` / `warning` / `error` / `info` | 成功、注意、失敗、情報。必ず文字またはIconと併用 |
| `categoryPhoto` / `categoryVideo` / `categoryAudio` / `categoryDocument` | カテゴリの補助色。状態判別には使用しない |

Light color schemeを参考UIの基準とする。Dark color schemeは同じ意味tokenを別値で提供し、システム設定に追従する。Dynamic colorはブランドの一貫性を崩すため既定で使用しない。

### Typography・Shape・Spacing

- 本文、入力、操作LabelはAndroidで可読性の高いsystem sans-serifを使う。
- ロゴタイプまたは装飾見出しだけに限定してserifを使用可とし、本文に強制しない。
- Material 3のTypography roleにKuraStorageの字幅・太さ・行間を対応させ、画面ごとの任意な`sp`指定を避ける。
- Spacingは4dp基準の意味token（`xxs` / `xs` / `sm` / `md` / `lg` / `xl` / `xxl`）で構成する。
- カード、入力、ボタン、Bottom navigationは用途ごとに形状tokenを分け、モックアップの柔らかい角丸を再現する。
- 境界線と小さなElevationで層を示し、強い影に依存しない。

### Icon・Logo・Asset

- 操作IconはMaterial Symbols/Iconsの一貫したstyleを使い、同じ操作に別Iconを使わない。
- ファイル種別はMIMEとEntry typeから共通の`KuraFileTypeIcon`へ変換する。サムネイル取得の有無でレイアウトを変えない。
- ロゴは出典と利用可否を確認したリポジトリ内AssetまたはCompose Vector/Canvasで実装する。操作性のない装飾はSemantics treeから除外する。
- モックアップ内のサンプル写真はProduction assetとして追加せず、実Thumbnailまたは種別Iconを使う。

## 共通コンポーネント設計

### 画面枠

| Component | 責務 |
| --- | --- |
| `KuraAppScaffold` | Window inset、背景、Top bar、Bottom navigation、Snackbar host、FABの共通配置 |
| `KuraTopAppBar` | タイトル、Back、主操作、overflow、スクロール連動 |
| `KuraBottomNavigation` | ホーム、ファイル、共有、検索、設定の最大5導線と選択状態 |
| `KuraScreenContent` | 画面幅に応じた最大幅、水平余白、縦スクロールの基準 |
| `KuraSectionHeader` | 見出しSemantics、件数、「すべて見る」等の補助操作 |

Bottom navigationは認証前、フルスクリーンViewer、確認Dialogでは表示しない。下位画面から主要Destinationへ移動する場合は、同一Destinationの重複を避け、主要Destinationごとの状態を不要に複製しないNavigation optionを使う。

### 表示・操作部品

- `KuraCard`: 通常、操作可能、選択中、警告のvariantを持つ。
- `KuraListRow`: leading、title、supporting text、metadata、status、trailing actionをslotで受ける。
- `KuraPrimaryButton` / `KuraSecondaryButton` / `KuraDestructiveButton`: 操作の重要度と危険性を一貫させる。
- `KuraStatusBadge` / `KuraStatusPanel`: success、warning、error、info、neutralとlabel・Iconを対応させる。
- `KuraTextField` / `KuraPasswordField` / `KuraSelectionField`: label、description、error、enabled、processingの構造を統一する。
- `KuraSegmentedControl`: 画質、Filter等の排他選択に使い、文字拡大時は縦方向へ変形できる。
- `KuraProgressSummary` / `KuraProgressRow`: 不定、進捗率、件数、再試行、停止理由を表示する。
- `KuraConfirmationDialog`: 対象、影響範囲、取り消し、実行を持ち、危険操作variantを分ける。
- `KuraLoadingState` / `KuraEmptyState` / `KuraErrorState` / `KuraBlockingState`: 状態の理由、次操作、request IDを必要に応じて表示する。

共通コンポーネントはDomain固有のModelを受け取らず、色、label、Icon、Callback等のUI入力のみを受ける。Domain stateからvariantへの変換は各Featureで行う。

## コンポーネント境界

### 1. `core-ui`

**責務**:

- Theme、Design token、Icon、共通Scaffold、共通部品、共通状態、アクセシビリティ補助を提供する。
- PreviewとCompose instrumented testから各部品のLight/Dark、通常/拡大文字、有効/無効、状態variantを確認可能にする。

**実装の要点**:

- 現在の`KuraStorageTheme.kt`をtheme、component、stateへ分割する。
- Feature固有のScreen、ViewModel、Domain wordingを配置しない。
- `MaterialTheme`のColorScheme、Typography、Shapesを正式な拡張点とし、CompositionLocalはMaterial tokenにないSpacing等の最小限に限る。

### 2. `app`

**責務**:

- App全体のNavigation、主要Destination、Bottom navigation、Snackbar host、Feature間Callbackを構成する。
- ホーム画面に必要な複数Featureの要約を表示用stateとして組み立てる。

**実装の要点**:

- 肥大化した`MainActivity.kt`の挙動を変えず、App shell、Home presentation、Navigation helperを必要な範囲で分割する。
- Feature間でComposableを直接呼び出さず、現行どおりAppがIDとCallbackを介して遷移させる。
- Session喪失時の認証画面、接続喪失時の接続画面への復帰は、見た目に依存させない。

### 3. 各`feature-*`

**責務**:

- UiStateを正式文言、情報階層、画面固有componentに変換する。
- 画面固有の一覧、Viewer、Editor、Form、Dialog、処理中表示を実装する。

**実装の要点**:

- 既存ViewModelとRepository契約は保ち、Composableの再構成を主とする。
- Feature内で繰り返す部品だけをFeature固有componentとし、他Featureと共通化するものは`core-ui`へ置く。
- UIはRepositoryを直接参照せず、UiStateとCallbackだけに依存する。

## 画面群の設計

### 1. 起動・接続・認証（`001`〜`008`）

- SplashはLogo、アプリ名、最小限の読み込みを表示し、長時間の処理画面と兼用しない。
- Connectionは確認中、LOCAL_DIRECT、REMOTE_SECURE、DISCONNECTED、TLS失敗、HDD利用不可をStatus panelで明確に分ける。
- Legacyの`VPN`文言はZeroTierと表記し、別アプリ確認後の「再確認」のみを提供する。
- AuthはLogo、画面タイトル、説明、ユーザー名、パスワード、表示切替、主ボタン、エラーを一つのFormとして表示する。
- 初回Device登録は通常Loginと同じForm構造を再利用しつつ、処理中とLOCAL_DIRECTが必要な拒否理由を別状態で表示する。

### 2. ホーム・主要Navigation（`009`〜`015`）

- Homeは「現在状態」「主要導線」「カテゴリ」「最近の項目」の順とし、管理者限定容量警告は現在状態の近くに表示する。
- 主要Navigationはホーム、ファイル、共有、検索、設定を基本とする。その他はHomeまたはSettingsから遷移する。
- File、Recent、Shared、Category、Searchは`KuraListRow`と共通Entry metadata表現を利用する。
- Settingsはアカウント、接続、自動バックアップ、許可Wi-Fi、画質・通信量、キャッシュ、その他導線をSection化する。
- Favorites、Tags、Activityは専用モックアップがないため、Search、Recent、Settingsの各パターンを再利用する。

### 3. ファイル・共有・転送（`010`、`021`〜`029`）

- File browserは現在位置、フォルダー、ファイル、List/Grid、検索、並べ替え、Uploadを、画面幅に適した順序で表示する。
- Entry行はサムネイル/種別Icon、名前、更新日時、サイズ/項目数、共有、権限、MISSING、overflowの順とする。
- Detailはヘッダーカード、metadataカード、操作カードに分ける。権限で実行できない操作を誤って有効にしない。
- Unsupported fileは非対応理由、metadata、Downloadと外部操作を正式仕様の許可範囲で表示する。
- Sharingは共有元、適用範囲、Member、Permission、継承を階層化し、Fileに`CONTRIBUTOR`を提示しない等の既存Ruleを保つ。
- Transferは項目ごとの状態、進捗、再試行、取消し、完了を表示し、通信結果不明で成功を合成しない。
- TrashとMissingは通常File listと共通部品を使い、復元可能期間、再確認、索引削除、完全削除の危険性を独立表示する。

### 4. Viewer・Player・Text（`016`〜`020`）

- Viewer共通枠はBack、title、詳細、コンテンツ領域、主要操作、ステータスを持つ。フルスクリーン表示と通常表示の両方でWindow insetを考慮する。
- PhotoはピンチZoom、前後移動、品質、サイズ/転送量、Download、Detailをコンテンツより優先しすぎない配置にする。
- Video/Audioは再生、Seek、3/10秒移動、速度、時間、Video品質、Job状態、再接続をまとめる。AudioにVideo変換用UIを表示しない。
- PDFはDownload確認、進捗、現在/総Page、拡大縮小、上限Errorを提供する。
- Textは閲覧/編集、dirty、保存、version、encoding、競合時の再読込・比較・別名保存、履歴・preview・復元を一貫表示する。

### 5. Settings・Backup（`030`〜`036`）

- Backup overviewは最終成功、保留、Upload中、成功、失敗の要約、Rule/Task一覧、今すぐ実行、一時停止/再開、失敗retry、Policy説明を表示する。
- Backup ruleはSource、Server保存先、有効状態、Network mode、Battery、初回充電を表示・編集し、SAF・Server pickerはApp callbackのまま保つ。
- Trusted Wi-Fiは一覧とFormを分け、読み取り権限、SSID/BSSID、従量制、有効状態、登録確認を表示する。
- Qualityは接続種別ごの説明と低/中/元画質をSegmented controlで表示し、正式文言でZeroTierを使う。自動BackupのMobile通信禁止は変更不可とする。
- Cacheは管理者にだけ表示し、低・中画質キャッシュの現在量、10GB上限、6GB清掃目標、最終清掃、生成中、失敗、手動清掃、再生成可能なデータであることを表示する。
- モックアップのThumbnail内訳は10GB対象ではないため上限使用量へ合算しない。「失敗項目を再試行」は正式仕様にないため実装せず、Mockup対応表に意図的差分を記録する。

## キャッシュ管理の不足実装設計

### Server契約

- `GET /api/v1/admin/media-cache` はAdminだけに、対象キャッシュの合計、Image Low/MediumとVideo Low/Mediumの内訳、上限、清掃目標、生成中件数、失敗件数、最新Cleanup runを返す。物理Path、File名、User名、Job入力は返さない。
- `POST /api/v1/admin/media-cache/cleanup-requests` は必須のUUID `Idempotency-Key`を受け、既存の保留または実行中Requestに収束させて`202 Accepted`とRun ID/状態を返す。HTTP Requestの中でCleanup全体を実行しない。
- Member、未認証、失効Device/Sessionは既存のAdmin認可・Error envelopeで拒否する。別Userへの存在差や物理Storage詳細を公開しない。

### 永続状態とWorker

- `MediaCleanupRun`にRun ID、trigger（scheduled/manual）、状態（pending/running/completed/failed）、Idempotency key hash、requesting Admin、開始/終了時刻、候補、削除件数、解放Byte、失敗件数を保持する。対象File名や物理Pathは保持しない。
- APIはmanual runを作成するだけとし、独立`KuraStorage.Worker`が未処理runを有界な間隔でclaimする。既存PostgreSQL advisory lockと`IMediaCleanupService`を再利用し、scheduled/manualの同時Cleanupを防ぐ。
- Worker停止またはProcess終了でrunningのままとなったrunはlease期限後に回収し、冪等なCleanupと最終状態更新へ収束させる。
- 状態Queryは`file_derivatives`と`media_jobs`を集計し、上限対象をLow/MediumのREADYに限る。一覧の個人データは返さない。

### Android

- `core-network`にDTO/API、`core-model`に表示Model、`core-data`にAdmin専用Repository、`feature-settings`にCache ViewModel/Screenを置く。
- Appは認証済みRoleがAdminの場合だけCache導線を表示する。Serverは必ず再認可し、UIの非表示を認可の代替にしない。
- 手動清掃後はRunをpollし、pending/running/completed/failedを表示する。通信結果不明時は同一Idempotency keyの再送またはGET再取得で収束させ、成功を合成しない。

## Navigation設計

### Destination分類

```text
Pre-auth:
  CONNECTION -> AUTHENTICATION -> HOME

Primary authenticated:
  HOME | FILES | SHARING | SEARCH | SETTINGS

Secondary authenticated:
  RECENT | CATEGORY | FAVORITES | TAGS | ACTIVITY | TRASH
  FILE_DETAIL | FOLDER_DETAIL | SHARING_SETTINGS | ENTRY_ORGANIZATION
  MEDIA_SETTINGS | BACKUP_* | CACHE

Immersive/detail:
  PHOTO | PDF | VIDEO | AUDIO | TEXT_EDITOR | TEXT_HISTORY
```

- 現在の`AppDestination`にない正式画面は、機能が既に同一Composable内の状態として実現されているかを先に監査する。独立DestinationはBack stackとdeep state復元に必要な場合だけ追加する。
- Bottom navigation選択時は`launchSingleTop`、主要Destinationへの`popUpTo`、必要なstate復元を使い、タブを押すたびに無限にBack stackを積まない。
- Session喪失と接続喪失は保護画面のBack stackを破棄し、別User・別ServerのUI stateを再利用しない。
- スクリーンショット・Compose testがNavigation containerに強く依存しないよう、画面ComposableはstateとCallbackで独立描画可能に保つ。

## 表示データフロー

### 通常画面

```text
Repository/API/Room
  -> existing ViewModel
  -> existing UiState
  -> Feature内で表示variant・表示文言・操作可否を導出
  -> core-ui componentにprimitive/slotとCallbackを渡す
  -> user action
  -> existing ViewModel method
```

### 画面間遷移

```text
Feature screen
  -> Entry ID / route-safe valueとnavigation callbackをappへ通知
  -> app NavControllerがDestinationを選択
  -> Destination側でSession-scoped ViewModelを構築
  -> 最新の詳細・権限を取得して画面表示
```

画面遷移時にFile全体、Token、Password、SSID/BSSID、端末PathをRoute引数に含めない。

## エラーハンドリング戦略

### 画面状態分類

| 分類 | 表示 |
| --- | --- |
| Initial loading | スケルトンまたは不定Progressと処理内容 |
| Empty | 空の理由、初回操作、更新。Errorと混同しない |
| Recoverable error | 簡潔な理由、retry、必要な場合のrequest ID |
| Blocking error | 安全に続行できない理由、戻る/再認証/接続確認等の次操作 |
| Inline validation | 対象Fieldの近くに文言とSemanticsで表示 |
| Operation error | 入力を保持し、SnackbarまたはInline panelで再実行可能性を表示 |
| Unknown result | 成功を推測せず、再取得または同一Key再試行へ案内 |

新しい例外classは追加せず、既存の型付きUiStateと`ApiError`を表示variantへ変換する。未知enum、未知Error code、権限不明はfail-closedとし、操作可能と推測しない。

## Responsive設計

- ルートは`WindowInsets.safeDrawing`等でstatus/navigation barとCutoutを避け、各画面で重複paddingしない。
- Compact幅は1列、十分な幅ではHomeの状態カードやBackup要約を2列へ変形する。固定の縦サイズでテキストを切らない。
- 横画面ではコンテンツの最大幅を制限し、上下余白が不足する画面をLazyColumnまたはvertical scrollで到達可能にする。
- 200%文字拡大では、水平に固定したボタン列、カード列、segmented control、metadata key/valueを縦配置へ変形する。
- Viewerのコンテンツ領域は主要操作を押し出さず、操作部をスクロールまたは折り畳み可能にする。

## アクセシビリティ設計

- 操作可能領域は48dp相当以上とし、Iconの可視サイズとタップ領域を分ける。
- Icon-only buttonには具体的なContent descriptionを設定し、装飾Iconは読み上げ対象外にする。
- Section titleにheading semantics、選択部品にselected/state description、Progressにprogress semanticsを付与する。
- Errorと処理完了はLive regionまたは適切なFocus移動で認識できるようにする。実装時は不必要な連続読み上げを避ける。
- 危険操作、成功、警告、失敗は色だけでなく、labelとIcon/形状を組み合わせる。
- 画面の読み上げ順は、タイトル、現在状態、主コンテンツ、主操作、補助操作の順を基本とする。

## テスト戦略

### 単体テスト

- Domain/UiStateからstatus variant、label、操作可否、Icon typeへの純粋な変換を対象にする。
- Navigation destinationの分類、Bottom navigation選択、Session変更時の画面状態破棄をJVM test可能な範囲で検証する。
- MIME/Entry typeからファイル種別表示への変換、状態のUnknown fallbackを検証する。

### Compose instrumented test

- `core-ui`でTheme、共通部品、タップ領域、Semantics、Light/Dark、文字拡大時の到達性を検証する。
- 各FeatureでLoading、Empty、Content、Error、Processing、Permission、主要操作、危険操作確認を対象にする。
- UI testは文字列の完全一致だけに依存せず、安定したtest tag、role、state、意味のあるSemanticsを用いる。
- Lazy list/gridは1,000件Fixtureで表示Window以外を不要にcomposeしない既存の性能Testを保つ。

### 視覚検証

- 各画面群に決定的Fixtureを使うキャプチャ可能なCompose testを用意する。初期は既存の`captureToImage()`パターンを再利用する。
- 画像の自動pixel diff基盤は、フォント・GPU・API差で不安定化しない実行環境を固定できる場合だけ導入する。導入する場合は専用依存を別のPRにせず、基盤PRで検証まで完了する。
- 各PRで、対応Mockup番号、検証端末/解像度、表示状態、意図的差分、残存する不具合を`docs/testing/`に記録する。

### 結合・実機テスト

- App Navigationで認証前→認証後、Bottom navigation、Entry→Detail/Viewer、Settings→Backup/Qualityを検証する。
- Android 13物理端末でLOCAL_DIRECTとREMOTE_SECURE、通信断・再接続、文字拡大、縦/横、TalkBack、Dark themeを確認する。
- 参考UI 36枚の対応表を最終E2E時に照合し、未検証画面を残さない。

## 依存ライブラリ

原則として新しいProduction依存は追加しない。現行のJetpack Compose、Material 3、Navigation Compose、Coil/Media3等の既存依存を使う。

- Material Icons Extendedが未導入で、必要Iconを現行依存で表現できない場合は、まずリポジトリ内Vector assetを優先する。依存追加はAPKサイズとLock fileを確認して最小限にする。
- Screenshot regression用ライブラリは必須とせず、まず既存のAndroid Compose testと`captureToImage()`を使う。
- フォントファイルはライセンス、APKサイズ、日本語glyphの網羅性を確認した場合だけ追加する。基本はsystem fontとする。

## ディレクトリ構造

```text
apps/android/
├── core-ui/
│   └── src/
│       ├── main/kotlin/com/kurastorage/core/ui/
│       │   ├── theme/
│       │   │   ├── Color.kt
│       │   │   ├── Theme.kt
│       │   │   ├── Type.kt
│       │   │   ├── Shape.kt
│       │   │   └── Spacing.kt
│       │   ├── components/
│       │   │   ├── KuraAppBars.kt
│       │   │   ├── KuraButtons.kt
│       │   │   ├── KuraCards.kt
│       │   │   ├── KuraForms.kt
│       │   │   ├── KuraLists.kt
│       │   │   ├── KuraNavigation.kt
│       │   │   ├── KuraStatus.kt
│       │   │   └── KuraDialogs.kt
│       │   ├── icons/
│       │   ├── state/
│       │   └── accessibility/
│       └── androidTest/kotlin/com/kurastorage/core/ui/
├── app/src/
│   ├── main/kotlin/com/kurastorage/app/
│   │   ├── MainActivity.kt
│   │   ├── navigation/
│   │   └── home/
│   └── androidTest/kotlin/com/kurastorage/app/
├── feature-connection/src/{main,androidTest}/...
├── feature-auth/src/{main,androidTest}/...
├── feature-files/src/{main,androidTest}/...
├── feature-sharing/src/{main,androidTest}/...
├── feature-search/src/{main,androidTest}/...
├── feature-activity/src/{main,androidTest}/...
├── feature-media/src/{main,androidTest}/...
├── feature-text/src/{main,androidTest}/...
├── feature-settings/src/{main,androidTest}/...
└── feature-backup/src/{main,androidTest}/...

docs/testing/
└── YYYYMMDD-android-ui-<pr-scope>.md
```

上記は責務の配置を示す。空Directoryは先に作らず、必要な分割だけ行う。現行ファイルの小規模Composableは、単に構造表と合わせるためだけに移動しない。

## Mockup追跡設計

実装開始時に`001`〜`036`の各画面について、tasklistまたは検証記録に次を持つ。

| 項目 | 内容 |
| --- | --- |
| Mockup | 番号とPath |
| Formal screen | `docs/functional-design.md`の対応画面・節 |
| Production owner | `app`または`feature-*`のComposable |
| States | Loading/Empty/Content/Error/Processing等の検証対象 |
| Intentional differences | ZeroTier表記、未実装値の非表示、Responsive再配置等 |
| Automated evidence | Compose test名または対象test file |
| Manual evidence | 端末、方向、文字倍率、screenshotの記録 |

この対応が未記録の画面は、見た目が整っていても完了としない。

## Pull Request分割方針

タスクは以下の依存順でPull Request単位に分ける。対象PRは実装、テスト、検証記録をまとめて完了する。

1. **UI監査・Design system基盤**: 36画面対応、Theme、Token、最小の共通部品、core-ui test。
2. **App shell・Home・Navigation**: `009`、Bottom navigation、Settingsの主要導線、App navigation test。
3. **Connection・Auth**: `001`〜`008`、接続状態、ZeroTier案内、Login/Device登録。
4. **File browser・Detail・Transfer**: `010`、`021`〜`023`、`026`〜`029`、Entry共通表示、危険操作。
5. **Sharing・Search・Recent・Organization・Activity**: `011`〜`014`、`024`〜`025`、参考UIのない追加Featureの統一。
6. **Media viewer・Player**: `016`〜`019`、Viewer共通枠、Photo/PDF/Video/Audio。
7. **Text editor・History**: `020`、未保存、競合、履歴・preview・復元。
8. **Server cache management契約**: Admin状態取得、永続Cleanup run、非同期手動清掃、Worker復旧、OpenAPI/Server test。
9. **Settings・Backup・Cache UI**: `015`、`030`〜`036`、Rule/Wi-Fi/Quality/Cache、Android契約とUI test。
10. **全体Adaptive・Accessibility・実機E2E**: 360dp、200%文字、横画面、TalkBack、Light/Dark、36画面追跡の最終照合。

実装時の差分量により、1つのPRが大きすぎる場合は同じ目的内で画面群を細分化する。ただし、実装と対応テスト、検証記録を分離しない。

## 実装の順序

1. 36モックアップと現行Composable/UiState/testの対応を監査し、意図的差分と欠落状態を固定する。
2. `core-ui`にColor、Typography、Shape、Spacing、Icon、状態のTokenと基本部品を実装し、Compose testで固定する。
3. `app`のScaffold、Bottom navigation、Home、Settings導線を作り、各Featureが使う画面枠を固定する。
4. Connection/Authを更新し、認証前の画面パターンを完成させる。
5. File/Sharing/Search/Activityを更新し、一覧、metadata、detail、フォーム、危険操作を揃える。
6. Media/Textを更新し、Viewer、Player、EditorのAdaptive操作領域を完成させる。
7. Cache管理に必要なAdmin API、永続Cleanup run、Worker claim/recoveryを実装し、Serverで完結するTestを行う。
8. Settings/Backup/Cacheを更新し、要約、進捗、Rule/Wi-Fi/Quality/Cache formを揃える。
9. 各PRでBuild、Lint、unit test、対象instrumented test、screenshot確認を実施する。
10. 最終PRで36画面、縦/横、360dp、200%文字、TalkBack、Light/Dark、Android 13実機フローを照合する。

## セキュリティ考慮事項

- UI変更でTLS、Hostname、Server identity、Route、User/Device/Session認証を迂回する操作を追加しない。
- ZeroTier操作は別アプリ案内に留め、KuraStorageから接続、切断、Member認可を行わない。
- Password、Token、SSID/BSSID、端末Path、`localDocumentKey`、秘密情報をSemantics、test tag、screenshot fixture、Log、Navigation routeへ含めない。
- Password表示切替は利用者の明示操作でのみ行い、画面再構成や遷移後に意図せず表示状態を維持しない。
- 共有、削除、完全削除、復元、上書き、キャッシュ清掃は対象と影響範囲を表示し、処理中の二重送信を防ぐ。
- 権限不明、未知state、情報取得失敗時は破壊的・高権限操作を無効にする。
- キャッシュ状態と手動清掃はAdminに限定し、清掃runに必要な監査情報を残すが、File名、物理Path、User入力を記録しない。

## パフォーマンス考慮事項

- Lazy list/gridを維持し、モックアップに合わせるために全項目を一括composeしない。
- 各行のkeyを安定させ、UiState変更で全リストを不要に再作成しない。
- 一覧のサムネイルに元ファイルを使わず、既存のサムネイル取得とmemory/disk cache境界を維持する。
- 装飾用の大きなBitmapをパッケージしない。Vector、Shape、Brushを優先し、Canvas描画はフレームごとの割り当てを避ける。
- 画面の縦サイズを無制限に固定せず、文字拡大時の計測ループや過剰なSubcomposeを避ける。
- Screenshot取得はTest/debugに限定し、Production実行のメモリやI/Oに影響させない。

## 文書更新方針

- 実装中に正式UI仕様の不足または矛盾が見つかった場合、独自に補完せず影響を明示し、必要な`docs/product-requirements.md`または`docs/functional-design.md`を同じPRで更新する。
- Repository配置、依存方向、検証手順が変わる場合は、`docs/repository-structure.md`または`docs/development-guidelines.md`を更新する。
- 各PRの表示検証結果は`docs/testing/YYYYMMDD-android-ui-<pr-scope>.md`に記録する。
- 各PR完了後は`tasklist.md`のPull Request完了記録を追記する。全体振り返りは全PRと全タスク完了後だけ記録する。

## 将来の拡張性

- 将来のWeb UIとデザイン値を共有できるよう意味tokenの命名は一般化するが、AndroidのComposeコンポーネントそのものを無理に共通化しない。
- Tablet/foldableへの対応時は、意味tokenとScreen contentの最大幅を再利用し、Navigation railやlist-detailを別の正式仕様として追加できる。
- 安定したScreenshot実行環境をCIで固定できた場合、現行の決定的FixtureとCapture testをGolden image比較へ拡張できる。
- 新しいFeatureはDesign token、App scaffold、List/Form/Stateパターンを再利用し、専用MockupがなくてもKuraStorageの視覚言語に整合できる。
